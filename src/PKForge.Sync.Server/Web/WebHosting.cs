using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;

namespace PKForge.Sync.Server.Web;

/// <summary>The web app and PKHeX art, served beside the API from the same origin.</summary>
public static class WebHosting
{
    public static WebApplication UseWebApp(this WebApplication app)
    {
        var options = app.Services.GetRequiredService<IOptions<SyncOptions>>().Value;
        var baseDir = AppContext.BaseDirectory;
        var pkhex = Path.GetFullPath(Path.Combine(baseDir, options.PkhexAssets));
        if (Directory.Exists(pkhex))
        {
            app.UseStaticFiles(new StaticFileOptions
            {
                RequestPath = "/pkhex",
                FileProvider = new PhysicalFileProvider(pkhex),
                OnPrepareResponse = c => c.Context.Response.Headers.CacheControl = "public, max-age=2592000",
            });
        }

        var root = Path.GetFullPath(Path.Combine(baseDir, options.WebRoot));
        if (!File.Exists(Path.Combine(root, "index.html"))) return app;
        var files = new PhysicalFileProvider(root);
        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = files,
            OnPrepareResponse = c => c.Context.Response.Headers.CacheControl =
                c.Context.Request.Path.StartsWithSegments("/assets") ? "public, max-age=31536000, immutable" : "no-cache",
        });
        app.MapFallback(async context =>
        {
            var path = context.Request.Path;
            if (!HttpMethods.IsGet(context.Request.Method) || path.StartsWithSegments("/v1") || path.StartsWithSegments("/assets")
                || path.StartsWithSegments("/pkhex") || Path.HasExtension(path.Value))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }
            context.Response.Headers.CacheControl = "no-cache";
            context.Response.ContentType = "text/html; charset=utf-8";
            await context.Response.SendFileAsync(files.GetFileInfo("index.html"));
        });
        return app;
    }
}
