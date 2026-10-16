using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PKForge.Sync.Protocol;
using PKForge.Sync.Server.Data;
using PKForge.Sync.Server.Email;

namespace PKForge.Sync.Server.Http;

public static class ServerInfoEndpoints
{
    public static void MapServerInfoEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/healthz", () => Results.Text("ok"));
        app.MapGet("/v1/server-info", async (IOptions<SyncOptions> options, SyncDbContext db, IEmailSender email, CancellationToken ct) => new ServerInfo(
            SyncProtocol.MinSupportedVersion, SyncProtocol.Version, options.Value.DisplayName,
            RegistrationOpen: options.Value.AllowRegistration || !await db.Accounts.AnyAsync(ct),
            EmailEnabled: email.IsEnabled,
            EmailVerificationRequired: options.Value.RequireEmailVerification));
    }
}
