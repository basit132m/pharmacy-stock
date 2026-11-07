using System.Globalization;
using System.Text;
using PharmacyStock.Data;
using PharmacyStock.Models;
using PharmacyStock.Providers;

namespace PharmacyStock.Services;

public sealed record OrderRow
{
    public required string ItemCode { get; init; }
    public required string Name { get; init; }
    public string? Generic { get; init; }
    public string? Company { get; init; }
    public string? Supplier { get; init; }
    public string PackUnit { get; init; } = "Pack";

    public decimal InStockPacks { get; init; }

    /// <summary>Whole packs on the shelf, with any part-pack reported separately as
    /// <see cref="LooseUnits"/>. "2 packs and 7 loose" reads far better on a buying list
    /// than "2.35 packs".</summary>
    public int WholePacks { get; init; }
    public decimal LooseUnits { get; init; }
    public decimal UnitsPerPack { get; init; } = 1m;

    public decimal LevelPacks { get; init; }
    public decimal ShortfallPacks { get; init; }

    /// <summary>What to buy. The shortfall by default, or whatever the user typed.</summary>
    public decimal QtyPacks { get; init; }

    public string State { get; init; } = "OK";
    public decimal? RetailPrice { get; init; }
    public decimal LineValue { get; init; }

    public long? OrderId { get; init; }
    public string Status { get; init; } = "Pending";
    public DateTime? OrderedAt { get; init; }
    public DateTime? ExpectedAt { get; init; }
    public string? Notes { get; init; }
    public decimal Sold30 { get; init; }
    public bool IsDeadStock { get; init; }
}

public sealed record SupplierGroup(string Supplier, List<OrderRow> Lines)
{
    public int Count => Lines.Count;
    public decimal Total => Lines.Sum(l => l.LineValue);
}

/// <summary>
/// The buying list: which medicines to order, grouped by distributor so one order can be
/// placed per supplier. Entirely our own data - the pharmacy system is never written to.
/// </summary>
public sealed class OrderService
{
    private readonly Store _store;
    private readonly StockService _stock;

    public OrderService(Store store, StockService stock)
    {
        _store = store;
        _stock = stock;
    }

    /// <summary>
    /// Everything that needs buying, plus anything already on order so it can be marked
    /// received. Grouped by supplier, biggest shortfall first inside each group.
    /// </summary>
    public List<SupplierGroup> Build(bool includeDeadStock = false)
    {
        var views = _stock.BuildAll().ToDictionary(v => v.ItemCode, StringComparer.OrdinalIgnoreCase);
        var openOrders = _store.GetOpenOrders();
        var rows = new List<OrderRow>();

        foreach (var view in views.Values)
        {
            openOrders.TryGetValue(view.ItemCode, out var order);

            // Shortfall, not state, decides whether something belongs on the buying list.
            // A medicine held at LOW purely by the hysteresis rule is already back at its
            // shelf level, so there is nothing to buy and it would only be noise here.
            var needsBuying = view.State is StockState.Out or StockState.Low && view.ShortfallPacks > 0m;
            if (!needsBuying && order is null) continue;
            if (!includeDeadStock && view.IsDeadStock && order is null) continue;

            var qty = order?.QtyPacks is > 0m ? order.QtyPacks : view.ShortfallPacks;
            if (qty <= 0m) qty = 1m;

            rows.Add(new OrderRow
            {
                ItemCode = view.ItemCode,
                Name = view.Name,
                Generic = view.Generic,
                Company = view.Company,
                Supplier = string.IsNullOrWhiteSpace(view.Supplier) ? "Other" : view.Supplier,
                PackUnit = view.PackUnit,
                InStockPacks = view.QtyPacks,
                WholePacks = view.WholePacks,
                LooseUnits = view.LooseUnits,
                UnitsPerPack = view.UnitsPerPack,
                LevelPacks = view.LevelPacks ?? 0m,
                ShortfallPacks = view.ShortfallPacks,
                QtyPacks = qty,
                State = view.StateText,
                RetailPrice = view.RetailPrice,
                LineValue = decimal.Round((view.RetailPrice ?? 0m) * qty, 0),
                OrderId = order?.Id,
                Status = (order?.Status ?? Models.OrderStatus.Pending).ToString(),
                OrderedAt = order?.OrderedAt,
                ExpectedAt = order?.ExpectedAt,
                Notes = order?.Notes,
                Sold30 = view.Sold30,
                IsDeadStock = view.IsDeadStock,
            });
        }

        return rows
            .GroupBy(r => r.Supplier ?? "Other", StringComparer.OrdinalIgnoreCase)
            .Select(g => new SupplierGroup(
                g.Key,
                g.OrderByDescending(r => r.State == "OUT")
                 .ThenByDescending(r => r.ShortfallPacks)
                 .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
                 .ToList()))
            // Suppliers with the most finished medicines come first - that is the call to
            // make before the others.
            .OrderByDescending(g => g.Lines.Count(l => l.State == "OUT"))
            .ThenByDescending(g => g.Count)
            .ThenBy(g => g.Supplier, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Puts every current shortage on the list as Pending, without duplicating lines.</summary>
    public BulkResult AutoFill()
    {
        var views = _stock.BuildAll();
        var open = _store.GetOpenOrders();
        var added = 0;

        foreach (var view in views)
        {
            if (view.State is not (StockState.Out or StockState.Low)) continue;
            if (view.ShortfallPacks <= 0m) continue;   // back at the shelf level already
            if (view.IsDeadStock) continue;
            if (open.ContainsKey(view.ItemCode)) continue;

            _store.UpsertOrder(new OrderLine
            {
                ItemCode = view.ItemCode,
                Status = Models.OrderStatus.Pending,
                QtyPacks = view.ShortfallPacks <= 0m ? 1m : view.ShortfallPacks,
                Supplier = view.Supplier,
                CreatedAt = DateTime.UtcNow,
            });
            added++;
        }

        return new BulkResult(added, 0, added == 0
            ? "Nothing new to add - everything short is already on the list."
            : $"Added {added} medicines to the buying list.");
    }

    public long Save(string itemCode, decimal qtyPacks, string status, string? supplier, string? notes, DateTime? expectedAt, long? id)
    {
        var parsed = Enum.TryParse<Models.OrderStatus>(status, ignoreCase: true, out var value)
            ? value
            : Models.OrderStatus.Pending;

        var existing = id is > 0
            ? _store.GetOrders(includeReceived: true).FirstOrDefault(o => o.Id == id)
            : _store.GetOpenOrders().GetValueOrDefault(itemCode);

        var now = DateTime.UtcNow;

        var line = new OrderLine
        {
            Id = existing?.Id ?? 0,
            ItemCode = itemCode,
            Status = parsed,
            QtyPacks = Math.Max(0m, decimal.Round(qtyPacks, 2)),
            Supplier = string.IsNullOrWhiteSpace(supplier) ? existing?.Supplier : supplier.Trim(),
            Notes = string.IsNullOrWhiteSpace(notes) ? existing?.Notes : notes.Trim(),
            CreatedAt = existing?.CreatedAt ?? now,
            OrderedAt = parsed >= Models.OrderStatus.Ordered ? existing?.OrderedAt ?? now : null,
            ExpectedAt = expectedAt ?? existing?.ExpectedAt,
            ReceivedAt = parsed == Models.OrderStatus.Received ? existing?.ReceivedAt ?? now : null,
        };

        return _store.UpsertOrder(line);
    }

    public void Delete(long id) => _store.DeleteOrder(id);

    /// <summary>
    /// Marks everything pending for one supplier as ordered in a single click - which is how
    /// it actually happens, one phone call per distributor.
    /// </summary>
    public BulkResult MarkSupplierOrdered(string supplier, int leadTimeDays)
    {
        var groups = Build(includeDeadStock: true)
            .Where(g => string.Equals(g.Supplier, supplier, StringComparison.OrdinalIgnoreCase));

        var now = DateTime.UtcNow;
        var count = 0;

        foreach (var line in groups.SelectMany(g => g.Lines))
        {
            if (line.Status is not "Pending") continue;
            Save(line.ItemCode, line.QtyPacks, nameof(Models.OrderStatus.Ordered), supplier, line.Notes,
                now.AddDays(leadTimeDays), line.OrderId);
            count++;
        }

        return new BulkResult(count, 0, count == 0
            ? $"Nothing pending for {supplier}."
            : $"Marked {count} medicines as ordered from {supplier}.");
    }

    /// <summary>Purchase order as a spreadsheet, ready to send to the distributor.</summary>
    public string ExportCsv(string? supplier = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Supplier,ItemCode,Medicine,Generic,Company,PackUnit,InStockPacks,ShelfLevelPacks,OrderQtyPacks,UnitPrice,LineValue,Status");

        foreach (var group in Build(includeDeadStock: true))
        {
            if (!string.IsNullOrWhiteSpace(supplier)
                && !string.Equals(group.Supplier, supplier, StringComparison.OrdinalIgnoreCase)) continue;

            foreach (var line in group.Lines)
            {
                sb.Append(Csv.Escape(group.Supplier)).Append(',');
                sb.Append(Csv.Escape(line.ItemCode)).Append(',');
                sb.Append(Csv.Escape(line.Name)).Append(',');
                sb.Append(Csv.Escape(line.Generic)).Append(',');
                sb.Append(Csv.Escape(line.Company)).Append(',');
                sb.Append(Csv.Escape(line.PackUnit)).Append(',');
                sb.Append(line.InStockPacks.ToString(CultureInfo.InvariantCulture)).Append(',');
                sb.Append(line.LevelPacks.ToString(CultureInfo.InvariantCulture)).Append(',');
                sb.Append(line.QtyPacks.ToString(CultureInfo.InvariantCulture)).Append(',');
                sb.Append((line.RetailPrice ?? 0m).ToString(CultureInfo.InvariantCulture)).Append(',');
                sb.Append(line.LineValue.ToString(CultureInfo.InvariantCulture)).Append(',');
                sb.AppendLine(Csv.Escape(line.Status));
            }
        }

        return sb.ToString();
    }

    /// <summary>Plain text order, formatted to paste straight into WhatsApp.</summary>
    public string ExportText(string? supplier = null)
    {
        var sb = new StringBuilder();
        var groups = Build(includeDeadStock: true)
            .Where(g => string.IsNullOrWhiteSpace(supplier)
                        || string.Equals(g.Supplier, supplier, StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var group in groups)
        {
            var pending = group.Lines.Where(l => l.Status is "Pending").ToList();
            if (pending.Count == 0) continue;

            sb.AppendLine($"*{group.Supplier}* - {DateTime.Now:dd MMM yyyy}");
            var n = 1;
            foreach (var line in pending)
            {
                sb.Append(n++).Append(". ").Append(line.Name);
                sb.Append("  x ").Append(line.QtyPacks.ToString("0.##", CultureInfo.InvariantCulture));
                sb.Append(' ').Append(line.PackUnit);
                if (line.State == "OUT") sb.Append("  (finished)");
                sb.AppendLine();
            }
            sb.AppendLine();
        }

        return sb.Length == 0 ? "Nothing pending to order." : sb.ToString();
    }
}
