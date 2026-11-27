using System.Net;
using System.Net.Sockets;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Issuarr.Core.Configuration;
using Issuarr.Core.Users;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace Issuarr.Host.Authentication;

/// <summary>Who a request is from, as worked out by <see cref="AuthGate"/>.</summary>
public static class CurrentUser
{
    private const string Key = "issuarr.user";

    public static void SetUser(this HttpContext context, User user) => context.Items[Key] = user;

    /// <summary>The signed-in user; requests that got past the gate without one (e.g. KOReader sync) count as the owner.</summary>
    public static int UserId(this HttpContext context) => context.Items[Key] is User u ? u.Id : User.OwnerId;

    public static User? IssuarrUser(this HttpContext context) => context.Items[Key] as User;

    public const string UserIdClaim = "issuarr:uid";
}

/// <summary>
/// Decides who gets in, *arr style, and who they are:
/// <list type="bullet">
/// <item>The API key (header or ?apikey=) is the owner: integrations like Prowlarr and the owner's own UI.</item>
/// <item>A login cookie is that user (owner, admin or reader).</item>
/// <item>With login off, or "Disabled for local addresses" on the local network, a request without a login is the owner.</item>
/// <item>Readers may only browse and read: anything else under /api answers 403.</item>
/// </list>
/// </summary>
public class AuthGate(RequestDelegate next, ConfigFile config, IUserService users)
{
    // /kosync does its own authentication with KOReader's x-auth-user / x-auth-key headers.
    private static readonly string[] PublicPrefixes = ["/login", "/logout", "/ping", "/assets/", "/logo.svg", "/favicon", "/kosync/", "/invite/"];

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? "/";

        if (path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase) || path.StartsWith("/feed/", StringComparison.OrdinalIgnoreCase))
        {
            var user = (HasValidApiKey(context) ? users.Owner : null) ?? CookieUser(context) ?? BypassUser(context)
                       ?? (config.AuthenticationMethod == AuthenticationMethod.Basic ? BasicUser(context) : null);
            if (user == null)
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(new { message = "Unauthorized. Supply the X-Api-Key header or apikey query parameter." });
                return;
            }

            if (!user.IsAdmin && !ReaderMayUse(context.Request.Method, path))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(new { message = "Your account can browse and read, but not change the library. Ask the owner." });
                return;
            }

            await Pass(context, user);
            return;
        }

        // OPDS clients (e-readers) can't do a login page: they use HTTP Basic or ?apikey=.
        if (path.Equals("/opds", StringComparison.OrdinalIgnoreCase) || path.StartsWith("/opds/", StringComparison.OrdinalIgnoreCase))
        {
            var user = BasicUser(context) ?? CookieUser(context) ?? (HasValidApiKey(context) ? users.Owner : null) ?? BypassUser(context);
            if (user != null)
            {
                await Pass(context, user);
                return;
            }

            context.Response.Headers.WWWAuthenticate = $"Basic realm=\"{config.InstanceName} OPDS\"";
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        if (PublicPrefixes.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
        {
            await next(context);
            return;
        }

        var uiUser = CookieUser(context) ?? BypassUser(context);
        if (uiUser != null)
        {
            await Pass(context, uiUser);
            return;
        }

        if (config.AuthenticationMethod == AuthenticationMethod.Basic)
        {
            if (BasicUser(context) is { } basic)
            {
                await Pass(context, basic);
                return;
            }

            context.Response.Headers.WWWAuthenticate = "Basic realm=\"Issuarr\"";
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        if (path.Equals("/initialize.json", StringComparison.OrdinalIgnoreCase))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { message = "Login required", loginUrl = config.UrlBase + "/login" });
            return;
        }

        var returnUrl = context.Request.PathBase + context.Request.Path + context.Request.QueryString;
        context.Response.Redirect($"{context.Request.PathBase}/login?returnUrl={Uri.EscapeDataString(returnUrl)}");
    }

    private async Task Pass(HttpContext context, User user)
    {
        context.SetUser(user);
        users.Touch(user.Id);
        await next(context);
    }

    /// <summary>What a reader can do: look at the library and covers, read, and keep their own place.</summary>
    public static bool ReaderMayUse(string method, string path)
    {
        var p = path.ToLowerInvariant().TrimEnd('/');
        if (p.StartsWith("/feed/"))
        {
            return HttpMethods.IsGet(method);
        }

        if (p == "/api/v1/me" || p.StartsWith("/api/v1/me/"))
        {
            return true;
        }

        if (System.Text.RegularExpressions.Regex.IsMatch(p, @"^/api/v1/read/\d+/progress$"))
        {
            return HttpMethods.IsPut(method) || HttpMethods.IsDelete(method) || HttpMethods.IsGet(method);
        }

        if (!HttpMethods.IsGet(method) && !HttpMethods.IsHead(method))
        {
            return false;
        }

        if (System.Text.RegularExpressions.Regex.IsMatch(p, @"^/api/v1/title(/\d+)?$|^/api/v1/title/[^/]+$"))
        {
            return !p.EndsWith("/lookup");
        }

        string[] readable = ["/api/v1/issue", "/api/v1/calendar", "/api/v1/mediacover/", "/api/v1/read/", "/api/v1/reading/", "/api/v1/system/status", "/api/v1/qualityprofile", "/api/v1/tag"];
        return readable.Any(r => p == r.TrimEnd('/') || p.StartsWith(r.EndsWith('/') ? r : r + "/") || p.StartsWith(r + "?"));
    }

    private User? CookieUser(HttpContext context)
    {
        if (context.User.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        // Cookies from before user accounts only carry the owner's name.
        var id = int.TryParse(context.User.FindFirst(CurrentUser.UserIdClaim)?.Value, out var uid) ? uid : User.OwnerId;
        return users.Get(id);
    }

    /// <summary>The owner, when this request doesn't need to log in.</summary>
    private User? BypassUser(HttpContext context)
    {
        var method = config.AuthenticationMethod;
        if (method is AuthenticationMethod.None or AuthenticationMethod.External)
        {
            return users.Owner;
        }

        return config.AuthenticationRequired == "DisabledForLocalAddresses" && IsLocal(context.Connection.RemoteIpAddress) ? users.Owner : null;
    }

    private bool HasValidApiKey(HttpContext context)
    {
        var provided = context.Request.Headers["X-Api-Key"].FirstOrDefault() ?? context.Request.Query["apikey"].FirstOrDefault();
        return provided != null && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(provided), Encoding.UTF8.GetBytes(config.ApiKey));
    }

    private User? BasicUser(HttpContext context)
    {
        var header = context.Request.Headers.Authorization.FirstOrDefault();
        if (header == null || !header.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        try
        {
            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(header[6..]));
            var sep = decoded.IndexOf(':');
            return sep > 0 ? users.Verify(decoded[..sep], decoded[(sep + 1)..]) : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    public static bool IsLocal(IPAddress? ip)
    {
        if (ip == null)
        {
            return false;
        }

        if (ip.IsIPv4MappedToIPv6)
        {
            ip = ip.MapToIPv4();
        }

        if (IPAddress.IsLoopback(ip))
        {
            return true;
        }

        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = ip.GetAddressBytes();
            return b[0] == 10 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 192 && b[1] == 168) || (b[0] == 169 && b[1] == 254);
        }

        return ip.IsIPv6LinkLocal || ip.IsIPv6UniqueLocal || ip.IsIPv6SiteLocal;
    }
}

public static class LoginEndpoints
{
    public static void MapLogin(this WebApplication app, ConfigFile config)
    {
        var users = app.Services.GetRequiredService<IUserService>();

        app.MapGet("/login", (HttpContext ctx) => Results.Content(LoginPage.Render(config.InstanceName, ctx.Request.Query["returnUrl"], ctx.Request.Query["failed"] == "1"), "text/html"));

        app.MapPost("/login", async (HttpContext ctx) =>
        {
            var form = await ctx.Request.ReadFormAsync();
            var returnUrl = SafeReturnUrl(ctx, form["returnUrl"].ToString());
            var user = users.Verify(form["username"].ToString(), form["password"].ToString());
            if (user == null)
            {
                await Task.Delay(Random.Shared.Next(400, 900)); // slow down guessing
                return Results.Redirect($"{ctx.Request.PathBase}/login?failed=1&returnUrl={Uri.EscapeDataString(returnUrl)}");
            }

            await SignIn(ctx, user, form["remember"] == "on");
            return Results.Redirect(returnUrl);
        }).DisableAntiforgery();

        app.MapGet("/logout", async (HttpContext ctx) =>
        {
            await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.Redirect($"{ctx.Request.PathBase}/login");
        });

        app.MapGet("/invite/{token}", (HttpContext ctx, string token) =>
        {
            var invite = users.ValidInvite(token);
            return Results.Content(InvitePage.Render(config.InstanceName, invite != null, null, null), "text/html");
        });

        app.MapPost("/invite/{token}", async (HttpContext ctx, string token) =>
        {
            var form = await ctx.Request.ReadFormAsync();
            var username = form["username"].ToString();
            var password = form["password"].ToString();
            if (password != form["confirm"].ToString())
            {
                return Results.Content(InvitePage.Render(config.InstanceName, true, "The two passwords don't match.", username), "text/html");
            }

            try
            {
                var user = users.AcceptInvite(token, username, password);
                await SignIn(ctx, user, true);
                return Results.Redirect(ctx.Request.PathBase + "/");
            }
            catch (ArgumentException e)
            {
                return Results.Content(InvitePage.Render(config.InstanceName, users.ValidInvite(token) != null, e.Message, username), "text/html");
            }
        }).DisableAntiforgery();
    }

    private static Task SignIn(HttpContext ctx, User user, bool remember)
    {
        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.Name, user.Username), new Claim(CurrentUser.UserIdClaim, user.Id.ToString(System.Globalization.CultureInfo.InvariantCulture))],
            CookieAuthenticationDefaults.AuthenticationScheme);
        return ctx.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity), new AuthenticationProperties { IsPersistent = remember });
    }

    private static string SafeReturnUrl(HttpContext ctx, string? url) =>
        !string.IsNullOrEmpty(url) && url.StartsWith('/') && !url.StartsWith("//", StringComparison.Ordinal) ? url : ctx.Request.PathBase + "/";
}

public static class LoginPage
{
    public static string Render(string instanceName, string? returnUrl, bool failed)
    {
        var enc = System.Text.Encodings.Web.HtmlEncoder.Default;
        return Shell(instanceName, "Login", $$"""
            <form method="post" action="login">
              <div class="brand"><img src="logo.svg" alt="" /> <span>Issu<span class="arr">arr</span></span></div>
              {{(failed ? "<div class=\"error\">Incorrect username or password.</div>" : "")}}
              <input type="hidden" name="returnUrl" value="{{enc.Encode(returnUrl ?? "")}}" />
              <label for="username">Username</label>
              <input id="username" name="username" type="text" autocomplete="username" autofocus required />
              <label for="password">Password</label>
              <input id="password" name="password" type="password" autocomplete="current-password" required />
              <label class="row"><input type="checkbox" name="remember" checked /> Remember me</label>
              <button type="submit">Log in</button>
            </form>
            """);
    }

    public static string Shell(string instanceName, string pageTitle, string body, string root = "")
    {
        var enc = System.Text.Encodings.Web.HtmlEncoder.Default;
        return $$"""
        <!doctype html>
        <html lang="en">
        <head>
          <meta charset="utf-8" />
          <meta name="viewport" content="width=device-width, initial-scale=1" />
          <title>{{enc.Encode(pageTitle)}} · {{enc.Encode(instanceName)}}</title>
          <link rel="icon" type="image/svg+xml" href="{{root}}logo.svg" />
          <style>
            :root { color-scheme: dark; }
            * { box-sizing: border-box; }
            body { margin: 0; min-height: 100vh; display: grid; place-items: center; padding: 16px;
              font-family: Inter, system-ui, -apple-system, 'Segoe UI', Roboto, sans-serif;
              background: radial-gradient(1200px 600px at 20% -10%, rgba(255,138,76,.18), transparent), #111318; color: #e7e9ee; }
            form { width: min(380px, 100%); background: #1d2129; border: 1px solid #2b313c; border-radius: 16px; padding: 28px; box-shadow: 0 20px 60px rgba(0,0,0,.4); }
            .brand { display: flex; align-items: center; gap: 10px; font-size: 22px; font-weight: 700; margin-bottom: 22px; letter-spacing: -.02em; }
            .brand img { width: 36px; height: 36px; }
            .arr { background: linear-gradient(135deg,#ffb347,#ff5e62); -webkit-background-clip: text; background-clip: text; color: transparent; }
            label { display: block; font-size: 13px; font-weight: 600; margin: 14px 0 6px; color: #9aa3b2; }
            input[type=text], input[type=password] { width: 100%; height: 42px; padding: 0 12px; border-radius: 8px; border: 1px solid #3a4250; background: #242934; color: #e7e9ee; font: inherit; outline: none; }
            input:focus { border-color: #ff8a4c; box-shadow: 0 0 0 3px rgba(255,138,76,.2); }
            .row { display: flex; align-items: center; gap: 8px; margin-top: 14px; font-size: 13px; color: #9aa3b2; }
            button { width: 100%; height: 44px; margin-top: 20px; border: 0; border-radius: 8px; font: inherit; font-weight: 700; cursor: pointer; color: #1a1208; background: linear-gradient(135deg,#ffb347,#ff5e62); }
            .lead { margin: -8px 0 4px; color: #9aa3b2; font-size: 14px; line-height: 1.5; }
            .error { margin-top: 4px; padding: 10px 12px; border-radius: 8px; background: rgba(255,92,112,.14); color: #ff5c70; font-size: 13px; }
          </style>
        </head>
        <body>
          {{body}}
        </body>
        </html>
        """;
    }
}

public static class InvitePage
{
    public static string Render(string instanceName, bool valid, string? error, string? username)
    {
        var enc = System.Text.Encodings.Web.HtmlEncoder.Default;
        if (!valid)
        {
            return LoginPage.Shell(instanceName, "Invite", """
                <form onsubmit="return false">
                  <div class="brand"><img src="../logo.svg" alt="" /> <span>Issu<span class="arr">arr</span></span></div>
                  <div class="error">This invite link has expired or was already used. Ask whoever sent it for a new one.</div>
                  <button type="button" onclick="location.href='../login'">Go to login</button>
                </form>
                """, "../");
        }

        return LoginPage.Shell(instanceName, "Join", $$"""
            <form method="post">
              <div class="brand"><img src="../logo.svg" alt="" /> <span>Issu<span class="arr">arr</span></span></div>
              <p class="lead">You've been invited to <b>{{enc.Encode(instanceName)}}</b>. Choose a username and password to read the library.</p>
              {{(error != null ? $"<div class=\"error\">{enc.Encode(error)}</div>" : "")}}
              <label for="username">Username</label>
              <input id="username" name="username" type="text" autocomplete="username" value="{{enc.Encode(username ?? "")}}" autofocus required minlength="2" maxlength="40" />
              <label for="password">Password</label>
              <input id="password" name="password" type="password" autocomplete="new-password" required minlength="6" />
              <label for="confirm">Confirm password</label>
              <input id="confirm" name="confirm" type="password" autocomplete="new-password" required minlength="6" />
              <button type="submit">Create account</button>
            </form>
            """, "../");
    }
}
