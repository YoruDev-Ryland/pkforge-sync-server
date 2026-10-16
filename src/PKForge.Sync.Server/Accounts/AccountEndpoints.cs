using System.Security.Claims;
using PKForge.Sync.Protocol;
using PKForge.Sync.Server.Auth;
using PKForge.Sync.Server.Http;

namespace PKForge.Sync.Server.Accounts;

public static class AccountEndpoints
{
    public static void MapAccountEndpoints(this IEndpointRouteBuilder app)
    {
        var open = app.MapGroup("/v1");
        open.MapPost("/accounts", (RegisterRequest request, AccountService accounts, CancellationToken ct) =>
            accounts.RegisterAsync(request, ct)).RequireRateLimiting(RateLimits.Auth);
        open.MapPost("/auth/login", (LoginRequest request, AccountService accounts, CancellationToken ct) =>
            accounts.LoginAsync(request, ct)).RequireRateLimiting(RateLimits.Auth);
        open.MapPost("/auth/verify-email", async (VerifyEmailRequest request, AccountService accounts, CancellationToken ct) =>
            await accounts.VerifyEmailAsync(request.Token, ct)
                ? Results.NoContent()
                : throw ApiException.BadRequest(ErrorCodes.InvalidToken, "This link is invalid or has expired."));
        open.MapPost("/auth/password-reset", async (PasswordResetRequest request, AccountService accounts, CancellationToken ct) =>
        {
            await accounts.RequestPasswordResetAsync(request.Email, ct);
            return Results.Accepted();
        }).RequireRateLimiting(RateLimits.Auth);
        open.MapPost("/auth/password-reset/confirm", async (PasswordResetConfirmRequest request, AccountService accounts, CancellationToken ct) =>
            await accounts.ConfirmPasswordResetAsync(request.Token, request.NewPassword, ct)
                ? Results.NoContent()
                : throw ApiException.BadRequest(ErrorCodes.InvalidToken, "This link is invalid or has expired.")).RequireRateLimiting(RateLimits.Auth);

        var signedIn = app.MapGroup("/v1").RequireAuthorization();
        signedIn.MapPost("/auth/password", async (ChangePasswordRequest request, ClaimsPrincipal user, AccountService accounts, CancellationToken ct) =>
        {
            await accounts.ChangePasswordAsync(user.AccountId(), user.DeviceId(), request, ct);
            return Results.NoContent();
        }).RequireRateLimiting(RateLimits.Auth);

        signedIn.MapPost("/auth/resend-verification", async (ClaimsPrincipal user, AccountService accounts, CancellationToken ct) =>
        {
            await accounts.ResendVerificationAsync(user.AccountId(), ct);
            return Results.NoContent();
        }).RequireRateLimiting(RateLimits.Auth);
    }
}
