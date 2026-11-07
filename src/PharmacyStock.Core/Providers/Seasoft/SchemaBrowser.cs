using System.Data;
using System.Data.Common;

namespace PharmacyStock.Providers.Seasoft;

public sealed record TablePreview(List<string> Columns, List<string[]> Rows);

/// <summary>
/// Lets the user look around the SeaSoft database from inside this program: which tables
/// exist, which columns each has, and a few sample rows.
///
/// This is what removes the guesswork. Rather than needing the vendor to describe the
/// schema, you can open the database, see a table called something like ITEMMASTER, look
/// at a few rows and recognise your own medicines.
///
/// Everything here is read-only.
/// </summary>
public sealed class SchemaBrowser
{
    private readonly SeasoftConnection _connection;

    public SchemaBrowser(SeasoftConnection connection) => _connection = connection;

    /// <summary>Opens and closes a connection, to prove the details are right.</summary>
    public async Task<string> TestAsync(CancellationToken ct = default)
    {
        await using var connection = _connection.CreateConnection();
        await connection.OpenAsync(ct);

        var version = "";
        try { version = connection.ServerVersion; } catch { /* not all drivers report it */ }

        var tables = await ListTablesAsync(ct);
        return $"Connected to {_connection.Describe()}." +
               (version.Length > 0 ? $" Server version {version}." : "") +
               $" {tables.Count} tables found.";
    }

    /// <summary>
    /// Schemas every engine keeps for its own bookkeeping. None of them hold medicines,
    /// and listing them just buries the tables the user is actually looking for.
    /// </summary>
    private static readonly string[] SystemSchemas =
    {
        "information_schema", "performance_schema", "mysql", "sys", "sysdiagrams", "pg_catalog",
    };

    /// <summary>Every table and view the login can see, most useful-looking first.</summary>
    public async Task<List<string>> ListTablesAsync(CancellationToken ct = default)
    {
        await using var connection = _connection.CreateConnection();
        await connection.OpenAsync(ct);

        // Tables in the database we actually connected to need no prefix; anything
        // else is shown fully qualified so it can still be selected.
        var ownDatabase = _connection.Database.Trim().Length > 0
            ? _connection.Database.Trim()
            : connection.Database ?? "";

        var names = new List<string>();

        foreach (var collection in new[] { "Tables", "Views" })
        {
            DataTable schema;
            try { schema = await connection.GetSchemaAsync(collection, ct); }
            catch { continue; }

            foreach (DataRow row in schema.Rows)
            {
                var name = Value(row, "TABLE_NAME");
                if (name.Length == 0) continue;

                var type = Value(row, "TABLE_TYPE");
                // Access and SQL Server both expose internal catalogue tables; skip them.
                if (type.Contains("SYSTEM", StringComparison.OrdinalIgnoreCase)) continue;
                if (name.StartsWith("MSys", StringComparison.OrdinalIgnoreCase)) continue;
                if (name.StartsWith("sys", StringComparison.Ordinal) && _connection.Engine == SeasoftEngine.SqlServer) continue;
                if (name.StartsWith("RDB$", StringComparison.OrdinalIgnoreCase)) continue;   // Firebird
                if (name.StartsWith("MON$", StringComparison.OrdinalIgnoreCase)) continue;   // Firebird

                var schemaName = Value(row, "TABLE_SCHEMA");
                if (schemaName.Length == 0) schemaName = Value(row, "TABLE_CATALOG");

                if (SystemSchemas.Contains(schemaName, StringComparer.OrdinalIgnoreCase)) continue;

                var ownTable = schemaName.Length == 0
                               || schemaName.Equals("dbo", StringComparison.OrdinalIgnoreCase)
                               || schemaName.Equals(ownDatabase, StringComparison.OrdinalIgnoreCase);

                var full = ownTable ? name : schemaName + "." + name;

                if (!names.Contains(full, StringComparer.OrdinalIgnoreCase)) names.Add(full);
            }
        }

        // Tables that look like a medicine list float to the top of the list.
        var interesting = new[] { "item", "medicine", "product", "drug", "stock", "inventory" };
        return names
            .OrderByDescending(n => interesting.Any(w => n.Contains(w, StringComparison.OrdinalIgnoreCase)))
            .ThenBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<List<string>> ListColumnsAsync(string table, CancellationToken ct = default)
    {
        var preview = await PreviewAsync(table, 1, ct);
        return preview.Columns;
    }

    /// <summary>A few rows from a table, so the user can recognise their own data.</summary>
    public async Task<TablePreview> PreviewAsync(string table, int rows = 25, CancellationToken ct = default)
    {
        var sql = _connection.TopRows(table, rows);
        SqlGuard.EnsureReadOnly(sql);
        return await RunAsync(sql, rows, ct);
    }

    /// <summary>Runs a hand-written SELECT, after checking it cannot change anything.</summary>
    public async Task<TablePreview> RunAsync(string sql, int maxRows, CancellationToken ct = default)
    {
        SqlGuard.EnsureReadOnly(sql);

        await using var connection = _connection.CreateConnection();
        await connection.OpenAsync(ct);

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = 30;

        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleResult, ct);

        var columns = new List<string>();
        for (var i = 0; i < reader.FieldCount; i++) columns.Add(reader.GetName(i));

        var result = new List<string[]>();
        while (result.Count < maxRows && await reader.ReadAsync(ct))
        {
            var row = new string[reader.FieldCount];
            for (var i = 0; i < reader.FieldCount; i++)
                row[i] = reader.IsDBNull(i) ? "" : (reader.GetValue(i)?.ToString() ?? "");
            result.Add(row);
        }

        return new TablePreview(columns, result);
    }

    private static string Value(DataRow row, string column)
    {
        if (!row.Table.Columns.Contains(column)) return "";
        var value = row[column];
        return value is null || value == DBNull.Value ? "" : value.ToString() ?? "";
    }
}
