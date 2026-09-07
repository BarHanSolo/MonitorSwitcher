using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace MonitorSwitcher;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using var mutex = new Mutex(
            true,
            "MonitorSwitcher-SingleInstance",
            out bool createdNew);

        if (!createdNew)
        {
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new TrayApplicationContext());
    }
}

internal sealed class TrayApplicationContext : ApplicationContext
{
    private readonly NotifyIcon _trayIcon;
    private readonly MonitorManager _monitorManager;
    private readonly MonitorConfiguration _configuration;

    public TrayApplicationContext()
    {
        _configuration = ConfigurationLoader.Load();
        _monitorManager = new MonitorManager(_configuration);

        var menu = BuildMenu();

        string iconPath = Path.Combine(
            AppContext.BaseDirectory,
            "monitor.ico");

        Icon trayIcon = File.Exists(iconPath)
            ? new Icon(iconPath)
            : SystemIcons.Application;

        _trayIcon = new NotifyIcon
        {
            Icon = trayIcon,
            Text = "Monitor Switcher",
            ContextMenuStrip = menu,
            Visible = true
        };
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();

        var profileNames = _configuration.Monitors
            .SelectMany(m => m.Profiles.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (string profileName in profileNames)
        {
            var item = new ToolStripMenuItem(profileName);

            item.Click += (_, _) =>
            {
                bool success =
                    _monitorManager.SetProfile(profileName);

                if (!success)
                {
                    MessageBox.Show(
                        $"Failed to set profile \"{profileName}\".",
                        "Monitor Switcher",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                }
            };

            menu.Items.Add(item);
        }

        menu.Items.Add(new ToolStripSeparator());

        foreach (var configuredMonitor in _configuration.Monitors)
        {
            var item = new ToolStripMenuItem(
                configuredMonitor.Name);

            item.Click += (_, _) =>
            {
                bool success =
                    _monitorManager.ToggleMonitor(
                        configuredMonitor.Name);

                if (!success)
                {
                    MessageBox.Show(
                        $"Failed to switch monitor \"{configuredMonitor.Name}\".",
                        "Monitor Switcher",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                }
            };

            menu.Items.Add(item);
        }

        menu.Items.Add(new ToolStripSeparator());

        var exitItem = new ToolStripMenuItem("Exit");

        exitItem.Click += (_, _) =>
        {
            ExitApplication();
        };

        menu.Items.Add(exitItem);

        return menu;
    }

    private void ExitApplication()
    {
        _trayIcon.Visible = false;
        _trayIcon.Dispose();

        Application.Exit();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _trayIcon.Dispose();
        }

        base.Dispose(disposing);
    }
}

internal sealed class MonitorConfiguration
{
    public List<MonitorConfigurationEntry> Monitors { get; set; } = new();
}

internal sealed class MonitorConfigurationEntry
{
    public string Name { get; set; } = "";

    public string Id { get; set; } = "";

    public Dictionary<string, uint> Profiles { get; set; } = new();
}

internal static class ConfigurationLoader
{
    public static MonitorConfiguration Load()
    {
        string path = Path.Combine(
            AppContext.BaseDirectory,
            "monitors.json");

        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"Configuration file not found:\n{path}");
        }

        string json = File.ReadAllText(path);

        var configuration =
            JsonSerializer.Deserialize<MonitorConfiguration>(
                json,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

        if (configuration == null)
        {
            throw new InvalidOperationException(
                "Failed to read monitors.json.");
        }

        return configuration;
    }
}

internal sealed class MonitorManager
{
    private const byte VcpInputSource = 0x60;

    private const uint EddGetDeviceInterfaceName = 0x00000001;

    private const int VcpRetryTimeoutMs = 5000;

    private const int VcpRetryIntervalMs = 200;

    private const int VcpVerifyDelayMs = 100;

    private readonly MonitorConfiguration _configuration;

    public MonitorManager(
        MonitorConfiguration configuration)
    {
        _configuration = configuration;
    }

    public bool SetProfile(string profileName)
    {
        var physicalMonitors = FindMonitors();

        if (physicalMonitors.Count == 0)
        {
            MessageBox.Show(
                "No physical monitors were found.",
                "Monitor Switcher",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);

            return false;
        }

        bool allOk = true;

        try
        {
            foreach (var configuredMonitor in _configuration.Monitors)
            {
                if (!configuredMonitor.Profiles.TryGetValue(
                        profileName,
                        out uint targetValue))
                {
                    allOk = false;
                    continue;
                }

                MonitorInfo? monitor =
                    FindConfiguredMonitor(
                        physicalMonitors,
                        configuredMonitor.Id);

                if (monitor == null)
                {
                    allOk = false;

                    ShowMonitorDiagnostic(
                        configuredMonitor.Id,
                        physicalMonitors);

                    continue;
                }

                if (!GetInputSourceWithRetry(
                        monitor.Handle,
                        out uint currentValue))
                {
                    MessageBox.Show(
                        $"Failed to read VCP 0x60 from monitor " +
                        $"after {VcpRetryTimeoutMs / 1000} seconds:\n\n" +
                        $"Name: {configuredMonitor.Name}\n" +
                        $"ID: {configuredMonitor.Id}\n" +
                        $"DISPLAY: {monitor.DeviceName}\n" +
                        $"Device ID: {monitor.DeviceId}",
                        "Monitor Switcher",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);

                    allOk = false;
                    continue;
                }

                if (currentValue == targetValue)
                {
                    continue;
                }

                if (!SetInputSourceWithRetry(
                        monitor.Handle,
                        targetValue))
                {
                    MessageBox.Show(
                        $"Failed to set input on monitor " +
                        $"after {VcpRetryTimeoutMs / 1000} seconds:\n\n" +
                        $"Name: {configuredMonitor.Name}\n" +
                        $"ID: {configuredMonitor.Id}\n" +
                        $"DISPLAY: {monitor.DeviceName}\n" +
                        $"Device ID: {monitor.DeviceId}\n\n" +
                        $"Current value: {currentValue}\n" +
                        $"Target value: {targetValue}",
                        "Monitor Switcher",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);

                    allOk = false;
                }
            }
        }
        finally
        {
            DestroyPhysicalMonitorHandles(
                physicalMonitors);
        }

        return allOk;
    }

    public bool ToggleMonitor(string monitorName)
    {
        var configuredMonitor =
            _configuration.Monitors.FirstOrDefault(
                m => string.Equals(
                    m.Name,
                    monitorName,
                    StringComparison.OrdinalIgnoreCase));

        if (configuredMonitor == null)
        {
            return false;
        }

        if (configuredMonitor.Profiles.Count < 2)
        {
            return false;
        }

        var physicalMonitors = FindMonitors();

        if (physicalMonitors.Count == 0)
        {
            return false;
        }

        try
        {
            MonitorInfo? monitor =
                FindConfiguredMonitor(
                    physicalMonitors,
                    configuredMonitor.Id);

            if (monitor == null)
            {
                ShowMonitorDiagnostic(
                    configuredMonitor.Id,
                    physicalMonitors);

                return false;
            }

            if (!GetInputSourceWithRetry(
                    monitor.Handle,
                    out uint currentValue))
            {
                MessageBox.Show(
                    $"Failed to read VCP 0x60 from monitor " +
                    $"after {VcpRetryTimeoutMs / 1000} seconds:\n\n" +
                    $"Name: {configuredMonitor.Name}\n" +
                    $"ID: {configuredMonitor.Id}\n" +
                    $"DISPLAY: {monitor.DeviceName}\n" +
                    $"Device ID: {monitor.DeviceId}",
                    "Monitor Switcher",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);

                return false;
            }

            var profiles = configuredMonitor.Profiles
                .ToList();

            int currentIndex = profiles.FindIndex(
                p => p.Value == currentValue);

            if (currentIndex < 0)
            {
                string availableProfiles = string.Join(
                    "\n",
                    profiles.Select(
                        p => $"{p.Key}: {p.Value}"));

                MessageBox.Show(
                    $"Monitor \"{configuredMonitor.Name}\" " +
                    $"has an unknown VCP 0x60 value: {currentValue}.\n\n" +
                    $"Available profiles:\n{availableProfiles}",
                    "Monitor Switcher",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);

                return false;
            }

            int nextIndex =
                (currentIndex + 1) % profiles.Count;

            uint targetValue =
                profiles[nextIndex].Value;

            return SetInputSourceWithRetry(
                monitor.Handle,
                targetValue);
        }
        finally
        {
            DestroyPhysicalMonitorHandles(
                physicalMonitors);
        }
    }

    private static bool GetInputSourceWithRetry(
        IntPtr physicalMonitor,
        out uint value)
    {
        value = 0;

        Stopwatch stopwatch =
            Stopwatch.StartNew();

        while (stopwatch.ElapsedMilliseconds <
               VcpRetryTimeoutMs)
        {
            if (GetInputSource(
                    physicalMonitor,
                    out value))
            {
                return true;
            }

            Thread.Sleep(
                VcpRetryIntervalMs);
        }

        return GetInputSource(
            physicalMonitor,
            out value);
    }

    private static bool SetInputSourceWithRetry(
        IntPtr physicalMonitor,
        uint targetValue)
    {
        Stopwatch stopwatch =
            Stopwatch.StartNew();

        while (stopwatch.ElapsedMilliseconds <
               VcpRetryTimeoutMs)
        {
            bool setSuccess =
                SetInputSource(
                    physicalMonitor,
                    targetValue);

            if (setSuccess)
            {
                Thread.Sleep(
                    VcpVerifyDelayMs);

                if (GetInputSource(
                        physicalMonitor,
                        out uint currentValue) &&
                    currentValue == targetValue)
                {
                    return true;
                }
            }

            Thread.Sleep(
                VcpRetryIntervalMs);
        }

        if (!SetInputSource(
                physicalMonitor,
                targetValue))
        {
            return false;
        }

        Thread.Sleep(
            VcpVerifyDelayMs);

        return GetInputSource(
                   physicalMonitor,
                   out uint finalValue) &&
               finalValue == targetValue;
    }

    private static void DestroyPhysicalMonitorHandles(
        List<MonitorInfo> monitors)
    {
        foreach (MonitorInfo monitor in monitors)
        {
            if (monitor.Handle == IntPtr.Zero)
            {
                continue;
            }

            try
            {
                DestroyPhysicalMonitor(
                    monitor.Handle);
            }
            catch
            {
            }
        }
    }

    private static MonitorInfo? FindConfiguredMonitor(
        List<MonitorInfo> monitors,
        string configuredId)
    {
        return monitors.FirstOrDefault(
            m =>
                ContainsId(m.DeviceId, configuredId) ||
                ContainsId(m.DeviceKey, configuredId) ||
                ContainsId(m.DeviceName, configuredId) ||
                ContainsId(m.DeviceString, configuredId) ||
                ContainsId(m.DisplayDeviceName, configuredId) ||
                ContainsId(m.Description, configuredId));
    }

    private static bool ContainsId(
        string? text,
        string id)
    {
        if (string.IsNullOrWhiteSpace(text) ||
            string.IsNullOrWhiteSpace(id))
        {
            return false;
        }

        return text.Contains(
            id,
            StringComparison.OrdinalIgnoreCase);
    }

    private static List<MonitorInfo> FindMonitors()
    {
        var result = new List<MonitorInfo>();

        EnumDisplayMonitors(
            IntPtr.Zero,
            IntPtr.Zero,
            (
                IntPtr hMonitor,
                IntPtr hdcMonitor,
                ref Rect monitorRect,
                IntPtr dwData) =>
            {
                var monitorInfoEx =
                    new MonitorInfoEx
                    {
                        cbSize =
                            Marshal.SizeOf<MonitorInfoEx>()
                    };

                if (!GetMonitorInfo(
                        hMonitor,
                        ref monitorInfoEx))
                {
                    return true;
                }

                string displayName =
                    monitorInfoEx.szDevice ?? "";

                var displayDevice =
                    new DisplayDevice
                    {
                        cb =
                            Marshal.SizeOf<DisplayDevice>()
                    };

                bool foundDisplayDevice =
                    EnumDisplayDevices(
                        displayName,
                        0,
                        ref displayDevice,
                        EddGetDeviceInterfaceName);

                uint physicalMonitorCount = 0;

                if (!GetNumberOfPhysicalMonitorsFromHMONITOR(
                        hMonitor,
                        ref physicalMonitorCount))
                {
                    return true;
                }

                if (physicalMonitorCount == 0)
                {
                    return true;
                }

                var physicalMonitors =
                    new PhysicalMonitor[
                        physicalMonitorCount];

                if (!GetPhysicalMonitorsFromHMONITOR(
                        hMonitor,
                        physicalMonitorCount,
                        physicalMonitors))
                {
                    return true;
                }

                foreach (var physicalMonitor in physicalMonitors)
                {
                    result.Add(
                        new MonitorInfo
                        {
                            Handle =
                                physicalMonitor
                                    .hPhysicalMonitor,

                            Description =
                                physicalMonitor
                                    .szPhysicalMonitorDescription
                                ?? "",

                            DeviceName =
                                displayName,

                            DisplayDeviceName =
                                foundDisplayDevice
                                    ? displayDevice.DeviceName
                                    : "",

                            DeviceId =
                                foundDisplayDevice
                                    ? displayDevice.DeviceID
                                    : "",

                            DeviceKey =
                                foundDisplayDevice
                                    ? displayDevice.DeviceKey
                                    : "",

                            DeviceString =
                                foundDisplayDevice
                                    ? displayDevice.DeviceString
                                    : ""
                        });
                }

                return true;
            },
            IntPtr.Zero);

        return result;
    }

    private static void ShowMonitorDiagnostic(
        string searchedId,
        List<MonitorInfo> monitors)
    {
        string details;

        if (monitors.Count == 0)
        {
            details =
                "Windows did not return any physical monitors.";
        }
        else
        {
            details = string.Join(
                "\n\n",
                monitors.Select(
                    (m, index) =>
                        $"Monitor #{index + 1}\n" +
                        $"GDI Device Name: {m.DeviceName}\n" +
                        $"Display Device Name: {m.DisplayDeviceName}\n" +
                        $"Device ID: {m.DeviceId}\n" +
                        $"Device Key: {m.DeviceKey}\n" +
                        $"Device String: {m.DeviceString}\n" +
                        $"Description: {m.Description}"));
        }

        MessageBox.Show(
            $"Monitor not found:\n\n" +
            $"Requested ID: {searchedId}\n\n" +
            $"Detected devices:\n\n{details}",
            "Monitor Switcher",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
    }

    private static bool GetInputSource(
        IntPtr physicalMonitor,
        out uint value)
    {
        value = 0;

        bool success =
            GetVCPFeatureAndVCPFeatureReply(
                physicalMonitor,
                VcpInputSource,
                IntPtr.Zero,
                out uint currentValue,
                out _);

        if (!success)
        {
            return false;
        }

        value = currentValue;

        return true;
    }

    private static bool SetInputSource(
        IntPtr physicalMonitor,
        uint value)
    {
        return SetVCPFeature(
            physicalMonitor,
            VcpInputSource,
            value);
    }

    private delegate bool MonitorEnumProc(
        IntPtr hMonitor,
        IntPtr hdcMonitor,
        ref Rect lprcMonitor,
        IntPtr dwData);

    [DllImport(
        "user32.dll",
        SetLastError = true)]
    private static extern bool EnumDisplayMonitors(
        IntPtr hdc,
        IntPtr lprcClip,
        MonitorEnumProc lpfnEnum,
        IntPtr dwData);

    [StructLayout(
        LayoutKind.Sequential,
        CharSet = CharSet.Unicode)]
    private struct MonitorInfoEx
    {
        public int cbSize;
        public Rect rcMonitor;
        public Rect rcWork;
        public uint dwFlags;

        [MarshalAs(
            UnmanagedType.ByValTStr,
            SizeConst = 32)]
        public string szDevice;
    }

    [DllImport(
        "user32.dll",
        CharSet = CharSet.Unicode,
        SetLastError = true)]
    private static extern bool GetMonitorInfo(
        IntPtr hMonitor,
        ref MonitorInfoEx lpmi);

    [StructLayout(
        LayoutKind.Sequential,
        CharSet = CharSet.Unicode)]
    private struct DisplayDevice
    {
        public int cb;

        [MarshalAs(
            UnmanagedType.ByValTStr,
            SizeConst = 32)]
        public string DeviceName;

        [MarshalAs(
            UnmanagedType.ByValTStr,
            SizeConst = 128)]
        public string DeviceString;

        public int StateFlags;

        [MarshalAs(
            UnmanagedType.ByValTStr,
            SizeConst = 128)]
        public string DeviceID;

        [MarshalAs(
            UnmanagedType.ByValTStr,
            SizeConst = 128)]
        public string DeviceKey;
    }

    [DllImport(
        "user32.dll",
        CharSet = CharSet.Unicode,
        SetLastError = true)]
    private static extern bool EnumDisplayDevices(
        string? lpDevice,
        uint iDevNum,
        ref DisplayDevice lpDisplayDevice,
        uint dwFlags);

    [StructLayout(
        LayoutKind.Sequential)]
    private struct PhysicalMonitor
    {
        public IntPtr hPhysicalMonitor;

        [MarshalAs(
            UnmanagedType.ByValTStr,
            SizeConst = 128)]
        public string szPhysicalMonitorDescription;
    }

    [DllImport(
        "dxva2.dll",
        SetLastError = true)]
    private static extern bool
        GetNumberOfPhysicalMonitorsFromHMONITOR(
            IntPtr hMonitor,
            ref uint pdwNumberOfPhysicalMonitors);

    [DllImport(
        "dxva2.dll",
        SetLastError = true)]
    private static extern bool
        GetPhysicalMonitorsFromHMONITOR(
            IntPtr hMonitor,
            uint dwPhysicalMonitorArraySize,
            [Out] PhysicalMonitor[] pPhysicalMonitorArray);

    [DllImport(
        "dxva2.dll",
        SetLastError = true)]
    private static extern bool
        DestroyPhysicalMonitor(
            IntPtr hMonitor);

    [DllImport(
        "dxva2.dll",
        SetLastError = true)]
    private static extern bool
        GetVCPFeatureAndVCPFeatureReply(
            IntPtr hMonitor,
            byte bVCPCode,
            IntPtr pvct,
            out uint pvcpvct,
            out uint pValue);

    [DllImport(
        "dxva2.dll",
        SetLastError = true)]
    private static extern bool
        SetVCPFeature(
            IntPtr hMonitor,
            byte bVCPCode,
            uint dwNewValue);
}

internal sealed class MonitorInfo
{
    public IntPtr Handle { get; init; }

    public string Description { get; init; } = "";

    public string DeviceName { get; init; } = "";

    public string DisplayDeviceName { get; init; } = "";

    public string DeviceId { get; init; } = "";

    public string DeviceKey { get; init; } = "";

    public string DeviceString { get; init; } = "";
}

[StructLayout(LayoutKind.Sequential)]
internal struct Rect
{
    public int Left;
    public int Top;
    public int Right;
    public int Bottom;
}