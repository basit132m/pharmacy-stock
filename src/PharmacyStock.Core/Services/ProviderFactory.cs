using PharmacyStock.Providers;
using PharmacyStock.Providers.Seasoft;

namespace PharmacyStock.Services;

/// <summary>
/// Chooses where stock is read from, based on the Settings screen.
/// </summary>
public sealed class ProviderFactory
{
    private readonly SettingsService _settings;
    private readonly DemoStockProvider _demo;
    private readonly CsvStockProvider _csv;
    private readonly SeasoftStockProvider _seasoft;

    public ProviderFactory(SettingsService settings, DemoStockProvider demo, CsvStockProvider csv)
    {
        _settings = settings;
        _demo = demo;
        _csv = csv;
        _seasoft = new SeasoftStockProvider(settings.GetSeasoftConnection, settings.GetSeasoftMapping);
    }

    public IStockProvider Resolve() => _settings.Current.DataSource switch
    {
        "csv" => _csv,
        "seasoft" => _seasoft,
        _ => _demo,
    };

    public IReadOnlyList<IStockProvider> All => new IStockProvider[] { _demo, _csv, _seasoft };

    public SeasoftStockProvider Seasoft => _seasoft;

    public DemoStockProvider Demo => _demo;
}
