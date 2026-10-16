using System.Net;
using PKForge.Sync.Protocol;
using PKForge.Sync.Server.Cli;
using PKForge.Sync.Server.Tests.Infrastructure;

namespace PKForge.Sync.Server.Tests;

public sealed class AdminCliTests
{
    private static async Task<(int Code, string Output)> RunAsync(SyncServerFactory server, params string[] args)
    {
        _ = server.Client();   // starts the host
        var output = new StringWriter();
        var code = await AdminCli.RunAsync(server.Services, AdminCli.Parse(args)!, output);
        return (code, output.ToString());
    }

    private static string PasswordFrom(string output) => output.Split("Password: ")[1].Trim();

    [Fact]
    public void OnlyKnownCommandsAreParsed()
    {
        Assert.Equal("reset-password", AdminCli.Parse(["reset-password", "a@b.c"])!.Name);
        Assert.Equal(["a@b.c"], AdminCli.Parse(["reset-password", "a@b.c"])!.Args);
        Assert.Null(AdminCli.Parse([]));
        Assert.Null(AdminCli.Parse(["--urls", "http://*:8080"]));
    }

    [Fact]
    public async Task CreateUserPrintsAWorkingPassword()
    {
        using var server = new SyncServerFactory();

        var (code, output) = await RunAsync(server, "create-user", "brock@example.com", "--admin");
        var login = await server.Client().PostJsonAsync("/v1/auth/login", new LoginRequest("brock@example.com", PasswordFrom(output), "Phone"));

        Assert.Equal(0, code);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.True((await login.ReadAsync<DeviceKeyResponse>()).EmailVerified);
    }

    [Fact]
    public async Task ResetPasswordReplacesThePassword()
    {
        using var server = new SyncServerFactory();
        await server.RegisterAsync();

        var (code, output) = await RunAsync(server, "reset-password", "ASH@example.com");
        var oldLogin = await server.Client().PostJsonAsync("/v1/auth/login", new LoginRequest("ash@example.com", TestApi.Password, "Phone"));
        var newLogin = await server.Client().PostJsonAsync("/v1/auth/login", new LoginRequest("ash@example.com", PasswordFrom(output), "Phone"));

        Assert.Equal(0, code);
        Assert.Equal(HttpStatusCode.Unauthorized, oldLogin.StatusCode);
        Assert.Equal(HttpStatusCode.OK, newLogin.StatusCode);
    }

    [Fact]
    public async Task ResetPasswordForUnknownEmailFails()
    {
        using var server = new SyncServerFactory();
        var (code, output) = await RunAsync(server, "reset-password", "nobody@example.com");

        Assert.Equal(1, code);
        Assert.Contains("No account", output);
    }

    [Fact]
    public async Task ListUsersShowsEveryAccount()
    {
        using var server = new SyncServerFactory();
        await server.RegisterAsync("ash@example.com");
        await server.RegisterAsync("misty@example.com");

        var (code, output) = await RunAsync(server, "list-users");

        Assert.Equal(0, code);
        Assert.Contains("ash@example.com", output);
        Assert.Contains("misty@example.com", output);
        Assert.Contains("admin", output);
    }

    [Fact]
    public void FriendlyEnvironmentNamesMapToSettings()
    {
        var env = new Dictionary<string, string> { ["ALLOW_REGISTRATION"] = "true", ["SMTP_HOST"] = "mail.test" };

        var settings = EnvironmentAliases.Read(name => env.GetValueOrDefault(name)).ToDictionary();

        Assert.Equal("true", settings["Sync:AllowRegistration"]);
        Assert.Equal("mail.test", settings["Sync:Smtp:Host"]);
        Assert.Equal(2, settings.Count);
    }

    [Fact]
    public void WebRootEnvironmentNameMapsToWebRootSetting()
    {
        var env = new Dictionary<string, string> { ["WEB_ROOT"] = "/srv/web" };

        var settings = EnvironmentAliases.Read(name => env.GetValueOrDefault(name)).ToDictionary();

        Assert.Equal("/srv/web", settings["Sync:WebRoot"]);
    }

    [Fact]
    public void ServerNameEnvironmentNameMapsToServerNameSetting()
    {
        var env = new Dictionary<string, string> { ["SERVER_NAME"] = "Misty's Bank" };

        var settings = EnvironmentAliases.Read(name => env.GetValueOrDefault(name)).ToDictionary();

        Assert.Equal("Misty's Bank", settings["Sync:ServerName"]);
    }
}
