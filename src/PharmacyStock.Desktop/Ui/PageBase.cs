using System.Windows.Forms;

namespace PharmacyStock.Desktop.Ui;

/// <summary>One screen inside the main window.</summary>
internal abstract class PageBase : UserControl
{
    protected AppServices Services { get; }

    /// <summary>Ask the main window to re-read the counters and status bar.</summary>
    public Action? CountsChanged { get; set; }

    /// <summary>Jump to the stock board with a filter applied ("out", "low", "needs").</summary>
    public Action<string>? ShowBoard { get; set; }

    protected PageBase(AppServices services)
    {
        Services = services;
        Dock = DockStyle.Fill;
        BackColor = Theme.Bg;
        Padding = new Padding(12);
        Font = Theme.Base;
    }

    /// <summary>Redraw this screen from the latest data.</summary>
    public abstract void Reload();

    protected void Toast(string message) =>
        MessageBox.Show(this, message, "Stock Manager", MessageBoxButtons.OK, MessageBoxIcon.Information);

    protected void Problem(string message) =>
        MessageBox.Show(this, message, "Stock Manager", MessageBoxButtons.OK, MessageBoxIcon.Warning);
}
