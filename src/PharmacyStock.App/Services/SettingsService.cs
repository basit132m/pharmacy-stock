using System.Globalization;
using PharmacyStock.Data;
using PharmacyStock.Models;

namespace PharmacyStock.Services;

/// <summary>Reads and writes the user's settings, keeping a cached copy in memory.</summary>
public sealed class SettingsService
{
    private readonly Store _store;
    private AppSettings _current;

    public SettingsService(Store store)
    {
        _store = store;
        _current = Load();
    }

    public AppSettings Current => _current;

    private AppSettings Load()
    {
        var raw = _store.GetSettingsRaw();
        var settings = new AppSettings();

        string Text(string key, string fallback) => raw.TryGetValue(key, out var v) && v.Length > 0 ? v : fallback;
        int Int(string key, int fallback) => raw.TryGetValue(key, out var v) && int.TryParse(v, CultureInfo.InvariantCulture, out var n) ? n : fallback;
        decimal Dec(string key, decimal fallback) => raw.TryGetValue(key, out var v) && decimal.TryParse(v, CultureInfo.InvariantCulture, out var n) ? n : fallback;
        bool Bool(string key, bool fallback) => raw.TryGetValue(key, out var v) ? v is "1" or "true" or "True" : fallback;

        settings.PharmacyName = Text("pharmacyName", settings.PharmacyName);
        settings.SyncSeconds = Math.Clamp(Int("syncSeconds", settings.SyncSeconds), 10, 3600);
        settings.HysteresisPacks = Math.Clamp(Dec("hysteresisPacks", settings.HysteresisPacks), 0m, 100m);
        settings.LeadTimeDays = Math.Clamp(Int("leadTimeDays", settings.LeadTimeDays), 1, 60);
        settings.SafetyFactor = Math.Clamp(Dec("safetyFactor", settings.SafetyFactor), 1m, 5m);
        settings.DeadStockDays = Math.Clamp(Int("deadStockDays", settings.DeadStockDays), 30, 1000);
        settings.SoundAlerts = Bool("soundAlerts", settings.SoundAlerts);
        settings.DataSource = Text("dataSource", settings.DataSource);
        settings.CsvPath = Text("csvPath", settings.CsvPath);
        settings.Port = Math.Clamp(Int("port", settings.Port), 1024, 65535);
        settings.AllowLan = Bool("allowLan", settings.AllowLan);

        return settings;
    }

    public AppSettings Save(AppSettings incoming)
    {
        var values = new Dictionary<string, string>
        {
            ["pharmacyName"] = incoming.PharmacyName.Trim() is { Length: > 0 } n ? n : "Ten Percent Discount Pharmacy",
            ["syncSeconds"] = Math.Clamp(incoming.SyncSeconds, 10, 3600).ToString(CultureInfo.InvariantCulture),
            ["hysteresisPacks"] = Math.Clamp(incoming.HysteresisPacks, 0m, 100m).ToString(CultureInfo.InvariantCulture),
            ["leadTimeDays"] = Math.Clamp(incoming.LeadTimeDays, 1, 60).ToString(CultureInfo.InvariantCulture),
            ["safetyFactor"] = Math.Clamp(incoming.SafetyFactor, 1m, 5m).ToString(CultureInfo.InvariantCulture),
            ["deadStockDays"] = Math.Clamp(incoming.DeadStockDays, 30, 1000).ToString(CultureInfo.InvariantCulture),
            ["soundAlerts"] = incoming.SoundAlerts ? "1" : "0",
            ["dataSource"] = incoming.DataSource is "demo" or "csv" or "seasoft" ? incoming.DataSource : "demo",
            ["csvPath"] = incoming.CsvPath ?? "",
            ["port"] = Math.Clamp(incoming.Port, 1024, 65535).ToString(CultureInfo.InvariantCulture),
            ["allowLan"] = incoming.AllowLan ? "1" : "0",
        };

        _store.SetSettings(values);
        _current = Load();
        return _current;
    }
}
