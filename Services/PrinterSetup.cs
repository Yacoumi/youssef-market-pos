using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace MarketPos.Services;

/// <summary>What a printer found by <see cref="PrinterSetup.Scan"/> is, and so what installing it means.</summary>
public enum FoundPrinterKind
{
    /// <summary>Installed in Windows already. Nothing to install; it can be used as it is.</summary>
    Ready,

    /// <summary>Plugged in, but Windows has no driver for it.</summary>
    DriverMissing,

    /// <summary>A USB printer port with no printer set up on it — a receipt printer without a driver.</summary>
    UsbPort,

    /// <summary>A printer answering on the shop's network that this computer has not set up.</summary>
    Network,
}

/// <summary>One printer the scan found.</summary>
public sealed record FoundPrinter(
    FoundPrinterKind Kind,
    string Name,
    string Detail,
    string? Address = null,
    string? Port = null,
    string? DeviceId = null,
    bool Ipp = false);

/// <summary>
/// Finds the printers a shop has plugged in or put on its network, and sets them up in Windows.
///
/// <para>
/// A shop buys whichever receipt printer the supplier had that week. Setting one up by hand is
/// a trip through Windows settings nobody at a counter knows; this does it from the till:
/// Windows is asked for the driver first (and so Windows Update), and a receipt printer it has
/// no driver for is set up on Windows' own "Generic / Text Only" driver, which is exactly what
/// the till's direct ESC/POS printing wants.
/// </para>
///
/// <para>
/// Installing needs administrator rights, so those steps run in a PowerShell that Windows asks
/// permission for. The scan itself does not.
/// </para>
/// </summary>
public static class PrinterSetup
{
    private const string GenericDriver = "Generic / Text Only";

    /// <summary>Raw printing, which every network receipt printer speaks.</summary>
    private const int RawPort = 9100;

    /// <summary>IPP, which office printers speak and Windows has a class driver for.</summary>
    private const int IppPort = 631;

    // ============================== Scanning ==============================

    /// <summary>Everything this computer can see: installed, unplugged-in-driver, USB ports and the network.</summary>
    public static async Task<List<FoundPrinter>> Scan()
    {
        var local = Task.Run(ScanWindows);
        var network = ScanNetwork();

        var (found, knownAddresses) = await local;

        foreach (var (ip, ipp) in await network)
        {
            if (knownAddresses.Contains(ip)) continue;

            found.Add(new FoundPrinter(FoundPrinterKind.Network,
                Loc.T("Network printer {0}", ip),
                Loc.T(ipp ? "Office printer on the network, not set up on this computer."
                          : "Receipt printer on the network, not set up on this computer."),
                Address: ip, Ipp: ipp));
        }

        return found;
    }

    /// <summary>What Windows itself knows: its printers, its printer ports, and devices missing a driver.</summary>
    private static (List<FoundPrinter> Found, HashSet<string> KnownAddresses) ScanWindows()
    {
        var found = new List<FoundPrinter>();
        var addresses = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        const string script = """
            [Console]::OutputEncoding = [System.Text.Encoding]::UTF8
            $ErrorActionPreference = 'SilentlyContinue'
            $printers = @(Get-Printer | Select-Object Name, PortName, DriverName)
            $ports = @(Get-PrinterPort | Select-Object Name, PrinterHostAddress)
            $devices = @(Get-PnpDevice -PresentOnly | Where-Object {
                ([int]$_.ConfigManagerErrorCode -ne 0) -and (
                    $_.Class -eq 'Printer' -or $_.Class -eq 'PrintQueue' -or $_.Service -eq 'usbprint' -or
                    $_.FriendlyName -match 'print|pos|receipt|thermal|epson|star|xprinter|bixolon|citizen|zebra|sewoo|rongta|hprt|brother|canon|hp |ricoh|kyocera|lexmark|samsung')
            } | Select-Object FriendlyName, InstanceId, @{ n = 'Code'; e = { [int]$_.ConfigManagerErrorCode } })
            [pscustomobject]@{ Printers = $printers; Ports = $ports; Devices = $devices } | ConvertTo-Json -Depth 4 -Compress
            """;

        var json = RunHidden(script);
        if (json.Length == 0) return (found, addresses);

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var usedPorts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var printer in Items(root, "Printers"))
            {
                var name = Text(printer, "Name");
                if (name.Length == 0) continue;
                usedPorts.Add(Text(printer, "PortName"));

                if (ReceiptPrinter.IsVirtualPrinter(name)) continue;
                found.Add(new FoundPrinter(FoundPrinterKind.Ready, name,
                    Loc.T("Installed and ready. Driver: {0}", Text(printer, "DriverName"))));
            }

            foreach (var port in Items(root, "Ports"))
            {
                var name = Text(port, "Name");
                var host = Text(port, "PrinterHostAddress");
                if (host.Length > 0) addresses.Add(host);

                // A USB printing port nothing prints to: a printer was plugged in and no driver
                // ever put a printer on it.
                if (name.StartsWith("USB", StringComparison.OrdinalIgnoreCase) && !usedPorts.Contains(name))
                    found.Add(new FoundPrinter(FoundPrinterKind.UsbPort,
                        Loc.T("USB printer on {0}", name),
                        Loc.T("Plugged in, but no printer is set up for it."),
                        Port: name));
            }

            foreach (var device in Items(root, "Devices"))
            {
                var name = Text(device, "FriendlyName");
                found.Add(new FoundPrinter(FoundPrinterKind.DriverMissing,
                    name.Length > 0 ? name : Loc.T("Unknown printer"),
                    Loc.T("Plugged in, but its driver is not installed."),
                    DeviceId: Text(device, "InstanceId")));
            }
        }
        catch (JsonException)
        {
            // An answer that is not what was asked for is a scan that found nothing.
        }

        return (found, addresses);
    }

    /// <summary>
    /// Knocks on every address of this computer's own networks, on the printing ports. Only the
    /// shop's local networks, and only a few hundred addresses: a /24 at most around each.
    /// </summary>
    private static async Task<List<(string Ip, bool Ipp)>> ScanNetwork()
    {
        var own = new HashSet<string>();
        var candidates = new HashSet<string>();

        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up) continue;
            if (nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;

            foreach (var unicast in nic.GetIPProperties().UnicastAddresses)
            {
                if (unicast.Address.AddressFamily != AddressFamily.InterNetwork) continue;

                var bytes = unicast.Address.GetAddressBytes();
                if (bytes[0] == 169 && bytes[1] == 254) continue;     // no network behind it

                own.Add(unicast.Address.ToString());
                for (var last = 1; last < 255; last++)
                    candidates.Add($"{bytes[0]}.{bytes[1]}.{bytes[2]}.{last}");
            }
        }

        candidates.ExceptWith(own);

        var found = new List<(string, bool)>();
        using var gate = new SemaphoreSlim(96);

        await Task.WhenAll(candidates.Select(async ip =>
        {
            await gate.WaitAsync();
            try
            {
                var raw = await Answers(ip, RawPort);
                var ipp = !raw && await Answers(ip, IppPort);
                if (raw || ipp) lock (found) found.Add((ip, ipp));
            }
            finally
            {
                gate.Release();
            }
        }));

        return found.OrderBy(f => Version.TryParse(f.Item1, out var v) ? v : new Version()).ToList();
    }

    private static async Task<bool> Answers(string ip, int port)
    {
        try
        {
            using var client = new TcpClient();
            using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(450));
            await client.ConnectAsync(IPAddress.Parse(ip), port, timeout.Token);
            return client.Connected;
        }
        catch
        {
            return false;
        }
    }

    // ============================== Installing ==============================

    /// <summary>The outcome of an install: the Windows printer it produced, or why not.</summary>
    public sealed record Installed(bool Ok, string? PrinterName, string Message);

    /// <summary>Sets up what the scan found. Windows asks for permission first.</summary>
    public static Task<Installed> Install(FoundPrinter printer) => Task.Run(() => printer.Kind switch
    {
        FoundPrinterKind.Ready => new Installed(true, printer.Name, Loc.T("{0} is ready.", printer.Name)),
        FoundPrinterKind.Network => RunElevated(NetworkScript(printer.Address!, printer.Ipp)),
        FoundPrinterKind.UsbPort => RunElevated(UsbScript(printer.Port!)),
        FoundPrinterKind.DriverMissing => RunElevated(DriverScript(printer.DeviceId ?? string.Empty)),
        _ => new Installed(false, null, Loc.T("Nothing to install.")),
    });

    /// <summary>Installs a driver the manufacturer supplied (an .inf file), then lets Windows use it.</summary>
    public static Task<Installed> InstallDriverFile(string infPath) => Task.Run(() => RunElevated($$"""
        $before = @(Get-Printer | ForEach-Object Name)
        $said = pnputil /add-driver '{{Quote(infPath)}}' /subdirs /install 2>&1 | Out-String
        pnputil /scan-devices | Out-Null
        Start-Sleep -Seconds 6
        $new = @(Get-Printer | ForEach-Object Name) | Where-Object { $before -notcontains $_ } | Select-Object -First 1
        if ($LASTEXITCODE -ne 0 -and -not $new) { Done 'ERR' $said }
        if ($new) { Done 'OK' $new }
        Done 'OK' ''
        """));

    private static string NetworkScript(string ip, bool ipp) => ipp
        ? $$"""
            $name = 'Printer {{ip}}'
            if (-not (Get-Printer -Name $name -ErrorAction SilentlyContinue)) {
                Add-Printer -Name $name -IppURL 'http://{{ip}}:631/ipp/print'
            }
            Done 'OK' $name
            """
        : $$"""
            {{EnsureGenericDriver}}
            $port = 'IP_{{ip}}'
            if (-not (Get-PrinterPort -Name $port -ErrorAction SilentlyContinue)) { Add-PrinterPort -Name $port -PrinterHostAddress '{{ip}}' -PortNumber {{RawPort}} }
            $name = 'POS Receipt Printer {{ip}}'
            if (-not (Get-Printer -Name $name -ErrorAction SilentlyContinue)) { Add-Printer -Name $name -DriverName '{{GenericDriver}}' -PortName $port }
            Done 'OK' $name
            """;

    private static string UsbScript(string port) => $$"""
        {{EnsureGenericDriver}}
        $name = 'POS Receipt Printer {{port}}'
        if (-not (Get-Printer -Name $name -ErrorAction SilentlyContinue)) { Add-Printer -Name $name -DriverName '{{GenericDriver}}' -PortName '{{port}}' }
        Done 'OK' $name
        """;

    /// <summary>
    /// Asks Windows to look for the driver again (Windows Update included). If it finds one the
    /// printer appears on its own; if not, and the device left a USB printing port behind, the
    /// receipt printer is set up on the generic driver instead.
    /// </summary>
    private static string DriverScript(string deviceId) => $$"""
        $before = @(Get-Printer | ForEach-Object Name)
        pnputil /scan-devices | Out-Null
        Start-Sleep -Seconds 8
        $new = @(Get-Printer | ForEach-Object Name) | Where-Object { $before -notcontains $_ } | Select-Object -First 1
        if ($new) { Done 'OK' $new }

        $used = @(Get-Printer | ForEach-Object PortName)
        $free = Get-PrinterPort | Where-Object { $_.Name -like 'USB*' -and $used -notcontains $_.Name } | Select-Object -First 1
        if ($free) {
            {{EnsureGenericDriver}}
            $name = 'POS Receipt Printer ' + $free.Name
            Add-Printer -Name $name -DriverName '{{GenericDriver}}' -PortName $free.Name
            Done 'OK' $name
        }

        $device = Get-PnpDevice -InstanceId '{{Quote(deviceId)}}' -ErrorAction SilentlyContinue
        if ($device -and [int]$device.ConfigManagerErrorCode -eq 0) { Done 'OK' '' }
        Done 'NODRIVER' ''
        """;

    private static readonly string EnsureGenericDriver =
        $"if (-not (Get-PrinterDriver -Name '{GenericDriver}' -ErrorAction SilentlyContinue)) {{ Add-PrinterDriver -Name '{GenericDriver}' }}";

    /// <summary>
    /// Runs an install script as administrator, hidden, and reads what it wrote. The script and
    /// its answer are kept beside the exe for the moment it runs and then removed.
    /// </summary>
    private static Installed RunElevated(string body)
    {
        var stamp = Guid.NewGuid().ToString("N")[..8];
        var script = AppFolder.File($"printer-setup-{stamp}.ps1");
        var answer = AppFolder.File($"printer-setup-{stamp}.txt");

        var full = $$"""
            $ErrorActionPreference = 'Stop'
            function Done([string]$status, [string]$value) {
                [System.IO.File]::WriteAllText('{{Quote(answer)}}', $status + '|' + $value, [System.Text.Encoding]::UTF8)
                exit 0
            }
            try {
            {{body}}
            } catch {
                Done 'ERR' $_.Exception.Message
            }
            """;

        try
        {
            File.WriteAllText(script, full, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

            using var process = Process.Start(new ProcessStartInfo("powershell.exe",
                $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{script}\"")
            {
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
            });

            if (process is null) return new Installed(false, null, Loc.T("Windows did not start the installer."));
            if (!process.WaitForExit(TimeSpan.FromMinutes(3)))
                return new Installed(false, null, Loc.T("Windows is taking too long. Try again in a moment."));

            var said = File.Exists(answer) ? File.ReadAllText(answer).Trim().TrimStart('﻿') : string.Empty;
            var bar = said.IndexOf('|');
            var status = bar < 0 ? said : said[..bar];
            var value = bar < 0 ? string.Empty : said[(bar + 1)..].Trim();

            return status switch
            {
                "OK" when value.Length > 0 => new Installed(true, value, Loc.T("{0} is installed and ready.", value)),
                "OK" => new Installed(true, null, Loc.T("The driver is installed. Press Scan again to see the printer.")),
                "NODRIVER" => new Installed(false, null,
                    Loc.T("Windows found no driver for this printer. Install the manufacturer's driver with Install a driver from a file.")),
                "ERR" => new Installed(false, null, Loc.T("Could not install the printer: {0}", value)),
                _ => new Installed(false, null, Loc.T("Could not install the printer.")),
            };
        }
        catch (Win32Exception refused) when (refused.NativeErrorCode == 1223)
        {
            return new Installed(false, null, Loc.T("Installing a printer needs permission. Press Yes when Windows asks."));
        }
        catch (Exception error)
        {
            return new Installed(false, null, Loc.T("Could not install the printer: {0}", error.Message));
        }
        finally
        {
            TryDelete(script);
            TryDelete(answer);
        }
    }

    // ============================== Helpers ==============================

    /// <summary>A PowerShell run with no window, for reading what Windows knows. No rights needed.</summary>
    private static string RunHidden(string script)
    {
        try
        {
            var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
            using var process = Process.Start(new ProcessStartInfo("powershell.exe",
                $"-NoProfile -ExecutionPolicy Bypass -EncodedCommand {encoded}")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
            });

            if (process is null) return string.Empty;

            var output = process.StandardOutput.ReadToEndAsync();
            _ = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(TimeSpan.FromSeconds(40))) { try { process.Kill(); } catch { } return string.Empty; }

            return output.Result.Trim();
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>A property that may hold one object or a list of them, as ConvertTo-Json writes either.</summary>
    private static IEnumerable<JsonElement> Items(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value)) yield break;

        if (value.ValueKind == JsonValueKind.Array)
            foreach (var item in value.EnumerateArray()) yield return item;
        else if (value.ValueKind == JsonValueKind.Object)
            yield return value;
    }

    private static string Text(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()?.Trim() ?? string.Empty
            : string.Empty;

    /// <summary>Safe inside a single-quoted PowerShell string.</summary>
    private static string Quote(string text) => text.Replace("'", "''");

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}
