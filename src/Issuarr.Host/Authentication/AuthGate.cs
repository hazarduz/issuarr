using System.Net;
using System.Net.Sockets;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Issuarr.Core.Configuration;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace Issuarr.Host.Authentication;

/// <summary>
/// Decides who gets in, *arr style:
/// <list type="bullet">
/// <item>/api and /feed need the API key (header or ?apikey=).</item>
/// <item>With Forms or Basic login on, the web UI (and /initialize.json, which hands out the key) needs a login,
/// unless "Disabled for local addresses" is chosen and the request comes from the local network.</item>
/// </list>
/// </summary>
public class AuthGate(RequestDelegate next, ConfigFile config)
{
    // /kosync does its own authentication with KOReader's x-auth-user / x-auth-key headers.
    private static readonly string[] PublicPrefixes = ["/login", "/logout", "/ping", "/assets/", "/logo.svg", "/favicon", "/kosync/"];

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? "/";

        if (path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase) || path.StartsWith("/feed/", StringComparison.OrdinalIgnoreCase))
        {
            if (!HasValidApiKey(context))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(new { message = "Unauthorized. Supply the X-Api-Key header or apikey query parameter." });
                return;
            }

            await next(context);
            return;
        }

        // OPDS clients (e-readers) can't do a login page: they use HTTP Basic or ?apikey=.
        if (path.Equals("/opds", StringComparison.OrdinalIgnoreCase) || path.StartsWith("/opds/", StringComparison.OrdinalIgnoreCase))
        {
            if (IsAllowedWithoutLogin(context) || HasValidApiKey(context) || TryBasic(context))
            {
                await next(context);
                return;
            }

            context.Response.Headers.WWWAuthenticate = $"Basic realm=\"{config.InstanceName} OPDS\"";
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        if (PublicPrefixes.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase)) || IsAllowedWithoutLogin(context))
        {
            await next(context);
            return;
        }

        if (config.AuthenticationMethod == AuthenticationMethod.Basic)
        {
            if (TryBasic(context))
            {
                await next(context);
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

    private bool IsAllowedWithoutLogin(HttpContext context)
    {
        var method = config.AuthenticationMethod;
        if (method is AuthenticationMethod.None or AuthenticationMethod.External)
        {
            return true;
        }

        if (config.AuthenticationRequired == "DisabledForLocalAddresses" && IsLocal(context.Connection.RemoteIpAddress))
        {
            return true;
        }

        return method == AuthenticationMethod.Forms && context.User.Identity?.IsAuthenticated == true;
    }

    private bool HasValidApiKey(HttpContext context)
    {
        var provided = context.Request.Headers["X-Api-Key"].FirstOrDefault() ?? context.Request.Query["apikey"].FirstOrDefault();
        return provided != null && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(provided), Encoding.UTF8.GetBytes(config.ApiKey));
    }

    private bool TryBasic(HttpContext context)
    {
        var header = context.Request.Headers.Authorization.FirstOrDefault();
        if (header == null || !header.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(header[6..]));
            var sep = decoded.IndexOf(':');
            return sep > 0 && config.VerifyPassword(decoded[..sep], decoded[(sep + 1)..]);
        }
        catch (FormatException)
        {
            return false;
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
        app.MapGet("/login", (HttpContext ctx) => Results.Content(LoginPage.Render(config.InstanceName, ctx.Request.Query["returnUrl"], ctx.Request.Query["failed"] == "1"), "text/html"));

        app.MapPost("/login", async (HttpContext ctx) =>
        {
            var form = await ctx.Request.ReadFormAsync();
            var username = form["username"].ToString();
            var password = form["password"].ToString();
            var returnUrl = SafeReturnUrl(ctx, form["returnUrl"].ToString());

            if (!config.VerifyPassword(username, password))
            {
                await Task.Delay(Random.Shared.Next(400, 900)); // slow down guessing
                return Results.Redirect($"{ctx.Request.PathBase}/login?failed=1&returnUrl={Uri.EscapeDataString(returnUrl)}");
            }

            var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, config.Username)], CookieAuthenticationDefaults.AuthenticationScheme);
            await ctx.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity),
                new AuthenticationProperties { IsPersistent = form["remember"] == "on" });
            return Results.Redirect(returnUrl);
        }).DisableAntiforgery();

        app.MapGet("/logout", async (HttpContext ctx) =>
        {
            await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.Redirect($"{ctx.Request.PathBase}/login");
        });
    }

    private static string SafeReturnUrl(HttpContext ctx, string? url) =>
        !string.IsNullOrEmpty(url) && url.StartsWith('/') && !url.StartsWith("//", StringComparison.Ordinal) ? url : ctx.Request.PathBase + "/";
}

public static class LoginPage
{
    public static string Render(string instanceName, string? returnUrl, bool failed)
    {
        var enc = System.Text.Encodings.Web.HtmlEncoder.Default;
        return $$"""
        <!doctype html>
        <html lang="en">
        <head>
          <meta charset="utf-8" />
          <meta name="viewport" content="width=device-width, initial-scale=1" />
          <title>Login · {{enc.Encode(instanceName)}}</title>
          <link rel="icon" type="image/svg+xml" href="logo.svg" />
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
            .error { margin-top: 4px; padding: 10px 12px; border-radius: 8px; background: rgba(255,92,112,.14); color: #ff5c70; font-size: 13px; }
          </style>
        </head>
        <body>
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
        </body>
        </html>
        """;
    }
}
