using System.Globalization;
using PharmacyStock.Data;

namespace PharmacyStock.Diagnostics;

/// <summary>
/// Plain file logging. A desktop program has no console to write to, so when something
/// goes wrong on the counter PC this file is the only way to find out what happened.
/// Kept small on purpose - it is a diary, not telemetry.
/// </summary>
public static class Log
{
    private static readonly object Gate = new();
    private const long MaxBytes = 1_000_000;

    public static string FilePath => Path.Combine(AppPaths.DataFolder, "log.txt");

    public static void Info(string message) => Write("INFO ", message);
    public static void Warn(string message) => Write("WARN ", message);

    public static void Error(string message, Exception? ex = null) =>
        Write("ERROR", ex is null ? message : $"{message} :: {ex.GetType().Name}: {ex.Message}");

    private static void Write(string level, string message)
    {
        try
        {
            lock (Gate)
            {
                var path = FilePath;

                // Start a fresh file rather than letting it grow forever.
                if (File.Exists(path) && new FileInfo(path).Length > MaxBytes)
                    File.Delete(path);

                var stamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                File.AppendAllText(path, $"{stamp} {level} {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // Logging must never be the thing that breaks the program.
        }
    }
}
