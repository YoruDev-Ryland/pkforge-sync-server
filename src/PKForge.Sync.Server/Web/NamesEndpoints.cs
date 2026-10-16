using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;
using PKHeX.Core;
using PKForge.Sync.Protocol;

namespace PKForge.Sync.Server.Web;

public static class NamesEndpoints
{
    private static readonly Lazy<(byte[] Json, string ETag)> Payload = new(() =>
    {
        var s = GameInfo.GetStrings("en");
        var version = typeof(PKM).Assembly.GetName().Version?.ToString() ?? "0";
        var json = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new
        {
            species = s.specieslist, items = s.itemlist, moves = s.movelist, abilities = s.abilitylist,
            natures = s.natures, types = s.types, balls = s.balllist, version,
        }, ProtocolJson.Options);
        return (json, $"\"pkhex-{version}\"");
    });

    public static void MapNamesEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/v1/web/names", (HttpContext http) =>
        {
            var (json, etag) = Payload.Value;
            http.Response.Headers.ETag = etag;
            http.Response.Headers.CacheControl = "private, max-age=86400";
            if (http.Request.Headers.IfNoneMatch.ToString() == etag) return Results.StatusCode(StatusCodes.Status304NotModified);
            return Results.Bytes(json, "application/json");
        }).RequireAuthorization();
    }
}
