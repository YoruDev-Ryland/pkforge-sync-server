using System.Security.Claims;
using PKForge.Sync.Protocol;
using PKForge.Sync.Server.Auth;
using PKForge.Sync.Server.Http;

namespace PKForge.Sync.Server.Pairing;

public static class PairingEndpoints
{
    public static void MapPairingEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/v1/pairing", (ClaimsPrincipal user, PairingService pairing, CancellationToken ct) =>
                pairing.CreateAsync(user.AccountId(), ct))
            .RequireAuthorization()
            .AddEndpointFilter<RequireVerifiedEmailFilter>();

        app.MapPost("/v1/pairing/redeem", (RedeemPairingRequest request, PairingService pairing, CancellationToken ct) =>
                pairing.RedeemAsync(request, ct))
            .RequireRateLimiting(RateLimits.Auth);
    }
}
