using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PKForge.Sync.Protocol;
using PKForge.Sync.Server.Auth;
using PKForge.Sync.Server.Data;
using PKForge.Sync.Server.Http;

namespace PKForge.Sync.Server.Admin;

/// <summary>Server admin tools for the web's Account page. Every call checks that the signed-in account is an admin.</summary>
public static class AdminEndpoints
{
    public static void MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/v1/admin").RequireAuthorization()
            .AddEndpointFilter<RequireVerifiedEmailFilter>()
            .AddEndpointFilter<RequireAdminFilter>();

        admin.MapGet("/settings", async (ServerSettingsStore settings, CancellationToken ct) =>
            new AdminSettings(await settings.AllowRegistrationAsync(ct)));

        admin.MapPut("/settings", async (AdminSettings request, ServerSettingsStore settings, CancellationToken ct) =>
        {
            await settings.SetAllowRegistrationAsync(request.AllowRegistration, ct);
            return new AdminSettings(await settings.AllowRegistrationAsync(ct));
        });

        admin.MapGet("/users", (AdminService admins, CancellationToken ct) => admins.ListUsersAsync(AdminService.MaxListedUsers, ct));

        admin.MapPost("/users", (AdminCreateUserRequest request, AdminService admins, CancellationToken ct) =>
            admins.CreateUserAsync(request.Email, request.IsAdmin, ct));

        admin.MapPost("/users/{id:guid}/reset-password", async (Guid id, AdminResetPasswordRequest request, AdminService admins, CancellationToken ct) =>
            new AdminPasswordResponse(await admins.ResetPasswordAsync(id, request.SignOutDevices, ct)));

        admin.MapPut("/users/{id:guid}/admin", async (Guid id, AdminSetRoleRequest request, ClaimsPrincipal user, AdminService admins, CancellationToken ct) =>
        {
            await admins.SetAdminAsync(user.AccountId(), id, request.IsAdmin, ct);
            return await admins.GetUserAsync(id, ct);
        });

        admin.MapPost("/users/{id:guid}/sign-out", async (Guid id, AdminService admins, CancellationToken ct) =>
        {
            await admins.SignOutDevicesAsync(id, ct);
            return Results.NoContent();
        });

        admin.MapDelete("/users/{id:guid}", async (Guid id, [FromBody] AdminDeleteUserRequest request, ClaimsPrincipal user, AdminService admins, CancellationToken ct) =>
        {
            await admins.DeleteUserAsync(user.AccountId(), id, request.Email, ct);
            return Results.NoContent();
        });
    }
}

/// <summary>403 admin_required unless the signed-in account is an admin (checked on every call, not cached).</summary>
public sealed class RequireAdminFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        var accountId = http.User.AccountId();
        var db = http.RequestServices.GetRequiredService<SyncDbContext>();
        if (!await db.Accounts.AnyAsync(a => a.Id == accountId && a.IsAdmin, http.RequestAborted))
            throw new ApiException(StatusCodes.Status403Forbidden, ErrorCodes.AdminRequired, "Only server admins can do this.");
        return await next(context);
    }
}
