using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PKForge.Sync.Server.Data;

namespace PKForge.Sync.Server.Auth;

public static class DeviceKeyDefaults
{
    public const string Scheme = "DeviceKey";
    public const string AccountClaim = "pkforge:account";
    public const string DeviceClaim = "pkforge:device";

    public static IServiceCollection AddDeviceKeyAuthentication(this IServiceCollection services)
    {
        services.AddAuthentication(Scheme).AddScheme<AuthenticationSchemeOptions, DeviceKeyHandler>(Scheme, null);
        services.AddAuthorization();
        return services;
    }

    public static Guid AccountId(this ClaimsPrincipal user) => Guid.Parse(user.FindFirstValue(AccountClaim)!);
    public static Guid DeviceId(this ClaimsPrincipal user) => Guid.Parse(user.FindFirstValue(DeviceClaim)!);
}

/// <summary>Authenticates "Authorization: Bearer &lt;device key&gt;", or the web's pkf_device cookie when no header is sent.</summary>
public sealed class DeviceKeyHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder, SyncDbContext db)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        string key;
        if (header.StartsWith("Bearer ", StringComparison.Ordinal)) key = header["Bearer ".Length..].Trim();
        else if (header.Length == 0 && Request.Cookies.TryGetValue(Web.WebSession.CookieName, out var cookie) && cookie.Length > 0) key = cookie;
        else return AuthenticateResult.NoResult();

        var hash = Secrets.Hash(key);
        var device = await db.Devices.AsNoTracking()
            .Where(d => d.KeyHash == hash)
            .Select(d => new { d.Id, d.AccountId })
            .SingleOrDefaultAsync(Context.RequestAborted);
        if (device is null) return AuthenticateResult.Fail("Unknown device key.");

        var identity = new ClaimsIdentity(
            [new Claim(DeviceKeyDefaults.AccountClaim, device.AccountId.ToString()), new Claim(DeviceKeyDefaults.DeviceClaim, device.Id.ToString())],
            DeviceKeyDefaults.Scheme);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), DeviceKeyDefaults.Scheme));
    }
}
