using System.Net;
using PKForge.Sync.Protocol;
using PKForge.Sync.Server.Tests.Infrastructure;

namespace PKForge.Sync.Server.Tests;

public sealed class PairingTests
{
    [Fact]
    public async Task ACodeAddsADeviceToTheSameAccountOnce()
    {
        using var server = new SyncServerFactory();
        var (phone, phoneKey) = await server.RegisterAsync();

        var code = await (await phone.PostAsync("/v1/pairing", null)).ReadAsync<PairingCodeResponse>();
        server.Time.Advance(TimeSpan.FromSeconds(1));
        var redeemed = await server.Client().PostJsonAsync("/v1/pairing/redeem", new RedeemPairingRequest(code.Code, "Retroid Pocket 5"));
        var again = await server.Client().PostJsonAsync("/v1/pairing/redeem", new RedeemPairingRequest(code.Code, "Another"));

        var deck = await redeemed.ReadAsync<DeviceKeyResponse>();
        Assert.Equal(phoneKey.AccountId, deck.AccountId);
        Assert.Equal(8, code.Code.Length);
        Assert.Equal(server.Time.GetUtcNow().AddMinutes(4).AddSeconds(59), code.ExpiresUtc);
        Assert.Equal($"pkforge-sync://pair?server={Uri.EscapeDataString("https://sync.test")}&code={code.Code}", code.QrPayload);
        Assert.Equal(ErrorCodes.InvalidPairingCode, (await again.ReadErrorAsync()).Code);
        var devices = await (await server.AuthedClient(deck.DeviceKey).GetAsync("/v1/devices")).ReadAsync<List<DeviceInfo>>();
        Assert.Equal(["Phone", "Retroid Pocket 5"], devices.Select(d => d.Name));
    }

    [Fact]
    public async Task CodesForgiveCaseSpacesAndDashes()
    {
        using var server = new SyncServerFactory();
        var (phone, _) = await server.RegisterAsync();
        var code = (await (await phone.PostAsync("/v1/pairing", null)).ReadAsync<PairingCodeResponse>()).Code;
        var typed = $" {code[..4].ToLowerInvariant()}-{code[4..]} ";

        var redeemed = await server.Client().PostJsonAsync("/v1/pairing/redeem", new RedeemPairingRequest(typed, "Deck"));

        redeemed.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task CodesExpireAfterFiveMinutes()
    {
        using var server = new SyncServerFactory();
        var (phone, _) = await server.RegisterAsync();
        var code = (await (await phone.PostAsync("/v1/pairing", null)).ReadAsync<PairingCodeResponse>()).Code;

        server.Time.Advance(TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(1));
        var redeemed = await server.Client().PostJsonAsync("/v1/pairing/redeem", new RedeemPairingRequest(code, "Deck"));

        Assert.Equal(HttpStatusCode.BadRequest, redeemed.StatusCode);
        Assert.Equal(ErrorCodes.InvalidPairingCode, (await redeemed.ReadErrorAsync()).Code);
    }

    [Fact]
    public async Task UnverifiedAccountsCannotPairWhenVerificationIsRequired()
    {
        using var server = new SyncServerFactory();
        server.Settings["Sync:RequireEmailVerification"] = "true";
        var (phone, _) = await server.RegisterAsync();

        var blocked = await phone.PostAsync("/v1/pairing", null);
        await server.Client().PostJsonAsync("/v1/auth/verify-email", new VerifyEmailRequest(server.Email.LastToken()));
        var allowed = await phone.PostAsync("/v1/pairing", null);

        Assert.Equal(HttpStatusCode.Forbidden, blocked.StatusCode);
        Assert.Equal(ErrorCodes.EmailNotVerified, (await blocked.ReadErrorAsync()).Code);
        allowed.EnsureSuccessStatusCode();
    }
}
