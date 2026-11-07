using PharmacyStock.Models;

namespace PharmacyStock.Providers;

/// <summary>
/// Everything the app knows about reading stock. The rest of the application depends only
/// on this interface, so connecting the real SeaSoft database later means adding ONE new
/// class here - no other file needs to change.
///
/// Implementations must be strictly read-only against the source system.
/// </summary>
public interface IStockProvider
{
    /// <summary>Short key used in settings: "demo", "csv", "seasoft".</summary>
    string Key { get; }

    /// <summary>Shown in the UI, e.g. "Demo data (sample medicines)".</summary>
    string Display { get; }

    /// <summary>Read the full medicine list with current stock. Must never write to the source.</summary>
    Task<ProviderResult> GetAllAsync(CancellationToken ct = default);

    /// <summary>Quick connectivity test for the Settings screen.</summary>
    Task<ProviderResult> TestAsync(CancellationToken ct = default);
}

public sealed record ProviderResult(bool Ok, IReadOnlyList<MedicineStock> Items, string? Message = null)
{
    public static ProviderResult Fail(string message) => new(false, Array.Empty<MedicineStock>(), message);
    public static ProviderResult Success(IReadOnlyList<MedicineStock> items, string? message = null) => new(true, items, message);
}
