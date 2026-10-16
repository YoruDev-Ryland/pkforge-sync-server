using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using PKForge.Sync.Protocol;
using PKForge.Sync.Server.Email;

namespace PKForge.Sync.Server.Tests.Infrastructure;

/// <summary>A server on a private temp directory, a fake clock and test-friendly settings.</summary>
public sealed class SyncServerFactory : WebApplicationFactory<Program>
{
    public string DataDirectory { get; } = Path.Combine(Path.GetTempPath(), "pkforge-sync-tests", Guid.NewGuid().ToString("N"));

    /// <summary>Configuration overrides; change before the first client is created.</summary>
    public Dictionary<string, string?> Settings { get; } = new()
    {
        ["Sync:AllowRegistration"] = "true",
        ["Sync:AuthRequestsPerMinute"] = "10000",
        ["Sync:EnablePruning"] = "false",
        ["Sync:PublicBaseUrl"] = "https://sync.test",
    };

    public CapturingEmailSender Email { get; } = new();

    public FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 10, 14, 12, 0, 0, TimeSpan.Zero));

    /// <summary>Extra service overrides, applied after the app's own registrations.</summary>
    public Action<IServiceCollection>? ServiceOverrides { get; set; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Sync:DataDirectory", DataDirectory);
        foreach (var (key, value) in Settings) builder.UseSetting(key, value);
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Time);
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender>(Email);
            ServiceOverrides?.Invoke(services);
        });
    }

    /// <summary>An anonymous client that speaks the current protocol.</summary>
    public HttpClient Client()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add(SyncProtocol.VersionHeader, SyncProtocol.Version.ToString());
        return client;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(DataDirectory, recursive: true); }
        catch (DirectoryNotFoundException) { }
        catch (IOException) { }
    }
}
