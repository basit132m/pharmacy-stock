namespace PharmacyStock.Models;

/// <summary>
/// One row of the stock board: source data joined with our shelf level, alert state and order.
/// This is the shape the UI receives as JSON.
/// </summary>
public sealed record StockView
{
    public required string ItemCode { get; init; }
    public required string Name { get; init; }
    public string? Generic { get; init; }
    public string? Company { get; init; }
    public string? Supplier { get; init; }
    public string PackUnit { get; init; } = "Pack";
    public decimal UnitsPerPack { get; init; }

    public decimal QtyPacks { get; init; }
    public int WholePacks { get; init; }
    public decimal LooseUnits { get; init; }

    public decimal? LevelPacks { get; init; }
    public bool IsManaged { get; init; }
    public decimal SuggestedLevelPacks { get; init; }

    public StockState State { get; init; }
    public string StateText => State switch
    {
        StockState.Out => "OUT",
        StockState.Low => "LOW",
        StockState.Ok => "OK",
        _ => "OFF",
    };

    /// <summary>Packs to buy to get back above the shelf level. Zero when nothing is needed.</summary>
    public decimal ShortfallPacks { get; init; }

    public decimal? RetailPrice { get; init; }
    public decimal Sold30 { get; init; }
    public decimal Sold90 { get; init; }
    public DateOnly? LastSaleDate { get; init; }
    public DateOnly? NearestExpiry { get; init; }

    public string? OrderStatus { get; init; }
    public long? OrderId { get; init; }
    public DateTime? OrderedAt { get; init; }
    public DateTime? SnoozeUntil { get; init; }

    /// <summary>No sale within the dead-stock window - probably should not be restocked.</summary>
    public bool IsDeadStock { get; init; }
}

public sealed record DashboardCounts
{
    public int OutOfStock { get; init; }
    public int BelowLevel { get; init; }
    public int Ok { get; init; }
    public int Unmanaged { get; init; }
    public int TotalItems { get; init; }
    public int OpenOrders { get; init; }
    public int NewAlerts { get; init; }
    public decimal OrderValueEstimate { get; init; }
    public DateTime? LastSyncUtc { get; init; }
    public string SyncStatus { get; init; } = "never";
    public string? SyncMessage { get; init; }
    public string DataSource { get; init; } = "demo";
}
