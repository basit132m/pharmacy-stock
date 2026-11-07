using System.Globalization;
using System.Text.Json;
using PharmacyStock.Data;
using PharmacyStock.Models;
using PharmacyStock.Providers.Seasoft;

namespace PharmacyStock.Services;

/// <summary>Reads and writes the user's settings, keeping a cached copy in memory.</summary>
public sealed class SettingsService
{
    private readonly Store _store;
    private AppSettings _current;
    private SeasoftConnection? _seasoftConnection;
    private SeasoftMapping? _seasoftMapping;

    private const string ConnectionKey = "seasoft.connection";
    private const string MappingKey = "seasoft.mapping";
    private const string PasswordKey = "seasoft.password";

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

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
        settings.MinimiseToTray = Bool("minimiseToTray", settings.MinimiseToTray);
        settings.StartWithWindows = Bool("startWithWindows", settings.StartWithWindows);

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
            ["minimiseToTray"] = incoming.MinimiseToTray ? "1" : "0",
            ["startWithWindows"] = incoming.StartWithWindows ? "1" : "0",
        };

        _store.SetSettings(values);
        _current = Load();
        return _current;
    }

    // ------------------------------------------------- SeaSoft connection

    /// <summary>
    /// How to reach the SeaSoft database. The password is held separately and encrypted,
    /// so it never sits in plain text in the settings file.
    /// </summary>
    public SeasoftConnection GetSeasoftConnection()
    {
        if (_seasoftConnection is not null) return _seasoftConnection;

        var raw = _store.GetSettingsRaw();
        SeasoftConnection connection;

        try
        {
            connection = raw.TryGetValue(ConnectionKey, out var text) && text.Length > 0
                ? JsonSerializer.Deserialize<SeasoftConnection>(text) ?? new SeasoftConnection()
                : new SeasoftConnection();
        }
        catch (JsonException)
        {
            connection = new SeasoftConnection();
        }

        var password = raw.TryGetValue(PasswordKey, out var stored) ? Secrets.Unprotect(stored) : "";
        _seasoftConnection = connection with { Password = password };
        return _seasoftConnection;
    }

    public void SaveSeasoftConnection(SeasoftConnection connection)
    {
        // The password goes in its own protected entry, not into the JSON.
        var withoutPassword = connection with { Password = "" };

        _store.SetSettings(new Dictionary<string, string>
        {
            [ConnectionKey] = JsonSerializer.Serialize(withoutPassword, Json),
            [PasswordKey] = Secrets.Protect(connection.Password),
        });

        _seasoftConnection = connection;
    }

    public SeasoftMapping GetSeasoftMapping()
    {
        if (_seasoftMapping is not null) return _seasoftMapping;

        var raw = _store.GetSettingsRaw();
        try
        {
            _seasoftMapping = raw.TryGetValue(MappingKey, out var text) && text.Length > 0
                ? JsonSerializer.Deserialize<SeasoftMapping>(text) ?? new SeasoftMapping()
                : new SeasoftMapping();
        }
        catch (JsonException)
        {
            _seasoftMapping = new SeasoftMapping();
        }

        return _seasoftMapping;
    }

    public void SaveSeasoftMapping(SeasoftMapping mapping)
    {
        _store.SetSettings(new Dictionary<string, string>
        {
            [MappingKey] = JsonSerializer.Serialize(mapping, Json),
        });

        _seasoftMapping = mapping;
    }
}
