using Microsoft.Win32;
using PharmacyStock.Diagnostics;

namespace PharmacyStock.Desktop;

/// <summary>
/// Adds or removes the program from the list Windows starts automatically at login.
/// Uses the per-user key, so no administrator rights are needed.
/// </summary>
internal static class Startup
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "TenPercentStockManager";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is not null;
        }
        catch (Exception ex)
        {
            Log.Warn("Could not read the Windows start-up setting: " + ex.Message);
            return false;
        }
    }

    public static bool Apply(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            if (key is null) return false;

            if (enabled)
            {
                var path = Environment.ProcessPath;
                if (string.IsNullOrWhiteSpace(path)) return false;
                key.SetValue(ValueName, "\"" + path + "\"");
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }

            return true;
        }
        catch (Exception ex)
        {
            Log.Warn("Could not change the Windows start-up setting: " + ex.Message);
            return false;
        }
    }
}
