using Microsoft.EntityFrameworkCore;
using PKForge.Sync.Server.Accounts;
using PKForge.Sync.Server.Auth;
using PKForge.Sync.Server.Data;
using PKForge.Sync.Server.Http;

namespace PKForge.Sync.Server.Cli;

public sealed record AdminCommand(string Name, IReadOnlyList<string> Args);

/// <summary>Server-owner commands, run as `docker exec pkforge-sync &lt;command&gt;`.</summary>
public static class AdminCli
{
    private static readonly string[] Commands = ["create-user", "reset-password", "list-users", "force-resync", "help"];

    private const string Usage = """
        PKForge Sync admin commands:
          create-user <email> [--admin]   create an account and print its password
          reset-password <email>          set a new random password and print it
          list-users                      list every account
          force-resync <email>|--all      make devices re-download their Bank (run after restoring a backup)
        """;

    public static AdminCommand? Parse(string[] args) =>
        args.Length > 0 && Commands.Contains(args[0]) ? new AdminCommand(args[0], args[1..]) : null;

    public static async Task<int> RunAsync(IServiceProvider services, AdminCommand command, TextWriter output)
    {
        using var scope = services.CreateScope();
        var accounts = scope.ServiceProvider.GetRequiredService<AccountService>();
        var db = scope.ServiceProvider.GetRequiredService<SyncDbContext>();
        try
        {
            switch (command.Name)
            {
                case "create-user" when command.Args.Count >= 1:
                {
                    var password = NewPassword();
                    var account = await accounts.CreateAccountAsync(command.Args[0], password, isAdmin: command.Args.Contains("--admin"), emailVerified: true, CancellationToken.None);
                    output.WriteLine($"Created {account.Email}{(account.IsAdmin ? " (admin)" : "")}.");
                    output.WriteLine($"Password: {password}");
                    return 0;
                }
                case "reset-password" when command.Args.Count == 1:
                {
                    var normalized = AccountService.NormalizeEmail(command.Args[0]);
                    var account = await db.Accounts.AsNoTracking().SingleOrDefaultAsync(a => a.NormalizedEmail == normalized);
                    if (account is null)
                    {
                        output.WriteLine($"No account uses {command.Args[0]}.");
                        return 1;
                    }
                    var password = NewPassword();
                    await accounts.SetPasswordAsync(account.Id, password, CancellationToken.None);
                    output.WriteLine($"New password for {account.Email}.");
                    output.WriteLine($"Password: {password}");
                    return 0;
                }
                case "list-users":
                {
                    var rows = await db.Accounts.AsNoTracking().OrderBy(a => a.CreatedUtc)
                        .Select(a => new { a.Email, a.IsAdmin, a.EmailVerified, a.CreatedUtc, Devices = db.Devices.Count(d => d.AccountId == a.Id) })
                        .ToListAsync();
                    foreach (var row in rows)
                        output.WriteLine($"{row.Email}\t{(row.IsAdmin ? "admin" : "user")}\t{(row.EmailVerified ? "verified" : "unverified")}\t{row.Devices} devices\tsince {row.CreatedUtc:yyyy-MM-dd}");
                    output.WriteLine($"{rows.Count} accounts.");
                    return 0;
                }
                case "force-resync" when command.Args.Count == 1:
                {
                    var all = command.Args[0] == "--all";
                    var normalized = AccountService.NormalizeEmail(command.Args[0]);
                    var accountIds = await db.Accounts.AsNoTracking()
                        .Where(a => all || a.NormalizedEmail == normalized).Select(a => a.Id).ToListAsync();
                    if (accountIds.Count == 0)
                    {
                        output.WriteLine($"No account uses {command.Args[0]}.");
                        return 1;
                    }
                    foreach (var id in accountIds)
                    {
                        var epoch = Account.NewEpoch();
                        await db.Accounts.Where(a => a.Id == id).ExecuteUpdateAsync(s => s.SetProperty(a => a.Epoch, epoch));
                    }
                    output.WriteLine($"{accountIds.Count} Bank(s) will be re-downloaded by their devices at the next sync.");
                    return 0;
                }
                default:
                    output.WriteLine(Usage);
                    return command.Name == "help" ? 0 : 2;
            }
        }
        catch (ApiException e)
        {
            output.WriteLine(e.Message);
            return 1;
        }
    }

    private static string NewPassword() => Secrets.NewKey()[..16];
}
