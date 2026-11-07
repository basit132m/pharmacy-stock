namespace PharmacyStock.Models;

/// <summary>
/// The user's own setting: how many packs of this medicine must stay on the shelf.
/// Lives in OUR database, never in the source pharmacy system.
/// </summary>
public sealed record ShelfLevel
{
    public required string ItemCode { get; init; }

    /// <summary>Minimum packs to keep. Below this, the medicine is flagged.</summary>
    public decimal LevelPacks { get; init; }

    /// <summary>False = ignore this medicine entirely (dead stock, discontinued, never restocked).</summary>
    public bool IsManaged { get; init; } = true;

    /// <summary>Overrides the supplier from the source system when grouping orders.</summary>
    public string? PreferredSupplier { get; init; }

    public string? Notes { get; init; }

    /// <summary>"manual" if a person typed it, "suggested" if the app calculated it from sales.</summary>
    public string Source { get; init; } = "manual";

    public DateTime UpdatedAt { get; init; } = DateTime.UtcNow;
}
