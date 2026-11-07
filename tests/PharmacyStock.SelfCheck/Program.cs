using PharmacyStock.Data;
using PharmacyStock.Models;
using PharmacyStock.Providers;
using PharmacyStock.Services;

var dbFile = Path.Combine(Path.GetTempPath(), "coretest-" + Guid.NewGuid().ToString("N") + ".db");

var store = new Store(dbFile);
var settings = new SettingsService(store);
var demo = new DemoStockProvider();
var csv = new CsvStockProvider(() => settings.Current.CsvPath);
var providers = new ProviderFactory(settings, demo, csv);
var stock = new StockService(store, settings);
var sync = new SyncRunner(store, stock, providers);
var levels = new ShelfLevelService(store, stock, settings);
var orders = new OrderService(store, stock);

var failures = 0;
void Check(string what, bool ok, string detail = "")
{
    Console.WriteLine($"  [{(ok ? "PASS" : "FAIL")}] {what}{(detail.Length > 0 ? "  -> " + detail : "")}");
    if (!ok) failures++;
}

Console.WriteLine("=== 1. Reading stock from the demo source ===");
var first = await sync.RunOnceAsync();
Check("stock read succeeded", first.Ok, first.Message ?? "");
Check("medicines loaded", first.Items.Count > 100, $"{first.Items.Count} medicines");

Console.WriteLine();
Console.WriteLine("=== 2. Shelf levels suggested from sales history ===");
var applied = levels.ApplySuggestions("missing");
Check("levels were set", applied.Changed > 0, applied.Message);
Check("dead stock left switched off", applied.Message.Contains("switched off"));

await sync.RunOnceAsync();
var dash = stock.Dashboard();
Console.WriteLine($"       out={dash.OutOfStock} low={dash.BelowLevel} ok={dash.Ok} off={dash.Unmanaged} total={dash.TotalItems}");
Check("every medicine is classified",
    dash.OutOfStock + dash.BelowLevel + dash.Ok + dash.Unmanaged == dash.TotalItems);

Console.WriteLine();
Console.WriteLine("=== 3. Alert state machine, shelf level 5, hysteresis 1 ===");
const string code = "SF-0001";
levels.SetOne(code, 5m, true, null, null);

StockView Row()
{
    var page = stock.Query(new StockQuery(Search: code, Status: "", PageSize: 10));
    return page.Items.First(i => i.ItemCode == code);
}

var expected = new[] { "OK", "LOW", "OUT", "LOW", "OK" };
var scenarios = new (string Label, decimal Packs)[]
{
    ("plenty in stock (10 packs)", 10m),
    ("drops below the level (3)", 3m),
    ("completely finished (0)", 0m),
    ("restocked exactly to the level (5)", 5m),
    ("restocked above the level (7)", 7m),
};

var actual = new List<string>();
Console.WriteLine($"  {"scenario",-38}{"stock",7}{"level",7}{"state",8}{"buy",6}");
foreach (var (label, packs) in scenarios)
{
    demo.SetQtyPacksForDemo(code, packs);
    await sync.RunOnceAsync();
    var r = Row();
    actual.Add(r.StateText);
    Console.WriteLine($"  {label,-38}{r.QtyPacks,7}{r.LevelPacks,7}{r.StateText,8}{r.ShortfallPacks,6}");
}

Check("state machine follows OK -> LOW -> OUT -> LOW -> OK",
    actual.SequenceEqual(expected), string.Join(" ", actual));
Check("hysteresis holds the warning at exactly the shelf level", actual[3] == "LOW");

var raised = store.GetAlertEvents(50).Where(e => e.ItemCode == code).ToList();
bool Transition(StockState from, StockState to) => raised.Any(e => e.From == from && e.To == to);

Check("alert raised when it fell below the level", Transition(StockState.Ok, StockState.Low));
Check("alert raised when it finished", Transition(StockState.Low, StockState.Out));
Check("alert raised when stock came back", Transition(StockState.Out, StockState.Low));
Check("alert raised when it cleared the level", Transition(StockState.Low, StockState.Ok));
foreach (var e in raised.OrderBy(e => e.Id))
    Console.WriteLine($"       {e.From,-9} -> {e.To,-9} at {e.QtyPacks} packs");

Console.WriteLine();
Console.WriteLine("=== 4. Alerts fire on change only, never on the condition ===");
var before = store.GetAlertEvents(500).Count;
await sync.RunOnceAsync();
await sync.RunOnceAsync();
var after = store.GetAlertEvents(500).Count;
Check("repeated syncs do not repeat the same shortage",
    after - before < 12, $"{after - before} new alerts across two syncs of {dash.TotalItems} medicines");

Console.WriteLine();
Console.WriteLine("=== 5. Buying list ===");
demo.SetQtyPacksForDemo(code, 0m);
await sync.RunOnceAsync();
var groups = orders.Build();
var lines = groups.Sum(g => g.Lines.Count);
Check("buying list is grouped by supplier", groups.Count > 1, $"{groups.Count} suppliers, {lines} lines");
Check("nothing on the list has a zero shortfall",
    groups.SelectMany(g => g.Lines).All(l => l.QtyPacks > 0m));

var text = orders.ExportText();
Check("WhatsApp order text is produced", text.Contains('*') && text.Length > 40);
var csvOut = orders.ExportCsv();
Check("order CSV has a header and rows", csvOut.StartsWith("Supplier,") && csvOut.Split('\n').Length > 3);

Console.WriteLine();
Console.WriteLine("=== 6. Shelf levels survive an export/import round trip ===");
var exported = levels.ExportCsv();
Check("export has every medicine",
    exported.Split('\n').Length >= dash.TotalItems, $"{exported.Split('\n').Length} lines");
var imported = levels.ImportCsv(exported);
Check("import accepts its own export", imported.Changed > 0, imported.Message);

Console.WriteLine();
Console.WriteLine("=== 7. A failed read keeps the last good figures on screen ===");
settings.Save(new AppSettings { DataSource = "csv", CsvPath = "/nowhere/missing.csv" });
var failed = await sync.RunOnceAsync();
Check("the read reports failure", !failed.Ok, failed.Message ?? "");
Check("the medicine list is NOT wiped", stock.Current.Items.Count > 100,
    $"{stock.Current.Items.Count} medicines still shown");
Check("the failure is flagged", !stock.Current.Ok);

try { File.Delete(dbFile); } catch { }

Console.WriteLine();
Console.WriteLine(failures == 0 ? "ALL CHECKS PASSED" : $"{failures} CHECK(S) FAILED");
return failures == 0 ? 0 : 1;
