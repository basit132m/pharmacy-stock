using PharmacyStock.Models;

namespace PharmacyStock.Services;

/// <summary>
/// Works out a sensible shelf level from the medicine's own sales history.
///
/// Why this matters: a pharmacy has thousands of items. Nobody will ever sit down and
/// type a shelf level for each one, so without a suggestion the feature goes unused.
/// The user accepts the suggestions in bulk, then hand-tunes their fast movers.
/// </summary>
public static class ShelfLevelAdvisor
{
    /// <summary>
    /// Suggested shelf level in packs:
    ///     average daily sales x lead time x safety factor
    /// rounded up to a whole pack. Items with no recent sale get zero - there is no point
    /// keeping a shelf level on something that does not sell.
    /// </summary>
    public static decimal Suggest(MedicineStock medicine, AppSettings settings, DateOnly today)
    {
        if (IsDeadStock(medicine, settings, today)) return 0m;

        var unitsPerPack = medicine.UnitsPerPack <= 0m ? 1m : medicine.UnitsPerPack;
        var dailyPacks = medicine.Sold90 / 90m / unitsPerPack;

        // Fall back to the 30-day figure when there is no 90-day history yet.
        if (dailyPacks <= 0m && medicine.Sold30 > 0m)
            dailyPacks = medicine.Sold30 / 30m / unitsPerPack;

        if (dailyPacks <= 0m) return 0m;

        var cover = dailyPacks * settings.LeadTimeDays * settings.SafetyFactor;
        var suggested = decimal.Ceiling(cover);

        // Anything that sells at all deserves at least one pack on the shelf.
        return suggested < 1m ? 1m : suggested;
    }

    /// <summary>No sale within the dead-stock window, and nothing sold in 90 days.</summary>
    public static bool IsDeadStock(MedicineStock medicine, AppSettings settings, DateOnly today)
    {
        if (medicine.Sold90 > 0m || medicine.Sold30 > 0m) return false;
        if (medicine.LastSaleDate is null) return true;
        return medicine.LastSaleDate.Value < today.AddDays(-settings.DeadStockDays);
    }
}
