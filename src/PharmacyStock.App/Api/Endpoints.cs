using System.Globalization;
using PharmacyStock.Data;
using PharmacyStock.Models;
using PharmacyStock.Services;

namespace PharmacyStock.Api;

public static class Endpoints
{
    public static void MapApi(this WebApplication app)
    {
        var api = app.MapGroup("/api");

        // ------------------------------------------------------------ dashboard
        api.MapGet("/dashboard", (StockService stock, SettingsService settings) => Results.Ok(new
        {
            counts = stock.Dashboard(),
            pharmacyName = settings.Current.PharmacyName,
            syncSeconds = settings.Current.SyncSeconds,
            soundAlerts = settings.Current.SoundAlerts,
        }));

        // ---------------------------------------------------------- stock board
        api.MapGet("/stock", (
            StockService stock,
            string? search, string? status, string? supplier, string? company,
            bool? dead, string? sort, bool? desc, int? page, int? pageSize) =>
        {
            var result = stock.Query(new StockQuery(
                Search: search,
                Status: status,
                Supplier: supplier,
                Company: company,
                Dead: dead,
                Sort: sort ?? "urgency",
                Desc: desc ?? false,
                Page: page ?? 1,
                PageSize: pageSize ?? 50));

            return Results.Ok(new { items = result.Items, total = result.Total, page = result.Page, pages = result.Pages, pageSize = result.PageSize });
        });

        api.MapGet("/suppliers", (StockService stock) => Results.Ok(stock.Suppliers()));

        // --------------------------------------------------------- shelf levels
        api.MapPut("/stock/{code}/level", (string code, SetLevelRequest body, ShelfLevelService levels) =>
        {
            if (body.LevelPacks < 0m) return Results.BadRequest(new ActionResult(false, "Shelf level cannot be negative."));
            levels.SetOne(code, body.LevelPacks, body.IsManaged, body.PreferredSupplier, body.Notes);
            return Results.Ok(new ActionResult(true, $"Shelf level saved: {body.LevelPacks:0.##} packs.", 1));
        });

        api.MapPost("/stock/{code}/managed", (string code, SetManagedRequest body, ShelfLevelService levels) =>
        {
            levels.SetManaged(code, body.IsManaged);
            return Results.Ok(new ActionResult(true, body.IsManaged ? "Medicine switched on." : "Medicine switched off.", 1));
        });

        api.MapPost("/levels/suggest", (SuggestRequest body, ShelfLevelService levels) =>
        {
            var result = levels.ApplySuggestions(body.Scope);
            return Results.Ok(new ActionResult(true, result.Message, result.Changed));
        });

        api.MapGet("/levels/export.csv", (ShelfLevelService levels) =>
            Results.File(System.Text.Encoding.UTF8.GetBytes(levels.ExportCsv()),
                "text/csv", $"shelf-levels-{DateTime.Now:yyyy-MM-dd}.csv"));

        api.MapPost("/levels/import", async (HttpRequest request, ShelfLevelService levels) =>
        {
            using var reader = new StreamReader(request.Body);
            var text = await reader.ReadToEndAsync();
            if (string.IsNullOrWhiteSpace(text)) return Results.BadRequest(new ActionResult(false, "No file content received."));
            var result = levels.ImportCsv(text);
            return Results.Ok(new ActionResult(result.Changed > 0, result.Message, result.Changed));
        });

        // ----------------------------------------------------------- order list
        api.MapGet("/orders", (OrderService orders, bool? includeDead) =>
            Results.Ok(orders.Build(includeDead ?? false)));

        api.MapPost("/orders", (SaveOrderRequest body, OrderService orders) =>
        {
            var id = orders.Save(body.ItemCode, body.QtyPacks, body.Status, body.Supplier, body.Notes, body.ExpectedAt, body.Id);
            return Results.Ok(new { ok = true, message = "Saved.", id });
        });

        api.MapPost("/orders/autofill", (OrderService orders) =>
        {
            var result = orders.AutoFill();
            return Results.Ok(new ActionResult(true, result.Message, result.Changed));
        });

        api.MapPost("/orders/supplier/{supplier}/ordered", (string supplier, OrderService orders, SettingsService settings) =>
        {
            var result = orders.MarkSupplierOrdered(supplier, settings.Current.LeadTimeDays);
            return Results.Ok(new ActionResult(true, result.Message, result.Changed));
        });

        api.MapDelete("/orders/{id:long}", (long id, OrderService orders) =>
        {
            orders.Delete(id);
            return Results.Ok(new ActionResult(true, "Removed from the buying list.", 1));
        });

        api.MapGet("/orders/export.csv", (OrderService orders, string? supplier) =>
            Results.File(System.Text.Encoding.UTF8.GetBytes(orders.ExportCsv(supplier)),
                "text/csv", $"order-{DateTime.Now:yyyy-MM-dd}.csv"));

        api.MapGet("/orders/export.txt", (OrderService orders, string? supplier) =>
            Results.Text(orders.ExportText(supplier), "text/plain"));

        // --------------------------------------------------------------- alerts
        api.MapGet("/alerts", (Store store, int? limit) =>
            Results.Ok(store.GetAlertEvents(Math.Clamp(limit ?? 100, 1, 500))));

        api.MapPost("/alerts/seen", (Store store) =>
        {
            store.MarkAlertsSeen();
            return Results.Ok(new ActionResult(true, "Alerts marked as read."));
        });

        api.MapPost("/alerts/{code}/ack", (string code, Store store) =>
        {
            store.AcknowledgeAlert(code, DateTime.UtcNow);
            return Results.Ok(new ActionResult(true, "Acknowledged."));
        });

        api.MapPost("/alerts/{code}/snooze", (string code, SnoozeRequest body, Store store) =>
        {
            var hours = Math.Clamp(body.Hours, 1, 24 * 30);
            store.SnoozeAlert(code, DateTime.UtcNow.AddHours(hours));
            return Results.Ok(new ActionResult(true, $"Quiet for {hours} hours."));
        });

        // ------------------------------------------------------------- settings
        api.MapGet("/settings", (SettingsService settings, ProviderFactory providers) => Results.Ok(new
        {
            settings = settings.Current,
            sources = providers.All.Select(p => new { key = p.Key, display = p.Display }),
            dataFolder = AppPaths.DataFolder,
        }));

        api.MapPut("/settings", (AppSettings body, SettingsService settings) =>
            Results.Ok(new { settings = settings.Save(body) }));

        api.MapPost("/providers/test", async (ProviderFactory providers, CancellationToken ct) =>
        {
            var provider = providers.Resolve();
            var result = await provider.TestAsync(ct);
            return Results.Ok(new ActionResult(result.Ok, result.Message ?? (result.Ok ? "Connection fine." : "Connection failed.")));
        });

        // ----------------------------------------------------------------- sync
        api.MapPost("/sync", async (SyncRunner runner, CancellationToken ct) =>
        {
            var result = await runner.RunOnceAsync(ct);
            return Results.Ok(new ActionResult(result.Ok, result.Message ?? "Refreshed.", result.Items.Count));
        });

        // ----------------------------------------------- demo helper (demo only)
        api.MapPost("/demo/set-qty", (DemoQtyRequest body, ProviderFactory providers, SettingsService settings, SyncRunner runner) =>
        {
            if (settings.Current.DataSource != "demo")
                return Results.BadRequest(new ActionResult(false, "Only available while using demo data."));

            var ok = providers.Demo.SetQtyPacksForDemo(body.ItemCode, body.Packs);
            return ok
                ? Results.Ok(new ActionResult(true, $"Demo stock set to {body.Packs:0.##} packs. Press Refresh to see the alert."))
                : Results.NotFound(new ActionResult(false, "Medicine not found."));
        });

        api.MapGet("/health", () => Results.Ok(new { ok = true, utc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture) }));
    }
}
