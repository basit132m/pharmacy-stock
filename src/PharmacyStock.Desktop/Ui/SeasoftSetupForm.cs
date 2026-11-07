using System.Drawing;
using System.Windows.Forms;
using PharmacyStock.Providers.Seasoft;
using PharmacyStock.Services;

namespace PharmacyStock.Desktop.Ui;

/// <summary>
/// "Connect to SeaSoft" - three steps: reach the database, find the table that holds the
/// medicines, then say which column is which.
///
/// Step 2 matters more than it looks. Nobody knows what SeaSoft's tables are called, and
/// the vendor may never say. This lets you open the database, read down the list of
/// tables, look at a few rows and recognise your own medicines - and then the program
/// works out the column mapping for you.
///
/// Everything this screen does is read-only.
/// </summary>
internal sealed class SeasoftSetupForm : Form
{
    private readonly SettingsService _settings;

    private readonly TabControl _tabs = new();

    // Step 1 - connection
    private readonly ComboBox _engine = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 300 };
    private readonly TextBox _server = new() { Width = 300 };
    private readonly NumericUpDown _port = new() { Minimum = 0, Maximum = 65535, Width = 100 };
    private readonly TextBox _database = new() { Width = 300 };
    private readonly TextBox _file = new() { Width = 300 };
    private readonly Button _browseFile = Theme.Secondary("Browse...", 90);
    private readonly CheckBox _windowsAuth = new() { Text = "Sign in as the current Windows user", AutoSize = true };
    private readonly TextBox _user = new() { Width = 300 };
    private readonly TextBox _password = new() { Width = 300, UseSystemPasswordChar = true };
    private readonly TextBox _odbc = new() { Width = 300 };
    private readonly Label _connectStatus = new();

    // Step 2 - table
    private readonly ListBox _tables = new() { Dock = DockStyle.Fill, Font = Theme.Base, IntegralHeight = false };
    private readonly DataGridView _preview = Theme.Grid();
    private readonly Label _tableStatus = new();

    // Step 3 - columns
    private readonly Dictionary<string, ComboBox> _columns = new(StringComparer.Ordinal);
    private readonly CheckBox _qtyIsPacks = new() { Text = "This quantity is already in packs, not tablets", AutoSize = true };
    private readonly CheckBox _useCustomSql = new() { Text = "Advanced: write my own SELECT instead", AutoSize = true };
    private readonly TextBox _customSql = new() { Multiline = true, ScrollBars = ScrollBars.Vertical, Height = 110, Dock = DockStyle.Fill, Font = new Font("Consolas", 9.5f) };
    private readonly Label _mapStatus = new();

    private List<string> _availableColumns = new();

    public SeasoftSetupForm(SettingsService settings)
    {
        _settings = settings;

        Text = "Connect to SeaSoft";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(1020, 720);
        MinimumSize = new Size(900, 640);
        BackColor = Theme.Bg;
        Font = Theme.Base;
        Icon = Theme.AppIcon(32);
        ShowInTaskbar = false;
        MinimizeBox = false;

        _tabs.Dock = DockStyle.Fill;
        _tabs.Font = Theme.Tab;
        _tabs.Padding = new Point(14, 6);
        _tabs.TabPages.Add(BuildConnectTab());
        _tabs.TabPages.Add(BuildTableTab());
        _tabs.TabPages.Add(BuildMappingTab());

        var save = Theme.Primary("Save and use SeaSoft", 200);
        save.Click += (s, e) => SaveAndClose();

        var cancel = Theme.Secondary("Cancel", 110);
        cancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };

        var safety = new Label
        {
            Dock = DockStyle.Fill,
            Font = Theme.Small,
            ForeColor = Theme.TextDim,
            TextAlign = ContentAlignment.MiddleLeft,
            Text = "This screen only reads. The program refuses to send anything but a SELECT to SeaSoft.",
        };

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(0, 6, 8, 0),
        };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(save);

        var bottom = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bottom.Controls.Add(safety, 0, 0);
        bottom.Controls.Add(buttons, 1, 0);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(10) };
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46f));
        root.Controls.Add(_tabs, 0, 0);
        root.Controls.Add(bottom, 0, 1);

        Controls.Add(root);

        LoadExisting();
    }

    // ------------------------------------------------------------- step 1

    private TabPage BuildConnectTab()
    {
        _engine.Items.AddRange(new object[]
        {
            "Microsoft SQL Server  (most common)",
            "MySQL / MariaDB",
            "Firebird",
            "Microsoft Access file  (.mdb / .accdb)",
            "Other, through ODBC",
        });
        _engine.SelectedIndex = 0;
        _engine.SelectedIndexChanged += (s, e) => UpdateFieldsForEngine();

        _windowsAuth.Checked = true;
        _windowsAuth.CheckedChanged += (s, e) => UpdateFieldsForEngine();

        _browseFile.Click += (s, e) => BrowseForFile();

        var test = Theme.Primary("Test connection", 170);
        test.Click += async (s, e) => await TestConnection();

        _connectStatus.Dock = DockStyle.Fill;
        _connectStatus.Font = Theme.Base;
        _connectStatus.ForeColor = Theme.Text;
        _connectStatus.BackColor = Theme.Surface;
        _connectStatus.Padding = new Padding(10, 8, 10, 8);
        _connectStatus.Text = "Fill in the details and press Test connection.";

        var help = new Label
        {
            Dock = DockStyle.Fill,
            Font = Theme.Base,
            BackColor = Theme.BrandSoft,
            ForeColor = Theme.Text,
            Padding = new Padding(12, 8, 12, 8),
            Text =
                "Ask the SeaSoft vendor for a READ-ONLY database login, and for the database type and name.\r\n" +
                "If you do not know the server name, use the name of the counter PC that acts as the server, " +
                "or its IP address. For a file-based database, browse to the .mdb or .fdb file.",
        };

        var fileRow = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = new Padding(0) };
        fileRow.Controls.Add(_file);
        fileRow.Controls.Add(_browseFile);

        var form = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoScroll = true, Padding = new Padding(4, 10, 4, 4) };
        form.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 250f));
        form.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        AddRow(form, "Database type", _engine);
        AddRow(form, "Server PC name or IP", _server);
        AddRow(form, "Port  (0 = normal)", _port);
        AddRow(form, "Database name", _database);
        AddRow(form, "Database file", fileRow);
        AddRow(form, "", _windowsAuth);
        AddRow(form, "Username", _user);
        AddRow(form, "Password", _password);
        AddRow(form, "ODBC name or connection text", _odbc);
        AddRow(form, "", test);

        var page = new TabPage("1.  Connect") { BackColor = Theme.Bg, Padding = new Padding(10) };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 64f));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 80f));
        layout.Controls.Add(help, 0, 0);
        layout.Controls.Add(form, 0, 1);
        layout.Controls.Add(_connectStatus, 0, 2);
        page.Controls.Add(layout);
        return page;
    }

    private static void AddRow(TableLayoutPanel form, string label, Control control)
    {
        form.Controls.Add(new Label
        {
            Text = label,
            Font = Theme.Base,
            AutoSize = true,
            MaximumSize = new Size(240, 0),
            Margin = new Padding(0, 8, 10, 4),
        });

        control.Margin = new Padding(0, 4, 0, 6);
        form.Controls.Add(control);
    }

    private void UpdateFieldsForEngine()
    {
        var engine = SelectedEngine();

        var needsServer = engine is SeasoftEngine.SqlServer or SeasoftEngine.MySql or SeasoftEngine.Firebird;
        var needsDatabase = engine is SeasoftEngine.SqlServer or SeasoftEngine.MySql;
        var needsFile = engine is SeasoftEngine.Access or SeasoftEngine.Firebird;
        var isOdbc = engine is SeasoftEngine.Odbc;
        var canWindowsAuth = engine is SeasoftEngine.SqlServer;

        _server.Enabled = needsServer;
        _port.Enabled = needsServer;
        _database.Enabled = needsDatabase;
        _file.Enabled = needsFile;
        _browseFile.Enabled = needsFile;
        _odbc.Enabled = isOdbc;
        _windowsAuth.Enabled = canWindowsAuth;

        var useLogin = !canWindowsAuth || !_windowsAuth.Checked;
        _user.Enabled = useLogin && !isOdbc;
        _password.Enabled = (useLogin || engine == SeasoftEngine.Access) && !isOdbc;
    }

    private SeasoftEngine SelectedEngine() => (SeasoftEngine)Math.Max(0, _engine.SelectedIndex);

    private SeasoftConnection CurrentConnection() => new()
    {
        Engine = SelectedEngine(),
        Server = _server.Text.Trim(),
        Port = (int)_port.Value,
        Database = _database.Text.Trim(),
        FilePath = _file.Text.Trim(),
        User = _user.Text.Trim(),
        Password = _password.Text,
        WindowsAuth = _windowsAuth.Enabled && _windowsAuth.Checked,
        Odbc = _odbc.Text.Trim(),
    };

    private void BrowseForFile()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Find the SeaSoft database file",
            Filter = SelectedEngine() == SeasoftEngine.Access
                ? "Access database (*.mdb;*.accdb)|*.mdb;*.accdb|All files (*.*)|*.*"
                : "Firebird database (*.fdb;*.gdb)|*.fdb;*.gdb|All files (*.*)|*.*",
        };
        if (dialog.ShowDialog(this) == DialogResult.OK) _file.Text = dialog.FileName;
    }

    private async Task TestConnection()
    {
        var connection = CurrentConnection();
        if (!connection.IsConfigured)
        {
            Say(_connectStatus, "Fill in the details above first.", false);
            return;
        }

        try
        {
            Cursor = Cursors.WaitCursor;
            Say(_connectStatus, "Connecting...", true);

            var browser = new SchemaBrowser(connection);
            var message = await browser.TestAsync();

            Say(_connectStatus, message + "\r\n\r\nGo to step 2 and choose the table that holds your medicines.", true);
            await LoadTables();
            _tabs.SelectedIndex = 1;
        }
        catch (Exception ex)
        {
            Say(_connectStatus, Friendly(ex), false);
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    // ------------------------------------------------------------- step 2

    private TabPage BuildTableTab()
    {
        var help = new Label
        {
            Dock = DockStyle.Fill,
            Font = Theme.Base,
            BackColor = Theme.BrandSoft,
            ForeColor = Theme.Text,
            Padding = new Padding(12, 8, 12, 8),
            Text =
                "Click each table on the left to see a few real rows on the right. Look for the one that lists " +
                "your medicines with a quantity - it is often called something like ITEMMASTER, PRODUCT or " +
                "STOCK. Tables whose names look promising are listed first.",
        };

        var refresh = Theme.Secondary("Reload table list", 160);
        refresh.Click += async (s, e) => await LoadTables();

        _tables.SelectedIndexChanged += async (s, e) => await LoadPreview();

        _tableStatus.Dock = DockStyle.Fill;
        _tableStatus.Font = Theme.Small;
        _tableStatus.ForeColor = Theme.TextDim;
        _tableStatus.TextAlign = ContentAlignment.MiddleLeft;

        var left = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = new Padding(0, 0, 8, 0) };
        left.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        left.RowStyles.Add(new RowStyle(SizeType.Absolute, 40f));
        var tableCard = Theme.Card();
        tableCard.Controls.Add(_tables);
        left.Controls.Add(tableCard, 0, 0);
        left.Controls.Add(refresh, 0, 1);

        var previewCard = Theme.Card();
        previewCard.Controls.Add(_preview);

        var split = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        split.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 280f));
        split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        split.Controls.Add(left, 0, 0);
        split.Controls.Add(previewCard, 1, 0);

        var page = new TabPage("2.  Find your medicines") { BackColor = Theme.Bg, Padding = new Padding(10) };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 64f));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26f));
        layout.Controls.Add(help, 0, 0);
        layout.Controls.Add(split, 0, 1);
        layout.Controls.Add(_tableStatus, 0, 2);
        page.Controls.Add(layout);
        return page;
    }

    private async Task LoadTables()
    {
        var connection = CurrentConnection();
        if (!connection.IsConfigured) return;

        try
        {
            Cursor = Cursors.WaitCursor;
            _tableStatus.Text = "Reading the list of tables...";

            var names = await new SchemaBrowser(connection).ListTablesAsync();

            _tables.BeginUpdate();
            _tables.Items.Clear();
            foreach (var name in names) _tables.Items.Add(name);
            _tables.EndUpdate();

            _tableStatus.Text = $"{names.Count} tables. Click one to see what is inside it.";
        }
        catch (Exception ex)
        {
            _tableStatus.Text = Friendly(ex);
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private async Task LoadPreview()
    {
        if (_tables.SelectedItem is not string table) return;

        try
        {
            Cursor = Cursors.WaitCursor;
            _tableStatus.Text = $"Reading {table}...";

            var data = await new SchemaBrowser(CurrentConnection()).PreviewAsync(table, 25);

            _preview.SuspendLayout();
            _preview.Columns.Clear();
            _preview.Rows.Clear();
            foreach (var column in data.Columns)
                _preview.Columns.Add(Theme.TextColumn("c" + _preview.Columns.Count, column, 10));
            foreach (var row in data.Rows) _preview.Rows.Add(row.Cast<object>().ToArray());
            _preview.ResumeLayout();

            _availableColumns = data.Columns;
            _tableStatus.Text = $"{table}: {data.Columns.Count} columns, showing {data.Rows.Count} rows. " +
                                "If these are your medicines, go to step 3.";

            ApplyGuess(table);
        }
        catch (Exception ex)
        {
            _tableStatus.Text = Friendly(ex);
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    // ------------------------------------------------------------- step 3

    private static readonly (string Key, string Label, bool Required)[] Fields =
    {
        ("ItemCode",     "Item code  (the medicine's own code)", false),
        ("Name",         "Medicine name", true),
        ("Qty",          "Stock quantity", true),
        ("UnitsPerPack", "Units in one pack  (tablets per pack)", false),
        ("PackUnit",     "Pack unit name  (Pack / Bottle / Tube)", false),
        ("Generic",      "Formula / generic name", false),
        ("Company",      "Company", false),
        ("Supplier",     "Supplier / distributor", false),
        ("Price",        "Retail price", false),
        ("Barcode",      "Barcode", false),
        ("Sold30",       "Sold in last 30 days", false),
        ("Sold90",       "Sold in last 90 days", false),
        ("LastSale",     "Last sale date", false),
        ("Expiry",       "Nearest expiry date", false),
    };

    private TabPage BuildMappingTab()
    {
        var help = new Label
        {
            Dock = DockStyle.Fill,
            Font = Theme.Base,
            BackColor = Theme.BrandSoft,
            ForeColor = Theme.Text,
            Padding = new Padding(12, 8, 12, 8),
            Text =
                "The program fills these in by itself from the column names. Check the first three are right - " +
                "they are the only ones it cannot work without. Leave anything you do not have as (none).",
        };

        var form = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoScroll = true, Padding = new Padding(4, 8, 4, 4) };
        form.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 280f));
        form.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        foreach (var (key, label, required) in Fields)
        {
            var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 320, Font = Theme.Base };
            _columns[key] = combo;

            form.Controls.Add(new Label
            {
                Text = required ? label + "  *" : label,
                Font = required ? Theme.Bold : Theme.Base,
                AutoSize = true,
                MaximumSize = new Size(270, 0),
                Margin = new Padding(0, 8, 10, 4),
            });
            combo.Margin = new Padding(0, 4, 0, 5);
            form.Controls.Add(combo);
        }

        form.Controls.Add(new Label { Text = "", AutoSize = true });
        form.Controls.Add(_qtyIsPacks);

        _useCustomSql.CheckedChanged += (s, e) => _customSql.Enabled = _useCustomSql.Checked;
        _customSql.Enabled = false;
        _customSql.Text = "SELECT i.ITEM_CODE, i.ITEM_NAME, i.PACK_SIZE, s.CLOSING_BALANCE\r\nFROM ...";

        var runSql = Theme.Secondary("Check this query", 160);
        runSql.Click += async (s, e) => await CheckCustomSql();

        var guess = Theme.Secondary("Fill in again automatically", 210);
        guess.Click += (s, e) =>
        {
            if (_tables.SelectedItem is string table) ApplyGuess(table);
        };

        var verify = Theme.Primary("Preview what will be read", 210);
        verify.Click += async (s, e) => await PreviewMapped();

        _mapStatus.Dock = DockStyle.Fill;
        _mapStatus.Font = Theme.Base;
        _mapStatus.BackColor = Theme.Surface;
        _mapStatus.ForeColor = Theme.Text;
        _mapStatus.Padding = new Padding(10, 8, 10, 8);
        _mapStatus.Text = "Press \"Preview what will be read\" to check before saving.";

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
        buttons.Controls.Add(verify);
        buttons.Controls.Add(guess);
        buttons.Controls.Add(runSql);

        var advanced = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        advanced.RowStyles.Add(new RowStyle(SizeType.Absolute, 26f));
        advanced.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        advanced.Controls.Add(_useCustomSql, 0, 0);
        advanced.Controls.Add(_customSql, 0, 1);

        var page = new TabPage("3.  Match the columns") { BackColor = Theme.Bg, Padding = new Padding(10) };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 58f));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 140f));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42f));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 74f));
        layout.Controls.Add(help, 0, 0);
        layout.Controls.Add(form, 0, 1);
        layout.Controls.Add(advanced, 0, 2);
        layout.Controls.Add(buttons, 0, 3);
        layout.Controls.Add(_mapStatus, 0, 4);
        page.Controls.Add(layout);
        return page;
    }

    private void FillColumnChoices()
    {
        foreach (var combo in _columns.Values)
        {
            var chosen = combo.SelectedItem?.ToString();
            combo.Items.Clear();
            combo.Items.Add("(none)");
            foreach (var column in _availableColumns) combo.Items.Add(column);

            var index = chosen is null ? 0 : combo.Items.IndexOf(chosen);
            combo.SelectedIndex = index < 0 ? 0 : index;
        }
    }

    private void ApplyGuess(string table)
    {
        FillColumnChoices();
        var guess = SeasoftMapping.Guess(table, _availableColumns);

        void Set(string key, string value)
        {
            var combo = _columns[key];
            var index = value.Length == 0 ? 0 : combo.Items.IndexOf(value);
            combo.SelectedIndex = index < 0 ? 0 : index;
        }

        Set("ItemCode", guess.ItemCode);
        Set("Name", guess.Name);
        Set("Qty", guess.Qty);
        Set("UnitsPerPack", guess.UnitsPerPack);
        Set("PackUnit", guess.PackUnit);
        Set("Generic", guess.Generic);
        Set("Company", guess.Company);
        Set("Supplier", guess.Supplier);
        Set("Price", guess.Price);
        Set("Barcode", guess.Barcode);
        Set("Sold30", guess.Sold30);
        Set("Sold90", guess.Sold90);
        Set("LastSale", guess.LastSale);
        Set("Expiry", guess.Expiry);
    }

    private string Chosen(string key)
    {
        var value = _columns[key].SelectedItem?.ToString() ?? "";
        return value == "(none)" ? "" : value;
    }

    private SeasoftMapping CurrentMapping() => new()
    {
        Table = _tables.SelectedItem?.ToString() ?? "",
        CustomSql = _useCustomSql.Checked ? _customSql.Text.Trim() : "",
        ItemCode = Chosen("ItemCode"),
        Name = Chosen("Name"),
        Qty = Chosen("Qty"),
        UnitsPerPack = Chosen("UnitsPerPack"),
        PackUnit = Chosen("PackUnit"),
        Generic = Chosen("Generic"),
        Company = Chosen("Company"),
        Supplier = Chosen("Supplier"),
        Price = Chosen("Price"),
        Barcode = Chosen("Barcode"),
        Sold30 = Chosen("Sold30"),
        Sold90 = Chosen("Sold90"),
        LastSale = Chosen("LastSale"),
        Expiry = Chosen("Expiry"),
        QtyIsPacks = _qtyIsPacks.Checked,
    };

    private async Task CheckCustomSql()
    {
        if (!_useCustomSql.Checked)
        {
            Say(_mapStatus, "Tick \"write my own SELECT\" first.", false);
            return;
        }

        try
        {
            Cursor = Cursors.WaitCursor;
            var data = await new SchemaBrowser(CurrentConnection()).RunAsync(_customSql.Text.Trim(), 25);
            _availableColumns = data.Columns;
            FillColumnChoices();
            Say(_mapStatus, $"The query works: {data.Columns.Count} columns, {data.Rows.Count} sample rows. " +
                            "Now match the columns above.", true);
        }
        catch (Exception ex)
        {
            Say(_mapStatus, Friendly(ex), false);
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private async Task PreviewMapped()
    {
        var mapping = CurrentMapping();
        if (!mapping.IsComplete)
        {
            Say(_mapStatus, mapping.WhatIsMissing(), false);
            return;
        }

        try
        {
            Cursor = Cursors.WaitCursor;
            var connection = CurrentConnection();
            var provider = new SeasoftStockProvider(() => connection, () => mapping);
            var result = await provider.GetAllAsync();

            if (!result.Ok)
            {
                Say(_mapStatus, result.Message ?? "Could not read.", false);
                return;
            }

            var sample = result.Items.Take(3)
                .Select(i =>
                {
                    var (packs, loose) = i.PacksAndLoose();
                    return $"{i.Name} - {packs} packs" + (loose > 0 ? $" + {loose:0.##} loose" : "");
                });

            Say(_mapStatus,
                $"Looks right: {result.Items.Count} medicines read.\r\n" +
                string.Join("    |    ", sample), true);
        }
        catch (Exception ex)
        {
            Say(_mapStatus, Friendly(ex), false);
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    // --------------------------------------------------------------- save

    private void LoadExisting()
    {
        var connection = _settings.GetSeasoftConnection();
        _engine.SelectedIndex = (int)connection.Engine;
        _server.Text = connection.Server;
        _port.Value = Math.Clamp(connection.Port, 0, 65535);
        _database.Text = connection.Database;
        _file.Text = connection.FilePath;
        _user.Text = connection.User;
        _password.Text = connection.Password;
        _windowsAuth.Checked = connection.WindowsAuth;
        _odbc.Text = connection.Odbc;

        var mapping = _settings.GetSeasoftMapping();
        if (mapping.Table.Length > 0)
        {
            _tables.Items.Add(mapping.Table);
            _tables.SelectedIndex = 0;
        }
        _qtyIsPacks.Checked = mapping.QtyIsPacks;
        if (mapping.CustomSql.Length > 0)
        {
            _useCustomSql.Checked = true;
            _customSql.Text = mapping.CustomSql;
            _customSql.Enabled = true;
        }

        UpdateFieldsForEngine();
    }

    private async void SaveAndClose()
    {
        var connection = CurrentConnection();
        var mapping = CurrentMapping();

        if (!connection.IsConfigured)
        {
            _tabs.SelectedIndex = 0;
            Say(_connectStatus, "Fill in the connection details first.", false);
            return;
        }

        if (!mapping.IsComplete)
        {
            _tabs.SelectedIndex = mapping.Table.Length == 0 && mapping.CustomSql.Length == 0 ? 1 : 2;
            Say(_mapStatus, mapping.WhatIsMissing(), false);
            return;
        }

        // Never save a setup that does not actually work - it would leave the program
        // switched to SeaSoft and showing an error on every refresh.
        try
        {
            Cursor = Cursors.WaitCursor;
            var check = await new SeasoftStockProvider(() => connection, () => mapping).GetAllAsync();
            if (!check.Ok)
            {
                _tabs.SelectedIndex = 2;
                Say(_mapStatus, "Not saved - " + check.Message, false);
                return;
            }

            _settings.SaveSeasoftConnection(connection);
            _settings.SaveSeasoftMapping(mapping);

            MessageBox.Show(this,
                $"Connected to SeaSoft.\r\n\r\n{check.Items.Count} medicines are now being read live.\r\n\r\n" +
                "Your shelf levels have been kept. Medicines are matched by their SeaSoft item code.",
                "Connected", MessageBoxButtons.OK, MessageBoxIcon.Information);

            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            Say(_mapStatus, Friendly(ex), false);
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    // ------------------------------------------------------------- helpers

    private static void Say(Label label, string message, bool good)
    {
        label.Text = message;
        label.BackColor = good ? Theme.OkBg : Theme.OutBg;
        label.ForeColor = good ? Theme.OkFg : Theme.OutFg;
    }

    private static string Friendly(Exception ex) => ex switch
    {
        InvalidOperationException => ex.Message,
        PlatformNotSupportedException => ex.Message,
        _ => ex.Message,
    };
}
