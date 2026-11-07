using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using PharmacyStock.Models;
using PharmacyStock.Services;

namespace PharmacyStock.Desktop.Ui;

internal sealed class StockBoardPage : PageBase
{
    private const int PageSize = 400;

    private readonly TextBox _search = new();
    private readonly ComboBox _status = new();
    private readonly ComboBox _supplier = new();
    private readonly DataGridView _grid = Theme.Grid();
    private readonly Label _count = new();
    private readonly Label _pageText = new();
    private readonly Button _prev = Theme.Secondary("< Previous", 110);
    private readonly Button _next = Theme.Secondary("Next >", 110);
    private readonly System.Windows.Forms.Timer _searchDelay = new() { Interval = 300 };

    private int _page = 1;
    private int _pages = 1;
    private bool _loading;

    public StockBoardPage(AppServices services) : base(services)
    {
        _status.DropDownStyle = ComboBoxStyle.DropDownList;
        _status.Width = 210;
        _status.Font = Theme.Base;
        _status.Items.AddRange(new object[]
        {
            "Needs ordering (out + low)",
            "All medicines",
            "Out of stock only",
            "Below shelf level only",
            "Stock is fine",
            "No shelf level set",
        });
        _status.SelectedIndex = 0;
        _status.SelectedIndexChanged += (s, e) => { if (_loading) return; _page = 1; Reload(); };

        _supplier.DropDownStyle = ComboBoxStyle.DropDownList;
        _supplier.Width = 200;
        _supplier.Font = Theme.Base;
        _supplier.SelectedIndexChanged += (s, e) => { if (_loading) return; _page = 1; Reload(); };

        _search.Width = 260;
        _search.Font = Theme.Base;
        _search.PlaceholderText = "Search medicine, formula, company...";
        _search.TextChanged += (s, e) => { _searchDelay.Stop(); _searchDelay.Start(); };
        _searchDelay.Tick += (s, e) => { _searchDelay.Stop(); _page = 1; Reload(); };

        var export = Theme.Secondary("Download spreadsheet", 180);
        export.Click += (s, e) => ExportSpreadsheet();

        BuildGrid();

        _prev.Click += (s, e) => { if (_page > 1) { _page--; Reload(); } };
        _next.Click += (s, e) => { if (_page < _pages) { _page++; Reload(); } };

        _count.Font = Theme.Base;
        _count.ForeColor = Theme.TextDim;
        _count.AutoSize = true;
        _count.Margin = new Padding(3, 9, 16, 3);
        _pageText.Font = Theme.Base;
        _pageText.ForeColor = Theme.TextDim;
        _pageText.AutoSize = true;
        _pageText.Margin = new Padding(8, 9, 8, 3);

        var filters = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0, 0, 0, 8),
        };
        filters.Controls.Add(_search);
        filters.Controls.Add(_status);
        filters.Controls.Add(_supplier);
        filters.Controls.Add(export);

        var pager = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0, 6, 0, 0),
        };
        pager.Controls.Add(_count);
        pager.Controls.Add(_prev);
        pager.Controls.Add(_pageText);
        pager.Controls.Add(_next);

        var hint = new Label
        {
            Text = "Tip: click a Shelf level box and type a new number. It saves when you press Enter or click away.",
            Dock = DockStyle.Fill,
            Font = Theme.Small,
            ForeColor = Theme.TextDim,
            TextAlign = ContentAlignment.MiddleLeft,
        };

        var card = Theme.Card();
        card.Controls.Add(_grid);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4 };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42f));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40f));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 22f));
        root.Controls.Add(filters, 0, 0);
        root.Controls.Add(card, 0, 1);
        root.Controls.Add(pager, 0, 2);
        root.Controls.Add(hint, 0, 3);

        Controls.Add(root);
    }

    private void BuildGrid()
    {
        _grid.ReadOnly = false;
        _grid.EditMode = DataGridViewEditMode.EditOnKeystrokeOrF2;

        _grid.Columns.Add(Theme.TextColumn("name", "Medicine", 26));
        _grid.Columns.Add(Theme.TextColumn("generic", "Formula", 16));
        _grid.Columns.Add(Theme.TextColumn("company", "Company", 11));
        _grid.Columns.Add(Theme.TextColumn("supplier", "Supplier", 15));
        _grid.Columns.Add(Theme.NumberColumn("stock", "In stock", 10));

        var level = Theme.NumberColumn("level", "Shelf level", 10);
        level.ReadOnly = false;                       // the one editable column
        level.DefaultCellStyle.BackColor = Theme.Surface2;
        level.DefaultCellStyle.Font = Theme.Bold;
        level.ToolTipText = "Minimum packs to keep. Type a number and press Enter.";
        _grid.Columns.Add(level);

        _grid.Columns.Add(Theme.NumberColumn("buy", "Buy", 7));
        _grid.Columns.Add(Theme.NumberColumn("sold", "30-day sale", 10));
        _grid.Columns.Add(Theme.TextColumn("state", "Status", 9));

        _grid.CellEndEdit += OnLevelEdited;
        _grid.DataError += (s, e) => e.ThrowException = false;
    }

    /// <summary>Lets the dashboard tiles jump straight to a filtered view.</summary>
    public void ApplyFilter(string filter)
    {
        _status.SelectedIndex = filter switch
        {
            "out" => 2,
            "low" => 3,
            "ok" => 4,
            "unmanaged" => 5,
            "all" => 1,
            _ => 0,
        };
        _page = 1;
    }

    public override void Reload()
    {
        _loading = true;
        try
        {
            RefreshSuppliers();

            var status = _status.SelectedIndex switch
            {
                1 => "",
                2 => "out",
                3 => "low",
                4 => "ok",
                5 => "unmanaged",
                _ => "needs",
            };

            var supplier = _supplier.SelectedIndex > 0 ? _supplier.SelectedItem?.ToString() : null;

            var result = Services.Stock.Query(new StockQuery(
                Search: _search.Text.Trim(),
                Status: status,
                Supplier: supplier,
                Sort: "urgency",
                Page: _page,
                PageSize: PageSize));

            _pages = result.Pages;
            _page = result.Page;

            Fill(result.Items);

            _count.Text = $"{Fmt.Whole(result.Total)} medicines";
            _pageText.Text = $"Page {result.Page} of {result.Pages}";
            _prev.Enabled = result.Page > 1;
            _next.Enabled = result.Page < result.Pages;
        }
        finally
        {
            _loading = false;
        }
    }

    private void RefreshSuppliers()
    {
        var wanted = _supplier.SelectedItem?.ToString();
        var suppliers = Services.Stock.Suppliers();

        if (_supplier.Items.Count == suppliers.Count + 1) return;   // unchanged

        _supplier.Items.Clear();
        _supplier.Items.Add("All suppliers");
        foreach (var name in suppliers) _supplier.Items.Add(name);

        var index = wanted is null ? 0 : _supplier.Items.IndexOf(wanted);
        _supplier.SelectedIndex = index < 0 ? 0 : index;
    }

    private void Fill(IReadOnlyList<StockView> rows)
    {
        _grid.SuspendLayout();
        _grid.Rows.Clear();

        foreach (var row in rows)
        {
            var index = _grid.Rows.Add(
                row.Name,
                row.Generic ?? "",
                row.Company ?? "",
                row.Supplier ?? "",
                Fmt.Stock(row),
                row.LevelPacks is null or 0m ? "" : Fmt.Packs(row.LevelPacks.Value),
                row.ShortfallPacks > 0m ? Fmt.Packs(row.ShortfallPacks) : "",
                Fmt.Whole(Math.Round(Fmt.SoldPacks(row))),
                row.StateText);

            var gridRow = _grid.Rows[index];
            gridRow.Tag = row.ItemCode;

            var (fore, back) = Theme.StateColours(row.StateText);
            gridRow.Cells["state"].Style.ForeColor = fore;
            gridRow.Cells["state"].Style.BackColor = back;
            gridRow.Cells["state"].Style.Font = Theme.Bold;

            if (row.StateText == "OUT")
                gridRow.Cells["name"].Style.ForeColor = Theme.OutFg;

            if (row.LevelPacks is null or 0m)
                gridRow.Cells["level"].ToolTipText =
                    $"No shelf level set. Suggested from sales history: {Fmt.Packs(row.SuggestedLevelPacks)} packs.";
        }

        _grid.ResumeLayout();
    }

    private void OnLevelEdited(object? sender, DataGridViewCellEventArgs e)
    {
        if (_loading || e.RowIndex < 0) return;
        if (_grid.Columns[e.ColumnIndex].Name != "level") return;

        var row = _grid.Rows[e.RowIndex];
        if (row.Tag is not string itemCode) return;

        var raw = (row.Cells["level"].Value?.ToString() ?? "").Trim();

        // Rebuilding the rows, or opening a dialog, from inside CellEndEdit throws -
        // WinForms will not allow the row collection to change while the event runs.
        // Doing the work straight after the event completes avoids that.
        BeginInvoke(new Action(() => SaveLevel(itemCode, raw)));
    }

    private void SaveLevel(string itemCode, string raw)
    {
        if (raw.Length == 0)
        {
            Services.Levels.SetOne(itemCode, 0m, isManaged: false, null, null);
        }
        else if (decimal.TryParse(raw, NumberStyles.Any, CultureInfo.CurrentCulture, out var level) && level >= 0m)
        {
            Services.Levels.SetOne(itemCode, level, isManaged: level > 0m, null, null);
        }
        else
        {
            Problem($"\"{raw}\" is not a number. Type how many packs to keep, for example 5.");
        }

        Reload();
        CountsChanged?.Invoke();
    }

    private void ExportSpreadsheet()
    {
        using var dialog = new SaveFileDialog
        {
            Title = "Save stock list",
            Filter = "CSV spreadsheet (*.csv)|*.csv",
            FileName = $"stock-{DateTime.Now:yyyy-MM-dd}.csv",
        };

        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            File.WriteAllText(dialog.FileName, Services.Levels.ExportCsv());
            Toast("Saved.\n\n" + dialog.FileName);
        }
        catch (Exception ex)
        {
            Problem("Could not save the file.\n\n" + ex.Message);
        }
    }
}
