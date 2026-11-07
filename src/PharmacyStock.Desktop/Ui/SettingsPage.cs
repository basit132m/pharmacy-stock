using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using PharmacyStock.Data;
using PharmacyStock.Models;
using PharmacyStock.Providers.Seasoft;

namespace PharmacyStock.Desktop.Ui;

internal sealed class SettingsPage : PageBase
{
    private readonly TextBox _pharmacyName = new() { Width = 320 };
    private readonly ComboBox _source = new() { Width = 320, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox _csvPath = new() { Width = 240 };
    private readonly Button _browse = Theme.Secondary("Browse...", 90);
    private readonly NumericUpDown _syncSeconds = Spin(10, 3600, 0);
    private readonly NumericUpDown _hysteresis = Spin(0, 100, 1);
    private readonly NumericUpDown _leadTime = Spin(1, 60, 0);
    private readonly NumericUpDown _safety = Spin(1, 5, 1);
    private readonly NumericUpDown _deadStock = Spin(30, 1000, 0);
    private readonly CheckBox _sound = Check("Play a sound when a medicine goes out of stock");
    private readonly CheckBox _tray = Check("Keep running in the notification area when I close the window");
    private readonly CheckBox _startWithWindows = Check("Start automatically when Windows starts");
    private readonly Label _status = new();
    private readonly Button _connectSeasoft = Theme.Primary("Connect to SeaSoft...", 200);
    private readonly Label _seasoftStatus = new();

    /// <summary>Raised after Save, so the main window can re-apply the sync interval.</summary>
    public Action? SettingsSaved { get; set; }

    public SettingsPage(AppServices services) : base(services)
    {
        _source.Items.Add("Demo data (sample medicines, not your real stock)");
        _source.Items.Add("CSV / Excel file exported from SeaSoft");
        _source.Items.Add("SeaSoft database (live stock)");
        _source.SelectedIndexChanged += (s, e) => UpdateCsvVisibility();

        _connectSeasoft.Click += (s, e) => OpenSeasoftSetup();

        _browse.Click += (s, e) => BrowseCsv();

        _seasoftStatus.Font = Theme.Base;
        _seasoftStatus.AutoSize = true;
        _seasoftStatus.MaximumSize = new Size(520, 0);
        _seasoftStatus.ForeColor = Theme.TextDim;

        _status.Font = Theme.Base;
        _status.ForeColor = Theme.TextDim;
        _status.AutoSize = false;
        _status.Dock = DockStyle.Fill;
        _status.TextAlign = ContentAlignment.MiddleLeft;

        var save = Theme.Primary("Save settings", 150);
        save.Click += (s, e) => Save();

        var test = Theme.Secondary("Test connection", 150);
        test.Click += (s, e) => TestConnection();

        var openFolder = Theme.Secondary("Open my data folder", 180);
        openFolder.Click += (s, e) => OpenDataFolder();

        var csvRow = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0),
        };
        csvRow.Controls.Add(_csvPath);
        csvRow.Controls.Add(_browse);

        var form = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            AutoScroll = true,
            Padding = new Padding(4),
        };
        form.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 300f));
        form.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        AddHeading(form, "Where stock comes from");
        AddRow(form, "Pharmacy name", _pharmacyName, null);
        AddRow(form, "Stock source", _source,
            "Demo data lets you set up your shelf levels before connecting anything. " +
            "The CSV option reads a stock report exported from SeaSoft. " +
            "The SeaSoft option reads your live stock straight from the database.");
        AddRow(form, "", _connectSeasoft,
            "Opens a three-step screen: connect to the database, look through the tables to find " +
            "your medicines, and match up the columns. It only ever reads.");
        AddRow(form, "", _seasoftStatus, null);
        AddRow(form, "CSV file from SeaSoft", csvRow,
            "Re-export over the same file and the program picks it up automatically.");
        AddRow(form, "Check stock every (seconds)", _syncSeconds,
            "60 suits a pharmacy - a purchase entered in SeaSoft shows up here within a minute.");

        AddHeading(form, "When to warn you");
        AddRow(form, "Clear a warning only once stock is this many packs above the level", _hysteresis,
            "Stops a medicine sitting exactly on its shelf level from flipping between warning and fine all day.");
        AddRow(form, "Distributor delivery time (days)", _leadTime,
            "Used to suggest shelf levels and to set expected delivery dates.");
        AddRow(form, "Safety margin", _safety,
            "1.5 keeps half again more than expected demand. Raise it if you run out too often, " +
            "lower it to tie up less money in stock.");
        AddRow(form, "Treat as dead stock after (days with no sale)", _deadStock,
            "Dead stock is left switched off, so the board is not filled with medicines nobody buys.");
        AddRow(form, "", _sound, null);

        AddHeading(form, "This program");
        AddRow(form, "", _tray, null);
        AddRow(form, "", _startWithWindows, null);

        var safety = new Label
        {
            Dock = DockStyle.Fill,
            Font = Theme.Base,
            BackColor = Theme.BrandSoft,
            ForeColor = Theme.Text,
            Padding = new Padding(12, 8, 12, 8),
            Text = "This program never writes to SeaSoft. It only reads.\r\n" +
                   "Your shelf levels, buying list and alert history are saved in a separate file of its own, " +
                   "shown below. SeaSoft cannot be changed, corrupted or locked by this program.",
        };

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0, 6, 0, 0),
        };
        buttons.Controls.Add(save);
        buttons.Controls.Add(test);
        buttons.Controls.Add(openFolder);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4 };
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 62f));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42f));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 26f));
        root.Controls.Add(form, 0, 0);
        root.Controls.Add(safety, 0, 1);
        root.Controls.Add(buttons, 0, 2);
        root.Controls.Add(_status, 0, 3);

        Controls.Add(root);
    }

    private static NumericUpDown Spin(decimal min, decimal max, int decimals) => new()
    {
        Minimum = min,
        Maximum = max,
        DecimalPlaces = decimals,
        Increment = decimals > 0 ? 0.5m : 1m,
        Width = 110,
        Font = Theme.Base,
        TextAlign = HorizontalAlignment.Right,
    };

    private static CheckBox Check(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Font = Theme.Base,
        Margin = new Padding(0, 6, 0, 6),
    };

    private static void AddHeading(TableLayoutPanel form, string text)
    {
        var label = new Label
        {
            Text = text,
            Font = Theme.Title,
            ForeColor = Theme.Brand,
            AutoSize = true,
            Margin = new Padding(0, 16, 0, 6),
        };
        form.Controls.Add(label);
        form.SetColumnSpan(label, 2);
    }

    private static void AddRow(TableLayoutPanel form, string label, Control control, string? help)
    {
        var caption = new Label
        {
            Text = label,
            Font = Theme.Base,
            ForeColor = Theme.Text,
            AutoSize = true,
            MaximumSize = new Size(290, 0),
            Margin = new Padding(0, 7, 10, 4),
        };

        var right = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Margin = new Padding(0, 2, 0, 4),
        };
        right.Controls.Add(control);

        if (help is not null)
        {
            right.Controls.Add(new Label
            {
                Text = help,
                Font = Theme.Small,
                ForeColor = Theme.TextDim,
                AutoSize = true,
                MaximumSize = new Size(520, 0),
                Margin = new Padding(2, 2, 0, 0),
            });
        }

        form.Controls.Add(caption);
        form.Controls.Add(right);
    }

    public override void Reload()
    {
        var settings = Services.Settings.Current;

        _pharmacyName.Text = settings.PharmacyName;
        _source.SelectedIndex = settings.DataSource switch { "csv" => 1, "seasoft" => 2, _ => 0 };
        _csvPath.Text = settings.CsvPath;
        _syncSeconds.Value = Math.Clamp(settings.SyncSeconds, 10, 3600);
        _hysteresis.Value = Math.Clamp(settings.HysteresisPacks, 0m, 100m);
        _leadTime.Value = Math.Clamp(settings.LeadTimeDays, 1, 60);
        _safety.Value = Math.Clamp(settings.SafetyFactor, 1m, 5m);
        _deadStock.Value = Math.Clamp(settings.DeadStockDays, 30, 1000);
        _sound.Checked = settings.SoundAlerts;
        _tray.Checked = settings.MinimiseToTray;
        _startWithWindows.Checked = Startup.IsEnabled();

        UpdateCsvVisibility();
        ShowSeasoftStatus();
        _status.Text = "Your data: " + AppPaths.DataFolder;
    }

    private void UpdateCsvVisibility()
    {
        var isCsv = _source.SelectedIndex == 1;
        _csvPath.Enabled = isCsv;
        _browse.Enabled = isCsv;
    }

    private bool SeasoftReady() =>
        Services.Settings.GetSeasoftConnection().IsConfigured &&
        Services.Settings.GetSeasoftMapping().IsComplete;

    private void ShowSeasoftStatus()
    {
        var connection = Services.Settings.GetSeasoftConnection();
        var mapping = Services.Settings.GetSeasoftMapping();

        if (!connection.IsConfigured)
        {
            _seasoftStatus.Text = "Not connected yet.";
            _seasoftStatus.ForeColor = Theme.TextDim;
        }
        else if (!mapping.IsComplete)
        {
            _seasoftStatus.Text = connection.Describe() + " - connected, but the columns are not matched yet.";
            _seasoftStatus.ForeColor = Theme.LowFg;
        }
        else
        {
            var what = mapping.CustomSql.Length > 0 ? "a custom query" : mapping.Table;
            _seasoftStatus.Text = $"Connected: {connection.Describe()}, reading {what}.";
            _seasoftStatus.ForeColor = Theme.OkFg;
        }
    }

    private void OpenSeasoftSetup()
    {
        using var setup = new SeasoftSetupForm(Services.Settings);
        var answer = setup.ShowDialog(this);

        ShowSeasoftStatus();
        if (answer != DialogResult.OK) return;

        // The setup screen already proved it works, so switch over to it.
        _source.SelectedIndex = 2;
        UpdateCsvVisibility();
        Save();
    }

    private void BrowseCsv()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Choose the stock file exported from SeaSoft",
            Filter = "Spreadsheet (*.csv;*.txt)|*.csv;*.txt|All files (*.*)|*.*",
        };
        if (dialog.ShowDialog(this) == DialogResult.OK) _csvPath.Text = dialog.FileName;
    }

    private void Save()
    {
        if (_source.SelectedIndex == 2 && !SeasoftReady())
        {
            Problem("SeaSoft is not set up yet.\r\n\r\n" +
                    "Press \"Connect to SeaSoft...\" and work through the three steps first.");
            return;
        }

        var source = _source.SelectedIndex switch { 1 => "csv", 2 => "seasoft", _ => "demo" };

        if (source == "csv" && string.IsNullOrWhiteSpace(_csvPath.Text))
        {
            Problem("Choose the CSV file exported from SeaSoft first.");
            return;
        }

        Services.Settings.Save(new AppSettings
        {
            PharmacyName = _pharmacyName.Text,
            DataSource = source,
            CsvPath = _csvPath.Text.Trim(),
            SyncSeconds = (int)_syncSeconds.Value,
            HysteresisPacks = _hysteresis.Value,
            LeadTimeDays = (int)_leadTime.Value,
            SafetyFactor = _safety.Value,
            DeadStockDays = (int)_deadStock.Value,
            SoundAlerts = _sound.Checked,
            MinimiseToTray = _tray.Checked,
            StartWithWindows = _startWithWindows.Checked,
        });

        if (_startWithWindows.Checked != Startup.IsEnabled() && !Startup.Apply(_startWithWindows.Checked))
            Problem("Settings saved, but Windows would not let the program change the start-up list.");

        _status.Text = "Saved.   Your data: " + AppPaths.DataFolder;
        SettingsSaved?.Invoke();
        Toast("Settings saved.");
    }

    private async void TestConnection()
    {
        try
        {
            Cursor = Cursors.WaitCursor;
            var provider = Services.Providers.Resolve();
            var result = await provider.TestAsync();
            _status.Text = result.Message ?? (result.Ok ? "Connection fine." : "Connection failed.");

            if (result.Ok) Toast(_status.Text);
            else Problem(_status.Text);
        }
        catch (Exception ex)
        {
            Problem("Test failed.\r\n\r\n" + ex.Message);
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private void OpenDataFolder()
    {
        try
        {
            Process.Start(new ProcessStartInfo(AppPaths.DataFolder) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Problem("Could not open the folder.\r\n\r\n" + AppPaths.DataFolder + "\r\n\r\n" + ex.Message);
        }
    }
}
