using System.IO;
using System.Reflection;
namespace Paradox;

internal static class DesktopHost
{
    private static readonly IReadOnlyDictionary<string, (string ResourceName, string ContentType)> FrontendFiles =
        new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase)
        {
            ["/"] = ("Paradox.index.html", "text/html; charset=utf-8"),
            ["/index.html"] = ("Paradox.index.html", "text/html; charset=utf-8"),
            ["/app.js"] = ("Paradox.app.js", "text/javascript; charset=utf-8"),
            ["/styles.css"] = ("Paradox.styles.css", "text/css; charset=utf-8")
        };

    public static async Task<WebApplication> StartAsync(string[] args)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = args,
            ContentRootPath = AppContext.BaseDirectory
        });
        builder.WebHost.UseUrls("http://0.0.0.0:0");
        builder.Services.AddParadoxServices(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Paradox",
            "data"));

        var app = builder.Build();
        app.UseExceptionHandler();
        app.UseStatusCodePages();
        app.MapGet("/", () => FrontendResult("/"));
        app.MapGet("/index.html", () => FrontendResult("/index.html"));
        app.MapGet("/app.js", () => FrontendResult("/app.js"));
        app.MapGet("/styles.css", () => FrontendResult("/styles.css"));
        app.MapParadoxGameEndpoints();
        app.MapQuizLibraryEndpoints();

        try
        {
            await app.StartAsync();
            return app;
        }
        catch
        {
            await app.DisposeAsync();
            throw;
        }
    }

    private static IResult FrontendResult(string path)
    {
        var (resourceSuffix, contentType) = FrontendFiles[path];
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = assembly.GetManifestResourceNames()
            .SingleOrDefault(name => string.Equals(name, resourceSuffix, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"Embedded app asset ending in '{resourceSuffix}' was not found.");
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded app asset '{resourceName}' could not be opened.");
        using var reader = new StreamReader(stream);
        return Results.Content(reader.ReadToEnd(), contentType);
    }
}
