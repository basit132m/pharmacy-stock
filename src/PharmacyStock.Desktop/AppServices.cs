using PharmacyStock.Data;
using PharmacyStock.Providers;
using PharmacyStock.Services;

namespace PharmacyStock.Desktop;

/// <summary>
/// Everything the program is made of, wired up once at start-up. Small enough that a
/// dependency-injection container would be ceremony for its own sake.
/// </summary>
internal sealed class AppServices
{
    public Store Store { get; }
    public SettingsService Settings { get; }
    public DemoStockProvider Demo { get; }
    public CsvStockProvider Csv { get; }
    public ProviderFactory Providers { get; }
    public StockService Stock { get; }
    public SyncRunner Sync { get; }
    public ShelfLevelService Levels { get; }
    public OrderService Orders { get; }

    public AppServices()
    {
        Store = new Store();
        Settings = new SettingsService(Store);
        Demo = new DemoStockProvider();
        Csv = new CsvStockProvider(() => Settings.Current.CsvPath);
        Providers = new ProviderFactory(Settings, Demo, Csv);
        Stock = new StockService(Store, Settings);
        Sync = new SyncRunner(Store, Stock, Providers);
        Levels = new ShelfLevelService(Store, Stock, Settings);
        Orders = new OrderService(Store, Stock);
    }
}
