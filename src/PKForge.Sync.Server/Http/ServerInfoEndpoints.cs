using Microsoft.Extensions.Options;
using PKForge.Sync.Protocol;
using PKForge.Sync.Server.Admin;
using PKForge.Sync.Server.Email;

namespace PKForge.Sync.Server.Http;

public static class ServerInfoEndpoints
{
    public static void MapServerInfoEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/healthz", () => Results.Text("ok"));
        app.MapGet("/v1/server-info", async (IOptions<SyncOptions> options, ServerSettingsStore settings, IEmailSender email, CancellationToken ct) => new ServerInfo(
            SyncProtocol.MinSupportedVersion, SyncProtocol.Version, options.Value.DisplayName,
            RegistrationOpen: await settings.RegistrationOpenAsync(ct),
            EmailEnabled: email.IsEnabled,
            EmailVerificationRequired: options.Value.RequireEmailVerification));
    }
}
