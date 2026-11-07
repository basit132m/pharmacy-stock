namespace PharmacyStock.Data;

/// <summary>
/// Where the app keeps its own files. Deliberately outside the folder the .exe sits in,
/// so the program can be replaced with a newer version without losing your shelf levels.
/// On Windows this is %LOCALAPPDATA%\TenPercentStockManager.
/// </summary>
public static class AppPaths
{
    public static string DataFolder { get; } = Resolve();

    public static string DatabaseFile => Path.Combine(DataFolder, "stock.db");

    private static string Resolve()
    {
        var baseFolder = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(baseFolder))
            baseFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");

        var folder = Path.Combine(baseFolder, "TenPercentStockManager");
        Directory.CreateDirectory(folder);
        return folder;
    }
}
