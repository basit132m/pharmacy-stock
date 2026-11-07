using PharmacyStock.Data;

namespace PharmacyStock.Services;

/// <summary>
/// Re-reads stock on a timer, so a purchase entered in SeaSoft shows up here by itself.
/// The interval comes from Settings (60 seconds by default), and is re-read each loop so
/// changing it takes effect without restarting the program.
/// </summary>
public sealed class SyncWorker : BackgroundService
{
    private readonly SyncRunner _runner;
    private readonly SettingsService _settings;
    private readonly Store _store;
    private readonly ILogger<SyncWorker> _log;

    public SyncWorker(SyncRunner runner, SettingsService settings, Store store, ILogger<SyncWorker> log)
    {
        _runner = runner;
        _settings = settings;
        _store = store;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _log.LogInformation("Stock sync started, every {Seconds}s", _settings.Current.SyncSeconds);

        var housekeeping = DateTime.UtcNow;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _runner.RunOnceAsync(stoppingToken);

                // Keep the local file tidy once a day.
                if (DateTime.UtcNow - housekeeping > TimeSpan.FromHours(24))
                {
                    _store.Prune();
                    housekeeping = DateTime.UtcNow;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Never let the loop die - the board must keep refreshing all day.
                _log.LogError(ex, "Sync loop error");
            }

            var wait = TimeSpan.FromSeconds(Math.Clamp(_settings.Current.SyncSeconds, 10, 3600));
            try { await Task.Delay(wait, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }

        _log.LogInformation("Stock sync stopped");
    }
}
