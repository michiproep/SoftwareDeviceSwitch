using System.Text.Json;
using SoftwareDeviceSwitch;

int passed = 0;
void Check(string name, Action action)
{
    action();
    Console.WriteLine($"PASS: {name}");
    passed++;
}
void ExpectFailure(Action action)
{
    try { action(); }
    catch (Exception) { return; }
    throw new InvalidOperationException("Expected validation to reject the value.");
}

var capabilityCases = new (string Name, string Text, int[] Expected)[]
{
    ("hex input codes", "(model(example)vcp(10 12 60(0F 10 11 12) D6(01 04)))", [15, 16, 17, 18]),
    ("case and whitespace", "(VCP (60 (0f\t11\r\n12) 10))", [15, 17, 18]),
    ("nested feature values", "(vcp(14(01 60(11 12)) 60(0F 10)))", [15, 16]),
    ("missing input source", "(model(60(0F))vcp(10 12 14(0F 11)))", []),
    ("input source without values", "(vcp(60 D6(01 04)))", []),
    ("truncated value list", "vcp(60(0F 11", []),
    ("truncated feature list", "vcp(60(0F 11)", []),
    ("malformed nested inputs", "vcp(60(0F(11) 12))", []),
    ("duplicate values", "vcp(60(0F 11 11) 60(11 12))", [15, 17, 18]),
    ("invalid and vendor values", "vcp(60(00 10000 nope 11 1B))", [17, 27]),
    ("empty capabilities", "", [])
};
foreach (var item in capabilityCases)
    Check(item.Name, () =>
    {
        if (!MonitorInputs.ParseCapabilities(item.Text).SequenceEqual(item.Expected)) throw new Exception(item.Name);
    });
Check("input names and valid option filtering", () =>
{
    if (MonitorInputs.Label(15) != "DisplayPort 1 (15)" || MonitorInputs.Label(17) != "HDMI 1 (17)" || MonitorInputs.Name(27) != "Custom input") throw new Exception("Incorrect standard name.");
    if (!MonitorInputs.Options([17, 15, 17, 27, 0, 65536]).Select(o => o.Code).SequenceEqual([15, 17, 27])) throw new Exception("Incorrect option filtering.");
});
foreach (bool toPc in new[] { false, true })
{
    Check($"command planning and JSON roundtrip: {toPc}", () =>
    {
        var profile = Profile.Legacy(toPc);
        for (int i = 0; i < profile.Monitors.Count; i++) profile.Monitors[i].Selector = $"MONITOR\\CHECKS\\{i + 1}";
        var plan = Switching.Plan(profile);
        if (plan.Count != 4 || plan[0].Arguments[3] != (toPc ? "17" : "15") || plan[1].Arguments[3] != (toPc ? "16" : "15")) throw new Exception("Monitor input mismatch.");
        if (plan[2].Arguments[^1] != $"0x10,0x01,0x09,0x1E,0x{(toPc ? 0 : 1):X2},0x00,0x00" || plan[3].Arguments[^1] != $"0x10,0x02,0x08,0x1B,0x{(toPc ? 0 : 1):X2},0x00,0x00") throw new Exception("Receiver report mismatch.");
        profile.Shortcut = new ProfileShortcut(3, 226);
        var copy = JsonSerializer.Deserialize<Profile>(JsonSerializer.Serialize(profile))!;
        if (copy.Shortcut != profile.Shortcut || !Switching.Plan(copy).Select(c => c.ToString()).SequenceEqual(plan.Select(c => c.ToString()))) throw new Exception("JSON changed the commands or shortcut.");
        profile.Channel = 4;
        ExpectFailure(() => Switching.Plan(profile));
    });
}
Check("invalid monitor and device settings", () =>
{
    ExpectFailure(() => Switching.Plan(new Profile()));
    ExpectFailure(() => Switching.Plan(new Profile { Monitors = [new() { Selector = @"\\.\DISPLAY1\Monitor0", Input = 17 }] }));
    ExpectFailure(() => Switching.Plan(new Profile { Monitors = [new() { Selector = "MONITOR\\CHECKS", Input = 0 }] }));
    ExpectFailure(() => Switching.Plan(new Profile { Devices = [new() { Receiver = "bad-id" }] }));
    ExpectFailure(() => Switching.Plan(new Profile { Devices = [new() { Slot = 7 }] }));
    ExpectFailure(() => Switching.Plan(new Profile { Devices = [new() { Feature = "XX" }] }));
});
Check("shortcut validation and legacy profiles", () =>
{
    if (!new ProfileShortcut(3, 226).IsValid(out _)) throw new Exception("German < key is invalid.");
    foreach (var invalid in new[] { new ProfileShortcut(0, 65), new ProfileShortcut(8, 65), new ProfileShortcut(3, 46), new ProfileShortcut(3, 123), new ProfileShortcut(3, 17) })
        if (invalid.IsValid(out _)) throw new Exception("Reserved shortcut accepted.");
    if (JsonSerializer.Deserialize<Profile>("{\"Target\":\"Legacy\",\"Channel\":1}")!.Shortcut != null) throw new Exception("Legacy profile compatibility failed.");
});
Check("quoted startup command", () =>
{
    if (WindowsStartup.Command(@"C:\Apps With Spaces\DeviceSwitch.exe") != "\"C:\\Apps With Spaces\\DeviceSwitch.exe\" --tray") throw new Exception("Startup path is not quoted.");
    ExpectFailure(() => WindowsStartup.Command("C:\\" + new string('x', 260) + ".exe"));
});
Console.WriteLine($"{passed} checks passed. No monitor, HID, hotkey registration, UI or registry changes performed.");
