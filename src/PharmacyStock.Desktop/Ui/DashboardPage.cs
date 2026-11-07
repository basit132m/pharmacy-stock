using System.Drawing;
using System.Windows.Forms;
using PharmacyStock.Models;

namespace PharmacyStock.Desktop.Ui;

internal sealed class DashboardPage : PageBase
{
    private readonly Label _notice = new();
    private readonly Tile _outTile, _lowTile, _okTile, _valueTile;
    private readonly DataGridView _urgent = Theme.Grid();
    private readonly DataGridView _alerts = Theme.Grid();

    public DashboardPage(AppServices services) : base(services)
    {
        _outTile   = new Tile("OUT OF STOCK",       "Nothing left on the shelf",   Theme.OutFg, "out");
        _lowTile   = new Tile("BELOW SHELF LEVEL",  "Running short - order soon",  Theme.LowFg, "low");
        _okTile    = new Tile("STOCK IS FINE",      "At or above shelf level",     Theme.OkFg,  "ok");
        _valueTile = new Tile("TO BUY, APPROX",     "On the buying list",          Theme.Brand, "needs");

        foreach (var tile in new[] { _outTile, _lowTile, _okTile, _valueTile })
            tile.Clicked = filter => ShowBoard?.Invoke(filter);

        _notice.AutoSize = false;
        _notice.Dock = DockStyle.Fill;
        _notice.Height = 44;
        _notice.TextAlign = ContentAlignment.MiddleLeft;
        _notice.Padding = new Padding(12, 0, 12, 0);
        _notice.BackColor = Theme.BrandSoft;
        _notice.ForeColor = Theme.Text;
        _notice.Font = Theme.Base;

        BuildUrgentGrid();
        BuildAlertsGrid();

        var tiles = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 1,
            Margin = new Padding(0, 10, 0, 10),
        };
        for (var i = 0; i < 4; i++)
            tiles.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));
        tiles.Controls.Add(_outTile, 0, 0);
        tiles.Controls.Add(_lowTile, 1, 0);
        tiles.Controls.Add(_okTile, 2, 0);
        tiles.Controls.Add(_valueTile, 3, 0);

        var lists = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        lists.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58f));
        lists.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42f));
        lists.Controls.Add(Section("Order these first", _urgent), 0, 0);
        lists.Controls.Add(Section("What changed recently", _alerts), 1, 0);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46f));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 112f));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        root.Controls.Add(_notice, 0, 0);
        root.Controls.Add(tiles, 0, 1);
        root.Controls.Add(lists, 0, 2);

        Controls.Add(root);
    }

    private static Control Section(string title, Control content)
    {
        var header = new Label
        {
            Text = title,
            Dock = DockStyle.Top,
            Height = 34,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(10, 0, 0, 0),
            Font = Theme.Bold,
            ForeColor = Theme.Text,
            BackColor = Theme.Surface2,
        };

        var card = Theme.Card();
        card.Margin = new Padding(0, 0, 8, 0);
        card.Padding = new Padding(1);

        content.Dock = DockStyle.Fill;
        card.Controls.Add(content);
        card.Controls.Add(header);
        return card;
    }

    private void BuildUrgentGrid()
    {
        _urgent.Columns.Add(Theme.TextColumn("name", "Medicine", 34));
        _urgent.Columns.Add(Theme.NumberColumn("stock", "In stock", 16));
        _urgent.Columns.Add(Theme.NumberColumn("level", "Shelf level", 17));
        _urgent.Columns.Add(Theme.NumberColumn("buy", "Buy", 13));
        _urgent.Columns.Add(Theme.TextColumn("state", "Status", 14));
        _urgent.CellDoubleClick += (s, e) => { if (e.RowIndex >= 0) ShowBoard?.Invoke("needs"); };
    }

    private void BuildAlertsGrid()
    {
        _alerts.Columns.Add(Theme.TextColumn("time", "When", 22));
        _alerts.Columns.Add(Theme.TextColumn("what", "What happened", 56));
        _alerts.Columns.Add(Theme.TextColumn("tag", "", 16));
    }

    public override void Reload()
    {
        var counts = Services.Stock.Dashboard();

        _outTile.Set(Fmt.Whole(counts.OutOfStock));
        _lowTile.Set(Fmt.Whole(counts.BelowLevel));
        _okTile.Set(Fmt.Whole(counts.Ok));
        _valueTile.Set(Fmt.Money(counts.OrderValueEstimate));
        _valueTile.SetNote($"{counts.OutOfStock + counts.BelowLevel} medicines need ordering");

        if (counts.TotalItems > 0 && counts.Unmanaged == counts.TotalItems)
        {
            _notice.BackColor = Theme.LowBg;
            _notice.Text = "No shelf levels are set yet, so nothing can be flagged.  " +
                           "Open the Shelf Levels tab and press \"Work out shelf levels\".";
        }
        else if (counts.SyncStatus == "error")
        {
            _notice.BackColor = Theme.OutBg;
            _notice.Text = "Could not read stock: " + counts.SyncMessage +
                           "   The figures below are the last ones read successfully.";
        }
        else if (counts.DataSource == "demo")
        {
            _notice.BackColor = Theme.BrandSoft;
            _notice.Text = "Demo data - these are sample medicines, not your real stock.  " +
                           "Shelf levels you set now are saved and will carry over to SeaSoft.";
        }
        else
        {
            _notice.BackColor = Theme.OkBg;
            _notice.Text = $"Reading from {counts.DataSource}.  " +
                           $"{Fmt.Whole(counts.TotalItems)} medicines, " +
                           $"{Fmt.Whole(counts.Unmanaged)} not managed.";
        }

        FillUrgent();
        FillAlerts();
    }

    private void FillUrgent()
    {
        var rows = Services.Stock
            .Query(new Services.StockQuery(Status: "needs", Sort: "urgency", PageSize: 40))
            .Items;

        _urgent.SuspendLayout();
        _urgent.Rows.Clear();

        foreach (var row in rows)
        {
            var index = _urgent.Rows.Add(
                row.Name,
                Fmt.Stock(row),
                Fmt.Packs(row.LevelPacks ?? 0m),
                Fmt.Packs(row.ShortfallPacks),
                row.StateText);

            var (fore, back) = Theme.StateColours(row.StateText);
            _urgent.Rows[index].Cells["state"].Style.ForeColor = fore;
            _urgent.Rows[index].Cells["state"].Style.BackColor = back;
            _urgent.Rows[index].Cells["state"].Style.Font = Theme.Bold;
        }

        _urgent.ResumeLayout();
    }

    private void FillAlerts()
    {
        var events = Services.Store.GetAlertEvents(40);

        _alerts.SuspendLayout();
        _alerts.Rows.Clear();

        foreach (var alert in events)
        {
            var index = _alerts.Rows.Add(
                Fmt.LocalTime(alert.AtUtc),
                Fmt.AlertText(alert),
                Fmt.AlertTag(alert));

            var tag = alert.To switch
            {
                StockState.Out => (Theme.OutFg, Theme.OutBg),
                StockState.Low => (Theme.LowFg, Theme.LowBg),
                _ => (Theme.OkFg, Theme.OkBg),
            };
            _alerts.Rows[index].Cells["tag"].Style.ForeColor = tag.Item1;
            _alerts.Rows[index].Cells["tag"].Style.BackColor = tag.Item2;
            _alerts.Rows[index].Cells["tag"].Style.Font = Theme.Bold;
        }

        _alerts.ResumeLayout();
    }

    /// <summary>A big clickable number with a coloured edge.</summary>
    private sealed class Tile : Panel
    {
        private readonly Label _value;
        private readonly Label _note;
        private readonly string _filter;

        public Action<string>? Clicked;

        public Tile(string caption, string note, Color accent, string filter)
        {
            _filter = filter;
            Dock = DockStyle.Fill;
            Margin = new Padding(0, 0, 10, 0);
            BackColor = accent;
            Padding = new Padding(4, 0, 0, 0);
            Cursor = Cursors.Hand;

            var inner = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Surface, Padding = new Padding(14, 10, 10, 8) };

            var captionLabel = new Label
            {
                Text = caption,
                Dock = DockStyle.Top,
                Height = 18,
                Font = Theme.Caption,
                ForeColor = Theme.TextDim,
            };

            _value = new Label
            {
                Text = "0",
                Dock = DockStyle.Top,
                Height = 46,
                Font = Theme.Huge,
                ForeColor = accent,
            };

            _note = new Label
            {
                Text = note,
                Dock = DockStyle.Top,
                Height = 18,
                Font = Theme.Small,
                ForeColor = Theme.TextDim,
            };

            // Docked controls stack from the last added to the first, so these go on in
            // reverse of how they should appear.
            inner.Controls.Add(_note);
            inner.Controls.Add(_value);
            inner.Controls.Add(captionLabel);
            Controls.Add(inner);

            Wire(this);
        }

        private void Wire(Control control)
        {
            control.Click += (s, e) => Clicked?.Invoke(_filter);
            foreach (Control child in control.Controls) Wire(child);
        }

        public void Set(string value)
        {
            // A long money figure must not be clipped by the fixed tile height.
            _value.Font = value.Length > 8 ? Theme.BigNum : Theme.Huge;
            _value.Text = value;
        }

        public void SetNote(string note) => _note.Text = note;
    }
}
