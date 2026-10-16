using System.Text.RegularExpressions;
using PKForge.Sync.Server.Email;

namespace PKForge.Sync.Server.Tests.Infrastructure;

public sealed record SentEmail(string To, string Subject, string Body);

public sealed partial class CapturingEmailSender : IEmailSender
{
    public bool Enabled { get; set; } = true;
    public bool IsEnabled => Enabled;
    public List<SentEmail> Sent { get; } = [];

    public Task SendAsync(string to, string subject, string body, CancellationToken ct)
    {
        lock (Sent) Sent.Add(new SentEmail(to, subject, body));
        return Task.CompletedTask;
    }

    /// <summary>The token of the last link sent (…?token=X).</summary>
    public string LastToken() => Uri.UnescapeDataString(TokenPattern().Match(Sent[^1].Body).Groups[1].Value);

    [GeneratedRegex(@"token=([^\s]+)")]
    private static partial Regex TokenPattern();
}
