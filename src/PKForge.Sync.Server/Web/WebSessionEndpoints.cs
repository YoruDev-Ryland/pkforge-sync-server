using System.Globalization;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PKForge.Sync.Protocol;
using PKForge.Sync.Server.Accounts;
using PKForge.Sync.Server.Auth;
using PKForge.Sync.Server.Data;
using PKForge.Sync.Server.Http;
using PKForge.Sync.Server.Pairing;

namespace PKForge.Sync.Server.Web;

/// <summary>The browser's sign-in: a normal device whose key lives in an HttpOnly cookie.</summary>
public static class WebSession
{
    public const string CookieName = "pkf_device";
    private static readonly TimeSpan Lifetime = TimeSpan.FromDays(400);
    private const int MaxNameLength = 100;

    public static void MapWebSessionEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/v1/web/session", async (WebSessionRequest request, HttpContext http, AccountService accounts, PairingService pairing,
            IOptions<SyncOptions> options, CancellationToken ct) =>
        {
            var name = DeviceName(request.DeviceName);
            var key = request.PairingCode is { Length: > 0 } code
                ? await pairing.RedeemAsync(new RedeemPairingRequest(code, name), ct)
                : await accounts.LoginAsync(new LoginRequest(request.Email ?? "", request.Password ?? "", name), ct);
            http.Response.Cookies.Append(CookieName, key.DeviceKey, Cookie(http, options.Value, DateTimeOffset.UtcNow.Add(Lifetime)));
            return new WebSessionResponse(key.AccountId, key.DeviceId, key.EmailVerified);
        }).RequireRateLimiting(RateLimits.Auth);

        // Sign-up from the web: creates the account (same rules as POST /v1/accounts) with this browser as its first device.
        app.MapPost("/v1/web/register", async (RegisterRequest request, HttpContext http, AccountService accounts,
            IOptions<SyncOptions> options, CancellationToken ct) =>
        {
            var key = await accounts.RegisterAsync(request with { DeviceName = DeviceName(request.DeviceName) }, ct);
            http.Response.Cookies.Append(CookieName, key.DeviceKey, Cookie(http, options.Value, DateTimeOffset.UtcNow.Add(Lifetime)));
            return new WebSessionResponse(key.AccountId, key.DeviceId, key.EmailVerified);
        }).RequireRateLimiting(RateLimits.Auth);

        app.MapDelete("/v1/web/session", async (ClaimsPrincipal user, HttpContext http, SyncDbContext db, IOptions<SyncOptions> options,
            AccountEvents events, CancellationToken ct) =>
        {
            var deviceId = user.DeviceId();
            await db.Devices.Where(d => d.Id == deviceId).ExecuteDeleteAsync(ct);
            events.DevicesRemoved(user.AccountId(), [deviceId]);
            http.Response.Cookies.Delete(CookieName, Cookie(http, options.Value, DateTimeOffset.UnixEpoch));
            return Results.NoContent();
        }).RequireAuthorization().RequireRateLimiting(RateLimits.Auth);
    }

    /// <summary>The device's name, trimmed and at most 100 UTF-16 units, cut between whole characters
    /// (text elements) so an emoji or accent is never split into invalid text.</summary>
    public static string DeviceName(string? requested)
    {
        var name = string.IsNullOrWhiteSpace(requested) ? "Web browser" : requested.Trim();
        if (name.Length <= MaxNameLength) return name;
        var end = 0;
        var elements = StringInfo.GetTextElementEnumerator(name);
        while (elements.MoveNext() && elements.ElementIndex + ((string)elements.Current).Length <= MaxNameLength)
            end = elements.ElementIndex + ((string)elements.Current).Length;
        return name[..end].TrimEnd();
    }

    private static CookieOptions Cookie(HttpContext http, SyncOptions options, DateTimeOffset expires) => new()
    {
        HttpOnly = true,
        SameSite = SameSiteMode.Strict,
        Path = "/",
        Secure = http.Request.IsHttps || options.PublicBaseUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase),
        Expires = expires,
        IsEssential = true,
    };
}
