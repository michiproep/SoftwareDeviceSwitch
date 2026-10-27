using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace SoftwareDeviceSwitch;

// Windows inbox DLLs only. No helper executables or third-party drivers.
internal static class NativeHardware
{
    internal sealed record HidDevice(string Path, string Receiver, ushort UsagePage, ushort Usage, ushort InputLength, ushort OutputLength)
    {
        public override string ToString() => $"{Receiver} usage {UsagePage:X4}:{Usage:X4} output length {OutputLength}\r\n{Path}";
    }
    internal sealed record ChangeHostDiscovery(int Slot, byte? FeatureIndex, string Status);
    internal sealed record MonitorChoice(string Id, string Label, string LegacySelector, string CurrentInput)
    {
        internal IReadOnlyList<int> SupportedInputs { get; init; } = [];
        internal string InputDiscoveryStatus { get; init; } = "Inputs have not been read yet. Click Find monitors.";
    }
    internal sealed class Monitor : IDisposable
    {
        public string Selector { get; internal set; }
        public string LegacySelector { get; }
        public string Description { get; }
        public string InstanceKey { get; }
        internal IntPtr Handle { get; private set; }
        public Monitor(string selector, string legacySelector, string description, IntPtr handle, string instanceKey) => (Selector, LegacySelector, Description, Handle, InstanceKey) = (selector, legacySelector, description, handle, instanceKey);
        public void Dispose() { if (Handle != IntPtr.Zero) { DestroyPhysicalMonitor(Handle); Handle = IntPtr.Zero; } }
    }
    internal static List<Monitor> OpenMonitors()
    {
        var result = new List<Monitor>();
        Exception? failure = null;
        MonitorCallback callback = (IntPtr h, IntPtr dc, ref Rect rect, IntPtr data) =>
        {
            try
            {
                var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
                if (!GetMonitorInfoW(h, ref info)) throw Error("GetMonitorInfo");
                if (!GetNumberOfPhysicalMonitorsFromHMONITOR(h, out uint count)) return true; // e.g. remote or virtual display
                if (count == 0) return true;
                var physical = new PhysicalMonitor[count];
                if (!GetPhysicalMonitorsFromHMONITOR(h, count, physical)) throw Error("Enumerating physical monitors");
                for (int i = 0; i < physical.Length; i++)
                {
                    var display = new DisplayDevice { Size = Marshal.SizeOf<DisplayDevice>() };
                    string legacy = $"{info.Device}\\Monitor{i}";
                    bool found = EnumDisplayDevicesW(info.Device, (uint)i, ref display, 0);
                    string stableId = found ? (display.DeviceId?.Trim() ?? "") : "";
                    string deviceKey = found ? (display.DeviceKey?.Trim() ?? "") : "";
                    if (string.IsNullOrWhiteSpace(stableId)) stableId = deviceKey;
                    if (string.IsNullOrWhiteSpace(stableId)) throw new Exception($"Windows did not provide a persistent monitor ID for {legacy}.");
                    result.Add(new(stableId, legacy, physical[i].Description, physical[i].Handle, deviceKey));
                }
                return true;
            }
            catch (Exception e) { failure = e; return false; }
        };
        bool success = EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero);
        if (!success || failure != null)
        {
            foreach (var m in result) m.Dispose();
            throw failure ?? Error("Enumerating displays");
        }
        foreach (var group in result.GroupBy(m => m.Selector, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
        {
            if (group.Any(m => string.IsNullOrWhiteSpace(m.InstanceKey)))
            {
                foreach (var m in result) m.Dispose();
                throw new Exception("Two monitors share the same Plug and Play ID and Windows did not provide instance paths to distinguish them.");
            }
            foreach (var m in group) m.Selector += "|INSTANCE:" + m.InstanceKey;
        }
        return result;
    }
    internal static string DescribeMonitors()
    {
        var monitors = OpenMonitors();
        try
        {
            if (monitors.Count == 0) return "No physical monitors found. Remote sessions and some docks do not expose DDC/CI.";
            return string.Join("\r\n\r\n", monitors.Select(m =>
            {
                var input = GetVCPFeatureAndVCPFeatureReply(m.Handle, 0x60, out _, out uint current, out _) ? current.ToString() : "unavailable (enable DDC/CI or check cable/dock)";
                return $"Monitor ID: {m.Selector}\r\nName: {m.Description}\r\nCurrent input (decimal): {input}";
            }));
        }
        finally { foreach (var m in monitors) m.Dispose(); }
    }
    internal static List<MonitorChoice> GetMonitorChoices()
    {
        var monitors = OpenMonitors();
        try
        {
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var choices = new List<MonitorChoice>();
            foreach (var m in monitors)
            {
                string id = m.Selector;
                if (!ids.Add(id)) throw new Exception("Windows returned duplicate monitor IDs. Check the monitor cables and display arrangement.");
                choices.Add(ReadMonitorChoice(m));
            }
            return choices;
        }
        finally { foreach (var m in monitors) m.Dispose(); }
    }
    internal static MonitorChoice ReadMonitorInputs(string selector)
    {
        var monitors = OpenMonitors();
        try
        {
            var monitor = monitors.SingleOrDefault(m => m.Selector.Equals(selector, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException("That monitor is not currently available. Use Find monitors and select its connection first.");
            return ReadMonitorChoice(monitor);
        }
        finally { foreach (var monitor in monitors) monitor.Dispose(); }
    }
    static MonitorChoice ReadMonitorChoice(Monitor monitor)
    {
        var input = GetVCPFeatureAndVCPFeatureReply(monitor.Handle, 0x60, out _, out uint current, out _) ? current.ToString() : "unavailable";
        List<int> inputs = [];
        string status;
        if (!GetCapabilitiesStringLength(monitor.Handle, out uint length))
            status = "The monitor did not return input capabilities. Standard codes are unconfirmed.";
        else if (length is < 2 or > 65536)
            status = "The monitor returned an invalid capabilities length. Standard codes are unconfirmed.";
        else
        {
            var buffer = new StringBuilder((int)length);
            if (!CapabilitiesRequestAndCapabilitiesReply(monitor.Handle, buffer, length))
                status = "Reading input capabilities failed. Standard codes are unconfirmed.";
            else
            {
                inputs = MonitorInputs.ParseCapabilities(buffer.ToString());
                status = inputs.Count > 0 ? $"{inputs.Count} inputs reported by this monitor."
                    : "The monitor did not list supported input values. Standard codes are unconfirmed.";
            }
        }
        return new(monitor.Selector, $"{monitor.Description} | {monitor.Selector}", monitor.LegacySelector, input)
        {
            SupportedInputs = inputs,
            InputDiscoveryStatus = status
        };
    }
    internal static void SetInput(Monitor monitor, uint input)
    {
        if (!SetVCPFeature(monitor.Handle, 0x60, input)) throw Error($"Switching {monitor.Selector}; check DDC/CI and connection");
    }
    internal static List<HidDevice> ListHid()
    {
        HidD_GetHidGuid(out Guid guid);
        IntPtr set = SetupDiGetClassDevsW(ref guid, null, IntPtr.Zero, 0x12);
        if (set == new IntPtr(-1)) throw Error("Enumerating HID devices");
        var result = new List<HidDevice>();
        try
        {
            for (uint i = 0; ; i++)
            {
                var iface = new DeviceInterface { Size = Marshal.SizeOf<DeviceInterface>() };
                if (!SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref guid, i, ref iface))
                {
                    if (Marshal.GetLastWin32Error() == 259) break;
                    throw Error("Enumerating HID interface");
                }
                SetupDiGetDeviceInterfaceDetailW(set, ref iface, IntPtr.Zero, 0, out uint required, IntPtr.Zero);
                if (required < 6) continue;
                IntPtr detail = Marshal.AllocHGlobal((int)required);
                try
                {
                    Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
                    if (!SetupDiGetDeviceInterfaceDetailW(set, ref iface, detail, required, out _, IntPtr.Zero)) throw Error("Reading HID path");
                    string path = Marshal.PtrToStringUni(detail + 4)!;
                    using var handle = CreateFileW(path, 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
                    if (handle.IsInvalid) continue;
                    var attributes = new HidAttributes { Size = Marshal.SizeOf<HidAttributes>() };
                    if (!HidD_GetAttributes(handle, ref attributes) || !HidD_GetPreparsedData(handle, out IntPtr preparsed)) continue;
                    try
                    {
                        if (HidP_GetCaps(preparsed, out HidCaps caps) >= 0)
                            result.Add(new(path, $"{attributes.Vendor:X4}:{attributes.Product:X4}", caps.UsagePage, caps.Usage, caps.InputLength, caps.OutputLength));
                    }
                    finally { HidD_FreePreparsedData(preparsed); }
                }
                finally { Marshal.FreeHGlobal(detail); }
            }
        }
        finally { SetupDiDestroyDeviceInfoList(set); }
        return result;
    }
    internal static HidDevice ResolveReceiver(List<HidDevice> devices, string receiver, string path)
    {
        var matches = devices.Where(d => d.Receiver.Equals(receiver, StringComparison.OrdinalIgnoreCase) && d.UsagePage == 0xFF00 && d.Usage == 1 && d.OutputLength >= 7 && (string.IsNullOrWhiteSpace(path) || d.Path.Equals(path, StringComparison.OrdinalIgnoreCase))).ToList();
        return matches.Count == 1 ? matches[0] : throw new Exception(matches.Count == 0
            ? $"Receiver {receiver} not found on HID usage FF00:0001. Check the USB receiver and saved path."
            : $"Multiple {receiver} receivers found. Paste the desired device path from List HID devices into ReceiverPath.");
    }
    internal static SafeFileHandle OpenReceiver(HidDevice device)
    {
        var handle = CreateFileW(device.Path, 0xC0000000, 3, IntPtr.Zero, 3, 0x40000000, IntPtr.Zero);
        if (!handle.IsInvalid) return handle;
        var error = Error("Opening Logitech receiver"); handle.Dispose(); throw error;
    }
    internal static async Task<List<ChangeHostDiscovery>> DiscoverChangeHost(SafeFileHandle handle, HidDevice device)
    {
        using var stream = OpenAsyncStream(handle, Math.Max(device.InputLength, device.OutputLength));
        var results = new List<ChangeHostDiscovery>();
        for (byte slot = 1; slot <= 6; slot++)
        {
            // HID++ 2.0 IRoot.GetFeature(0x1814), SWID E. This only reads capabilities.
            byte[] request = [0x10, slot, 0x00, 0x0E, 0x18, 0x14, 0x00];
            byte[]? response = await Exchange(stream, device.InputLength, device.OutputLength, request, slot, 0x0E);
            if (response == null) results.Add(new(slot, null, "No HID++ reply (empty slot or legacy device)"));
            else if (response[0] != 0x10) results.Add(new(slot, null, $"HID++ error report 0x{response[0]:X2}"));
            else if (response.Length < 6) results.Add(new(slot, null, "Incomplete HID++ reply"));
            else if (response[4] == 0) results.Add(new(slot, null, "Change Host feature not supported"));
            else results.Add(new(slot, response[4], $"Change Host feature index 0x{response[4]:X2}"));
        }
        return results;
    }
    internal static async Task Send(SafeFileHandle handle, int length, byte[] report)
    {
        // HID++ short reports use their 7-byte report length even if the collection
        // also supports 20-byte long reports.
        using var stream = OpenAsyncStream(handle, report.Length);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await stream.WriteAsync(report, timeout.Token);
    }
    static FileStream OpenAsyncStream(SafeFileHandle handle, int bufferSize)
    {
        if (!DuplicateHandle(GetCurrentProcess(), handle, GetCurrentProcess(), out SafeFileHandle copy, 0, false, 2)) throw Error("Duplicating receiver handle");
        return new FileStream(copy, FileAccess.ReadWrite, Math.Max(1, bufferSize), true);
    }
    static async Task<byte[]?> Exchange(FileStream stream, int inputLength, int outputLength, byte[] request, byte slot, byte softwareId)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(550));
        byte[] output = request;
        try
        {
            byte[] buffer = new byte[Math.Max(inputLength, 7)];
            Task<int> read = stream.ReadAsync(buffer, timeout.Token).AsTask();
            await stream.WriteAsync(output, timeout.Token);
            while (true)
            {
                int count = await read;
                if (count >= 6 && buffer[1] == slot && (buffer[0] != 0x10 || buffer[3] == softwareId))
                    return buffer[..count];
                Array.Clear(buffer);
                read = stream.ReadAsync(buffer, timeout.Token).AsTask();
            }
        }
        catch (OperationCanceledException) { return null; }
        catch (IOException) when (timeout.IsCancellationRequested) { return null; }
    }
    static Win32Exception Error(string operation) => new(Marshal.GetLastWin32Error(), operation);

    [StructLayout(LayoutKind.Sequential)] struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] struct MonitorInfo { public int Size; public Rect Monitor, Work; public uint Flags; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Device; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] struct PhysicalMonitor { public IntPtr Handle; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] struct DisplayDevice
    {
        public int Size;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
        public uint StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceId;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
    }
    [StructLayout(LayoutKind.Sequential)] struct DeviceInterface { public int Size; public Guid ClassGuid; public uint Flags; public IntPtr Reserved; }
    [StructLayout(LayoutKind.Sequential)] struct HidAttributes { public int Size; public ushort Vendor, Product, Version; }
    [StructLayout(LayoutKind.Sequential)] struct HidCaps
    {
        public ushort Usage, UsagePage, InputLength, OutputLength, FeatureLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
        public ushort LinkNodes, InputButtonCaps, InputValueCaps, InputDataIndices, OutputButtonCaps, OutputValueCaps, OutputDataIndices, FeatureButtonCaps, FeatureValueCaps, FeatureDataIndices;
    }
    delegate bool MonitorCallback(IntPtr monitor, IntPtr dc, ref Rect rect, IntPtr data);
    [DllImport("user32.dll", SetLastError = true)] static extern bool EnumDisplayMonitors(IntPtr dc, IntPtr clip, MonitorCallback callback, IntPtr data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool GetMonitorInfoW(IntPtr monitor, ref MonitorInfo info);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool EnumDisplayDevicesW(string device, uint index, ref DisplayDevice display, uint flags);
    [DllImport("dxva2.dll", SetLastError = true)] static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr monitor, out uint count);
    [DllImport("dxva2.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr monitor, uint count, [Out] PhysicalMonitor[] physical);
    [DllImport("dxva2.dll", SetLastError = true)] static extern bool DestroyPhysicalMonitor(IntPtr monitor);
    [DllImport("dxva2.dll", SetLastError = true)] static extern bool SetVCPFeature(IntPtr monitor, byte code, uint value);
    [DllImport("dxva2.dll", SetLastError = true)] static extern bool GetVCPFeatureAndVCPFeatureReply(IntPtr monitor, byte code, out uint type, out uint current, out uint maximum);
    [DllImport("dxva2.dll", SetLastError = true)] static extern bool GetCapabilitiesStringLength(IntPtr monitor, out uint length);
    [DllImport("dxva2.dll", CharSet = CharSet.Ansi, ExactSpelling = true, SetLastError = true)] static extern bool CapabilitiesRequestAndCapabilitiesReply(IntPtr monitor, [Out] StringBuilder capabilities, uint length);
    [DllImport("hid.dll")] static extern void HidD_GetHidGuid(out Guid guid);
    [DllImport("hid.dll")] [return: MarshalAs(UnmanagedType.U1)] static extern bool HidD_GetAttributes(SafeFileHandle handle, ref HidAttributes attributes);
    [DllImport("hid.dll")] [return: MarshalAs(UnmanagedType.U1)] static extern bool HidD_GetPreparsedData(SafeFileHandle handle, out IntPtr data);
    [DllImport("hid.dll")] [return: MarshalAs(UnmanagedType.U1)] static extern bool HidD_FreePreparsedData(IntPtr data);
    [DllImport("hid.dll")] static extern int HidP_GetCaps(IntPtr data, out HidCaps caps);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern IntPtr SetupDiGetClassDevsW(ref Guid guid, string? enumerator, IntPtr parent, uint flags);
    [DllImport("setupapi.dll", SetLastError = true)] static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr device, ref Guid guid, uint index, ref DeviceInterface iface);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool SetupDiGetDeviceInterfaceDetailW(IntPtr set, ref DeviceInterface iface, IntPtr detail, uint size, out uint required, IntPtr device);
    [DllImport("setupapi.dll")] static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern SafeFileHandle CreateFileW(string path, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll")] static extern IntPtr GetCurrentProcess();
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool DuplicateHandle(IntPtr sourceProcess, SafeFileHandle source, IntPtr targetProcess, out SafeFileHandle target, uint access, bool inherit, uint options);
}
