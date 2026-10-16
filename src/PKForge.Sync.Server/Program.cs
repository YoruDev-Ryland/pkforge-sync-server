using PKForge.Sync.Server;
using PKForge.Sync.Server.Cli;
using PKForge.Sync.Server.Data;

var command = AdminCli.Parse(args);
var builder = WebApplication.CreateBuilder(command is null ? args : []);
builder.WebHost.ConfigureKestrel(k => k.Limits.MaxRequestBodySize = 64 * 1024 * 1024);
builder.Configuration.AddInMemoryCollection(EnvironmentAliases.Read());
builder.Services.AddSyncServer();

var app = builder.Build();
app.Services.MigrateDatabase();

if (command is not null)
    return await AdminCli.RunAsync(app.Services, command, Console.Out);

app.UseSyncServer();
app.Run();
return 0;

public partial class Program;
