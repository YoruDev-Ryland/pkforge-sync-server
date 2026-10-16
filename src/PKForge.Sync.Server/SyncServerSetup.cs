using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using PKForge.Sync.Protocol;
using PKForge.Sync.Server.Accounts;
using PKForge.Sync.Server.Auth;
using PKForge.Sync.Server.Blobs;
using PKForge.Sync.Server.Data;
using PKForge.Sync.Server.Email;
using PKForge.Sync.Server.Http;
using PKForge.Sync.Server.Pairing;
using PKForge.Sync.Server.Sync;
using PKForge.Sync.Server.Web;
using PKForge.Sync.Server.Web.Details;

namespace PKForge.Sync.Server;

public static class SyncServerSetup
{
    public static IServiceCollection AddSyncServer(this IServiceCollection services)
    {
        services.AddOptions<SyncOptions>().BindConfiguration(SyncOptions.Section)
            .Validate(o => o.ReleasedRetentionDays >= 0 && o.ReleasedRetentionDays <= o.TombstoneRetentionDays,
                "Sync:ReleasedRetentionDays (RELEASED_RETENTION_DAYS) must be from 0 to Sync:TombstoneRetentionDays.")
            .ValidateOnStart();
        services.TryAddSingleton(TimeProvider.System);
        services.Configure<JsonOptions>(o => ProtocolJson.Configure(o.SerializerOptions));
        // Malformed bodies throw BadHttpRequestException, which ApiExceptionMiddleware turns into an ErrorResponse.
        services.Configure<RouteHandlerOptions>(o => o.ThrowOnBadRequest = true);
        services.AddSyncData();
        services.AddDeviceKeyAuthentication();
        services.AddSyncRateLimiting();
        services.AddMemoryCache();
        services.AddSingleton<DetailsCache>();
        services.AddScoped<AccountService>();
        services.AddScoped<PairingService>();
        services.AddSingleton<IBlobStore>(sp =>
            new FileSystemBlobStore(Path.Combine(sp.GetRequiredService<IOptions<SyncOptions>>().Value.DataDirectory, "blobs")));
        services.AddScoped<BlobService>();
        services.AddSingleton<AccountLocks>();
        services.AddSingleton<AccountEvents>();
        services.TryAddSingleton<IAccountWritePolicy, AllowAllWrites>();
        services.AddScoped<SyncService>();
        services.AddScoped<TombstonePruner>();
        services.AddHostedService<PruneBackgroundService>();
        services.AddSingleton<IEmailSender>(sp => sp.GetRequiredService<IOptions<SyncOptions>>().Value.Smtp is { Enabled: true } smtp
            ? new SmtpEmailSender(smtp)
            : new DisabledEmailSender());
        return services;
    }

    public static WebApplication UseSyncServer(this WebApplication app)
    {
        if (app.Services.GetRequiredService<IOptions<SyncOptions>>().Value.TrustForwardedHeaders)
        {
            var forwarded = new ForwardedHeadersOptions { ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto };
            forwarded.KnownIPNetworks.Clear();
            forwarded.KnownProxies.Clear();
            app.UseForwardedHeaders(forwarded);
        }
        app.UseMiddleware<ApiExceptionMiddleware>();
        app.UseWebApp();
        app.UseMiddleware<ProtocolVersionMiddleware>();
        app.UseRateLimiter();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapServerInfoEndpoints();
        app.MapAccountEndpoints();
        app.MapDeviceEndpoints();
        app.MapAccountPages();
        app.MapPairingEndpoints();
        app.MapWebSessionEndpoints();
        app.MapPreferencesEndpoints();
        app.MapDetailsEndpoints();
        app.MapReleasedEndpoints();
        app.MapNamesEndpoints();
        app.MapBlobEndpoints();
        app.MapSyncEndpoints();
        app.MapAccountManagementEndpoints();
        return app;
    }
}
