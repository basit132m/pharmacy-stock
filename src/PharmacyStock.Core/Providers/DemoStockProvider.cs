using System.Globalization;
using PharmacyStock.Models;

namespace PharmacyStock.Providers;

/// <summary>
/// Sample data source, used until the real SeaSoft database is connected.
///
/// The first read is deterministic, so the numbers on screen are stable. After that,
/// each read applies a little movement - some medicines sell, some get restocked - so
/// the shelf-level alerts can be seen working exactly as they will with live data.
/// </summary>
public sealed class DemoStockProvider : IStockProvider
{
    public string Key => "demo";
    public string Display => "Demo data (sample medicines, not your real stock)";

    private readonly object _gate = new();
    private readonly List<MedicineStock> _items = new();
    private readonly Dictionary<string, decimal> _velocity = new(StringComparer.OrdinalIgnoreCase);
    private readonly Random _drift = new(98765);

    public DemoStockProvider()
    {
        Build();
    }

    private void Build()
    {
        // Fixed seed: the opening snapshot is the same every time the app starts.
        var rng = new Random(20251107);
        var today = DateOnly.FromDateTime(DateTime.Today);
        var index = 0;

        foreach (var line in DemoCatalogue.Raw.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var text = line.Trim();
            if (text.Length == 0 || !text.Contains('|')) continue;

            var parts = text.Split('|');
            if (parts.Length < 7) continue;

            index++;
            var code = "SF-" + index.ToString("D4", CultureInfo.InvariantCulture);
            var unitsPerPack = decimal.Parse(parts[4], CultureInfo.InvariantCulture);
            var price = decimal.Parse(parts[5], CultureInfo.InvariantCulture);

            // How fast it moves, in packs per day. Mirrors a real pharmacy: a few fast
            // movers, a long tail of slow ones, and some stock that never sells.
            var roll = rng.NextDouble();
            decimal velocity = roll switch
            {
                < 0.22 => (decimal)(1.5 + rng.NextDouble() * 6.5),   // fast mover
                < 0.62 => (decimal)(0.30 + rng.NextDouble() * 1.2),  // steady
                < 0.88 => (decimal)(0.05 + rng.NextDouble() * 0.25), // slow
                _ => 0m,                                             // dead stock
            };
            velocity = decimal.Round(velocity, 3);

            var sold90 = decimal.Round(velocity * 90m * unitsPerPack, 0);
            var sold30 = decimal.Round(velocity * 30m * unitsPerPack * (decimal)(0.8 + rng.NextDouble() * 0.4), 0);

            // Where this medicine sits right now. Deliberately seeded with a realistic
            // mix so the dashboard has something to show on first launch.
            var natural = velocity * 3m * 1.5m;             // roughly lead time x safety
            var baseline = natural <= 0m ? 3m : natural;
            var stateRoll = rng.NextDouble();
            decimal packs = stateRoll switch
            {
                < 0.08 => 0m,                                                        // out of stock
                < 0.24 => baseline * (decimal)(0.15 + rng.NextDouble() * 0.70),      // below shelf level
                _ => baseline * (decimal)(1.15 + rng.NextDouble() * 3.0),            // comfortable
            };
            if (velocity == 0m) packs = (decimal)rng.Next(0, 6);

            var qtyBase = decimal.Round(packs * unitsPerPack, 0);
            // Part-used packs are normal on a pharmacy shelf.
            if (qtyBase > 0 && unitsPerPack > 1m && rng.NextDouble() < 0.45)
                qtyBase += rng.Next(1, (int)Math.Min(unitsPerPack - 1, 10) + 1);

            DateOnly? lastSale = velocity == 0m
                ? today.AddDays(-rng.Next(200, 540))
                : today.AddDays(-rng.Next(0, 4));

            DateOnly? expiry = qtyBase <= 0
                ? null
                : rng.NextDouble() < 0.06
                    ? today.AddDays(rng.Next(20, 95))        // expiring soon
                    : today.AddDays(rng.Next(180, 900));

            var med = new MedicineStock
            {
                ItemCode = code,
                Name = parts[0].Trim(),
                Generic = parts[1].Trim(),
                Company = parts[2].Trim(),
                Barcode = (8900000000000L + index * 7919L).ToString(CultureInfo.InvariantCulture),
                PackUnit = parts[3].Trim(),
                UnitsPerPack = unitsPerPack,
                QtyBaseUnits = qtyBase,
                RetailPrice = price,
                Supplier = parts[6].Trim(),
                LastSaleDate = lastSale,
                Sold30 = sold30,
                Sold90 = sold90,
                NearestExpiry = expiry,
            };

            _items.Add(med);
            _velocity[code] = velocity;
        }
    }

    public Task<ProviderResult> GetAllAsync(CancellationToken ct = default)
    {
        lock (_gate)
        {
            ApplyMovement();
            var snapshot = _items.ToList();
            return Task.FromResult(ProviderResult.Success(
                snapshot, $"Demo data - {snapshot.Count} medicines. Not your real stock."));
        }
    }

    public Task<ProviderResult> TestAsync(CancellationToken ct = default)
        => Task.FromResult(ProviderResult.Success(
            Array.Empty<MedicineStock>(), $"Demo data ready - {_items.Count} sample medicines."));

    /// <summary>
    /// Sells a few packs and restocks a few, so alerts and the order list can be seen
    /// changing. Replaced entirely by real figures once SeaSoft is connected.
    /// </summary>
    private void ApplyMovement()
    {
        if (_items.Count == 0) return;

        var sales = Math.Max(1, _items.Count / 28);   // a handful of items move each sync
        for (var i = 0; i < sales; i++)
        {
            var pos = _drift.Next(_items.Count);
            var med = _items[pos];
            if (_velocity.GetValueOrDefault(med.ItemCode) <= 0m) continue;
            if (med.QtyBaseUnits <= 0m) continue;

            var sold = decimal.Round(med.UnitsPerPack * (decimal)(0.4 + _drift.NextDouble() * 2.0), 0);
            var left = Math.Max(0m, med.QtyBaseUnits - Math.Max(1m, sold));
            _items[pos] = med with
            {
                QtyBaseUnits = left,
                LastSaleDate = DateOnly.FromDateTime(DateTime.Today),
            };
        }

        var restocks = Math.Max(1, _items.Count / 90);  // the odd delivery arrives
        for (var i = 0; i < restocks; i++)
        {
            var pos = _drift.Next(_items.Count);
            var med = _items[pos];
            var added = decimal.Round(med.UnitsPerPack * _drift.Next(3, 12), 0);
            _items[pos] = med with { QtyBaseUnits = med.QtyBaseUnits + added };
        }
    }

    /// <summary>Demo-only: lets the UI force a medicine low so alerting can be demonstrated.</summary>
    public bool SetQtyPacksForDemo(string itemCode, decimal packs)
    {
        lock (_gate)
        {
            var pos = _items.FindIndex(m => string.Equals(m.ItemCode, itemCode, StringComparison.OrdinalIgnoreCase));
            if (pos < 0) return false;
            var med = _items[pos];
            _items[pos] = med with { QtyBaseUnits = decimal.Round(Math.Max(0m, packs) * med.UnitsPerPack, 0) };
            return true;
        }
    }
}
