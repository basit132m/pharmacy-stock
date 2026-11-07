namespace PharmacyStock.Models;

public enum OrderStatus
{
    /// <summary>On the buying list, not yet ordered.</summary>
    Pending = 0,
    /// <summary>Ordered from the distributor, waiting for delivery.</summary>
    Ordered = 1,
    /// <summary>Delivered. Stock should rise in the source system shortly.</summary>
    Received = 2,
    /// <summary>Distributor has none either. Keep it visible, stop nagging.</summary>
    SupplierOut = 3,
    /// <summary>Company stopped making it. Candidate for switching off.</summary>
    Discontinued = 4,
}

/// <summary>
/// A line on the buying list. Lives in OUR database - the source system is never written to.
/// </summary>
public sealed record OrderLine
{
    public long Id { get; init; }
    public required string ItemCode { get; init; }
    public OrderStatus Status { get; init; } = OrderStatus.Pending;

    /// <summary>Packs to buy. Defaults to what is needed to get back above the shelf level.</summary>
    public decimal QtyPacks { get; init; }

    public string? Supplier { get; init; }
    public string? Notes { get; init; }

    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime? OrderedAt { get; init; }
    public DateTime? ExpectedAt { get; init; }
    public DateTime? ReceivedAt { get; init; }

    /// <summary>While an order is open we suppress alerts for the medicine - it is already handled.</summary>
    public bool SuppressesAlerts => Status is OrderStatus.Ordered or OrderStatus.SupplierOut or OrderStatus.Discontinued;
}
