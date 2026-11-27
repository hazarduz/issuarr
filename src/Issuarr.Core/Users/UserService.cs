using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Dapper;
using Issuarr.Core.Configuration;
using Issuarr.Core.Datastore;

namespace Issuarr.Core.Users;

public static class Roles
{
    /// <summary>Full control: settings, adding/removing titles, searching, users.</summary>
    public const string Admin = "admin";

    /// <summary>Browse the library and read/download; their own reading progress. No changes.</summary>
    public const string Reader = "reader";

    public static bool Valid(string? role) => role is Admin or Reader;
}

public class User
{
    /// <summary>The account whose login is the one in Settings → General (config.xml).</summary>
    public const int OwnerId = 1;

    public int Id { get; set; }
    public string Username { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string Role { get; set; } = Roles.Reader;
    public string? KoSyncKey { get; set; }
    public string Created { get; set; } = "";
    public string? LastSeen { get; set; }

    public bool IsOwner => Id == OwnerId;
    public bool IsAdmin => Role == Roles.Admin;
}

public class Invite
{
    public string Token { get; set; } = "";
    public string Role { get; set; } = Roles.Reader;
    public string? Note { get; set; }
    public string Created { get; set; } = "";
    public string Expires { get; set; } = "";
}

public interface IUserService
{
    User Owner { get; }
    User? Get(int id);
    List<User> All();

    /// <summary>Checks a username and password (the owner's from config.xml, everyone else's from the database).</summary>
    User? Verify(string username, string password);

    Invite CreateInvite(string role, string? note, int days);
    List<Invite> PendingInvites();
    Invite? ValidInvite(string token);
    void RevokeInvite(string token);
    User AcceptInvite(string token, string username, string password);

    void SetPassword(int id, string password);
    void ChangeOwnPassword(int id, string current, string password);
    void SetRole(int id, string role);
    void Delete(int id);

    /// <summary>A separate password for KOReader sync (KOReader sends an unsalted MD5 of it, so it shouldn't be the login password).</summary>
    void SetKoSyncPassword(int id, string? password);

    /// <summary>The user a KOReader sync login (username + MD5 key) belongs to.</summary>
    User? KoSyncUser(string? username, string? key);

    void Touch(int id);
}

public partial class UserService(IDatabase db, IConfigService config, ConfigFile file, IClock clock) : IUserService
{
    private readonly Dictionary<int, DateTime> _touched = [];

    public User Owner => Get(User.OwnerId)!;

    public User? Get(int id)
    {
        using var conn = db.Open();
        var user = conn.QuerySingleOrDefault<User>("SELECT * FROM Users WHERE Id = @id", new { id });
        if (user is { IsOwner: true })
        {
            // The owner's name is whatever is set in Settings → General.
            user.Username = file.Username.Length > 0 ? file.Username : "owner";
            user.Role = Roles.Admin;
        }

        return user;
    }

    public List<User> All()
    {
        using var conn = db.Open();
        var list = conn.Query<User>("SELECT * FROM Users ORDER BY Id").AsList();
        foreach (var u in list.Where(u => u.IsOwner))
        {
            u.Username = file.Username.Length > 0 ? file.Username : "owner";
            u.Role = Roles.Admin;
        }

        return list;
    }

    public User? Verify(string username, string password)
    {
        if (file.VerifyPassword(username, password))
        {
            return Get(User.OwnerId);
        }

        using var conn = db.Open();
        var user = conn.QuerySingleOrDefault<User>("SELECT * FROM Users WHERE Username = @username AND Id <> 1", new { username });
        return user != null && ConfigFile.VerifyHash(user.PasswordHash, password) ? user : null;
    }

    public Invite CreateInvite(string role, string? note, int days)
    {
        if (!Roles.Valid(role))
        {
            throw new ArgumentException("Role must be admin or reader");
        }

        var invite = new Invite
        {
            Token = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant(),
            Role = role,
            Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
            Created = Now(),
            Expires = clock.UtcNow.AddDays(Math.Clamp(days, 1, 90)).ToString("O"),
        };
        using var conn = db.Open();
        conn.Execute("INSERT INTO Invites (Token, Role, Note, Created, Expires) VALUES (@Token, @Role, @Note, @Created, @Expires)", invite);
        return invite;
    }

    public List<Invite> PendingInvites()
    {
        using var conn = db.Open();
        conn.Execute("DELETE FROM Invites WHERE Expires < @now", new { now = Now() });
        return conn.Query<Invite>("SELECT * FROM Invites ORDER BY Created DESC").AsList();
    }

    public Invite? ValidInvite(string token)
    {
        using var conn = db.Open();
        return conn.QuerySingleOrDefault<Invite>("SELECT * FROM Invites WHERE Token = @token AND Expires >= @now", new { token, now = Now() });
    }

    public void RevokeInvite(string token)
    {
        using var conn = db.Open();
        conn.Execute("DELETE FROM Invites WHERE Token = @token", new { token });
    }

    public User AcceptInvite(string token, string username, string password)
    {
        var invite = ValidInvite(token) ?? throw new ArgumentException("This invite link has expired or was already used. Ask for a new one.");
        username = username.Trim();
        ValidateUsername(username);
        ValidatePassword(password);

        using var conn = db.Open();
        using var tx = conn.BeginTransaction();
        if (conn.Execute("DELETE FROM Invites WHERE Token = @token", new { token }, tx) == 0)
        {
            throw new ArgumentException("This invite link was already used.");
        }

        var id = conn.ExecuteScalar<long>(
            "INSERT INTO Users (Username, PasswordHash, Role, Created) VALUES (@username, @hash, @role, @now); SELECT last_insert_rowid();",
            new { username, hash = ConfigFile.HashPassword(password), role = invite.Role, now = Now() },
            tx);
        tx.Commit();
        return Get((int)id)!;
    }

    public void SetPassword(int id, string password)
    {
        ValidatePassword(password);
        if (id == User.OwnerId)
        {
            file.SetPassword(password);
            return;
        }

        using var conn = db.Open();
        if (conn.Execute("UPDATE Users SET PasswordHash = @hash WHERE Id = @id", new { id, hash = ConfigFile.HashPassword(password) }) == 0)
        {
            throw new KeyNotFoundException("User not found");
        }
    }

    public void ChangeOwnPassword(int id, string current, string password)
    {
        var user = Get(id) ?? throw new KeyNotFoundException("User not found");
        if (Verify(user.Username, current)?.Id != id)
        {
            throw new ArgumentException("Your current password isn't right");
        }

        SetPassword(id, password);
    }

    public void SetRole(int id, string role)
    {
        if (!Roles.Valid(role))
        {
            throw new ArgumentException("Role must be admin or reader");
        }

        if (id == User.OwnerId)
        {
            throw new ArgumentException("The owner is always an admin");
        }

        using var conn = db.Open();
        conn.Execute("UPDATE Users SET Role = @role WHERE Id = @id", new { id, role });
    }

    public void Delete(int id)
    {
        if (id == User.OwnerId)
        {
            throw new ArgumentException("The owner account can't be deleted");
        }

        using var conn = db.Open();
        conn.Execute("DELETE FROM ReadProgress WHERE UserId = @id; DELETE FROM KoSyncProgress WHERE UserId = @id; DELETE FROM Users WHERE Id = @id", new { id });
    }

    public void SetKoSyncPassword(int id, string? password)
    {
        if (!string.IsNullOrEmpty(password) && password.Length < 4)
        {
            throw new ArgumentException("Use at least 4 characters");
        }

        using var conn = db.Open();
        conn.Execute("UPDATE Users SET KoSyncKey = @key WHERE Id = @id", new { id, key = string.IsNullOrEmpty(password) ? null : Md5(password) });
    }

    public User? KoSyncUser(string? username, string? key)
    {
        if (!config.KoSyncEnabled || string.IsNullOrEmpty(username) || string.IsNullOrEmpty(key))
        {
            return null;
        }

        key = key.ToLowerInvariant();

        // The owner's original setup (Settings → General → KOReader Sync) keeps working.
        if (config.KoSyncUsername.Length > 0 && config.KoSyncKey.Length > 0 && username == config.KoSyncUsername && SameKey(key, config.KoSyncKey))
        {
            return Get(User.OwnerId);
        }

        var owner = Get(User.OwnerId)!;
        using var conn = db.Open();
        var user = string.Equals(username, owner.Username, StringComparison.OrdinalIgnoreCase)
            ? owner
            : conn.QuerySingleOrDefault<User>("SELECT * FROM Users WHERE Username = @username AND Id <> 1", new { username });
        var stored = user == null ? null : conn.ExecuteScalar<string?>("SELECT KoSyncKey FROM Users WHERE Id = @id", new { id = user.Id });
        return stored != null && SameKey(key, stored) ? user : null;
    }

    public void Touch(int id)
    {
        // At most once every few minutes per user.
        lock (_touched)
        {
            if (_touched.TryGetValue(id, out var last) && clock.UtcNow - last < TimeSpan.FromMinutes(5))
            {
                return;
            }

            _touched[id] = clock.UtcNow;
        }

        using var conn = db.Open();
        conn.Execute("UPDATE Users SET LastSeen = @now WHERE Id = @id", new { id, now = Now() });
    }

    private void ValidateUsername(string username)
    {
        if (!UsernamePattern().IsMatch(username))
        {
            throw new ArgumentException("Usernames are 2–40 letters, numbers, dots, dashes, underscores or @");
        }

        using var conn = db.Open();
        var taken = string.Equals(username, file.Username, StringComparison.OrdinalIgnoreCase) || string.Equals(username, "owner", StringComparison.OrdinalIgnoreCase)
            || conn.ExecuteScalar<int>("SELECT COUNT(*) FROM Users WHERE Username = @username AND Id <> 1", new { username }) > 0;
        if (taken)
        {
            throw new ArgumentException("That username is taken");
        }
    }

    private static void ValidatePassword(string password)
    {
        if (password.Length < 6)
        {
            throw new ArgumentException("Passwords need at least 6 characters");
        }
    }

    private static bool SameKey(string a, string b) => CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));

    private static string Md5(string s) => Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(s))).ToLowerInvariant();

    private string Now() => clock.UtcNow.ToString("O");

    [GeneratedRegex(@"^[A-Za-z0-9._@-]{2,40}$")]
    private static partial Regex UsernamePattern();
}
