namespace PKForge.Sync.Server.Accounts;

/// <summary>Whether an account may change its Bank. The default allows everything; a deployment can register its own.</summary>
/// <remarks>
/// Reads, changes and downloads are never blocked. When a policy's answer for an account changes, it can call
/// <see cref="Web.AccountEvents.AccountChanged"/> so the account's open web Banks let the user try again.
/// </remarks>
public interface IAccountWritePolicy
{
    Task<WriteDecision> CheckAsync(Guid accountId, CancellationToken ct);
}

/// <summary><see cref="Reason"/> is shown to the user when <see cref="Allowed"/> is false.</summary>
public sealed record WriteDecision(bool Allowed, string? Reason = null)
{
    public static readonly WriteDecision Allow = new(true);
}

public sealed class AllowAllWrites : IAccountWritePolicy
{
    public Task<WriteDecision> CheckAsync(Guid accountId, CancellationToken ct) => Task.FromResult(WriteDecision.Allow);
}
