using PharmacyStock.Data;
using PharmacyStock.Diagnostics;
using PharmacyStock.Models;
using PharmacyStock.Providers;

namespace PharmacyStock.Services;

/// <summary>
/// The latest read from the source pharmacy system.
/// <para>
/// <see cref="AtUtc"/> is the last time stock was read SUCCESSFULLY, and
/// <see cref="Items"/> always holds that successful read. A failed attempt updates
/// <see cref="AttemptUtc"/> and <see cref="Message"/> but deliberately leaves the
/// medicine list alone - if the server goes down mid-afternoon the board keeps showing
/// the last known figures with a warning, rather than going blank.
/// </para>
/// </summary>
public sealed record Snapshot(
    IReadOnlyList<MedicineStock> Items,
    DateTime? AtUtc,
    DateTime? AttemptUtc,
    bool Ok,
    string? Message,
    string SourceKey)
{
    public static readonly Snapshot Empty =
        new(Array.Empty<MedicineStock>(), null, null, false, "Stock has not been read yet.", "demo");
}

public sealed record StockQuery(
    string? Search = null,
    string? Status = null,
    string? Supplier = null,
    string? Company = null,
    bool? Dead = null,
    string Sort = "urgency",
    bool Desc = false,
    int Page = 1,
    int PageSize = 50);

public sealed record Paged<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize)
{
    public int Pages => PageSize <= 0 ? 1 : Math.Max(1, (int)Math.Ceiling(Total / (double)PageSize));
}

/// <summary>
/// The heart of the app. Holds the latest stock read from the source system, joins it with
/// the user's shelf levels and the buying list, decides each medicine's state, and records
/// the state changes worth telling the user about.
/// </summary>
public sealed class StockService
{
    private readonly Store _store;
    private readonly SettingsService _settings;
    private volatile Snapshot _snapshot = Snapshot.Empty;

    public StockService(Store store, SettingsService settings)
    {
        _store = store;
        _settings = settings;
    }

    public Snapshot Current => _snapshot;

    // ------------------------------------------------------------------ state

    /// <summary>
    /// Decides where a medicine stands. The hysteresis rule is the important part: once a
    /// medicine has been flagged, it is not cleared the instant it touches its shelf level,
    /// because a medicine sitting exactly on its level would otherwise flip between LOW and
    /// OK all day long as customers buy it.
    /// </summary>
    public static StockState Evaluate(
        decimal qtyPacks, decimal levelPacks, bool isManaged, StockState previous, decimal hysteresisPacks)
    {
        if (!isManaged || levelPacks <= 0m) return StockState.Unmanaged;
        if (qtyPacks <= 0m) return StockState.Out;
        if (qtyPacks < levelPacks) return StockState.Low;

        if (previous is StockState.Low or StockState.Out && qtyPacks < levelPacks + hysteresisPacks)
            return StockState.Low;

        return StockState.Ok;
    }

    /// <summary>Packs to buy so the medicine ends up comfortably back above its shelf level.</summary>
    public static decimal Shortfall(decimal qtyPacks, decimal levelPacks, decimal hysteresisPacks)
    {
        if (levelPacks <= 0m) return 0m;
        if (qtyPacks >= levelPacks) return 0m;
        var target = levelPacks + hysteresisPacks;
        var need = decimal.Ceiling(target - qtyPacks);
        return need < 1m ? 1m : need;
    }

    // ----------------------------------------------------------------- ingest

    /// <summary>
    /// Takes a fresh read from the source system, works out every medicine's state, and
    /// writes an alert for each medicine whose state actually changed. Alerts fire on the
    /// CHANGE only - never on the condition - otherwise the same shortages would be
    /// re-announced every single sync and the user would stop reading them.
    /// </summary>
    public void Ingest(ProviderResult result, string sourceKey)
    {
        var now = DateTime.UtcNow;

        if (!result.Ok)
        {
            // Keep the last good figures on screen and just flag the problem.
            _snapshot = _snapshot with { AttemptUtc = now, Ok = false, Message = result.Message, SourceKey = sourceKey };
            return;
        }

        _snapshot = new Snapshot(result.Items, now, now, true, result.Message, sourceKey);

        var settings = _settings.Current;
        var levels = _store.GetShelfLevels();
        var previous = _store.GetAlertStates();
        var openOrders = _store.GetOpenOrders();

        var statesToSave = new List<AlertState>();
        var events = new List<AlertEvent>();

        foreach (var medicine in result.Items)
        {
            levels.TryGetValue(medicine.ItemCode, out var level);
            var levelPacks = level?.LevelPacks ?? 0m;
            var isManaged = level?.IsManaged ?? false;
            var qtyPacks = medicine.QtyPacks;

            var hadPrevious = previous.TryGetValue(medicine.ItemCode, out var before);
            var previousState = before?.State ?? StockState.Unmanaged;

            var state = Evaluate(qtyPacks, levelPacks, isManaged, previousState, settings.HysteresisPacks);

            if (!hadPrevious)
            {
                // First time we have seen this medicine: record where it stands, stay silent.
                // Without this, the very first sync would raise hundreds of alerts at once.
                statesToSave.Add(new AlertState
                {
                    ItemCode = medicine.ItemCode,
                    State = state,
                    QtyPacks = qtyPacks,
                    LevelPacks = levelPacks,
                    Since = now,
                });
                continue;
            }

            if (state == previousState)
            {
                // Same state - just refresh the figures, keep Since and any acknowledgement.
                statesToSave.Add(before! with { QtyPacks = qtyPacks, LevelPacks = levelPacks });
                continue;
            }

            statesToSave.Add(new AlertState
            {
                ItemCode = medicine.ItemCode,
                State = state,
                QtyPacks = qtyPacks,
                LevelPacks = levelPacks,
                Since = now,
                AcknowledgedAt = null,
                SnoozeUntil = before!.SnoozeUntil,
            });

            if (!ShouldNotify(previousState, state, before, openOrders, medicine.ItemCode, now)) continue;

            events.Add(new AlertEvent
            {
                ItemCode = medicine.ItemCode,
                Name = medicine.Name,
                From = previousState,
                To = state,
                QtyPacks = qtyPacks,
                LevelPacks = levelPacks,
                AtUtc = now,
            });
        }

        if (statesToSave.Count > 0) _store.SaveAlertStates(statesToSave);
        if (events.Count > 0)
        {
            _store.AddAlertEvents(events);
            Log.Info($"Shelf-level alerts raised: {events.Count}");
        }
    }

    private static bool ShouldNotify(
        StockState from, StockState to, AlertState? before,
        Dictionary<string, OrderLine> openOrders, string itemCode, DateTime now)
    {
        // Turning a medicine on or off in the app is a settings change, not news.
        if (from == StockState.Unmanaged || to == StockState.Unmanaged) return false;

        // The user asked for quiet on this one.
        if (before is not null && before.IsSnoozed(now)) return false;

        // Already ordered, or the distributor has none either - it is handled, stop nagging.
        if (openOrders.TryGetValue(itemCode, out var order) && order.SuppressesAlerts
            && to is StockState.Low or StockState.Out) return false;

        // Worth saying: it fell short, or it came back.
        return to is StockState.Low or StockState.Out
            || (to is StockState.Ok && from is StockState.Low or StockState.Out);
    }

    // ------------------------------------------------------------------ views

    /// <summary>Builds every stock-board row: source data joined with our own settings.</summary>
    public List<StockView> BuildAll()
    {
        var settings = _settings.Current;
        var today = DateOnly.FromDateTime(DateTime.Today);
        var levels = _store.GetShelfLevels();
        var states = _store.GetAlertStates();
        var orders = _store.GetOpenOrders();

        var views = new List<StockView>(_snapshot.Items.Count);

        foreach (var medicine in _snapshot.Items)
        {
            levels.TryGetValue(medicine.ItemCode, out var level);
            states.TryGetValue(medicine.ItemCode, out var state);
            orders.TryGetValue(medicine.ItemCode, out var order);

            var levelPacks = level?.LevelPacks ?? 0m;
            var isManaged = level?.IsManaged ?? false;
            var qtyPacks = medicine.QtyPacks;
            var (wholePacks, loose) = medicine.PacksAndLoose();

            // Worked out fresh on every request rather than read from the stored state, so
            // typing a shelf level updates the board straight away instead of waiting for
            // the next sync. The stored state is still passed in, because that is what the
            // hysteresis rule needs to know.
            var effectiveState = Evaluate(
                qtyPacks, levelPacks, isManaged, state?.State ?? StockState.Unmanaged, settings.HysteresisPacks);

            views.Add(new StockView
            {
                ItemCode = medicine.ItemCode,
                Name = medicine.Name,
                Generic = medicine.Generic,
                Company = medicine.Company,
                Supplier = level?.PreferredSupplier ?? medicine.Supplier,
                PackUnit = medicine.PackUnit,
                UnitsPerPack = medicine.UnitsPerPack,
                QtyPacks = qtyPacks,
                WholePacks = wholePacks,
                LooseUnits = loose,
                LevelPacks = level is null ? null : levelPacks,
                IsManaged = isManaged,
                SuggestedLevelPacks = ShelfLevelAdvisor.Suggest(medicine, settings, today),
                State = effectiveState,
                ShortfallPacks = Shortfall(qtyPacks, levelPacks, settings.HysteresisPacks),
                RetailPrice = medicine.RetailPrice,
                Sold30 = medicine.Sold30,
                Sold90 = medicine.Sold90,
                LastSaleDate = medicine.LastSaleDate,
                NearestExpiry = medicine.NearestExpiry,
                OrderStatus = order?.Status.ToString(),
                OrderId = order?.Id,
                OrderedAt = order?.OrderedAt,
                SnoozeUntil = state?.SnoozeUntil,
                IsDeadStock = ShelfLevelAdvisor.IsDeadStock(medicine, settings, today),
            });
        }

        return views;
    }

    public Paged<StockView> Query(StockQuery query)
    {
        IEnumerable<StockView> rows = BuildAll();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            rows = rows.Where(r =>
                r.Name.Contains(term, StringComparison.OrdinalIgnoreCase)
                || (r.Generic?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false)
                || (r.Company?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false)
                || r.ItemCode.Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        rows = query.Status?.ToLowerInvariant() switch
        {
            "out" => rows.Where(r => r.State == StockState.Out),
            "low" => rows.Where(r => r.State == StockState.Low),
            "ok" => rows.Where(r => r.State == StockState.Ok),
            "unmanaged" => rows.Where(r => r.State == StockState.Unmanaged),
            "needs" => rows.Where(r => r.State is StockState.Out or StockState.Low),
            _ => rows,
        };

        if (!string.IsNullOrWhiteSpace(query.Supplier))
            rows = rows.Where(r => string.Equals(r.Supplier, query.Supplier, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(query.Company))
            rows = rows.Where(r => string.Equals(r.Company, query.Company, StringComparison.OrdinalIgnoreCase));

        if (query.Dead is bool dead)
            rows = rows.Where(r => r.IsDeadStock == dead);

        rows = Sort(rows, query.Sort, query.Desc);

        var all = rows.ToList();
        var pageSize = Math.Clamp(query.PageSize, 10, 1000);
        var page = Math.Max(1, query.Page);
        var items = all.Skip((page - 1) * pageSize).Take(pageSize).ToList();

        return new Paged<StockView>(items, all.Count, page, pageSize);
    }

    private static IEnumerable<StockView> Sort(IEnumerable<StockView> rows, string? sort, bool desc)
    {
        var key = (sort ?? "").Trim().ToLowerInvariant();

        // Default order is "what needs buying first": out of stock at the top, then the
        // biggest shortfall, then the fastest mover. The top of the list is the real work.
        if (key is "" or "urgency")
        {
            return rows
                .OrderByDescending(r => r.State == StockState.Out)
                .ThenByDescending(r => r.State == StockState.Low)
                .ThenByDescending(r => r.ShortfallPacks)
                .ThenByDescending(r => r.Sold30)
                .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase);
        }

        var ordered = key switch
        {
            "name" => desc
                ? rows.OrderByDescending(r => r.Name, StringComparer.OrdinalIgnoreCase)
                : rows.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase),
            "supplier" => desc
                ? rows.OrderByDescending(r => r.Supplier ?? "", StringComparer.OrdinalIgnoreCase)
                : rows.OrderBy(r => r.Supplier ?? "", StringComparer.OrdinalIgnoreCase),
            "company" => desc
                ? rows.OrderByDescending(r => r.Company ?? "", StringComparer.OrdinalIgnoreCase)
                : rows.OrderBy(r => r.Company ?? "", StringComparer.OrdinalIgnoreCase),
            "qty" => Direct(rows, r => r.QtyPacks, desc),
            "level" => Direct(rows, r => r.LevelPacks ?? -1m, desc),
            "shortfall" => Direct(rows, r => r.ShortfallPacks, desc),
            "sold" => Direct(rows, r => r.Sold30, desc),
            "expiry" => Direct(rows, r => r.NearestExpiry ?? DateOnly.MaxValue, desc),
            _ => Direct(rows, r => r.ShortfallPacks, true),
        };

        return ordered.ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase);
    }

    private static IOrderedEnumerable<StockView> Direct<TKey>(
        IEnumerable<StockView> rows, Func<StockView, TKey> key, bool desc)
        => desc ? rows.OrderByDescending(key) : rows.OrderBy(key);

    // -------------------------------------------------------------- dashboard

    public DashboardCounts Dashboard()
    {
        var views = BuildAll();
        var orders = _store.GetOrders(includeReceived: false);

        var estimate = views
            .Where(v => v.State is StockState.Out or StockState.Low)
            .Sum(v => (v.RetailPrice ?? 0m) * v.ShortfallPacks);

        return new DashboardCounts
        {
            OutOfStock = views.Count(v => v.State == StockState.Out),
            BelowLevel = views.Count(v => v.State == StockState.Low),
            Ok = views.Count(v => v.State == StockState.Ok),
            Unmanaged = views.Count(v => v.State == StockState.Unmanaged),
            TotalItems = views.Count,
            OpenOrders = orders.Count,
            NewAlerts = _store.CountUnseenAlerts(),
            OrderValueEstimate = decimal.Round(estimate, 0),
            LastSyncUtc = _snapshot.AtUtc,
            SyncStatus = _snapshot.AttemptUtc is null ? "never" : _snapshot.Ok ? "ok" : "error",
            SyncMessage = _snapshot.Message,
            DataSource = _snapshot.SourceKey,
        };
    }

    public List<string> Suppliers() => BuildAll()
        .Select(v => v.Supplier)
        .Where(s => !string.IsNullOrWhiteSpace(s))
        .Select(s => s!)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
        .ToList();
}
