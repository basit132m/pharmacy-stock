using PharmacyStock.Providers;

namespace PharmacyStock.Services;

/// <summary>
/// Chooses where stock is read from, based on the Settings screen.
///
/// Connecting the real SeaSoft database later is a two-line change here: add the new
/// provider to the constructor and give it a case in <see cref="Resolve"/>. Nothing else
/// in the application needs to know.
/// </summary>
public sealed class ProviderFactory
{
    private readonly SettingsService _settings;
    private readonly DemoStockProvider _demo;
    private readonly CsvStockProvider _csv;

    public ProviderFactory(SettingsService settings, DemoStockProvider demo, CsvStockProvider csv)
    {
        _settings = settings;
        _demo = demo;
        _csv = csv;
    }

    public IStockProvider Resolve() => _settings.Current.DataSource switch
    {
        "csv" => _csv,
        // "seasoft" => _seasoft,  <- added once we know which database SeaSoft uses
        _ => _demo,
    };

    public IReadOnlyList<IStockProvider> All => new IStockProvider[] { _demo, _csv };

    public DemoStockProvider Demo => _demo;
}
