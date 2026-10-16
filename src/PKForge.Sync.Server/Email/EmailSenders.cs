using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace PKForge.Sync.Server.Email;

public interface IEmailSender
{
    /// <summary>False when the server has no way to send email (self-hosted without SMTP).</summary>
    bool IsEnabled { get; }
    Task SendAsync(string to, string subject, string body, CancellationToken ct);
}

public sealed class DisabledEmailSender : IEmailSender
{
    public bool IsEnabled => false;
    public Task SendAsync(string to, string subject, string body, CancellationToken ct) => Task.CompletedTask;
}

public sealed class SmtpEmailSender(SmtpOptions smtp) : IEmailSender
{
    public bool IsEnabled => true;

    public async Task SendAsync(string to, string subject, string body, CancellationToken ct)
    {
        var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(smtp.From));
        message.To.Add(MailboxAddress.Parse(to));
        message.Subject = subject;
        message.Body = new TextPart("plain") { Text = body };

        using var client = new SmtpClient();
        await client.ConnectAsync(smtp.Host!, smtp.Port, SecureSocketOptions.Auto, ct);
        if (!string.IsNullOrEmpty(smtp.Username)) await client.AuthenticateAsync(smtp.Username, smtp.Password ?? "", ct);
        await client.SendAsync(message, ct);
        await client.DisconnectAsync(quit: true, ct);
    }
}

public static class EmailTemplates
{
    public static string Verify(string link) =>
        $"Confirm the email address of your PKForge Sync account by opening this link:\n\n{link}\n\n" +
        "The link works for 7 days. If you didn't create an account, ignore this email.";

    public static string Reset(string link) =>
        $"Someone asked to reset the password of your PKForge Sync account. To choose a new password, open this link:\n\n{link}\n\n" +
        "The link works for 1 hour. If it wasn't you, ignore this email: your password stays the same.";
}
