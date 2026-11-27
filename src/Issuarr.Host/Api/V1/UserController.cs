using Issuarr.Core.Configuration;
using Issuarr.Core.Users;
using Issuarr.Host.Authentication;
using Microsoft.AspNetCore.Mvc;

namespace Issuarr.Host.Api.V1;

public record UserResource(int Id, string Username, string Role, bool IsOwner, string Created, string? LastSeen, bool KoSync);

public record InviteResource(string Token, string Role, string? Note, string Created, string Expires);

public record InviteRequest(string Role, string? Note, int Days = 7);

public record UserUpdateRequest(string? Role, string? Password);

public record PasswordChangeRequest(string Current, string Password);

public record KoSyncPasswordRequest(string? Password);

/// <summary>Accounts and invites (admins only; AuthGate keeps readers out).</summary>
[ApiController]
[Route("api/v1/user")]
public class UserController(IUserService users) : ControllerBase
{
    [HttpGet]
    public List<UserResource> All() => users.All().Select(ToResource).ToList();

    [HttpPut("{id:int}")]
    public IActionResult Update(int id, UserUpdateRequest req) => Run(() =>
    {
        if (users.Get(id) == null)
        {
            throw new KeyNotFoundException("User not found");
        }

        if (req.Role != null)
        {
            if (id == HttpContext.UserId() && req.Role != Roles.Admin)
            {
                throw new ArgumentException("You can't take away your own admin rights");
            }

            users.SetRole(id, req.Role);
        }

        if (!string.IsNullOrEmpty(req.Password))
        {
            users.SetPassword(id, req.Password);
        }

        return ToResource(users.Get(id)!);
    });

    [HttpDelete("{id:int}")]
    public IActionResult Delete(int id) => Run(() =>
    {
        if (id == HttpContext.UserId())
        {
            throw new ArgumentException("You can't delete your own account");
        }

        users.Delete(id);
        return new { };
    });

    [HttpGet("invite")]
    public List<InviteResource> Invites() => users.PendingInvites().Select(ToResource).ToList();

    [HttpPost("invite")]
    public IActionResult CreateInvite(InviteRequest req) => Run(() => ToResource(users.CreateInvite(req.Role, req.Note, req.Days)));

    [HttpDelete("invite/{token}")]
    public IActionResult RevokeInvite(string token)
    {
        users.RevokeInvite(token);
        return Ok(new { });
    }

    public static UserResource ToResource(User u) => new(u.Id, u.Username, u.Role, u.IsOwner, u.Created, u.LastSeen, u.KoSyncKey != null);

    private static InviteResource ToResource(Invite i) => new(i.Token, i.Role, i.Note, i.Created, i.Expires);

    private IActionResult Run(Func<object> action)
    {
        try
        {
            return Ok(action());
        }
        catch (ArgumentException e)
        {
            return BadRequest(new { message = e.Message });
        }
        catch (KeyNotFoundException e)
        {
            return NotFound(new { message = e.Message });
        }
    }
}

/// <summary>The signed-in user's own account (any role).</summary>
[ApiController]
[Route("api/v1/me")]
public class MeController(IUserService users, IConfigService config) : ControllerBase
{
    [HttpGet]
    public object Get()
    {
        var u = users.Get(HttpContext.UserId())!;
        return new { user = UserController.ToResource(u), koSyncEnabled = config.KoSyncEnabled };
    }

    [HttpPut("password")]
    public IActionResult Password(PasswordChangeRequest req)
    {
        try
        {
            users.ChangeOwnPassword(HttpContext.UserId(), req.Current, req.Password);
            return Ok(new { });
        }
        catch (ArgumentException e)
        {
            return BadRequest(new { message = e.Message });
        }
    }

    [HttpPut("kosync")]
    public IActionResult KoSync(KoSyncPasswordRequest req)
    {
        try
        {
            users.SetKoSyncPassword(HttpContext.UserId(), req.Password);
            return Ok(new { });
        }
        catch (ArgumentException e)
        {
            return BadRequest(new { message = e.Message });
        }
    }
}
