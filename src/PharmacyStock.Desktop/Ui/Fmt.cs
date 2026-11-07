using PharmacyStock.Models;

namespace PharmacyStock.Desktop.Ui;

/// <summary>Number and date formatting, in one place so every screen reads the same.</summary>
internal static class Fmt
{
    public static string Packs(decimal value) => value.ToString("0.##");
    public static string Whole(decimal value) => value.ToString("#,##0");
    public static string Money(decimal value) => "Rs " + value.ToString("#,##0");

    /// <summary>
    /// Stock the way a pharmacist reads a shelf: whole packs, with any part-pack shown
    /// separately. Pharmacy systems count in tablets, so "0.17 packs" really means
    /// "none left, 1 loose capsule" - and that is what gets shown.
    /// </summary>
    public static string Stock(decimal qtyPacks, int wholePacks, decimal looseUnits, decimal unitsPerPack)
    {
        if (unitsPerPack <= 1m) return Packs(qtyPacks);
        return looseUnits > 0m
            ? $"{wholePacks}  +{Packs(looseUnits)}"
            : wholePacks.ToString("#,##0");
    }

    public static string Stock(StockView row) =>
        Stock(row.QtyPacks, row.WholePacks, row.LooseUnits, row.UnitsPerPack);

    /// <summary>30-day sales converted from tablets into packs.</summary>
    public static decimal SoldPacks(StockView row) =>
        row.UnitsPerPack <= 0m ? row.Sold30 : row.Sold30 / row.UnitsPerPack;

    /// <summary>"nothing left" / "1 pack left" / "4 packs left" - never "1 packs".</summary>
    public static string PacksPhrase(decimal packs)
    {
        if (packs <= 0m) return "nothing left";
        if (packs < 1m) return "less than 1 pack left";
        if (packs < 2m) return "1 pack left";
        return Packs(packs) + " packs left";
    }

    public static string Ago(DateTime? utc)
    {
        if (utc is null) return "never";
        var seconds = Math.Max(0, (DateTime.UtcNow - utc.Value).TotalSeconds);
        if (seconds < 10) return "just now";
        if (seconds < 60) return $"{(int)seconds} seconds ago";
        if (seconds < 3600) return $"{(int)(seconds / 60)} minutes ago";
        if (seconds < 86400) return $"{(int)(seconds / 3600)} hours ago";
        return $"{(int)(seconds / 86400)} days ago";
    }

    public static string LocalTime(DateTime utc) => utc.ToLocalTime().ToString("dd MMM, HH:mm");

    /// <summary>Plain-English description of an alert.</summary>
    public static string AlertText(AlertEvent alert) => alert.To switch
    {
        StockState.Out => $"{alert.Name} has finished",
        StockState.Low => $"{alert.Name} is running short",
        StockState.Ok => $"{alert.Name} is back above its shelf level",
        _ => alert.Name,
    };

    public static string AlertTag(AlertEvent alert) => alert.To switch
    {
        StockState.Out => "OUT",
        StockState.Low => "LOW",
        StockState.Ok => "BACK",
        _ => "",
    };
}
