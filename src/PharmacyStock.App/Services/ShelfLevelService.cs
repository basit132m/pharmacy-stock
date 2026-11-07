using System.Globalization;
using System.Text;
using PharmacyStock.Data;
using PharmacyStock.Models;
using PharmacyStock.Providers;

namespace PharmacyStock.Services;

public sealed record BulkResult(int Changed, int Skipped, string Message);

/// <summary>
/// Managing the user's shelf levels - the one thing this app actually owns.
/// </summary>
public sealed class ShelfLevelService
{
    private readonly Store _store;
    private readonly StockService _stock;
    private readonly SettingsService _settings;

    public ShelfLevelService(Store store, StockService stock, SettingsService settings)
    {
        _store = store;
        _stock = stock;
        _settings = settings;
    }

    public void SetOne(string itemCode, decimal levelPacks, bool isManaged, string? preferredSupplier, string? notes)
    {
        _store.SaveShelfLevels(new[]
        {
            new ShelfLevel
            {
                ItemCode = itemCode,
                LevelPacks = Math.Max(0m, decimal.Round(levelPacks, 2)),
                IsManaged = isManaged,
                PreferredSupplier = string.IsNullOrWhiteSpace(preferredSupplier) ? null : preferredSupplier.Trim(),
                Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
                Source = "manual",
                UpdatedAt = DateTime.UtcNow,
            }
        });
    }

    /// <summary>
    /// Fills in shelf levels calculated from each medicine's own sales history.
    ///
    /// <paramref name="scope"/>:
    ///   "missing"  - only medicines that have no shelf level yet (the safe default: never
    ///                overwrites a number a person typed)
    ///   "selling"  - only medicines that have actually sold recently
    ///   "all"      - every medicine, overwriting manual numbers too
    /// </summary>
    public BulkResult ApplySuggestions(string scope = "missing")
    {
        var settings = _settings.Current;
        var today = DateOnly.FromDateTime(DateTime.Today);
        var existing = _store.GetShelfLevels();
        var updates = new List<ShelfLevel>();
        var skipped = 0;

        foreach (var medicine in _stock.Current.Items)
        {
            var hasExisting = existing.TryGetValue(medicine.ItemCode, out var current);
            var isManual = hasExisting && current!.Source == "manual" && current.LevelPacks > 0m;

            if (scope == "missing" && hasExisting && current!.LevelPacks > 0m) { skipped++; continue; }
            if (scope == "selling" && ShelfLevelAdvisor.IsDeadStock(medicine, settings, today)) { skipped++; continue; }
            if (scope != "all" && isManual) { skipped++; continue; }

            var suggested = ShelfLevelAdvisor.Suggest(medicine, settings, today);

            updates.Add(new ShelfLevel
            {
                ItemCode = medicine.ItemCode,
                LevelPacks = suggested,
                // Zero suggestion means dead stock, so leave it switched off rather than
                // filling the board with items nobody buys.
                IsManaged = suggested > 0m,
                PreferredSupplier = current?.PreferredSupplier,
                Notes = current?.Notes,
                Source = "suggested",
                UpdatedAt = DateTime.UtcNow,
            });
        }

        if (updates.Count > 0) _store.SaveShelfLevels(updates);

        var withLevel = updates.Count(u => u.LevelPacks > 0m);
        return new BulkResult(updates.Count, skipped,
            $"Set {withLevel} shelf levels from sales history. " +
            $"{updates.Count - withLevel} medicines switched off as dead stock. " +
            $"{skipped} left untouched.");
    }

    /// <summary>Switch a medicine on or off without changing its level.</summary>
    public void SetManaged(string itemCode, bool isManaged)
    {
        var existing = _store.GetShelfLevels();
        existing.TryGetValue(itemCode, out var current);
        _store.SaveShelfLevels(new[]
        {
            new ShelfLevel
            {
                ItemCode = itemCode,
                LevelPacks = current?.LevelPacks ?? 0m,
                IsManaged = isManaged,
                PreferredSupplier = current?.PreferredSupplier,
                Notes = current?.Notes,
                Source = current?.Source ?? "manual",
                UpdatedAt = DateTime.UtcNow,
            }
        });
    }

    /// <summary>Spreadsheet of every medicine with its current and suggested level.</summary>
    public string ExportCsv()
    {
        var sb = new StringBuilder();
        sb.AppendLine("ItemCode,Name,Generic,Company,Supplier,PackUnit,UnitsPerPack,InStockPacks,ShelfLevelPacks,SuggestedPacks,Managed,Sold30,Sold90,Status");

        foreach (var row in _stock.BuildAll().OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase))
        {
            sb.Append(Csv.Escape(row.ItemCode)).Append(',');
            sb.Append(Csv.Escape(row.Name)).Append(',');
            sb.Append(Csv.Escape(row.Generic)).Append(',');
            sb.Append(Csv.Escape(row.Company)).Append(',');
            sb.Append(Csv.Escape(row.Supplier)).Append(',');
            sb.Append(Csv.Escape(row.PackUnit)).Append(',');
            sb.Append(row.UnitsPerPack.ToString(CultureInfo.InvariantCulture)).Append(',');
            sb.Append(row.QtyPacks.ToString(CultureInfo.InvariantCulture)).Append(',');
            sb.Append((row.LevelPacks ?? 0m).ToString(CultureInfo.InvariantCulture)).Append(',');
            sb.Append(row.SuggestedLevelPacks.ToString(CultureInfo.InvariantCulture)).Append(',');
            sb.Append(row.IsManaged ? "Yes" : "No").Append(',');
            sb.Append(row.Sold30.ToString(CultureInfo.InvariantCulture)).Append(',');
            sb.Append(row.Sold90.ToString(CultureInfo.InvariantCulture)).Append(',');
            sb.AppendLine(Csv.Escape(row.StateText));
        }

        return sb.ToString();
    }

    /// <summary>
    /// Reads shelf levels back from a spreadsheet, so hundreds can be edited in Excel and
    /// pasted back in one go. Matches on ItemCode; rows with an unknown code are reported,
    /// not silently dropped.
    /// </summary>
    public BulkResult ImportCsv(string text)
    {
        var rows = Csv.Parse(text);
        if (rows.Count < 2) return new BulkResult(0, 0, "That file has no data rows.");

        string Key(string s) => new string(s.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
        var header = rows[0].Select(Key).ToArray();

        var iCode = Array.FindIndex(header, h => h is "itemcode" or "code" or "item");
        var iLevel = Array.FindIndex(header, h => h is "shelflevelpacks" or "shelflevel" or "level" or "levelpacks");
        var iManaged = Array.FindIndex(header, h => h is "managed" or "ismanaged" or "active" or "on");

        if (iCode < 0) return new BulkResult(0, 0, "No 'ItemCode' column found in that file.");
        if (iLevel < 0) return new BulkResult(0, 0, "No 'ShelfLevelPacks' column found in that file.");

        var known = _stock.Current.Items.Select(i => i.ItemCode).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var existing = _store.GetShelfLevels();
        var updates = new List<ShelfLevel>();
        var unknown = 0;

        for (var r = 1; r < rows.Count; r++)
        {
            var row = rows[r];
            string Cell(int i) => i >= 0 && i < row.Length ? row[i].Trim() : "";

            var code = Cell(iCode);
            if (code.Length == 0) continue;
            if (!known.Contains(code)) { unknown++; continue; }

            if (!decimal.TryParse(Cell(iLevel), NumberStyles.Any, CultureInfo.InvariantCulture, out var level))
                continue;

            var managedText = Cell(iManaged).ToLowerInvariant();
            var isManaged = iManaged < 0
                ? level > 0m
                : managedText is "yes" or "y" or "1" or "true" or "on";

            existing.TryGetValue(code, out var current);
            updates.Add(new ShelfLevel
            {
                ItemCode = code,
                LevelPacks = Math.Max(0m, decimal.Round(level, 2)),
                IsManaged = isManaged,
                PreferredSupplier = current?.PreferredSupplier,
                Notes = current?.Notes,
                Source = "manual",
                UpdatedAt = DateTime.UtcNow,
            });
        }

        if (updates.Count > 0) _store.SaveShelfLevels(updates);

        var message = $"Updated {updates.Count} shelf levels.";
        if (unknown > 0) message += $" {unknown} rows ignored - item code not found in your pharmacy system.";
        return new BulkResult(updates.Count, unknown, message);
    }
}
