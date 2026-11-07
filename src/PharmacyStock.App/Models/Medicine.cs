namespace PharmacyStock.Models;

/// <summary>
/// One medicine as read from the source pharmacy system (SeaSoft, a CSV export, or demo data).
/// This is READ-ONLY data. The app never writes any of it back to the source.
///
/// Quantities are held in BASE UNITS (the smallest unit the source system counts in,
/// usually tablets/capsules/ml), because that is how pharmacy software normally stores
/// stock. Shelf levels are set by the user in PACKS, so <see cref="UnitsPerPack"/> is
/// what bridges the two. Getting this conversion wrong is the single easiest way to
/// produce nonsense alerts, so it is explicit everywhere.
/// </summary>
public sealed record MedicineStock
{
    /// <summary>Source system's own item code. The join key for everything we store. Never the name - names get edited.</summary>
    public required string ItemCode { get; init; }

    public required string Name { get; init; }
    public string? Generic { get; init; }
    public string? Company { get; init; }
    public string? Barcode { get; init; }

    /// <summary>What one "pack" is called on the shelf: Pack, Box, Bottle, Vial, Tube.</summary>
    public string PackUnit { get; init; } = "Pack";

    /// <summary>Base units in one pack, e.g. 20 capsules per pack of Velosef 500mg.</summary>
    public decimal UnitsPerPack { get; init; } = 1m;

    /// <summary>Current stock in base units, summed across all batches.</summary>
    public decimal QtyBaseUnits { get; init; }

    public decimal? RetailPrice { get; init; }

    /// <summary>Default distributor, used to group the order list.</summary>
    public string? Supplier { get; init; }

    public DateOnly? LastSaleDate { get; init; }

    /// <summary>Base units sold in the last 30 days. Drives the suggested shelf level.</summary>
    public decimal Sold30 { get; init; }

    /// <summary>Base units sold in the last 90 days. Used to spot dead stock.</summary>
    public decimal Sold90 { get; init; }

    public DateOnly? NearestExpiry { get; init; }

    /// <summary>Stock expressed in packs, which is how the user thinks about the shelf.</summary>
    public decimal QtyPacks => UnitsPerPack <= 0m
        ? QtyBaseUnits
        : decimal.Round(QtyBaseUnits / UnitsPerPack, 2, MidpointRounding.AwayFromZero);

    /// <summary>Whole packs on the shelf, with the remainder as loose units.</summary>
    public (int Packs, decimal Loose) PacksAndLoose()
    {
        if (UnitsPerPack <= 1m) return ((int)decimal.Truncate(QtyBaseUnits), 0m);
        var packs = (int)decimal.Truncate(QtyBaseUnits / UnitsPerPack);
        var loose = QtyBaseUnits - (packs * UnitsPerPack);
        return (packs, loose);
    }

    /// <summary>Average base units sold per day over the last 90 days.</summary>
    public decimal DailyVelocity => decimal.Round(Sold90 / 90m, 4, MidpointRounding.AwayFromZero);
}
