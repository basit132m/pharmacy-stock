using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using PharmacyStock.Models;
using PharmacyStock.Services;

namespace PharmacyStock.Desktop.Ui;

internal sealed class ShelfLevelsPage : PageBase
{
    private readonly ComboBox _scope = new();
    private readonly DataGridView _grid = Theme.Grid();
    private readonly Label _result = new();
    private bool _loading;

    public ShelfLevelsPage(AppServices services) : base(services)
    {
        var explain = new Label
        {
            Dock = DockStyle.Fill,
            Font = Theme.Base,
            ForeColor = Theme.Text,
            BackColor = Theme.BrandSoft,
            Padding = new Padding(12, 8, 12, 8),
            Text =
                "You do not have to type thousands of numbers.\r\n" +
                "The program can work out a shelf level for every medicine from its own sales history - " +
                "roughly how many it sells per day, multiplied by how long your distributor takes to deliver, " +
                "plus a safety margin. Dead stock is switched off automatically. " +
                "Accept those in one click, then hand-adjust only your fast movers below.",
        };

        _scope.DropDownStyle = ComboBoxStyle.DropDownList;
        _scope.Width = 380;
        _scope.Font = Theme.Base;
        _scope.Items.AddRange(new object[]
        {
            "Only medicines with no level yet (safe - keeps what you typed)",
            "Only medicines that are actually selling",
            "Every medicine (overwrites your own numbers too)",
        });
        _scope.SelectedIndex = 0;

        var apply = Theme.Primary("Work out shelf levels", 190);
        apply.Click += (s, e) => ApplySuggestions();

        var export = Theme.Secondary("Download spreadsheet", 180);
        export.Click += (s, e) => Export();

        var import = Theme.Secondary("Load edited file", 150);
        import.Click += (s, e) => Import();

        _result.Font = Theme.Base;
        _result.ForeColor = Theme.TextDim;
        _result.AutoSize = false;
        _result.Dock = DockStyle.Fill;
        _result.TextAlign = ContentAlignment.MiddleLeft;
        _result.Padding = new Padding(2, 0, 0, 0);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0, 8, 0, 4),
        };
        buttons.Controls.Add(_scope);
        buttons.Controls.Add(apply);
        buttons.Controls.Add(export);
        buttons.Controls.Add(import);

        BuildGrid();

        var heading = new Label
        {
            Text = "Fast movers - these are the ones worth setting by hand",
            Dock = DockStyle.Top,
            Height = 34,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(10, 0, 0, 0),
            Font = Theme.Bold,
            BackColor = Theme.Surface2,
            ForeColor = Theme.Text,
        };

        var card = Theme.Card();
        card.Controls.Add(_grid);
        card.Controls.Add(heading);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4 };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 84f));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44f));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 24f));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        root.Controls.Add(explain, 0, 0);
        root.Controls.Add(buttons, 0, 1);
        root.Controls.Add(_result, 0, 2);
        root.Controls.Add(card, 0, 3);

        Controls.Add(root);
    }

    private void BuildGrid()
    {
        _grid.ReadOnly = false;
        _grid.EditMode = DataGridViewEditMode.EditOnKeystrokeOrF2;

        _grid.Columns.Add(Theme.TextColumn("name", "Medicine", 30));
        _grid.Columns.Add(Theme.TextColumn("company", "Company", 14));
        _grid.Columns.Add(Theme.NumberColumn("sold", "30-day sale", 12));
        _grid.Columns.Add(Theme.NumberColumn("stock", "In stock", 11));

        var level = Theme.NumberColumn("level", "Shelf level", 12);
        level.ReadOnly = false;
        level.DefaultCellStyle.BackColor = Theme.Surface2;
        level.DefaultCellStyle.Font = Theme.Bold;
        _grid.Columns.Add(level);

        _grid.Columns.Add(Theme.NumberColumn("suggested", "Suggested", 11));

        var managed = new DataGridViewCheckBoxColumn
        {
            Name = "managed",
            HeaderText = "Watch",
            FillWeight = 8,
            ReadOnly = false,
            SortMode = DataGridViewColumnSortMode.NotSortable,
            ToolTipText = "Untick to stop watching this medicine completely.",
        };
        _grid.Columns.Add(managed);

        _grid.CellEndEdit += OnCellEdited;

        // Checkboxes normally only commit when the cell loses focus; this makes a single
        // click take effect immediately, which is what people expect.
        _grid.CurrentCellDirtyStateChanged += (s, e) =>
        {
            if (_grid.IsCurrentCellDirty && _grid.CurrentCell is DataGridViewCheckBoxCell)
                _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        _grid.CellValueChanged += (s, e) =>
        {
            if (_loading || e.RowIndex < 0) return;
            if (_grid.Columns[e.ColumnIndex].Name != "managed") return;
            if (_grid.Rows[e.RowIndex].Tag is not string code) return;

            var on = _grid.Rows[e.RowIndex].Cells["managed"].Value is true;
            BeginInvoke(new Action(() =>
            {
                Services.Levels.SetManaged(code, on);
                CountsChanged?.Invoke();
            }));
        };
        _grid.DataError += (s, e) => e.ThrowException = false;
    }

    public override void Reload()
    {
        _loading = true;
        try
        {
            var rows = Services.Stock
                .Query(new StockQuery(Status: "", Sort: "sold", Desc: true, PageSize: 80))
                .Items;

            _grid.SuspendLayout();
            _grid.Rows.Clear();

            foreach (var row in rows)
            {
                var index = _grid.Rows.Add(
                    row.Name,
                    row.Company ?? "",
                    Fmt.Whole(Math.Round(Fmt.SoldPacks(row))),
                    Fmt.Stock(row),
                    row.LevelPacks is null or 0m ? "" : Fmt.Packs(row.LevelPacks.Value),
                    Fmt.Packs(row.SuggestedLevelPacks),
                    row.IsManaged);

                _grid.Rows[index].Tag = row.ItemCode;
                _grid.Rows[index].Cells["suggested"].Style.ForeColor = Theme.TextDim;
            }

            _grid.ResumeLayout();
        }
        finally
        {
            _loading = false;
        }
    }

    private void OnCellEdited(object? sender, DataGridViewCellEventArgs e)
    {
        if (_loading || e.RowIndex < 0) return;
        if (_grid.Columns[e.ColumnIndex].Name != "level") return;
        if (_grid.Rows[e.RowIndex].Tag is not string code) return;

        var raw = (_grid.Rows[e.RowIndex].Cells["level"].Value?.ToString() ?? "").Trim();

        // Rows cannot be rebuilt from inside CellEndEdit; run it straight afterwards.
        BeginInvoke(new Action(() => SaveLevel(code, raw)));
    }

    private void SaveLevel(string code, string raw)
    {
        if (raw.Length == 0)
        {
            Services.Levels.SetOne(code, 0m, false, null, null);
        }
        else if (decimal.TryParse(raw, NumberStyles.Any, CultureInfo.CurrentCulture, out var level) && level >= 0m)
        {
            Services.Levels.SetOne(code, level, level > 0m, null, null);
        }
        else
        {
            Problem($"\"{raw}\" is not a number. Type how many packs to keep, for example 5.");
        }

        Reload();
        CountsChanged?.Invoke();
    }

    private void ApplySuggestions()
    {
        var scope = _scope.SelectedIndex switch { 1 => "selling", 2 => "all", _ => "missing" };

        if (scope == "all")
        {
            var confirm = MessageBox.Show(this,
                "This will overwrite shelf levels you typed yourself, for every medicine.\r\n\r\nContinue?",
                "Overwrite all shelf levels?", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (confirm != DialogResult.Yes) return;
        }

        try
        {
            Cursor = Cursors.WaitCursor;
            var result = Services.Levels.ApplySuggestions(scope);
            _result.Text = result.Message;
            Reload();
            CountsChanged?.Invoke();
        }
        catch (Exception ex)
        {
            Problem("Could not work out shelf levels.\r\n\r\n" + ex.Message);
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private void Export()
    {
        using var dialog = new SaveFileDialog
        {
            Title = "Save shelf levels",
            Filter = "CSV spreadsheet (*.csv)|*.csv",
            FileName = $"shelf-levels-{DateTime.Now:yyyy-MM-dd}.csv",
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            File.WriteAllText(dialog.FileName, Services.Levels.ExportCsv());
            _result.Text = "Saved to " + dialog.FileName;
        }
        catch (Exception ex)
        {
            Problem("Could not save the file.\r\n\r\n" + ex.Message);
        }
    }

    private void Import()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Choose the edited spreadsheet",
            Filter = "CSV spreadsheet (*.csv)|*.csv|All files (*.*)|*.*",
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            var text = File.ReadAllText(dialog.FileName);
            var result = Services.Levels.ImportCsv(text);
            _result.Text = result.Message;
            Reload();
            CountsChanged?.Invoke();
        }
        catch (Exception ex)
        {
            Problem("Could not read that file.\r\n\r\n" + ex.Message);
        }
    }
}
