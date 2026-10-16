using System.Security.Claims;
using PKForge.Sync.Protocol;
using PKForge.Sync.Server.Auth;
using PKForge.Sync.Server.Http;

namespace PKForge.Sync.Server.Blobs;

public static class BlobEndpoints
{
    public static void MapBlobEndpoints(this IEndpointRouteBuilder app)
    {
        var blobs = app.MapGroup("/v1/blobs").RequireAuthorization().AddEndpointFilter<RequireVerifiedEmailFilter>();

        blobs.MapPost("/missing", (MissingBlobsRequest request, ClaimsPrincipal user, BlobService service, CancellationToken ct) =>
            service.MissingAsync(user.AccountId(), request, ct));

        blobs.MapPut("/{hash}", async (string hash, HttpRequest request, ClaimsPrincipal user, BlobService service, CancellationToken ct) =>
        {
            await service.PutAsync(user.AccountId(), hash, request.Body, ct);
            return Results.NoContent();
        });

        blobs.MapGet("/{hash}", async (string hash, ClaimsPrincipal user, BlobService service, CancellationToken ct) =>
            await service.GetAsync(user.AccountId(), hash, ct) is { } data
                ? Results.Bytes(data, "application/octet-stream")
                : throw ApiException.NotFound("No such blob."));

        blobs.MapPost("/upload", async (UploadBlobsRequest request, ClaimsPrincipal user, BlobService service, CancellationToken ct) =>
        {
            await service.UploadManyAsync(user.AccountId(), request, ct);
            return Results.NoContent();
        });

        blobs.MapPost("/download", (DownloadBlobsRequest request, ClaimsPrincipal user, BlobService service, CancellationToken ct) =>
            service.DownloadManyAsync(user.AccountId(), request, ct));
    }
}
