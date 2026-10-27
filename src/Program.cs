using System.Text.Json;
using System.Text.RegularExpressions;

namespace SoftwareDeviceSwitch;

static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        if (args.Contains("--self-test"))
        {
            foreach (bool pc in new[] { false, true })
            {
                var p = Profile.Legacy(pc);
                for (int i = 0; i < p.Monitors.Count; i++) p.Monitors[i].Selector = $"MONITOR\\SELFTEST\\{i + 1}";
                var plan = Switching.Plan(p);
                if (plan.Count != 4 || plan[0].Arguments[3] != (pc ? "17" : "15") || plan[1].Arguments[3] != (pc ? "16" : "15") || plan[2].Arguments[^1] != $"0x10,0x01,0x09,0x1E,0x{(pc ? 0 : 1):X2},0x00,0x00" || plan[3].Arguments[^1] != $"0x10,0x02,0x08,0x1B,0x{(pc ? 0 : 1):X2},0x00,0x00") throw new Exception("Legacy command regression");
                var copy = JsonSerializer.Deserialize<Profile>(JsonSerializer.Serialize(p))!;
                if (!Switching.Plan(copy).Select(c => c.ToString()).SequenceEqual(plan.Select(c => c.ToString()))) throw new Exception("Profile round-trip failed");
                p.Channel = 4;
                try { Switching.Plan(p); throw new InvalidOperationException("Invalid channel accepted"); } catch (Exception e) when (e is not InvalidOperationException) { }
            }
            return 0;
        }
        ApplicationConfiguration.Initialize();
        bool startInTray = args.Length == 1 && args[0].Equals("--tray", StringComparison.OrdinalIgnoreCase);
        if (args.Length == 0 || startInTray)
        {
            using var instance = new DesktopInstance();
            if (!instance.TryEnter())
            {
                if (!startInTray) instance.ShowExisting();
                return 0;
            }
            using var window = new SwitchWindow(ProfileLibrary.Load(), startInTray);
            instance.Listen(window);
            Application.Run(window);
            return 0;
        }
        var library = ProfileLibrary.Load();
        if (args.Length == 1 && args[0].Equals("--list-profiles", StringComparison.OrdinalIgnoreCase))
        {
            TryWriteRunLog($"Profile file: {library.ConfigPath}; count: {library.Profiles.Count}; names: {string.Join(", ", library.Profiles.Keys.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))}");
            return 0;
        }
        if (args.Length > 0)
        {
            string name = args.Length == 1 ? args[0].TrimStart('-') : "";
            if (string.IsNullOrWhiteSpace(name) || !library.Profiles.TryGetValue(name, out var selected))
            {
                TryWriteRunLog($"ERROR: Profile '{(name.Length == 0 ? string.Join(" ", args) : name)}' was not found.");
                return 2;
            }
            try
            {
                var plan = Switching.Plan(selected);
                Switching.RunAll(plan, message => TryWriteRunLog(message)).GetAwaiter().GetResult();
                TryWriteRunLog($"Done: {name}");
                return 0;
            }
            catch (Exception e)
            {
                TryWriteRunLog($"ERROR: {e.Message}");
                return 1;
            }
        }
        return 0;
    }
    static void TryWriteRunLog(string message)
    {
        try
        {
            string path = Path.Combine(AppPaths.ExecutableDirectory, "run.log");
            File.AppendAllText(path, $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz} {message}{Environment.NewLine}");
        }
        catch { }
    }
}

public static class AppPaths
{
    public static string ExecutableDirectory => Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;
}

public sealed class ProfileLibrary
{
    public string ActiveProfile { get; set; } = "Default";
    public Dictionary<string, Profile> Profiles { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    [System.Text.Json.Serialization.JsonIgnore]
    public string ConfigPath { get; private set; } = "";
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    public static ProfileLibrary Load()
    {
        string path = Path.Combine(AppPaths.ExecutableDirectory, "profile.json");
        string pluralPath = Path.Combine(AppPaths.ExecutableDirectory, "profiles.json");
        if (File.Exists(path))
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            if (TryGetPropertyIgnoreCase(document.RootElement, "Profiles", out _))
            {
                var library = JsonSerializer.Deserialize<ProfileLibrary>(document.RootElement.GetRawText(), Json) ?? new();
                library.Profiles = new(library.Profiles ?? [], StringComparer.OrdinalIgnoreCase);
                library.ConfigPath = path;
                // Older builds used profiles.json. If an empty profile.json was generated
                // before that file was found, keep the user's populated library visible.
                if (library.Profiles.Count == 0 && File.Exists(pluralPath))
                {
                    var olderLibrary = ReadLibrary(pluralPath);
                    if (olderLibrary.Profiles.Count > 0)
                    {
                        olderLibrary.Persist();
                        return olderLibrary;
                    }
                }
                return library;
            }
            var legacy = JsonSerializer.Deserialize<Profile>(document.RootElement.GetRawText(), Json);
            var migratedLocal = new ProfileLibrary();
            if (legacy != null) migratedLocal.Profiles["Default"] = legacy;
            migratedLocal.Persist();
            return migratedLocal;
        }

        if (File.Exists(pluralPath))
        {
            var library = ReadLibrary(pluralPath);
            library.Persist();
            return library;
        }

        // Migrate profiles created by earlier builds from LocalAppData on first launch.
        string oldRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SoftwareDeviceSwitch");
        string oldLibraryPath = Path.Combine(oldRoot, "profiles.json");
        if (File.Exists(oldLibraryPath))
        {
            var library = JsonSerializer.Deserialize<ProfileLibrary>(File.ReadAllText(oldLibraryPath), Json) ?? new();
            library.Profiles = new(library.Profiles ?? [], StringComparer.OrdinalIgnoreCase);
            library.Persist();
            return library;
        }
        var migrated = new ProfileLibrary();
        string oldProfilePath = Path.Combine(oldRoot, "profile.json");
        if (File.Exists(oldProfilePath))
        {
            var old = JsonSerializer.Deserialize<Profile>(File.ReadAllText(oldProfilePath), Json);
            if (old != null) migrated.Profiles["Default"] = old;
        }
        migrated.Persist();
        return migrated;
    }
    static ProfileLibrary ReadLibrary(string path)
    {
        var library = JsonSerializer.Deserialize<ProfileLibrary>(File.ReadAllText(path), Json) ?? new();
        library.Profiles = new(library.Profiles ?? [], StringComparer.OrdinalIgnoreCase);
        library.ConfigPath = path;
        return library;
    }
    static bool TryGetPropertyIgnoreCase(JsonElement element, string name, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
            if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        value = default;
        return false;
    }
    public void Save(string name, Profile profile)
    {
        bool existed = Profiles.TryGetValue(name, out var previous);
        string previousActive = ActiveProfile;
        Profiles[name] = profile;
        ActiveProfile = name;
        try { Persist(); }
        catch
        {
            if (existed) Profiles[name] = previous!;
            else Profiles.Remove(name);
            ActiveProfile = previousActive;
            throw;
        }
    }
    public void Activate(string name)
    {
        ActiveProfile = name;
        Persist();
    }
    public void Delete(string name)
    {
        bool existed = Profiles.TryGetValue(name, out var previous);
        string previousActive = ActiveProfile;
        Profiles.Remove(name);
        ActiveProfile = Profiles.Keys.FirstOrDefault() ?? "";
        try { Persist(); }
        catch
        {
            if (existed) Profiles[name] = previous!;
            ActiveProfile = previousActive;
            throw;
        }
    }
    void Persist()
    {
        string path = Path.Combine(AppPaths.ExecutableDirectory, "profile.json");
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(this, Json));
        File.Move(path + ".tmp", path, true);
        ConfigPath = path;
    }
}

public sealed class Profile
{
    public string Target { get; set; } = "Laptop";
    public int Channel { get; set; } = 2;
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public ProfileShortcut? Shortcut { get; set; }
    public List<MonitorSetting> Monitors { get; set; } = [];
    public List<DeviceSetting> Devices { get; set; } = [];
    public static Profile Legacy(bool toPc) => new()
    {
        Target = toPc ? "PC" : "Laptop", Channel = toPc ? 1 : 2,
        Monitors = [new() { Selector = "", Input = toPc ? 17 : 15 }, new() { Selector = "", Input = toPc ? 16 : 15 }],
        Devices = [new() { Name = "MX Master 2S", Slot = 1, Feature = "09", Function = "1E" }, new() { Name = "K850", Slot = 2, Feature = "08", Function = "1B" }]
    };
}
public sealed class MonitorSetting
{
    public bool Enabled { get; set; } = true;
    public string Selector { get; set; } = "";
    public int Input { get; set; } = 15;
}
public sealed class DeviceSetting
{
    public bool Enabled { get; set; } = true;
    public string Name { get; set; } = "";
    public string Receiver { get; set; } = "046D:C52B";
    public string ReceiverPath { get; set; } = "";
    public int Slot { get; set; } = 1;
    public string Feature { get; set; } = "09";
    public string Function { get; set; } = "1E";
}
public record Command(string File, string[] Arguments)
{
    public string ReceiverPath { get; init; } = "";
    public string DeviceName { get; init; } = "";
    public override string ToString()
    {
        if (File == "DDC/CI") return $"MONITOR: {Arguments[1]}\r\n  Input source (VCP 0x60): {Arguments[3]}";
        var bytes = Arguments[^1].Split(',').Select(b => b.Replace("0x", "", StringComparison.OrdinalIgnoreCase));
        return $"DEVICE: {DeviceName}\r\n  USB receiver: {Arguments[1]}\r\n  Destination channel: {Convert.ToInt32(Arguments[^1].Split(',')[4][2..], 16) + 1}\r\n  HID report bytes: {string.Join(" ", bytes)}" +
            (string.IsNullOrWhiteSpace(ReceiverPath) ? "" : $"\r\n  Receiver path: {ReceiverPath}");
    }
}
public static class Switching
{
    public static List<Command> Plan(Profile p)
    {
        if (string.IsNullOrWhiteSpace(p.Target)) throw new Exception("Enter the other computer's name.");
        if (p.Channel is < 1 or > 3) throw new Exception("Choose Logitech channel 1, 2 or 3.");
        var result = new List<Command>();
        foreach (var m in p.Monitors.Where(m => m.Enabled))
        {
            if (string.IsNullOrWhiteSpace(m.Selector) || m.Input is < 1 or > 65535) throw new Exception("Each monitor needs an identifier and an input value between 1 and 65535.");
            if (m.Selector.StartsWith(@"\\.\DISPLAY", StringComparison.OrdinalIgnoreCase)) throw new Exception("This profile uses a Windows DISPLAY number. Click Find monitors and select the matching Plug and Play ID first.");
            result.Add(new("DDC/CI", ["/SetValue", m.Selector, "60", m.Input.ToString()]));
        }
        foreach (var d in p.Devices.Where(d => d.Enabled))
        {
            if (!Regex.IsMatch(d.Receiver ?? "", "^[0-9A-Fa-f]{4}:[0-9A-Fa-f]{4}$") || d.Slot is < 1 or > 6 || !Regex.IsMatch(d.Feature ?? "", "^[0-9A-Fa-f]{2}$") || !Regex.IsMatch(d.Function ?? "", "^[0-9A-Fa-f]{2}$"))
                throw new Exception("Device settings need receiver VVVV:PPPP, slot 1-6, and two hex digits for feature and function.");
            result.Add(new Command("HID", ["--vidpid", d.Receiver!, "--send-output", $"0x10,0x{d.Slot:X2},0x{d.Feature},0x{d.Function},0x{p.Channel - 1:X2},0x00,0x00"]) { ReceiverPath = d.ReceiverPath ?? "", DeviceName = d.Name });
        }
        if (result.Count == 0) throw new Exception("Enable at least one monitor or device.");
        return result;
    }
    public static async Task RunAll(List<Command> commands, Action<string> log)
    {
        var monitors = commands.Any(c => c.File == "DDC/CI") ? NativeHardware.OpenMonitors() : [];
        var handles = new List<Microsoft.Win32.SafeHandles.SafeFileHandle>();
        try
        {
            var hid = commands.Any(c => c.File == "HID") ? NativeHardware.ListHid() : [];
            var operations = new List<Func<Task>>();
            foreach (var c in commands)
            {
                if (c.File == "DDC/CI")
                {
                    var matches = monitors.Where(m => m.Selector.Equals(c.Arguments[1], StringComparison.OrdinalIgnoreCase)).ToList();
                    if (matches.Count != 1) throw new Exception($"Monitor {c.Arguments[1]} not found; use List monitors to refresh its identifier.");
                    var monitor = matches[0]; uint input = uint.Parse(c.Arguments[3]);
                    operations.Add(() => { log(c.ToString()); NativeHardware.SetInput(monitor, input); return Task.CompletedTask; });
                }
                else
                {
                    var device = NativeHardware.ResolveReceiver(hid, c.Arguments[1], c.ReceiverPath);
                    var handle = NativeHardware.OpenReceiver(device); handles.Add(handle);
                    byte[] report = c.Arguments[^1].Split(',').Select(b => Convert.ToByte(b[2..], 16)).ToArray();
                    operations.Add(async () => { log(c.ToString()); await NativeHardware.Send(handle, device.OutputLength, report); });
                }
            }
            foreach (var operation in operations) await operation();
        }
        finally { foreach (var h in handles) h.Dispose(); foreach (var m in monitors) m.Dispose(); }
    }
}


