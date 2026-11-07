using System.Drawing;
using System.Windows.Forms;
using PharmacyStock.Models;

namespace PharmacyStock.Desktop.Ui;

internal sealed class AlertsPage : PageBase
{
    private readonly DataGridView _grid = Theme.Grid();

    public AlertsPage(AppServices services) : base(services)
    {
        var explain = new Label
        {
            Dock = DockStyle.Fill,
            Font = Theme.Base,
            ForeColor = Theme.TextDim,
            TextAlign = ContentAlignment.MiddleLeft,
            Text = "Only changes are listed. A medicine that stays short is not repeated every minute, " +
                   "so this list stays worth reading.",
        };

        var markRead = Theme.Secondary("Mark all as read", 160);
        markRead.Click += (s, e) =>
        {
            Services.Store.MarkAlertsSeen();
            Reload();
            CountsChanged?.Invoke();
        };

        var quiet = Theme.Secondary("Quiet for 24 hours", 170);
        quiet.Click += (s, e) => SnoozeSelected();

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0, 0, 0, 6),
        };
        buttons.Controls.Add(markRead);
        buttons.Controls.Add(quiet);

        _grid.Columns.Add(Theme.TextColumn("time", "When", 14));
        _grid.Columns.Add(Theme.TextColumn("what", "What happened", 46));
        _grid.Columns.Add(Theme.TextColumn("detail", "Detail", 28));
        _grid.Columns.Add(Theme.TextColumn("tag", "", 8));

        var card = Theme.Card();
        card.Controls.Add(_grid);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 26f));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42f));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        root.Controls.Add(explain, 0, 0);
        root.Controls.Add(buttons, 0, 1);
        root.Controls.Add(card, 0, 2);

        Controls.Add(root);
    }

    public override void Reload()
    {
        var events = Services.Store.GetAlertEvents(300);

        _grid.SuspendLayout();
        _grid.Rows.Clear();

        foreach (var alert in events)
        {
            var index = _grid.Rows.Add(
                Fmt.LocalTime(alert.AtUtc),
                Fmt.AlertText(alert),
                $"{Fmt.PacksPhrase(alert.QtyPacks)}  -  shelf level {Fmt.Packs(alert.LevelPacks)}",
                Fmt.AlertTag(alert));

            var row = _grid.Rows[index];
            row.Tag = alert.ItemCode;

            var colours = alert.To switch
            {
                StockState.Out => (Theme.OutFg, Theme.OutBg),
                StockState.Low => (Theme.LowFg, Theme.LowBg),
                _ => (Theme.OkFg, Theme.OkBg),
            };
            row.Cells["tag"].Style.ForeColor = colours.Item1;
            row.Cells["tag"].Style.BackColor = colours.Item2;
            row.Cells["tag"].Style.Font = Theme.Bold;

            if (!alert.Seen) row.Cells["what"].Style.Font = Theme.Bold;
        }

        _grid.ResumeLayout();
    }

    private void SnoozeSelected()
    {
        if (_grid.CurrentRow?.Tag is not string code)
        {
            Problem("Click a medicine in the list first.");
            return;
        }

        Services.Store.SnoozeAlert(code, DateTime.UtcNow.AddHours(24));
        Toast("No more alerts about that medicine for 24 hours.");
    }
}
