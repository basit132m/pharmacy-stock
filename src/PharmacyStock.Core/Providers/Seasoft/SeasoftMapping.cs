namespace PharmacyStock.Providers.Seasoft;

/// <summary>
/// Which table and columns in SeaSoft hold the medicine list and current stock.
///
/// This is configuration, not code, because every pharmacy system names things
/// differently - and because a SeaSoft upgrade that renames a column should be a
/// change on this screen, not a new build of the program.
/// </summary>
public sealed record SeasoftMapping
{
    /// <summary>The table to read. Ignored when <see cref="CustomSql"/> is set.</summary>
    public string Table { get; init; } = "";

    /// <summary>
    /// A hand-written SELECT, for when stock is not a column but has to be worked out
    /// (opening + purchases - sales + returns). Checked by <see cref="SqlGuard"/>.
    /// </summary>
    public string CustomSql { get; init; } = "";

    // Column names. Only ItemCode, Name and Qty are required.
    public string ItemCode { get; init; } = "";
    public string Name { get; init; } = "";
    public string Qty { get; init; } = "";

    public string Generic { get; init; } = "";
    public string Company { get; init; } = "";
    public string Barcode { get; init; } = "";
    public string PackUnit { get; init; } = "";
    public string UnitsPerPack { get; init; } = "";
    public string Price { get; init; } = "";
    public string Supplier { get; init; } = "";
    public string Sold30 { get; init; } = "";
    public string Sold90 { get; init; } = "";
    public string LastSale { get; init; } = "";
    public string Expiry { get; init; } = "";

    /// <summary>
    /// True when quantities in <see cref="Qty"/> are already whole packs rather than
    /// tablets. Most pharmacy systems count the smallest unit, but not all.
    /// </summary>
    public bool QtyIsPacks { get; init; }

    public bool IsComplete =>
        (Table.Trim().Length > 0 || CustomSql.Trim().Length > 0)
        && Name.Trim().Length > 0
        && Qty.Trim().Length > 0;

    public string WhatIsMissing()
    {
        if (Table.Trim().Length == 0 && CustomSql.Trim().Length == 0) return "Choose the table that holds your medicines.";
        if (Name.Trim().Length == 0) return "Choose which column holds the medicine name.";
        if (Qty.Trim().Length == 0) return "Choose which column holds the stock quantity.";
        return "";
    }

    /// <summary>
    /// Guesses the mapping from the column names in the chosen table. Gets most pharmacy
    /// schemas right first time; anything wrong can be corrected on the same screen.
    /// </summary>
    public static SeasoftMapping Guess(string table, IEnumerable<string> columns)
    {
        var all = columns.ToList();

        string Find(params string[] aliases)
        {
            // Exact match on a normalised name first, then a contains match.
            foreach (var alias in aliases)
            {
                var hit = all.FirstOrDefault(c => Normalise(c) == alias);
                if (hit is not null) return hit;
            }
            foreach (var alias in aliases)
            {
                var hit = all.FirstOrDefault(c => Normalise(c).Contains(alias, StringComparison.Ordinal));
                if (hit is not null) return hit;
            }
            return "";
        }

        return new SeasoftMapping
        {
            Table = table,
            ItemCode = Find("itemcode", "productcode", "medicinecode", "code", "itemid", "productid", "sku", "id"),
            Name = Find("itemname", "medicinename", "productname", "drugname", "name", "description", "item", "product"),
            Qty = Find("closingbalance", "stockinhand", "currentstock", "balanceqty", "qtyinhand",
                       "onhand", "available", "balance", "closing", "stockqty", "stock", "qty", "quantity"),
            Generic = Find("genericname", "formula", "salt", "composition", "generic"),
            Company = Find("companyname", "manufacturer", "company", "brand", "mfg", "maker"),
            Barcode = Find("barcode", "ean", "upc"),
            PackUnit = Find("packunit", "unitname", "packing", "uom", "unit"),
            UnitsPerPack = Find("unitsperpack", "packsize", "perpack", "packqty", "conversion", "unitpack"),
            Price = Find("retailprice", "saleprice", "mrp", "retail", "price", "tp"),
            Supplier = Find("suppliername", "distributor", "supplier", "vendor", "party"),
            Sold30 = Find("sold30", "sales30", "qty30"),
            Sold90 = Find("sold90", "sales90", "qty90"),
            LastSale = Find("lastsaledate", "lastsold", "lastsale"),
            Expiry = Find("expirydate", "nearestexpiry", "expiry", "exp"),
        };
    }

    internal static string Normalise(string value) =>
        new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
}
