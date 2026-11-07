using System.Drawing;
using System.Media;
using System.Windows.Forms;
using PharmacyStock.Desktop.Ui;
using PharmacyStock.Diagnostics;
using PharmacyStock.Models;

namespace PharmacyStock.Desktop;

internal sealed class MainForm : Form
{
    private readonly AppServices _services;

    private readonly TabControl _tabs = new();
    private readonly DashboardPage _dashboard;
    private readonly StockBoardPage _board;
    private readonly ShelfLevelsPage _levels;
    private readonly BuyingListPage _buying;
    private readonly AlertsPage _alerts;
    private readonly SettingsPage _settings;

    private readonly Label _brandName = new();
    private readonly Label _syncLabel = new();
    private readonly Button _refresh = Theme.Primary("Refresh now", 130);
    private readonly Label _statusBar = new();

    private readonly NotifyIcon _tray = new();
    private readonly System.Windows.Forms.Timer _syncTimer = new();
    private readonly System.Windows.Forms.Timer _clockTimer = new() { Interval = 15000 };

    private bool _syncing;
    private bool _reallyExit;
    private bool _trayHintShown;
    private long _lastAlertId;

    public MainForm(AppServices services)
    {
        _services = services;

        _dashboard = new DashboardPage(services);
        _board = new StockBoardPage(services);
        _levels = new ShelfLevelsPage(services);
        _buying = new BuyingListPage(services);
        _alerts = new AlertsPage(services);
        _settings = new SettingsPage(services);

        Text = services.Settings.Current.PharmacyName + " - Stock Manager";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(1280, 800);
        MinimumSize = new Size(1040, 660);
        BackColor = Theme.Bg;
        Font = Theme.Base;
        Icon = Theme.AppIcon(32);

        BuildLayout();
        BuildTray();

        foreach (var page in Pages())
        {
            page.CountsChanged = UpdateCounts;
            page.ShowBoard = GoToBoard;
        }

        _settings.SettingsSaved = OnSettingsSaved;

        _tabs.SelectedIndexChanged += (s, e) => ReloadCurrentPage();
        _refresh.Click += async (s, e) => await SyncAsync(manual: true);

        _syncTimer.Tick += async (s, e) => await SyncAsync(manual: false);
        _clockTimer.Tick += (s, e) => UpdateSyncLabel();

        Load += async (s, e) => await FirstRun();
        FormClosing += OnFormClosing;
    }

    private IEnumerable<PageBase> Pages()
    {
        yield return _dashboard;
        yield return _board;
        yield return _levels;
        yield return _buying;
        yield return _alerts;
        yield return _settings;
    }

    // ------------------------------------------------------------------ layout

    private void BuildLayout()
    {
        // ---- header bar ----
        _brandName.Text = _services.Settings.Current.PharmacyName;
        _brandName.Font = Theme.BrandF;
        _brandName.ForeColor = Color.White;
        _brandName.AutoSize = true;
        _brandName.Location = new Point(16, 9);

        var subtitle = new Label
        {
            Text = "STOCK MANAGER",
            Font = Theme.Caption,
            ForeColor = Color.FromArgb(190, 230, 224),
            AutoSize = true,
            Location = new Point(18, 32),
        };

        _syncLabel.Text = "Starting...";
        _syncLabel.Font = Theme.Base;
        _syncLabel.ForeColor = Color.FromArgb(205, 238, 233);
        _syncLabel.AutoSize = true;
        _syncLabel.Anchor = AnchorStyles.Right;

        _refresh.BackColor = Color.White;
        _refresh.ForeColor = Theme.BrandDark;
        _refresh.FlatAppearance.MouseOverBackColor = Color.FromArgb(226, 244, 241);
        _refresh.Anchor = AnchorStyles.Right;

        var right = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoSize = true,
            Padding = new Padding(0, 13, 14, 0),
        };
        _syncLabel.Margin = new Padding(0, 7, 14, 0);
        right.Controls.Add(_syncLabel);
        right.Controls.Add(_refresh);

        var header = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Brand };
        header.Controls.Add(right);
        header.Controls.Add(_brandName);
        header.Controls.Add(subtitle);

        // ---- tabs ----
        _tabs.Dock = DockStyle.Fill;
        _tabs.Font = Theme.Tab;
        _tabs.Padding = new Point(16, 7);
        _tabs.TabPages.Add(Tab("Dashboard", _dashboard));
        _tabs.TabPages.Add(Tab("Stock Board", _board));
        _tabs.TabPages.Add(Tab("Shelf Levels", _levels));
        _tabs.TabPages.Add(Tab("Buying List", _buying));
        _tabs.TabPages.Add(Tab("Alerts", _alerts));
        _tabs.TabPages.Add(Tab("Settings", _settings));

        // ---- status bar ----
        _statusBar.Dock = DockStyle.Fill;
        _statusBar.Font = Theme.Small;
        _statusBar.ForeColor = Theme.TextDim;
        _statusBar.BackColor = Theme.Surface2;
        _statusBar.TextAlign = ContentAlignment.MiddleLeft;
        _statusBar.Padding = new Padding(12, 0, 0, 0);
        _statusBar.Text = "Starting...";

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58f));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 24f));
        root.Controls.Add(header, 0, 0);
        root.Controls.Add(_tabs, 0, 1);
        root.Controls.Add(_statusBar, 0, 2);

        Controls.Add(root);
    }

    private static TabPage Tab(string title, Control page)
    {
        var tab = new TabPage(title) { BackColor = Theme.Bg, Padding = new Padding(0) };
        page.Dock = DockStyle.Fill;
        tab.Controls.Add(page);
        return tab;
    }

    private void BuildTray()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Open Stock Manager", null, (s, e) => RestoreWindow());
        menu.Items.Add("Refresh now", null, async (s, e) => await SyncAsync(manual: true));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (s, e) => { _reallyExit = true; Close(); });

        _tray.Icon = Theme.AppIcon(16);
        _tray.Text = "Stock Manager";
        _tray.Visible = true;
        _tray.ContextMenuStrip = menu;
        _tray.DoubleClick += (s, e) => RestoreWindow();
        _tray.BalloonTipClicked += (s, e) => { RestoreWindow(); _tabs.SelectedIndex = 4; };
    }

    private void RestoreWindow()
    {
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
    }

    // ------------------------------------------------------------------- flow

    private async Task FirstRun()
    {
        // Don't announce alerts that were already in the database before this session.
        var existing = _services.Store.GetAlertEvents(1);
        _lastAlertId = existing.Count > 0 ? existing[0].Id : 0;

        _settings.Reload();
        ApplySyncInterval();
        _clockTimer.Start();

        await SyncAsync(manual: false);

        if (_services.Store.CountShelfLevels() == 0)
            ShowFirstTimeHelp();
    }

    private void ShowFirstTimeHelp()
    {
        var answer = MessageBox.Show(this,
            "No shelf levels are set yet, so nothing can be flagged.\r\n\r\n" +
            "The program can work one out for every medicine from its own sales history - " +
            "how many it sells per day, how long your distributor takes, plus a safety margin. " +
            "Dead stock is switched off automatically.\r\n\r\n" +
            "Set them up now?",
            "Set up shelf levels",
            MessageBoxButtons.YesNo, MessageBoxIcon.Question);

        if (answer != DialogResult.Yes)
        {
            _tabs.SelectedIndex = 2;
            return;
        }

        try
        {
            Cursor = Cursors.WaitCursor;
            var result = _services.Levels.ApplySuggestions("missing");
            _tabs.SelectedIndex = 2;
            ReloadCurrentPage();
            UpdateCounts();
            MessageBox.Show(this,
                result.Message + "\r\n\r\nHave a look at the fast movers below and adjust any you want.",
                "Shelf levels ready", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            Log.Error("Could not apply suggested shelf levels", ex);
            MessageBox.Show(this, "Could not work out shelf levels.\r\n\r\n" + ex.Message,
                "Stock Manager", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private async Task SyncAsync(bool manual)
    {
        if (_syncing) return;

        _syncing = true;
        _refresh.Enabled = false;
        _syncLabel.Text = "Reading stock...";

        try
        {
            var result = await _services.Sync.RunOnceAsync();

            ReloadCurrentPage();
            UpdateCounts();
            AnnounceNewAlerts();

            if (manual && !result.Ok)
            {
                MessageBox.Show(this,
                    "Could not read stock.\r\n\r\n" + result.Message,
                    "Stock Manager", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
        catch (Exception ex)
        {
            Log.Error("Sync failed", ex);
            if (manual)
            {
                MessageBox.Show(this, "Could not read stock.\r\n\r\n" + ex.Message,
                    "Stock Manager", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
        finally
        {
            _syncing = false;
            _refresh.Enabled = true;
            UpdateSyncLabel();
        }
    }

    private void ReloadCurrentPage()
    {
        try
        {
            var index = _tabs.SelectedIndex;
            var page = Pages().ElementAtOrDefault(index);
            page?.Reload();
        }
        catch (Exception ex)
        {
            Log.Error("Could not redraw the screen", ex);
        }
    }

    private void UpdateCounts()
    {
        try
        {
            var counts = _services.Stock.Dashboard();

            _statusBar.Text =
                $"Out of stock: {counts.OutOfStock}      " +
                $"Below shelf level: {counts.BelowLevel}      " +
                $"Fine: {counts.Ok}      " +
                $"Not watched: {counts.Unmanaged}      " +
                $"Total medicines: {counts.TotalItems}      " +
                $"On order: {counts.OpenOrders}      " +
                $"Source: {counts.DataSource}";

            _tabs.TabPages[1].Text = counts.OutOfStock + counts.BelowLevel > 0
                ? $"Stock Board ({counts.OutOfStock + counts.BelowLevel})"
                : "Stock Board";

            _tabs.TabPages[4].Text = counts.NewAlerts > 0 ? $"Alerts ({counts.NewAlerts})" : "Alerts";

            _brandName.Text = _services.Settings.Current.PharmacyName;
            Text = _services.Settings.Current.PharmacyName + " - Stock Manager";
            _tray.Text = $"Stock Manager - {counts.OutOfStock} out, {counts.BelowLevel} low";
        }
        catch (Exception ex)
        {
            Log.Error("Could not update the counters", ex);
        }
    }

    private void UpdateSyncLabel()
    {
        if (_syncing) return;
        var snapshot = _services.Stock.Current;
        _syncLabel.Text = snapshot.Ok
            ? "Updated " + Fmt.Ago(snapshot.AtUtc)
            : "Could not read stock";
    }

    /// <summary>
    /// Pops up a Windows notification for shortages that appeared since the last check.
    /// Only new state CHANGES are announced, so a medicine that stays short is silent.
    /// </summary>
    private void AnnounceNewAlerts()
    {
        try
        {
            var events = _services.Store.GetAlertEvents(50, unseenOnly: true);
            var fresh = events.Where(e => e.Id > _lastAlertId).ToList();
            if (fresh.Count == 0) return;

            _lastAlertId = fresh.Max(e => e.Id);

            var shortages = fresh.Where(e => e.To is StockState.Out or StockState.Low).ToList();
            if (shortages.Count == 0) return;

            var wentOut = shortages.Any(e => e.To == StockState.Out);
            if (wentOut && _services.Settings.Current.SoundAlerts)
                SystemSounds.Exclamation.Play();

            var text = shortages.Count == 1
                ? Fmt.AlertText(shortages[0])
                : string.Join("\r\n", shortages.Take(4).Select(Fmt.AlertText)) +
                  (shortages.Count > 4 ? $"\r\n...and {shortages.Count - 4} more" : "");

            _tray.ShowBalloonTip(
                10000,
                wentOut ? "Medicine finished" : "Running short",
                text,
                wentOut ? ToolTipIcon.Error : ToolTipIcon.Warning);
        }
        catch (Exception ex)
        {
            Log.Error("Could not show the alert notification", ex);
        }
    }

    private void GoToBoard(string filter)
    {
        _board.ApplyFilter(filter);
        _tabs.SelectedIndex = 1;
        _board.Reload();
    }

    private void OnSettingsSaved()
    {
        ApplySyncInterval();
        UpdateCounts();
    }

    private void ApplySyncInterval()
    {
        var seconds = Math.Clamp(_services.Settings.Current.SyncSeconds, 10, 3600);
        _syncTimer.Stop();
        _syncTimer.Interval = seconds * 1000;
        _syncTimer.Start();
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (_reallyExit || e.CloseReason != CloseReason.UserClosing)
        {
            _tray.Visible = false;
            _tray.Dispose();
            return;
        }

        if (!_services.Settings.Current.MinimiseToTray) { _tray.Visible = false; return; }

        // Closing the window keeps the alerts running, which is the point of the program.
        e.Cancel = true;
        Hide();

        if (!_trayHintShown)
        {
            _trayHintShown = true;
            _tray.ShowBalloonTip(6000, "Still watching your stock",
                "Stock Manager is running near the clock. Double-click its icon to open it again, " +
                "or right-click to exit.", ToolTipIcon.Info);
        }
    }
}
