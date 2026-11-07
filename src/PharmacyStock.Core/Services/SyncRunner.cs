using PharmacyStock.Data;
using PharmacyStock.Diagnostics;
using PharmacyStock.Providers;

namespace PharmacyStock.Services;

/// <summary>
/// Performs one read of the source pharmacy system. Used by the background timer and by
/// the "Refresh now" button, so both paths behave identically.
/// </summary>
public sealed class SyncRunner
{
    private readonly Store _store;
    private readonly StockService _stock;
    private readonly ProviderFactory _factory;
    private readonly SemaphoreSlim _oneAtATime = new(1, 1);

    public SyncRunner(Store store, StockService stock, ProviderFactory factory)
    {
        _store = store;
        _stock = stock;
        _factory = factory;
    }

    public async Task<ProviderResult> RunOnceAsync(CancellationToken ct = default)
    {
        await _oneAtATime.WaitAsync(ct);
        var provider = _factory.Resolve();
        var logId = _store.StartSync(DateTime.UtcNow);

        try
        {
            var result = await provider.GetAllAsync(ct);
            _stock.Ingest(result, provider.Key);
            _store.FinishSync(logId, DateTime.UtcNow, result.Items.Count, result.Ok, result.Message);

            if (!result.Ok) Log.Warn($"Stock read failed: {result.Message}");
            return result;
        }
        catch (OperationCanceledException)
        {
            _store.FinishSync(logId, DateTime.UtcNow, 0, false, "Cancelled");
            throw;
        }
        catch (Exception ex)
        {
            Log.Error("Stock read threw an exception", ex);
            var failure = ProviderResult.Fail($"Could not read stock: {ex.Message}");
            _stock.Ingest(failure, provider.Key);
            _store.FinishSync(logId, DateTime.UtcNow, 0, false, failure.Message);
            return failure;
        }
        finally
        {
            _oneAtATime.Release();
        }
    }
}
