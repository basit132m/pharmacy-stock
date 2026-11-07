using System.Globalization;
using PharmacyStock.Models;

namespace PharmacyStock.Providers;

/// <summary>
/// Reads stock from a CSV or Excel-exported file.
///
/// This is the fallback if the SeaSoft vendor will not give database access: export the
/// stock report from SeaSoft to CSV, point this at the file, and the whole app works -
/// you just refresh by re-exporting instead of automatically.
///
/// Column headings are matched loosely, so most export formats work without editing.
/// </summary>
public sealed class CsvStockProvider : IStockProvider
{
    public string Key => "csv";
    public string Display => "CSV / Excel file exported from SeaSoft";

    private readonly Func<string> _pathAccessor;

    public CsvStockProvider(Func<string> pathAccessor) => _pathAccessor = pathAccessor;

    private static readonly string[] CodeNames = { "itemcode", "code", "item", "itemid", "productcode", "sku", "id" };
    private static readonly string[] NameNames = { "name", "itemname", "medicine", "product", "productname", "description", "item" };
    private static readonly string[] QtyNames = { "qty", "quantity", "stock", "balance", "closing", "closingbalance", "instock", "onhand", "available" };
    private static readonly string[] GenericNames = { "generic", "formula", "salt", "composition", "genericname" };
    private static readonly string[] CompanyNames = { "company", "manufacturer", "brand", "mfg", "maker" };
    private static readonly string[] PackUnitNames = { "packunit", "unit", "uom", "packing" };
    private static readonly string[] PerPackNames = { "unitsperpack", "packsize", "perpack", "packqty", "conversion", "unitpack" };
    private static readonly string[] PriceNames = { "price", "retail", "retailprice", "mrp", "saleprice", "tp" };
    private static readonly string[] SupplierNames = { "supplier", "distributor", "vendor", "party" };
    private static readonly string[] Sold30Names = { "sold30", "sales30", "qty30", "month" };
    private static readonly string[] Sold90Names = { "sold90", "sales90", "qty90", "quarter" };
    private static readonly string[] LastSaleNames = { "lastsale", "lastsaledate", "lastsold" };
    private static readonly string[] ExpiryNames = { "expiry", "expirydate", "exp", "nearestexpiry" };
    private static readonly string[] BarcodeNames = { "barcode", "ean", "upc" };

    public async Task<ProviderResult> GetAllAsync(CancellationToken ct = default)
    {
        var path = _pathAccessor();
        if (string.IsNullOrWhiteSpace(path))
            return ProviderResult.Fail("No CSV file chosen. Set the file path in Settings.");
        if (!File.Exists(path))
            return ProviderResult.Fail($"File not found: {path}");

        string text;
        try
        {
            // Read a copy so an open Excel window cannot block us, and so we never lock the file.
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            text = await reader.ReadToEndAsync(ct);
        }
        catch (Exception ex)
        {
            return ProviderResult.Fail($"Could not read the file: {ex.Message}");
        }

        var rows = Csv.Parse(text);
        if (rows.Count < 2) return ProviderResult.Fail("The file has no data rows.");

        var header = rows[0].Select(Normalise).ToArray();
        int Find(string[] aliases) => Array.FindIndex(header, h => aliases.Contains(h));

        var iCode = Find(CodeNames);
        var iName = Find(NameNames);
        var iQty = Find(QtyNames);

        if (iName < 0) return ProviderResult.Fail("Could not find a medicine name column. Expected a heading like 'Name' or 'Item Name'.");
        if (iQty < 0) return ProviderResult.Fail("Could not find a stock quantity column. Expected a heading like 'Qty', 'Stock' or 'Balance'.");

        var iGeneric = Find(GenericNames);
        var iCompany = Find(CompanyNames);
        var iPackUnit = Find(PackUnitNames);
        var iPerPack = Find(PerPackNames);
        var iPrice = Find(PriceNames);
        var iSupplier = Find(SupplierNames);
        var iSold30 = Find(Sold30Names);
        var iSold90 = Find(Sold90Names);
        var iLastSale = Find(LastSaleNames);
        var iExpiry = Find(ExpiryNames);
        var iBarcode = Find(BarcodeNames);

        var items = new List<MedicineStock>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var skipped = 0;

        for (var r = 1; r < rows.Count; r++)
        {
            var row = rows[r];
            string Cell(int i) => i >= 0 && i < row.Length ? row[i].Trim() : "";

            var name = Cell(iName);
            if (name.Length == 0) { skipped++; continue; }

            var code = iCode >= 0 ? Cell(iCode) : "";
            if (code.Length == 0) code = "CSV-" + r.ToString("D5", CultureInfo.InvariantCulture);
            if (!seen.Add(code)) code = code + "-" + r.ToString(CultureInfo.InvariantCulture);

            var perPack = Num(Cell(iPerPack));
            items.Add(new MedicineStock
            {
                ItemCode = code,
                Name = name,
                Generic = Blank(Cell(iGeneric)),
                Company = Blank(Cell(iCompany)),
                Barcode = Blank(Cell(iBarcode)),
                PackUnit = Cell(iPackUnit) is { Length: > 0 } u ? u : "Pack",
                UnitsPerPack = perPack > 0m ? perPack : 1m,
                QtyBaseUnits = Num(Cell(iQty)),
                RetailPrice = iPrice >= 0 ? Num(Cell(iPrice)) : null,
                Supplier = Blank(Cell(iSupplier)),
                Sold30 = Num(Cell(iSold30)),
                Sold90 = Num(Cell(iSold90)),
                LastSaleDate = Date(Cell(iLastSale)),
                NearestExpiry = Date(Cell(iExpiry)),
            });
        }

        if (items.Count == 0) return ProviderResult.Fail("No usable rows found in the file.");

        var msg = $"Read {items.Count} medicines from {Path.GetFileName(path)}";
        if (skipped > 0) msg += $" ({skipped} rows skipped - no name)";
        if (iPerPack < 0) msg += ". No pack-size column found, so quantities are treated as packs.";
        return ProviderResult.Success(items, msg);
    }

    public async Task<ProviderResult> TestAsync(CancellationToken ct = default)
    {
        var result = await GetAllAsync(ct);
        return result.Ok
            ? ProviderResult.Success(Array.Empty<MedicineStock>(), result.Message)
            : result;
    }

    private static string Normalise(string s) =>
        new string(s.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();

    private static string? Blank(string s) => s.Length == 0 ? null : s;

    private static decimal Num(string s)
    {
        if (s.Length == 0) return 0m;
        var cleaned = new string(s.Where(c => char.IsDigit(c) || c == '.' || c == '-').ToArray());
        return decimal.TryParse(cleaned, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : 0m;
    }

    private static DateOnly? Date(string s)
    {
        if (s.Length == 0) return null;
        if (DateOnly.TryParse(s, CultureInfo.InvariantCulture, out var d)) return d;
        string[] formats = { "dd/MM/yyyy", "d/M/yyyy", "MM/dd/yyyy", "yyyy-MM-dd", "dd-MM-yyyy", "MM/yyyy", "MM-yyyy" };
        foreach (var f in formats)
            if (DateTime.TryParseExact(s, f, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
                return DateOnly.FromDateTime(dt);
        return null;
    }
}
