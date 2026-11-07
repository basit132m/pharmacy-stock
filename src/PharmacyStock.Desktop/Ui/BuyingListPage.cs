using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using PharmacyStock.Models;
using PharmacyStock.Services;

namespace PharmacyStock.Desktop.Ui;

internal sealed class BuyingListPage : PageBase
{
    private readonly ComboBox _supplier = new();
    private readonly DataGridView _grid = Theme.Grid();
    private readonly Label _summary = new();
    private readonly CheckBox _includeDead = new();
    private bool _loading;

    public BuyingListPage(AppServices services) : base(services)
    {
        _supplier.DropDownStyle = ComboBoxStyle.DropDownList;
        _supplier.Width = 220;
        _supplier.Font = Theme.Base;
        _supplier.SelectedIndexChanged += (s, e) => { if (!_loading) Reload(); };

        var autofill = Theme.Primary("Add everything short", 180);
        autofill.Click += (s, e) => AutoFill();

        var copy = Theme.Secondary("Copy order for WhatsApp", 200);
        copy.Click += (s, e) => CopyOrder();

        var markOrdered = Theme.Secondary("Mark supplier ordered", 190);
        markOrdered.Click += (s, e) => MarkSupplierOrdered();

        var csv = Theme.Secondary("Download CSV", 140);
        csv.Click += (s, e) => ExportCsv();

        _includeDead.Text = "Include dead stock";
        _includeDead.Font = Theme.Base;
        _includeDead.AutoSize = true;
        _includeDead.Margin = new Padding(10, 9, 3, 3);
        _includeDead.CheckedChanged += (s, e) => { if (!_loading) Reload(); };

        _summary.Font = Theme.Base;
        _summary.ForeColor = Theme.TextDim;
        _summary.Dock = DockStyle.Fill;
        _summary.TextAlign = ContentAlignment.MiddleLeft;

        BuildGrid();

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0, 0, 0, 6),
        };
        buttons.Controls.Add(_supplier);
        buttons.Controls.Add(autofill);
        buttons.Controls.Add(copy);
        buttons.Controls.Add(markOrdered);
        buttons.Controls.Add(csv);
        buttons.Controls.Add(_includeDead);

        var hint = new Label
        {
            Text = "Double-click a row to mark it ordered. Type in the Order column to change how many packs to buy.",
            Dock = DockStyle.Fill,
            Font = Theme.Small,
            ForeColor = Theme.TextDim,
            TextAlign = ContentAlignment.MiddleLeft,
        };

        var card = Theme.Card();
        card.Controls.Add(_grid);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4 };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42f));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 24f));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 22f));
        root.Controls.Add(buttons, 0, 0);
        root.Controls.Add(_summary, 0, 1);
        root.Controls.Add(card, 0, 2);
        root.Controls.Add(hint, 0, 3);

        Controls.Add(root);
    }

    private void BuildGrid()
    {
        _grid.ReadOnly = false;
        _grid.EditMode = DataGridViewEditMode.EditOnKeystrokeOrF2;

        _grid.Columns.Add(Theme.TextColumn("supplier", "Supplier", 17));
        _grid.Columns.Add(Theme.TextColumn("name", "Medicine", 26));
        _grid.Columns.Add(Theme.TextColumn("company", "Company", 12));
        _grid.Columns.Add(Theme.NumberColumn("stock", "In stock", 9));
        _grid.Columns.Add(Theme.NumberColumn("level", "Shelf level", 10));

        var order = Theme.NumberColumn("order", "Order", 8);
        order.ReadOnly = false;
        order.DefaultCellStyle.BackColor = Theme.Surface2;
        order.DefaultCellStyle.Font = Theme.Bold;
        _grid.Columns.Add(order);

        _grid.Columns.Add(Theme.NumberColumn("value", "Value", 10));
        _grid.Columns.Add(Theme.TextColumn("status", "Status", 8));

        _grid.CellEndEdit += OnQtyEdited;
        _grid.CellDoubleClick += (s, e) =>
        {
            if (e.RowIndex < 0) return;
            var row = e.RowIndex;
            BeginInvoke(new Action(() => ToggleOrdered(row)));
        };
        _grid.DataError += (s, e) => e.ThrowException = false;
    }

    public override void Reload()
    {
        _loading = true;
        try
        {
            var groups = Services.Orders.Build(_includeDead.Checked);
            RefreshSuppliers(groups);

            var chosen = _supplier.SelectedIndex > 0 ? _supplier.SelectedItem?.ToString() : null;
            var shown = chosen is null
                ? groups
                : groups.Where(g => string.Equals(g.Supplier, chosen, StringComparison.OrdinalIgnoreCase)).ToList();

            _grid.SuspendLayout();
            _grid.Rows.Clear();

            var lines = 0;
            var outCount = 0;
            decimal total = 0m;

            foreach (var group in shown)
            {
                foreach (var line in group.Lines)
                {
                    var index = _grid.Rows.Add(
                        group.Supplier,
                        line.Name,
                        line.Company ?? "",
                        Fmt.Stock(line.InStockPacks, line.WholePacks, line.LooseUnits, line.UnitsPerPack),
                        Fmt.Packs(line.LevelPacks),
                        Fmt.Packs(line.QtyPacks),
                        Fmt.Money(line.LineValue),
                        line.Status == "Pending" ? line.State : line.Status);

                    var row = _grid.Rows[index];
                    row.Tag = line;

                    var (fore, back) = Theme.StateColours(line.State);
                    row.Cells["status"].Style.ForeColor = line.Status == "Pending" ? fore : Theme.Brand;
                    row.Cells["status"].Style.BackColor = line.Status == "Pending" ? back : Theme.BrandSoft;
                    row.Cells["status"].Style.Font = Theme.Bold;

                    lines++;
                    if (line.State == "OUT") outCount++;
                    total += line.LineValue;
                }
            }

            _grid.ResumeLayout();

            _summary.Text = lines == 0
                ? "Nothing to buy - no managed medicine is below its shelf level."
                : $"{lines} medicines from {shown.Count} supplier(s)  -  {outCount} completely finished  -  approx {Fmt.Money(total)}";
        }
        finally
        {
            _loading = false;
        }
    }

    private void RefreshSuppliers(List<SupplierGroup> groups)
    {
        var wanted = _supplier.SelectedItem?.ToString();
        var names = groups.Select(g => g.Supplier).ToList();

        if (_supplier.Items.Count == names.Count + 1) return;

        _supplier.Items.Clear();
        _supplier.Items.Add("All suppliers");
        foreach (var name in names) _supplier.Items.Add(name);

        var index = wanted is null ? 0 : _supplier.Items.IndexOf(wanted);
        _supplier.SelectedIndex = index < 0 ? 0 : index;
    }

    private void OnQtyEdited(object? sender, DataGridViewCellEventArgs e)
    {
        if (_loading || e.RowIndex < 0) return;
        if (_grid.Columns[e.ColumnIndex].Name != "order") return;
        if (_grid.Rows[e.RowIndex].Tag is not OrderRow line) return;

        var raw = (_grid.Rows[e.RowIndex].Cells["order"].Value?.ToString() ?? "").Trim();

        // Rows cannot be rebuilt from inside CellEndEdit; run it straight afterwards.
        BeginInvoke(new Action(() => SaveQty(line, raw)));
    }

    private void SaveQty(OrderRow line, string raw)
    {
        if (!decimal.TryParse(raw, NumberStyles.Any, CultureInfo.CurrentCulture, out var qty) || qty < 0m)
        {
            Problem($"\"{raw}\" is not a number. Type how many packs to order.");
            Reload();
            return;
        }

        Services.Orders.Save(line.ItemCode, qty, "Pending", line.Supplier, line.Notes, null, line.OrderId);
        Reload();
        CountsChanged?.Invoke();
    }

    private void ToggleOrdered(int rowIndex)
    {
        if (_grid.Rows[rowIndex].Tag is not OrderRow line) return;

        var next = line.Status switch
        {
            "Ordered" => nameof(OrderStatus.Received),
            "Received" => nameof(OrderStatus.Pending),
            _ => nameof(OrderStatus.Ordered),
        };

        Services.Orders.Save(line.ItemCode, line.QtyPacks, next, line.Supplier, line.Notes,
            next == nameof(OrderStatus.Ordered)
                ? DateTime.UtcNow.AddDays(Services.Settings.Current.LeadTimeDays)
                : null,
            line.OrderId);

        Reload();
        CountsChanged?.Invoke();
    }

    private void AutoFill()
    {
        var result = Services.Orders.AutoFill();
        Reload();
        CountsChanged?.Invoke();
        Toast(result.Message);
    }

    private void MarkSupplierOrdered()
    {
        if (_supplier.SelectedIndex <= 0)
        {
            Problem("Choose a supplier first, then press this to mark that whole order as placed.");
            return;
        }

        var supplier = _supplier.SelectedItem!.ToString()!;
        var result = Services.Orders.MarkSupplierOrdered(supplier, Services.Settings.Current.LeadTimeDays);
        Reload();
        CountsChanged?.Invoke();
        Toast(result.Message);
    }

    private void CopyOrder()
    {
        var supplier = _supplier.SelectedIndex > 0 ? _supplier.SelectedItem?.ToString() : null;
        var text = Services.Orders.ExportText(supplier);

        try
        {
            Clipboard.SetText(text);
            Toast("Order copied.\r\n\r\nOpen WhatsApp and paste it into the chat with your distributor.");
        }
        catch (Exception ex)
        {
            Problem("Could not copy to the clipboard.\r\n\r\n" + ex.Message);
        }
    }

    private void ExportCsv()
    {
        var supplier = _supplier.SelectedIndex > 0 ? _supplier.SelectedItem?.ToString() : null;

        using var dialog = new SaveFileDialog
        {
            Title = "Save order",
            Filter = "CSV spreadsheet (*.csv)|*.csv",
            FileName = $"order-{DateTime.Now:yyyy-MM-dd}.csv",
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            File.WriteAllText(dialog.FileName, Services.Orders.ExportCsv(supplier));
            Toast("Saved.\r\n\r\n" + dialog.FileName);
        }
        catch (Exception ex)
        {
            Problem("Could not save the file.\r\n\r\n" + ex.Message);
        }
    }
}
