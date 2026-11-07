using System.Drawing;
using System.Drawing.Drawing2D;
using System.Reflection;
using System.Windows.Forms;

namespace PharmacyStock.Desktop.Ui;

/// <summary>
/// Shared colours, fonts and control factories, so every screen looks the same.
/// Light only - a pharmacy counter is a bright room and the staff need high contrast.
/// </summary>
internal static class Theme
{
    public static readonly Color Brand      = Color.FromArgb(0x0D, 0x7D, 0x6E);
    public static readonly Color BrandDark  = Color.FromArgb(0x0A, 0x64, 0x59);
    public static readonly Color BrandSoft  = Color.FromArgb(0xE3, 0xF4, 0xF1);

    public static readonly Color Bg         = Color.FromArgb(0xF1, 0xF5, 0xF9);
    public static readonly Color Surface    = Color.White;
    public static readonly Color Surface2   = Color.FromArgb(0xF6, 0xF9, 0xFC);
    public static readonly Color Border     = Color.FromArgb(0xD8, 0xE0, 0xEA);

    public static readonly Color Text       = Color.FromArgb(0x0F, 0x17, 0x2A);
    public static readonly Color TextDim    = Color.FromArgb(0x5B, 0x6B, 0x80);

    public static readonly Color OutFg      = Color.FromArgb(0xC0, 0x1E, 0x1E);
    public static readonly Color OutBg      = Color.FromArgb(0xFD, 0xEA, 0xEA);
    public static readonly Color LowFg      = Color.FromArgb(0x9A, 0x5A, 0x00);
    public static readonly Color LowBg      = Color.FromArgb(0xFD, 0xF4, 0xE0);
    public static readonly Color OkFg       = Color.FromArgb(0x15, 0x80, 0x3D);
    public static readonly Color OkBg       = Color.FromArgb(0xEC, 0xF8, 0xF0);
    public static readonly Color OffFg      = Color.FromArgb(0x64, 0x74, 0x8B);
    public static readonly Color OffBg      = Color.FromArgb(0xF4, 0xF6, 0xF8);

    public static readonly Font Base    = new("Segoe UI", 9.75f);
    public static readonly Font Bold    = new("Segoe UI", 9.75f, FontStyle.Bold);
    public static readonly Font Small   = new("Segoe UI", 8.5f);
    public static readonly Font Tab     = new("Segoe UI", 10.5f);
    public static readonly Font Title   = new("Segoe UI", 12f, FontStyle.Bold);
    public static readonly Font BrandF  = new("Segoe UI", 12.5f, FontStyle.Bold);
    public static readonly Font Caption = new("Segoe UI", 8.25f, FontStyle.Bold);
    public static readonly Font Huge    = new("Segoe UI", 27f, FontStyle.Bold);
    public static readonly Font BigNum  = new("Segoe UI", 18f, FontStyle.Bold);

    /// <summary>Colours for a stock state: foreground, background.</summary>
    public static (Color Fore, Color Back) StateColours(string state) => state switch
    {
        "OUT" => (OutFg, OutBg),
        "LOW" => (LowFg, LowBg),
        "OK"  => (OkFg, Surface),
        _     => (OffFg, Surface),
    };

    // ------------------------------------------------------------- controls

    public static DataGridView Grid()
    {
        var grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            AllowUserToOrderColumns = false,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            BackgroundColor = Surface,
            BorderStyle = BorderStyle.None,
            CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
            ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single,
            EnableHeadersVisualStyles = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            ColumnHeadersHeight = 34,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
            ReadOnly = true,
            StandardTab = true,
            Font = Base,
            GridColor = Border,
        };

        grid.RowTemplate.Height = 30;
        grid.ColumnHeadersDefaultCellStyle.BackColor = Surface2;
        grid.ColumnHeadersDefaultCellStyle.ForeColor = TextDim;
        grid.ColumnHeadersDefaultCellStyle.Font = Bold;
        grid.ColumnHeadersDefaultCellStyle.Padding = new Padding(6, 0, 6, 0);
        grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Surface2;
        grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = TextDim;
        grid.DefaultCellStyle.Padding = new Padding(6, 0, 6, 0);
        grid.DefaultCellStyle.ForeColor = Text;
        grid.DefaultCellStyle.SelectionBackColor = BrandSoft;
        grid.DefaultCellStyle.SelectionForeColor = Text;

        DoubleBuffer(grid);
        return grid;
    }

    /// <summary>A plain left-aligned, read-only grid column.</summary>
    public static DataGridViewTextBoxColumn TextColumn(string name, string header, float weight) => new()
    {
        Name = name,
        HeaderText = header,
        FillWeight = weight,
        ReadOnly = true,
        SortMode = DataGridViewColumnSortMode.NotSortable,
    };

    /// <summary>A right-aligned, read-only grid column, for figures.</summary>
    public static DataGridViewTextBoxColumn NumberColumn(string name, string header, float weight)
    {
        var column = TextColumn(name, header, weight);
        column.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
        return column;
    }

    /// <summary>
    /// Stops the grid flickering while it redraws. DataGridView hides the property,
    /// so it has to be set through reflection - a long-standing WinForms workaround.
    /// </summary>
    public static void DoubleBuffer(DataGridView grid)
    {
        try
        {
            typeof(DataGridView)
                .GetProperty("DoubleBuffered", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.SetValue(grid, true, null);
        }
        catch
        {
            // Cosmetic only.
        }
    }

    public static Button Primary(string text, int width = 150)
    {
        var button = new Button
        {
            Text = text,
            Width = width,
            Height = 32,
            FlatStyle = FlatStyle.Flat,
            BackColor = Brand,
            ForeColor = Color.White,
            Font = Bold,
            Cursor = Cursors.Hand,
            UseVisualStyleBackColor = false,
        };
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.MouseOverBackColor = BrandDark;
        return button;
    }

    public static Button Secondary(string text, int width = 150)
    {
        var button = new Button
        {
            Text = text,
            Width = width,
            Height = 32,
            FlatStyle = FlatStyle.Flat,
            BackColor = Surface,
            ForeColor = Text,
            Font = Base,
            Cursor = Cursors.Hand,
            UseVisualStyleBackColor = false,
        };
        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.BorderColor = Border;
        button.FlatAppearance.MouseOverBackColor = Surface2;
        return button;
    }

    public static Label Lbl(string text, Font? font = null, Color? colour = null) => new()
    {
        Text = text,
        Font = font ?? Base,
        ForeColor = colour ?? Text,
        AutoSize = true,
        Margin = new Padding(3, 7, 3, 3),
    };

    /// <summary>A white card with a one-pixel border, used to frame each block.</summary>
    public static Panel Card()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Surface,
            Padding = new Padding(1),
            Margin = new Padding(0, 0, 0, 0),
        };
        panel.Paint += (s, e) =>
        {
            using var pen = new Pen(Border);
            e.Graphics.DrawRectangle(pen, 0, 0, panel.Width - 1, panel.Height - 1);
        };
        return panel;
    }

    /// <summary>The program icon, drawn in code so no image file has to travel with it.</summary>
    public static Icon AppIcon(int size = 32)
    {
        using var bitmap = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            using var brush = new SolidBrush(Brand);
            var radius = size * 0.22f;
            using (var path = RoundedRect(new RectangleF(0, 0, size, size), radius))
                g.FillPath(brush, path);

            using var white = new SolidBrush(Color.White);
            var arm = size * 0.155f;
            var pad = size * 0.205f;
            g.FillRectangle(white, (size - arm) / 2f, pad, arm, size - pad * 2);
            g.FillRectangle(white, pad, (size - arm) / 2f, size - pad * 2, arm);
        }

        return Icon.FromHandle(bitmap.GetHicon());
    }

    private static GraphicsPath RoundedRect(RectangleF bounds, float radius)
    {
        var d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
