using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PKForge.Sync.Protocol;
using PKForge.Sync.Server.Auth;
using PKForge.Sync.Server.Data;

namespace PKForge.Sync.Server.Accounts;

public static class AccountManagementEndpoints
{
    public static void MapAccountManagementEndpoints(this IEndpointRouteBuilder app)
    {
        var account = app.MapGroup("/v1/account").RequireAuthorization();

        account.MapGet("", async (ClaimsPrincipal user, SyncDbContext db, CancellationToken ct) =>
        {
            var id = user.AccountId();
            return await db.Accounts.AsNoTracking().Where(a => a.Id == id)
                .Select(a => new AccountInfo(a.Id, a.Email, a.EmailVerified, a.IsAdmin)).SingleAsync(ct);
        });

        account.MapDelete("", async ([FromBody] DeleteAccountRequest request, ClaimsPrincipal user, AccountService accounts, CancellationToken ct) =>
        {
            await accounts.DeleteAccountAsync(user.AccountId(), request.Password, ct);
            return Results.NoContent();
        });
    }
}
