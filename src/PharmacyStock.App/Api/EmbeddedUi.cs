using System.Reflection;

namespace PharmacyStock.Api;

/// <summary>
/// Serves the user interface straight out of the .exe.
///
/// The screens are embedded as resources rather than shipped as loose files, so the whole
/// program really is one file you can copy to any PC and double-click - nothing to install,
/// no folder of assets to keep together.
/// </summary>
public static class EmbeddedUi
{
    private const string Prefix = "PharmacyStock.wwwroot.";

    public static void MapEmbeddedUi(this WebApplication app)
    {
        var assembly = Assembly.GetExecutingAssembly();

        // Literal routes such as /api/... take priority over this catch-all.
        app.MapGet("/{**path}", (string? path) => Serve(assembly, path));
    }

    private static IResult Serve(Assembly assembly, string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.EndsWith('/')) path = "index.html";

        // Keep it to plain file names - no walking out of the resource prefix.
        var safe = path.Replace('\\', '/').TrimStart('/');
        if (safe.Contains("..", StringComparison.Ordinal)) return Results.NotFound();

        var resource = Prefix + safe.Replace('/', '.');
        var stream = assembly.GetManifestResourceStream(resource);

        if (stream is null)
        {
            // Unknown path inside a single-page app: hand back the app shell.
            stream = assembly.GetManifestResourceStream(Prefix + "index.html");
            if (stream is null) return Results.NotFound();
            return Results.Stream(stream, "text/html; charset=utf-8");
        }

        return Results.Stream(stream, ContentType(safe));
    }

    private static string ContentType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".html" => "text/html; charset=utf-8",
        ".js" => "text/javascript; charset=utf-8",
        ".css" => "text/css; charset=utf-8",
        ".json" => "application/json; charset=utf-8",
        ".svg" => "image/svg+xml",
        ".png" => "image/png",
        ".ico" => "image/x-icon",
        ".woff2" => "font/woff2",
        ".mp3" => "audio/mpeg",
        _ => "application/octet-stream",
    };
}
