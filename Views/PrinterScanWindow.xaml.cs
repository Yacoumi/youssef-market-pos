using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MarketPos.Services;

namespace MarketPos.Views;

/// <summary>
/// Scans for printers and installs the one the shop has. Hands back the Windows printer that is
/// ready to use, so Settings can select it.
/// </summary>
public partial class PrinterScanWindow : MarketPos.Views.DialogWindow
{
    /// <summary>One row: the printer and what its button does, both already in the shop's language.</summary>
    public sealed record Row(FoundPrinter Printer, string Name, string Detail, string Action);

    /// <summary>The printer to use, once one is installed or chosen.</summary>
    public string? Chosen { get; private set; }

    private bool _busy;

    public PrinterScanWindow()
    {
        InitializeComponent();
        Services.Localizer.Apply(this);
        Services.Responsive.Fit(this);

        Loaded += (_, _) => Scan_Click(this, new RoutedEventArgs());
    }

    /// <summary>Opens the scan. The printer that is ready to use, or null.</summary>
    public static string? Ask(Window owner)
    {
        var window = new PrinterScanWindow().By(owner);
        return window.ShowDialog() == true ? window.Chosen : null;
    }

    private async void Scan_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        Busy(true, Loc.T("Looking for printers on this computer and the shop's network…"));

        try
        {
            var found = await PrinterSetup.Scan();

            FoundList.ItemsSource = found
                .OrderBy(p => p.Kind == FoundPrinterKind.Ready ? 1 : 0)
                .Select(p => new Row(p, p.Name, p.Detail,
                    Loc.T(p.Kind == FoundPrinterKind.Ready ? "Use" : "Install")))
                .ToList();

            var waiting = found.Count(p => p.Kind != FoundPrinterKind.Ready);
            StatusText.Text = found.Count == 0
                ? Loc.T("No printer found. Check it is switched on and plugged in, or connected to the same network.")
                : waiting > 0
                    ? Loc.T("{0} printer(s) to install.", waiting)
                    : Loc.T("Every printer found is already installed.");
        }
        catch (Exception error)
        {
            StatusText.Text = error.Message;
        }
        finally
        {
            Busy(false);
        }
    }

    private async void Act_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || sender is not Button { Tag: Row row }) return;

        if (row.Printer.Kind == FoundPrinterKind.Ready)
        {
            Use(row.Printer.Name);
            return;
        }

        Busy(true, Loc.T("Installing {0}… Press Yes if Windows asks for permission.", row.Name));

        try
        {
            var result = await PrinterSetup.Install(row.Printer);
            StatusText.Text = result.Message;

            if (result.Ok && result.PrinterName is { Length: > 0 } installed)
            {
                Use(installed);
                return;
            }
        }
        finally
        {
            Busy(false);
        }
    }

    /// <summary>The manufacturer's driver, from a download or the CD in the box.</summary>
    private async void DriverFile_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;

        var picker = new Microsoft.Win32.OpenFileDialog
        {
            Title = Loc.T("Choose the printer driver (.inf file)"),
            Filter = Loc.T("Printer driver") + "|*.inf",
            CheckFileExists = true,
        };

        if (picker.ShowDialog(DialogOwner) != true) return;

        Busy(true, Loc.T("Installing the driver… Press Yes if Windows asks for permission."));

        try
        {
            var result = await PrinterSetup.InstallDriverFile(picker.FileName);
            StatusText.Text = result.Message;

            if (result.Ok && result.PrinterName is { Length: > 0 } installed)
            {
                Use(installed);
                return;
            }
        }
        finally
        {
            Busy(false);
        }

        // A driver on its own is not yet a printer on screen; show what is there now.
        Scan_Click(this, new RoutedEventArgs());
    }

    /// <summary>Windows' own printer page, for the printer this cannot set up by itself.</summary>
    private void WindowsPrinters_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ms-settings:printers")
            {
                UseShellExecute = true,
            });
        }
        catch (Exception error)
        {
            StatusText.Text = error.Message;
        }
    }

    private void Use(string printer)
    {
        Chosen = printer;
        DialogResult = true;
        Close();
    }

    private void Busy(bool busy, string? status = null)
    {
        _busy = busy;
        ScanButton.IsEnabled = !busy;
        DriverFileButton.IsEnabled = !busy;
        FoundList.IsEnabled = !busy;
        if (status is not null) StatusText.Text = status;
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        DialogResult = Chosen is not null;
        Close();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) Close_Click(sender, e);
    }
}
