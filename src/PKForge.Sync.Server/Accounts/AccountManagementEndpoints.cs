using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using PKForge.Sync.Protocol;
using PKForge.Sync.Server.Auth;

namespace PKForge.Sync.Server.Accounts;

public static class AccountManagementEndpoints
{
    public static void MapAccountManagementEndpoints(this IEndpointRouteBuilder app)
    {
        var account = app.MapGroup("/v1/account").RequireAuthorization();

        account.MapDelete("", async ([FromBody] DeleteAccountRequest request, ClaimsPrincipal user, AccountService accounts, CancellationToken ct) =>
        {
            await accounts.DeleteAccountAsync(user.AccountId(), request.Password, ct);
            return Results.NoContent();
        });
    }
}
