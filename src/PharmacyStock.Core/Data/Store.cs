using System.Globalization;
using Microsoft.Data.Sqlite;
using PharmacyStock.Models;

namespace PharmacyStock.Data;

/// <summary>
/// The app's own database: shelf levels, alert history, the buying list and settings.
///
/// This is the ONLY database the app writes to. The pharmacy system (SeaSoft) is read
/// from and never written to - that separation is the whole safety design.
/// </summary>
public sealed class Store
{
    private readonly string _connectionString;

    public Store(string? databaseFile = null)
    {
        var file = databaseFile ?? AppPaths.DatabaseFile;
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = file,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
        }.ToString();

        Initialise();
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA journal_mode=WAL; PRAGMA busy_timeout=5000; PRAGMA foreign_keys=ON;";
        pragma.ExecuteNonQuery();
        return connection;
    }

    private void Initialise()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS settings (
                key   TEXT PRIMARY KEY,
                value TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS shelf_level (
                item_code          TEXT PRIMARY KEY,
                level_packs        REAL NOT NULL DEFAULT 0,
                is_managed         INTEGER NOT NULL DEFAULT 1,
                preferred_supplier TEXT,
                notes              TEXT,
                source             TEXT NOT NULL DEFAULT 'manual',
                updated_at         TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS alert_state (
                item_code       TEXT PRIMARY KEY,
                state           INTEGER NOT NULL,
                qty_packs       REAL NOT NULL,
                level_packs     REAL NOT NULL,
                since           TEXT NOT NULL,
                acknowledged_at TEXT,
                snooze_until    TEXT
            );

            CREATE TABLE IF NOT EXISTS alert_event (
                id          INTEGER PRIMARY KEY AUTOINCREMENT,
                item_code   TEXT NOT NULL,
                name        TEXT NOT NULL,
                from_state  INTEGER NOT NULL,
                to_state    INTEGER NOT NULL,
                qty_packs   REAL NOT NULL,
                level_packs REAL NOT NULL,
                at_utc      TEXT NOT NULL,
                seen        INTEGER NOT NULL DEFAULT 0
            );
            CREATE INDEX IF NOT EXISTS ix_alert_event_at ON alert_event(at_utc DESC);

            CREATE TABLE IF NOT EXISTS order_line (
                id          INTEGER PRIMARY KEY AUTOINCREMENT,
                item_code   TEXT NOT NULL,
                status      INTEGER NOT NULL,
                qty_packs   REAL NOT NULL,
                supplier    TEXT,
                notes       TEXT,
                created_at  TEXT NOT NULL,
                ordered_at  TEXT,
                expected_at TEXT,
                received_at TEXT
            );
            CREATE INDEX IF NOT EXISTS ix_order_line_item ON order_line(item_code);

            CREATE TABLE IF NOT EXISTS sync_log (
                id          INTEGER PRIMARY KEY AUTOINCREMENT,
                started_at  TEXT NOT NULL,
                finished_at TEXT,
                item_count  INTEGER NOT NULL DEFAULT 0,
                ok          INTEGER NOT NULL DEFAULT 0,
                message     TEXT
            );
            """;
        command.ExecuteNonQuery();
    }

    // ---------------------------------------------------------------- settings

    public Dictionary<string, string> GetSettingsRaw()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT key, value FROM settings";
        using var reader = command.ExecuteReader();
        while (reader.Read()) result[reader.GetString(0)] = reader.GetString(1);
        return result;
    }

    public void SetSettings(IEnumerable<KeyValuePair<string, string>> values)
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        foreach (var pair in values)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO settings (key, value) VALUES ($k, $v)
                ON CONFLICT(key) DO UPDATE SET value = excluded.value
                """;
            command.Parameters.AddWithValue("$k", pair.Key);
            command.Parameters.AddWithValue("$v", pair.Value);
            command.ExecuteNonQuery();
        }
        transaction.Commit();
    }

    // ------------------------------------------------------------ shelf levels

    public Dictionary<string, ShelfLevel> GetShelfLevels()
    {
        var result = new Dictionary<string, ShelfLevel>(StringComparer.OrdinalIgnoreCase);
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT item_code, level_packs, is_managed, preferred_supplier, notes, source, updated_at
            FROM shelf_level
            """;
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var code = reader.GetString(0);
            result[code] = new ShelfLevel
            {
                ItemCode = code,
                LevelPacks = (decimal)reader.GetDouble(1),
                IsManaged = reader.GetInt64(2) != 0,
                PreferredSupplier = reader.IsDBNull(3) ? null : reader.GetString(3),
                Notes = reader.IsDBNull(4) ? null : reader.GetString(4),
                Source = reader.GetString(5),
                UpdatedAt = ParseUtc(reader.GetString(6)) ?? DateTime.UtcNow,
            };
        }
        return result;
    }

    public void SaveShelfLevels(IEnumerable<ShelfLevel> levels)
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        foreach (var level in levels)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO shelf_level
                    (item_code, level_packs, is_managed, preferred_supplier, notes, source, updated_at)
                VALUES ($code, $level, $managed, $supplier, $notes, $source, $updated)
                ON CONFLICT(item_code) DO UPDATE SET
                    level_packs        = excluded.level_packs,
                    is_managed         = excluded.is_managed,
                    preferred_supplier = excluded.preferred_supplier,
                    notes              = excluded.notes,
                    source             = excluded.source,
                    updated_at         = excluded.updated_at
                """;
            command.Parameters.AddWithValue("$code", level.ItemCode);
            command.Parameters.AddWithValue("$level", (double)level.LevelPacks);
            command.Parameters.AddWithValue("$managed", level.IsManaged ? 1 : 0);
            command.Parameters.AddWithValue("$supplier", (object?)level.PreferredSupplier ?? DBNull.Value);
            command.Parameters.AddWithValue("$notes", (object?)level.Notes ?? DBNull.Value);
            command.Parameters.AddWithValue("$source", level.Source);
            command.Parameters.AddWithValue("$updated", Iso(level.UpdatedAt));
            command.ExecuteNonQuery();
        }
        transaction.Commit();
    }

    public int CountShelfLevels()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM shelf_level WHERE level_packs > 0 AND is_managed = 1";
        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    // ----------------------------------------------------------- alert states

    public Dictionary<string, AlertState> GetAlertStates()
    {
        var result = new Dictionary<string, AlertState>(StringComparer.OrdinalIgnoreCase);
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT item_code, state, qty_packs, level_packs, since, acknowledged_at, snooze_until
            FROM alert_state
            """;
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var code = reader.GetString(0);
            result[code] = new AlertState
            {
                ItemCode = code,
                State = (StockState)reader.GetInt64(1),
                QtyPacks = (decimal)reader.GetDouble(2),
                LevelPacks = (decimal)reader.GetDouble(3),
                Since = ParseUtc(reader.GetString(4)) ?? DateTime.UtcNow,
                AcknowledgedAt = reader.IsDBNull(5) ? null : ParseUtc(reader.GetString(5)),
                SnoozeUntil = reader.IsDBNull(6) ? null : ParseUtc(reader.GetString(6)),
            };
        }
        return result;
    }

    public void SaveAlertStates(IEnumerable<AlertState> states)
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        foreach (var state in states)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO alert_state
                    (item_code, state, qty_packs, level_packs, since, acknowledged_at, snooze_until)
                VALUES ($code, $state, $qty, $level, $since, $ack, $snooze)
                ON CONFLICT(item_code) DO UPDATE SET
                    state           = excluded.state,
                    qty_packs       = excluded.qty_packs,
                    level_packs     = excluded.level_packs,
                    since           = excluded.since,
                    acknowledged_at = excluded.acknowledged_at,
                    snooze_until    = excluded.snooze_until
                """;
            command.Parameters.AddWithValue("$code", state.ItemCode);
            command.Parameters.AddWithValue("$state", (int)state.State);
            command.Parameters.AddWithValue("$qty", (double)state.QtyPacks);
            command.Parameters.AddWithValue("$level", (double)state.LevelPacks);
            command.Parameters.AddWithValue("$since", Iso(state.Since));
            command.Parameters.AddWithValue("$ack", state.AcknowledgedAt.HasValue ? Iso(state.AcknowledgedAt.Value) : DBNull.Value);
            command.Parameters.AddWithValue("$snooze", state.SnoozeUntil.HasValue ? Iso(state.SnoozeUntil.Value) : DBNull.Value);
            command.ExecuteNonQuery();
        }
        transaction.Commit();
    }

    public void AcknowledgeAlert(string itemCode, DateTime whenUtc)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE alert_state SET acknowledged_at = $when WHERE item_code = $code";
        command.Parameters.AddWithValue("$when", Iso(whenUtc));
        command.Parameters.AddWithValue("$code", itemCode);
        command.ExecuteNonQuery();
    }

    public void SnoozeAlert(string itemCode, DateTime untilUtc)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO alert_state (item_code, state, qty_packs, level_packs, since, snooze_until)
            VALUES ($code, 0, 0, 0, $now, $until)
            ON CONFLICT(item_code) DO UPDATE SET snooze_until = excluded.snooze_until
            """;
        command.Parameters.AddWithValue("$code", itemCode);
        command.Parameters.AddWithValue("$now", Iso(DateTime.UtcNow));
        command.Parameters.AddWithValue("$until", Iso(untilUtc));
        command.ExecuteNonQuery();
    }

    // ----------------------------------------------------------- alert events

    public void AddAlertEvents(IEnumerable<AlertEvent> events)
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        foreach (var item in events)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO alert_event
                    (item_code, name, from_state, to_state, qty_packs, level_packs, at_utc, seen)
                VALUES ($code, $name, $from, $to, $qty, $level, $at, 0)
                """;
            command.Parameters.AddWithValue("$code", item.ItemCode);
            command.Parameters.AddWithValue("$name", item.Name);
            command.Parameters.AddWithValue("$from", (int)item.From);
            command.Parameters.AddWithValue("$to", (int)item.To);
            command.Parameters.AddWithValue("$qty", (double)item.QtyPacks);
            command.Parameters.AddWithValue("$level", (double)item.LevelPacks);
            command.Parameters.AddWithValue("$at", Iso(item.AtUtc));
            command.ExecuteNonQuery();
        }
        transaction.Commit();
    }

    public List<AlertEvent> GetAlertEvents(int limit = 200, bool unseenOnly = false)
    {
        var result = new List<AlertEvent>();
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT id, item_code, name, from_state, to_state, qty_packs, level_packs, at_utc, seen
            FROM alert_event
            {(unseenOnly ? "WHERE seen = 0" : "")}
            ORDER BY at_utc DESC, id DESC
            LIMIT $limit
            """;
        command.Parameters.AddWithValue("$limit", limit);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            result.Add(new AlertEvent
            {
                Id = reader.GetInt64(0),
                ItemCode = reader.GetString(1),
                Name = reader.GetString(2),
                From = (StockState)reader.GetInt64(3),
                To = (StockState)reader.GetInt64(4),
                QtyPacks = (decimal)reader.GetDouble(5),
                LevelPacks = (decimal)reader.GetDouble(6),
                AtUtc = ParseUtc(reader.GetString(7)) ?? DateTime.UtcNow,
                Seen = reader.GetInt64(8) != 0,
            });
        }
        return result;
    }

    public int CountUnseenAlerts()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM alert_event WHERE seen = 0";
        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    public void MarkAlertsSeen()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE alert_event SET seen = 1 WHERE seen = 0";
        command.ExecuteNonQuery();
    }

    // ------------------------------------------------------------ order lines

    /// <summary>The newest not-yet-received order line for each medicine.</summary>
    public Dictionary<string, OrderLine> GetOpenOrders()
    {
        var result = new Dictionary<string, OrderLine>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in QueryOrders("WHERE status <> 2 ORDER BY id DESC"))
            result.TryAdd(line.ItemCode, line);
        return result;
    }

    public List<OrderLine> GetOrders(bool includeReceived = false)
        => QueryOrders(includeReceived ? "ORDER BY id DESC" : "WHERE status <> 2 ORDER BY id DESC");

    private List<OrderLine> QueryOrders(string where)
    {
        var result = new List<OrderLine>();
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT id, item_code, status, qty_packs, supplier, notes,
                   created_at, ordered_at, expected_at, received_at
            FROM order_line
            {where}
            """;
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            result.Add(new OrderLine
            {
                Id = reader.GetInt64(0),
                ItemCode = reader.GetString(1),
                Status = (OrderStatus)reader.GetInt64(2),
                QtyPacks = (decimal)reader.GetDouble(3),
                Supplier = reader.IsDBNull(4) ? null : reader.GetString(4),
                Notes = reader.IsDBNull(5) ? null : reader.GetString(5),
                CreatedAt = ParseUtc(reader.GetString(6)) ?? DateTime.UtcNow,
                OrderedAt = reader.IsDBNull(7) ? null : ParseUtc(reader.GetString(7)),
                ExpectedAt = reader.IsDBNull(8) ? null : ParseUtc(reader.GetString(8)),
                ReceivedAt = reader.IsDBNull(9) ? null : ParseUtc(reader.GetString(9)),
            });
        }
        return result;
    }

    public long UpsertOrder(OrderLine line)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();

        if (line.Id > 0)
        {
            command.CommandText = """
                UPDATE order_line SET
                    status = $status, qty_packs = $qty, supplier = $supplier, notes = $notes,
                    ordered_at = $ordered, expected_at = $expected, received_at = $received
                WHERE id = $id
                """;
            command.Parameters.AddWithValue("$id", line.Id);
        }
        else
        {
            command.CommandText = """
                INSERT INTO order_line
                    (item_code, status, qty_packs, supplier, notes, created_at, ordered_at, expected_at, received_at)
                VALUES ($code, $status, $qty, $supplier, $notes, $created, $ordered, $expected, $received);
                SELECT last_insert_rowid();
                """;
            command.Parameters.AddWithValue("$code", line.ItemCode);
            command.Parameters.AddWithValue("$created", Iso(line.CreatedAt));
        }

        command.Parameters.AddWithValue("$status", (int)line.Status);
        command.Parameters.AddWithValue("$qty", (double)line.QtyPacks);
        command.Parameters.AddWithValue("$supplier", (object?)line.Supplier ?? DBNull.Value);
        command.Parameters.AddWithValue("$notes", (object?)line.Notes ?? DBNull.Value);
        command.Parameters.AddWithValue("$ordered", line.OrderedAt.HasValue ? Iso(line.OrderedAt.Value) : DBNull.Value);
        command.Parameters.AddWithValue("$expected", line.ExpectedAt.HasValue ? Iso(line.ExpectedAt.Value) : DBNull.Value);
        command.Parameters.AddWithValue("$received", line.ReceivedAt.HasValue ? Iso(line.ReceivedAt.Value) : DBNull.Value);

        if (line.Id > 0) { command.ExecuteNonQuery(); return line.Id; }
        return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    public void DeleteOrder(long id)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM order_line WHERE id = $id";
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    // --------------------------------------------------------------- sync log

    public long StartSync(DateTime startedUtc)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO sync_log (started_at) VALUES ($at); SELECT last_insert_rowid();";
        command.Parameters.AddWithValue("$at", Iso(startedUtc));
        return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    public void FinishSync(long id, DateTime finishedUtc, int itemCount, bool ok, string? message)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE sync_log
            SET finished_at = $at, item_count = $count, ok = $ok, message = $message
            WHERE id = $id
            """;
        command.Parameters.AddWithValue("$at", Iso(finishedUtc));
        command.Parameters.AddWithValue("$count", itemCount);
        command.Parameters.AddWithValue("$ok", ok ? 1 : 0);
        command.Parameters.AddWithValue("$message", (object?)message ?? DBNull.Value);
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    /// <summary>Housekeeping so the file cannot grow without limit on a counter PC.</summary>
    public void Prune(int keepAlertDays = 120, int keepSyncRows = 500)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            DELETE FROM alert_event WHERE at_utc < $cutoff;
            DELETE FROM sync_log WHERE id NOT IN (SELECT id FROM sync_log ORDER BY id DESC LIMIT $keep);
            """;
        command.Parameters.AddWithValue("$cutoff", Iso(DateTime.UtcNow.AddDays(-keepAlertDays)));
        command.Parameters.AddWithValue("$keep", keepSyncRows);
        command.ExecuteNonQuery();
    }

    // --------------------------------------------------------------- helpers

    private static string Iso(DateTime value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    /// <summary>
    /// Reads a timestamp back out of the database. Everything is stored in round-trip
    /// format ("O"), which already carries the time zone, so RoundtripKind is used on its
    /// own - it cannot be combined with AdjustToUniversal.
    /// </summary>
    private static DateTime? ParseUtc(string? text)
    {
        if (!DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var value))
            return null;

        return value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };
    }
}
