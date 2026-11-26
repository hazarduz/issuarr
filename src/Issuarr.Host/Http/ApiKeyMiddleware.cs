using System.Security.Cryptography;
using System.Text;
using Issuarr.Core.Configuration;

namespace Issuarr.Host.Http;

/// <summary>
/// Requires the API key on every /api request, the same way the other *arr apps do:
/// either an <c>X-Api-Key</c> header or an <c>apikey</c> query string parameter.
/// </summary>
public class ApiKeyMiddleware(RequestDelegate next, ConfigFile config)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Path.StartsWithSegments("/api"))
        {
            var provided = context.Request.Headers["X-Api-Key"].FirstOrDefault()
                           ?? context.Request.Query["apikey"].FirstOrDefault();

            if (provided == null || !FixedTimeEquals(provided, config.ApiKey))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(new { message = "Unauthorized. Supply X-Api-Key header or apikey query parameter." });
                return;
            }
        }

        await next(context);
    }

    private static bool FixedTimeEquals(string a, string b) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));
}
