using PKForge.Sync.Protocol;

namespace PKForge.Sync.Server.Http;

/// <summary>Every /v1 call except server-info must name a protocol version this server speaks.</summary>
public sealed class ProtocolVersionMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path;
        if (path.StartsWithSegments("/v1") && !path.StartsWithSegments("/v1/server-info") && !Supported(context))
        {
            return ApiErrors.Write(context, StatusCodes.Status426UpgradeRequired, ErrorCodes.UnsupportedProtocol,
                $"This server speaks sync protocol {SyncProtocol.MinSupportedVersion} to {SyncProtocol.Version}. Update PKForge to keep syncing.");
        }
        return next(context);
    }

    private static bool Supported(HttpContext context) =>
        int.TryParse(context.Request.Headers[SyncProtocol.VersionHeader].ToString(), out var version)
        && version >= SyncProtocol.MinSupportedVersion && version <= SyncProtocol.Version;
}
