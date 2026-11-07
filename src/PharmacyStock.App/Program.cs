using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text.Json.Serialization;
using PharmacyStock.Api;
using PharmacyStock.Data;
using PharmacyStock.Models;
using PharmacyStock.Providers;
using PharmacyStock.Services;

// ---------------------------------------------------------------------------
// Ten Percent Discount Pharmacy - Stock Manager
//
// Reads stock from the pharmacy system (SeaSoft, once connected) and warns when a
// medicine falls below the shelf level you set. It NEVER writes to the pharmacy
// system - its own data lives in a separate SQLite file under %LOCALAPPDATA%.
// ---------------------------------------------------------------------------

// Settings live in our SQLite file, and the port is needed before the web server starts.
var store = new Store();
var settingsService = new SettingsService(store);
var settings = settingsService.Current;

var port = FindFreePort(settings.Port);
var bindHost = settings.AllowLan ? "0.0.0.0" : "127.0.0.1";

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(options =>
{
    options.SingleLine = true;
    options.TimestampFormat = "HH:mm:ss ";
});
builder.Logging.SetMinimumLevel(LogLevel.Information);
builder.Logging.AddFilter("Microsoft", LogLevel.Warning);

builder.WebHost.UseUrls($"http://{bindHost}:{port}");

builder.Services.AddSingleton(store);
builder.Services.AddSingleton(settingsService);
builder.Services.AddSingleton<DemoStockProvider>();
builder.Services.AddSingleton(provider => new CsvStockProvider(
    () => provider.GetRequiredService<SettingsService>().Current.CsvPath));
builder.Services.AddSingleton<ProviderFactory>();
builder.Services.AddSingleton<StockService>();
builder.Services.AddSingleton<SyncRunner>();
builder.Services.AddSingleton<ShelfLevelService>();
builder.Services.AddSingleton<OrderService>();
builder.Services.AddHostedService<SyncWorker>();

builder.Services.ConfigureHttpJsonOptions(options =>
{
    // States read as "Out"/"Low"/"Ok" in the browser rather than 3/2/1.
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

var app = builder.Build();

app.MapApi();
app.MapEmbeddedUi();

// Read stock once before the first screen is drawn, so the board is never empty.
try
{
    await app.Services.GetRequiredService<SyncRunner>().RunOnceAsync();
}
catch (Exception ex)
{
    app.Logger.LogWarning("First stock read failed: {Message}", ex.Message);
}

var localUrl = $"http://localhost:{port}";
Banner(settings, localUrl, port);

if (Environment.GetEnvironmentVariable("PHARMACY_NO_BROWSER") != "1")
    OpenBrowser(localUrl);

await app.RunAsync();
return;

// ---------------------------------------------------------------------------

// Takes the next free port if the preferred one is busy, so a second copy of the program -
// or anything else already using the port - never stops the app from starting.
static int FindFreePort(int preferred)
{
    for (var candidate = preferred; candidate < preferred + 20; candidate++)
    {
        try
        {
            var listener = new TcpListener(IPAddress.Loopback, candidate);
            listener.Start();
            listener.Stop();
            return candidate;
        }
        catch (SocketException)
        {
            // Busy - try the next one.
        }
    }
    return preferred;
}

static void Banner(AppSettings settings, string localUrl, int port)
{
    var lanUrls = settings.AllowLan ? LocalAddresses(port) : Array.Empty<string>();

    Console.WriteLine();
    Console.WriteLine("  ===========================================================");
    Console.WriteLine($"   {settings.PharmacyName}");
    Console.WriteLine("   STOCK MANAGER  -  out-of-stock and shelf-level alerts");
    Console.WriteLine("  ===========================================================");
    Console.WriteLine();
    Console.WriteLine($"   Open on this PC:      {localUrl}");

    foreach (var url in lanUrls)
        Console.WriteLine($"   From another PC/phone: {url}");

    Console.WriteLine();
    Console.WriteLine($"   Stock source:   {settings.DataSource}");
    Console.WriteLine($"   Refresh every:  {settings.SyncSeconds} seconds");
    Console.WriteLine($"   Your data:      {AppPaths.DataFolder}");
    Console.WriteLine();
    Console.WriteLine("   KEEP THIS WINDOW OPEN. Closing it stops the stock alerts.");
    Console.WriteLine("   You can minimise it.");
    Console.WriteLine();
}

// The shop-network addresses the board can be reached on from other devices.
static string[] LocalAddresses(int port)
{
    try
    {
        return Dns.GetHostAddresses(Dns.GetHostName())
            .Where(address => address.AddressFamily == AddressFamily.InterNetwork)
            .Where(address => !IPAddress.IsLoopback(address))
            .Select(address => $"http://{address}:{port}")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
    catch
    {
        return Array.Empty<string>();
    }
}

static void OpenBrowser(string url)
{
    try
    {
        if (OperatingSystem.IsWindows())
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        else if (OperatingSystem.IsLinux())
            Process.Start("xdg-open", url);
        else if (OperatingSystem.IsMacOS())
            Process.Start("open", url);
    }
    catch
    {
        // Not important - the address is printed above.
    }
}
