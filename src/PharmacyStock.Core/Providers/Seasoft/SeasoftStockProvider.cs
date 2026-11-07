using System.Data;
using System.Globalization;
using PharmacyStock.Models;

namespace PharmacyStock.Providers.Seasoft;

/// <summary>
/// Reads live stock straight from the SeaSoft database.
///
/// Strictly read-only: the only statement it ever sends is a SELECT, and every one is
/// checked by <see cref="SqlGuard"/> before it leaves this program. Nothing in SeaSoft
/// is inserted, updated, deleted or locked.
/// </summary>
public sealed class SeasoftStockProvider : IStockProvider
{
    private readonly Func<SeasoftConnection> _connection;
    private readonly Func<SeasoftMapping> _mapping;

    public SeasoftStockProvider(Func<SeasoftConnection> connection, Func<SeasoftMapping> mapping)
    {
        _connection = connection;
        _mapping = mapping;
    }

    public string Key => "seasoft";
    public string Display => "SeaSoft database (live stock)";

    public async Task<ProviderResult> GetAllAsync(CancellationToken ct = default)
    {
        var connection = _connection();
        var mapping = _mapping();

        if (!connection.IsConfigured)
            return ProviderResult.Fail("SeaSoft is not connected yet. Use Settings, then \"Connect to SeaSoft\".");

        if (!mapping.IsComplete)
            return ProviderResult.Fail(mapping.WhatIsMissing());

        var sql = Sql(connection, mapping);

        try
        {
            SqlGuard.EnsureReadOnly(sql);
        }
        catch (InvalidOperationException ex)
        {
            return ProviderResult.Fail(ex.Message);
        }

        try
        {
            var items = await ReadAsync(connection, mapping, sql, ct);

            if (items.Count == 0)
                return ProviderResult.Fail("The query ran, but returned no medicines. Check the table chosen in \"Connect to SeaSoft\".");

            return ProviderResult.Success(items, $"Read {items.Count} medicines from SeaSoft.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return ProviderResult.Fail(Explain(ex));
        }
    }

    public async Task<ProviderResult> TestAsync(CancellationToken ct = default)
    {
        var connection = _connection();
        var mapping = _mapping();

        if (!connection.IsConfigured)
            return ProviderResult.Fail("Fill in the connection details first.");

        try
        {
            if (!mapping.IsComplete)
            {
                var browser = new SchemaBrowser(connection);
                var message = await browser.TestAsync(ct);
                return ProviderResult.Fail(message + " Now choose the table and columns.");
            }

            var sample = await ReadAsync(connection, mapping, Sql(connection, mapping), ct, limit: 200);
            if (sample.Count == 0)
                return ProviderResult.Fail("Connected, but the chosen table returned no rows.");

            var withStock = sample.Count(i => i.QtyBaseUnits > 0m);
            return ProviderResult.Success(Array.Empty<MedicineStock>(),
                $"Connected. Read {sample.Count} medicines, {withStock} with stock. " +
                $"First one: {sample[0].Name}.");
        }
        catch (Exception ex)
        {
            return ProviderResult.Fail(Explain(ex));
        }
    }

    private static string Sql(SeasoftConnection connection, SeasoftMapping mapping) =>
        mapping.CustomSql.Trim().Length > 0
            ? mapping.CustomSql.Trim()
            : $"SELECT * FROM {connection.Quote(mapping.Table)}";

    private static async Task<List<MedicineStock>> ReadAsync(
        SeasoftConnection connection, SeasoftMapping mapping, string sql,
        CancellationToken ct, int limit = int.MaxValue)
    {
        await using var db = connection.CreateConnection();
        await db.OpenAsync(ct);

        await using var command = db.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = 60;

        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleResult, ct);

        // Match columns loosely, so "Item_Name" and "ITEMNAME" both work.
        var ordinals = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < reader.FieldCount; i++)
            ordinals.TryAdd(SeasoftMapping.Normalise(reader.GetName(i)), i);

        int Ordinal(string column) =>
            column.Trim().Length > 0 && ordinals.TryGetValue(SeasoftMapping.Normalise(column), out var i) ? i : -1;

        var iCode = Ordinal(mapping.ItemCode);
        var iName = Ordinal(mapping.Name);
        var iQty = Ordinal(mapping.Qty);
        var iGeneric = Ordinal(mapping.Generic);
        var iCompany = Ordinal(mapping.Company);
        var iBarcode = Ordinal(mapping.Barcode);
        var iPackUnit = Ordinal(mapping.PackUnit);
        var iPerPack = Ordinal(mapping.UnitsPerPack);
        var iPrice = Ordinal(mapping.Price);
        var iSupplier = Ordinal(mapping.Supplier);
        var iSold30 = Ordinal(mapping.Sold30);
        var iSold90 = Ordinal(mapping.Sold90);
        var iLastSale = Ordinal(mapping.LastSale);
        var iExpiry = Ordinal(mapping.Expiry);

        if (iName < 0)
            throw new InvalidOperationException(
                $"The query has no column called \"{mapping.Name}\". Re-check the medicine name column.");
        if (iQty < 0)
            throw new InvalidOperationException(
                $"The query has no column called \"{mapping.Qty}\". Re-check the stock quantity column.");

        var items = new List<MedicineStock>();
        var codes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var rowNumber = 0;

        while (items.Count < limit && await reader.ReadAsync(ct))
        {
            rowNumber++;

            var name = Text(reader, iName);
            if (name.Length == 0) continue;

            var code = iCode >= 0 ? Text(reader, iCode) : "";
            if (code.Length == 0) code = "ROW-" + rowNumber.ToString("D6", CultureInfo.InvariantCulture);
            // Two medicines sharing a code would overwrite each other's shelf level.
            if (!codes.Add(code))
            {
                code = code + "-" + rowNumber.ToString(CultureInfo.InvariantCulture);
                codes.Add(code);
            }

            var perPack = iPerPack >= 0 ? Number(reader, iPerPack) : 0m;
            if (perPack <= 0m) perPack = 1m;

            var qty = Number(reader, iQty);
            // Most pharmacy systems count tablets; some count packs. Normalise to tablets.
            var baseUnits = mapping.QtyIsPacks ? qty * perPack : qty;

            items.Add(new MedicineStock
            {
                ItemCode = code,
                Name = name,
                Generic = Blank(reader, iGeneric),
                Company = Blank(reader, iCompany),
                Barcode = Blank(reader, iBarcode),
                PackUnit = iPackUnit >= 0 && Text(reader, iPackUnit).Length > 0 ? Text(reader, iPackUnit) : "Pack",
                UnitsPerPack = perPack,
                QtyBaseUnits = baseUnits,
                RetailPrice = iPrice >= 0 ? Number(reader, iPrice) : null,
                Supplier = Blank(reader, iSupplier),
                Sold30 = iSold30 >= 0 ? Number(reader, iSold30) : 0m,
                Sold90 = iSold90 >= 0 ? Number(reader, iSold90) : 0m,
                LastSaleDate = Date(reader, iLastSale),
                NearestExpiry = Date(reader, iExpiry),
            });
        }

        return items;
    }

    // ------------------------------------------------------------- conversions

    private static string Text(IDataRecord row, int ordinal)
    {
        if (ordinal < 0 || row.IsDBNull(ordinal)) return "";
        return row.GetValue(ordinal)?.ToString()?.Trim() ?? "";
    }

    private static string? Blank(IDataRecord row, int ordinal)
    {
        var text = Text(row, ordinal);
        return text.Length == 0 ? null : text;
    }

    private static decimal Number(IDataRecord row, int ordinal)
    {
        if (ordinal < 0 || row.IsDBNull(ordinal)) return 0m;

        var value = row.GetValue(ordinal);
        switch (value)
        {
            case decimal d: return d;
            case double dd: return (decimal)dd;
            case float f: return (decimal)f;
            case int i: return i;
            case long l: return l;
            case short s: return s;
            case byte b: return b;
            case bool bo: return bo ? 1m : 0m;
        }

        // Stored as text in some older systems, sometimes with units attached.
        var text = value?.ToString() ?? "";
        var cleaned = new string(text.Where(c => char.IsDigit(c) || c == '.' || c == '-' || c == ',').ToArray())
            .Replace(",", "");

        return decimal.TryParse(cleaned, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0m;
    }

    private static DateOnly? Date(IDataRecord row, int ordinal)
    {
        if (ordinal < 0 || row.IsDBNull(ordinal)) return null;

        var value = row.GetValue(ordinal);
        if (value is DateTime dt) return DateOnly.FromDateTime(dt);

        var text = value?.ToString() ?? "";
        if (text.Length == 0) return null;

        if (DateTime.TryParse(text, CultureInfo.CurrentCulture, DateTimeStyles.None, out var local))
            return DateOnly.FromDateTime(local);
        if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var inv))
            return DateOnly.FromDateTime(inv);

        string[] formats = { "dd/MM/yyyy", "d/M/yyyy", "MM/dd/yyyy", "yyyy-MM-dd", "dd-MM-yyyy", "MM/yyyy", "MM-yyyy" };
        foreach (var format in formats)
            if (DateTime.TryParseExact(text, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out var exact))
                return DateOnly.FromDateTime(exact);

        return null;
    }

    /// <summary>Turns a driver error into something a pharmacist can act on.</summary>
    private static string Explain(Exception ex)
    {
        var message = ex.Message;

        if (message.Contains("Login failed", StringComparison.OrdinalIgnoreCase))
            return "The database refused the username or password. Ask the SeaSoft vendor for a read-only login.";

        if (message.Contains("network-related", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("could not be established", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("No connection could be made", StringComparison.OrdinalIgnoreCase))
            return "Could not reach the server PC. Check it is switched on, that the name or IP is right, " +
                   "and that the database allows connections from other PCs on the network.";

        if (message.Contains("Unable to connect to any of the specified MySQL hosts", StringComparison.OrdinalIgnoreCase))
            return "Could not reach the MySQL server. Check the server name or IP and the port.";

        if (message.Contains("provider is not registered", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("Microsoft.ACE.OLEDB", StringComparison.OrdinalIgnoreCase))
            return "Windows does not have the Access database driver installed. " +
                   "Install the free \"Microsoft Access Database Engine 2016 Redistributable\" (64-bit) and try again.";

        if (message.Contains("Invalid object name", StringComparison.OrdinalIgnoreCase))
            return "That table does not exist. Open \"Connect to SeaSoft\" and pick the table from the list.";

        if (message.Contains("permission", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("denied", StringComparison.OrdinalIgnoreCase))
            return "The login does not have permission to read that table. Ask the vendor to grant read access.";

        if (message.Contains("timeout", StringComparison.OrdinalIgnoreCase))
            return "The database did not answer in time. If the pharmacy is busy, try again in a moment.";

        return "Could not read from SeaSoft: " + message;
    }
}
