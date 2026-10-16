using System.Net;
using PKForge.Sync.Protocol;
using PKForge.Sync.Server.Tests.Infrastructure;
using PKForge.Sync.Server.Web;

namespace PKForge.Sync.Server.Tests.Web;

public class WebSessionTests
{
    private static string? Cookie(HttpResponseMessage r) =>
        r.Headers.TryGetValues("Set-Cookie", out var v) ? v.FirstOrDefault(c => c.StartsWith("pkf_device=")) : null;

    private static HttpClient WithCookie(SyncServerFactory server, string setCookie)
    {
        var client = server.Client();
        client.DefaultRequestHeaders.Add("Cookie", setCookie.Split(';')[0]);
        return client;
    }

    [Fact]
    public async Task PasswordSignInSetsAHardenedCookieThatAuthenticates()
    {
        using var server = new SyncServerFactory();
        await server.RegisterAsync();
        var response = await server.Client().PostJsonAsync("/v1/web/session",
            new WebSessionRequest("ash@example.com", TestApi.Password, null, "Firefox on Linux"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cookie = Cookie(response)!;
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase); // PublicBaseUrl is https in tests
        Assert.Contains("path=/", cookie, StringComparison.OrdinalIgnoreCase);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("deviceKey", body);

        var devices = await (await WithCookie(server, cookie).GetAsync("/v1/devices")).ReadAsync<List<DeviceInfo>>();
        Assert.Contains(devices, d => d.Name == "Firefox on Linux" && d.IsCurrent);
    }

    [Fact]
    public async Task ALongDeviceNameIsCutBetweenCharactersNotInsideAnEmoji()
    {
        using var server = new SyncServerFactory();
        await server.RegisterAsync();
        var name = new string('a', 99) + "\U0001F600 tail"; // the emoji is two UTF-16 units at positions 99-100
        var response = await server.Client().PostJsonAsync("/v1/web/session", new WebSessionRequest("ash@example.com", TestApi.Password, null, name));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var devices = await (await WithCookie(server, Cookie(response)!).GetAsync("/v1/devices")).ReadAsync<List<DeviceInfo>>();
        Assert.Equal(new string('a', 99), devices.Single(d => d.IsCurrent).Name);
        Assert.Equal(new string('a', 98) + "\U0001F600", WebSession.DeviceName(new string('a', 98) + "\U0001F600 tail"));
    }

    [Fact]
    public async Task PairingCodeSignInWorks()
    {
        using var server = new SyncServerFactory();
        var (phone, _) = await server.RegisterAsync();
        var code = await (await phone.PostAsync("/v1/pairing", null)).ReadAsync<PairingCodeResponse>();
        var response = await server.Client().PostJsonAsync("/v1/web/session", new WebSessionRequest(null, null, code.Code, "Chrome on Windows"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(Cookie(response));
    }

    [Fact]
    public async Task WrongPasswordIs401WithoutACookie()
    {
        using var server = new SyncServerFactory();
        await server.RegisterAsync();
        var response = await server.Client().PostJsonAsync("/v1/web/session", new WebSessionRequest("ash@example.com", "nope-nope", null, "Firefox"));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(Cookie(response));
    }

    [Fact]
    public async Task SignOutRemovesTheDeviceAndClearsTheCookie()
    {
        using var server = new SyncServerFactory();
        await server.RegisterAsync();
        var cookie = Cookie(await server.Client().PostJsonAsync("/v1/web/session", new WebSessionRequest("ash@example.com", TestApi.Password, null, "Firefox")))!;
        var browser = WithCookie(server, cookie);
        var response = await browser.DeleteAsync("/v1/web/session");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Contains("pkf_device=;", Cookie(response)!);
        Assert.Equal(HttpStatusCode.Unauthorized, (await browser.GetAsync("/v1/devices")).StatusCode);
    }

    [Fact]
    public async Task CookieRequestsStillNeedTheProtocolHeader()
    {
        using var server = new SyncServerFactory();
        await server.RegisterAsync();
        var cookie = Cookie(await server.Client().PostJsonAsync("/v1/web/session", new WebSessionRequest("ash@example.com", TestApi.Password, null, "Firefox")))!;
        var bare = server.CreateClient();
        bare.DefaultRequestHeaders.Add("Cookie", cookie.Split(';')[0]);
        Assert.Equal(HttpStatusCode.UpgradeRequired, (await bare.GetAsync("/v1/devices")).StatusCode);
    }

    [Fact]
    public async Task SessionSignInIsRateLimited()
    {
        using var server = new SyncServerFactory();
        server.Settings["Sync:AuthRequestsPerMinute"] = "2";
        var client = server.Client();
        for (var i = 0; i < 2; i++) await client.PostJsonAsync("/v1/web/session", new WebSessionRequest("x@example.com", "wrong-wrong", null, "F"));
        var third = await client.PostJsonAsync("/v1/web/session", new WebSessionRequest("x@example.com", "wrong-wrong", null, "F"));
        Assert.Equal(HttpStatusCode.TooManyRequests, third.StatusCode);
    }

    [Fact]
    public async Task BearerKeysStillWinOverACookie()
    {
        using var server = new SyncServerFactory();
        var (phone, key) = await server.RegisterAsync();
        var cookie = Cookie(await server.Client().PostJsonAsync("/v1/web/session", new WebSessionRequest("ash@example.com", TestApi.Password, null, "Firefox")))!;
        phone.DefaultRequestHeaders.Add("Cookie", cookie.Split(';')[0]);
        var devices = await (await phone.GetAsync("/v1/devices")).ReadAsync<List<DeviceInfo>>();
        Assert.Equal(key.DeviceId, devices.Single(d => d.IsCurrent).Id);
    }

    [Fact]
    public async Task WebSignUpCreatesTheAccountWithThisBrowserAsItsOnlyDevice()
    {
        using var server = new SyncServerFactory();
        var response = await server.Client().PostJsonAsync("/v1/web/register", new RegisterRequest("ash@example.com", TestApi.Password, "  Firefox on Linux "));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.ReadAsync<WebSessionResponse>();
        Assert.False(body.EmailVerified);
        Assert.DoesNotContain("deviceKey", await response.Content.ReadAsStringAsync());

        var client = WithCookie(server, Cookie(response)!);
        var devices = await (await client.GetAsync("/v1/devices")).ReadAsync<List<DeviceInfo>>();
        var device = Assert.Single(devices);
        Assert.Equal(("Firefox on Linux", true, body.DeviceId), (device.Name, device.IsCurrent, device.Id));
        Assert.True((await (await client.GetAsync("/v1/account")).ReadAsync<AccountInfo>()).IsAdmin); // the server's first account
    }

    [Fact]
    public async Task WebSignUpUsesTheServersPasswordRules()
    {
        using var server = new SyncServerFactory();
        var weak = await server.Client().PostJsonAsync("/v1/web/register", new RegisterRequest("ash@example.com", "short", "Firefox"));
        await server.RegisterAsync("misty@example.com");
        var taken = await server.Client().PostJsonAsync("/v1/web/register", new RegisterRequest("Misty@example.com", TestApi.Password, "Firefox"));

        Assert.Equal(ErrorCodes.WeakPassword, (await weak.ReadErrorAsync()).Code);
        Assert.Null(Cookie(weak));
        Assert.Equal(ErrorCodes.EmailTaken, (await taken.ReadErrorAsync()).Code);
    }
}
