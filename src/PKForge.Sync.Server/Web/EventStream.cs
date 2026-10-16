using System.Security.Claims;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PKForge.Sync.Server.Auth;
using PKForge.Sync.Server.Data;

namespace PKForge.Sync.Server.Web;

/// <summary>
/// <c>GET /v1/sync/events</c>: a server-sent event stream of <see cref="AccountEvents"/> for the signed-in device.
/// Starts with <c>ready</c> (the account's version), sends a <c>: ping</c> comment every heartbeat interval, and ends
/// when the client goes away, the device is removed or the server stops.
/// </summary>
public static class EventStream
{
    public static async Task RunAsync(HttpContext http, ClaimsPrincipal user, AccountEvents events, SyncDbContext db,
        IOptions<SyncOptions> options, TimeProvider time, IHostApplicationLifetime lifetime)
    {
        var accountId = user.AccountId();
        var deviceId = user.DeviceId();
        // Subscribe before reading the version, so nothing stored in between is missed.
        using var subscription = events.Subscribe(accountId, deviceId);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(http.RequestAborted, lifetime.ApplicationStopping);
        var ct = stop.Token;
        try
        {
            // The device may have been removed between authentication and subscribing (its device-removed went nowhere).
            var state = await db.Accounts.AsNoTracking().Where(a => a.Id == accountId)
                .Select(a => new { a.Version, DeviceExists = db.Devices.Any(d => d.Id == deviceId) })
                .SingleOrDefaultAsync(ct);

            http.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();
            var response = http.Response;
            response.ContentType = "text/event-stream";
            response.Headers.CacheControl = "no-cache, no-transform";
            response.Headers["X-Accel-Buffering"] = "no";
            // HTTP/1.1 only: the header is forbidden (and dropped) on HTTP/2 and later.
            if (HttpProtocol.IsHttp11(http.Request.Protocol)) response.Headers.Connection = "keep-alive";

            // The heartbeat timer starts before ready, so a client that has seen ready can count on the next ping.
            using var heartbeat = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(1, options.Value.EventHeartbeatSeconds)), time);
            if (state is not { DeviceExists: true })
            {
                await WriteAsync(response, new AccountEvent(AccountEvent.DeviceRemoved, "{}"), ct);
                return;
            }
            await WriteAsync(response, new AccountEvent(AccountEvent.Ready, $"{{\"version\":{state.Version}}}"), ct);

            var reader = subscription.Reader;
            var tick = heartbeat.WaitForNextTickAsync(ct).AsTask();
            var next = reader.WaitToReadAsync(ct).AsTask();
            while (true)
            {
                if (await Task.WhenAny(tick, next) == tick)
                {
                    if (!await tick) return;
                    await response.WriteAsync(": ping\n\n", ct);
                    await response.Body.FlushAsync(ct);
                    tick = heartbeat.WaitForNextTickAsync(ct).AsTask();
                    continue;
                }
                if (!await next) return; // completed: the device was removed
                while (reader.TryRead(out var e)) await WriteAsync(response, e, ct);
                next = reader.WaitToReadAsync(ct).AsTask();
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // The client went away or the server is stopping.
        }
    }

    private static async Task WriteAsync(HttpResponse response, AccountEvent e, CancellationToken ct)
    {
        await response.WriteAsync($"event: {e.Kind}\ndata: {e.Data}\n\n", ct);
        await response.Body.FlushAsync(ct);
    }
}
