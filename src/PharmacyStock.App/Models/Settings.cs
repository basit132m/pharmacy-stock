namespace PharmacyStock.Models;

/// <summary>
/// Everything the user can tune, stored as key/value rows in our SQLite file.
/// </summary>
public sealed class AppSettings
{
    public string PharmacyName { get; set; } = "Ten Percent Discount Pharmacy";

    /// <summary>How often to re-read stock from the source system, in seconds.</summary>
    public int SyncSeconds { get; set; } = 60;

    /// <summary>
    /// Extra packs required before a LOW alert clears. Without this, a medicine sitting
    /// exactly on its shelf level flips between LOW and OK all day as sales come in.
    /// </summary>
    public decimal HysteresisPacks { get; set; } = 1m;

    /// <summary>Days between placing an order and receiving it. Used for suggested shelf levels.</summary>
    public int LeadTimeDays { get; set; } = 3;

    /// <summary>Cushion on top of expected demand when suggesting a shelf level.</summary>
    public decimal SafetyFactor { get; set; } = 1.5m;

    /// <summary>No sale in this many days = treat as dead stock, suggest level 0.</summary>
    public int DeadStockDays { get; set; } = 180;

    /// <summary>Play a sound in the browser when a medicine goes OUT of stock.</summary>
    public bool SoundAlerts { get; set; } = true;

    /// <summary>Where stock comes from: "demo", "csv", or later "seasoft".</summary>
    public string DataSource { get; set; } = "demo";

    /// <summary>Folder watched for a stock export when DataSource is "csv".</summary>
    public string CsvPath { get; set; } = "";

    /// <summary>Port the local web server listens on.</summary>
    public int Port { get; set; } = 8420;

    /// <summary>True = other PCs and phones on the shop network can open the board.</summary>
    public bool AllowLan { get; set; } = true;
}
