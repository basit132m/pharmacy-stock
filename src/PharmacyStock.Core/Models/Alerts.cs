namespace PharmacyStock.Models;

/// <summary>Where a medicine stands against its shelf level.</summary>
public enum StockState
{
    /// <summary>No shelf level set, or the user switched this medicine off.</summary>
    Unmanaged = 0,
    /// <summary>At or above the shelf level. Nothing to do.</summary>
    Ok = 1,
    /// <summary>Some stock left, but below the shelf level. Time to order.</summary>
    Low = 2,
    /// <summary>Nothing on the shelf.</summary>
    Out = 3,
}

/// <summary>
/// Remembers which state each medicine was in last time we looked, so we can notify on
/// the TRANSITION only. Without this the app would re-announce the same 300 shortages
/// every single sync and nobody would read it.
/// </summary>
public sealed record AlertState
{
    public required string ItemCode { get; init; }
    public StockState State { get; init; }
    public decimal QtyPacks { get; init; }
    public decimal LevelPacks { get; init; }

    /// <summary>When the medicine entered this state.</summary>
    public DateTime Since { get; init; }

    /// <summary>Set when someone presses "Got it" - stops it appearing as new.</summary>
    public DateTime? AcknowledgedAt { get; init; }

    /// <summary>Stay quiet about this medicine until this time.</summary>
    public DateTime? SnoozeUntil { get; init; }

    public bool IsSnoozed(DateTime nowUtc) => SnoozeUntil.HasValue && SnoozeUntil.Value > nowUtc;
}

/// <summary>A state change worth telling the user about.</summary>
public sealed record AlertEvent
{
    public long Id { get; init; }
    public required string ItemCode { get; init; }
    public required string Name { get; init; }
    public StockState From { get; init; }
    public StockState To { get; init; }
    public decimal QtyPacks { get; init; }
    public decimal LevelPacks { get; init; }
    public DateTime AtUtc { get; init; }
    public bool Seen { get; init; }
}
