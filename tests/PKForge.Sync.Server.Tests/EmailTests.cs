using System.Net;
using PKForge.Sync.Protocol;
using PKForge.Sync.Server.Tests.Infrastructure;

namespace PKForge.Sync.Server.Tests;

public sealed class EmailTests
{
    [Fact]
    public async Task SignUpSendsAVerificationLinkThatVerifies()
    {
        using var server = new SyncServerFactory();
        var (_, key) = await server.RegisterAsync();

        Assert.False(key.EmailVerified);
        var email = Assert.Single(server.Email.Sent);
        Assert.Equal("ash@example.com", email.To);
        Assert.Contains("https://sync.test/verify-email?token=", email.Body);

        var verified = await server.Client().PostJsonAsync("/v1/auth/verify-email", new VerifyEmailRequest(server.Email.LastToken()));
        var login = await server.Client().PostJsonAsync("/v1/auth/login", new LoginRequest("ash@example.com", TestApi.Password, "Deck"));

        Assert.Equal(HttpStatusCode.NoContent, verified.StatusCode);
        Assert.True((await login.ReadAsync<DeviceKeyResponse>()).EmailVerified);
    }

    [Fact]
    public async Task VerificationPageConfirmsTheEmail()
    {
        using var server = new SyncServerFactory();
        await server.RegisterAsync();

        var page = await server.CreateClient().GetStringAsync($"/verify-email?token={Uri.EscapeDataString(server.Email.LastToken())}");
        var again = await server.CreateClient().GetStringAsync($"/verify-email?token={Uri.EscapeDataString(server.Email.LastToken())}");

        Assert.Contains("Your email is confirmed", page);
        Assert.Contains("invalid or has expired", again);
    }

    [Fact]
    public async Task ResendSendsANewLink()
    {
        using var server = new SyncServerFactory();
        var (client, _) = await server.RegisterAsync();

        var response = await client.PostAsync("/v1/auth/resend-verification", null);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(2, server.Email.Sent.Count);
    }

    [Fact]
    public async Task PasswordResetChangesThePasswordOnce()
    {
        using var server = new SyncServerFactory();
        await server.RegisterAsync();

        var request = await server.Client().PostJsonAsync("/v1/auth/password-reset", new PasswordResetRequest("ASH@example.com"));
        var token = server.Email.LastToken();
        var confirm = await server.Client().PostJsonAsync("/v1/auth/password-reset/confirm", new PasswordResetConfirmRequest(token, "squirtle77"));
        var reuse = await server.Client().PostJsonAsync("/v1/auth/password-reset/confirm", new PasswordResetConfirmRequest(token, "bulbasaur88"));
        var login = await server.Client().PostJsonAsync("/v1/auth/login", new LoginRequest("ash@example.com", "squirtle77", "Deck"));

        Assert.Equal(HttpStatusCode.Accepted, request.StatusCode);
        Assert.Contains("https://sync.test/reset-password?token=", server.Email.Sent[^1].Body);
        Assert.Equal(HttpStatusCode.NoContent, confirm.StatusCode);
        Assert.Equal(ErrorCodes.InvalidToken, (await reuse.ReadErrorAsync()).Code);
        login.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task ResetLinksExpireAfterAnHour()
    {
        using var server = new SyncServerFactory();
        await server.RegisterAsync();
        await server.Client().PostJsonAsync("/v1/auth/password-reset", new PasswordResetRequest("ash@example.com"));

        server.Time.Advance(TimeSpan.FromMinutes(61));
        var confirm = await server.Client().PostJsonAsync("/v1/auth/password-reset/confirm", new PasswordResetConfirmRequest(server.Email.LastToken(), "squirtle77"));

        Assert.Equal(HttpStatusCode.BadRequest, confirm.StatusCode);
        Assert.Equal(ErrorCodes.InvalidToken, (await confirm.ReadErrorAsync()).Code);
    }

    [Fact]
    public async Task ResetForUnknownEmailLooksTheSame()
    {
        using var server = new SyncServerFactory();
        await server.RegisterAsync();
        var sent = server.Email.Sent.Count;

        var response = await server.Client().PostJsonAsync("/v1/auth/password-reset", new PasswordResetRequest("nobody@example.com"));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal(sent, server.Email.Sent.Count);
    }

    [Fact]
    public async Task ResetPageChangesThePassword()
    {
        using var server = new SyncServerFactory();
        await server.RegisterAsync();
        await server.Client().PostJsonAsync("/v1/auth/password-reset", new PasswordResetRequest("ash@example.com"));
        var token = server.Email.LastToken();
        var browser = server.CreateClient();

        var form = await browser.GetStringAsync($"/reset-password?token={Uri.EscapeDataString(token)}");
        var mismatch = await browser.PostAsync("/reset-password", new FormUrlEncodedContent(
            new Dictionary<string, string> { ["token"] = token, ["password"] = "squirtle77", ["confirm"] = "different1" }));
        var done = await browser.PostAsync("/reset-password", new FormUrlEncodedContent(
            new Dictionary<string, string> { ["token"] = token, ["password"] = "squirtle77", ["confirm"] = "squirtle77" }));

        Assert.Contains("<form", form);
        Assert.Contains("don't match", WebUtility.HtmlDecode(await mismatch.Content.ReadAsStringAsync()));
        Assert.Contains("Your password was changed", await done.Content.ReadAsStringAsync());
        (await server.Client().PostJsonAsync("/v1/auth/login", new LoginRequest("ash@example.com", "squirtle77", "Deck"))).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task WithoutEmailNothingIsSentAndServerInfoSaysSo()
    {
        using var server = new SyncServerFactory();
        server.Email.Enabled = false;
        await server.RegisterAsync();
        await server.Client().PostJsonAsync("/v1/auth/password-reset", new PasswordResetRequest("ash@example.com"));

        var info = await (await server.Client().GetAsync("/v1/server-info")).ReadAsync<ServerInfo>();

        Assert.Empty(server.Email.Sent);
        Assert.False(info.EmailEnabled);
    }
}
