using System.Runtime.InteropServices;
using System.Text;

namespace SoftwareDeviceSwitch;

public sealed record ProfileShortcut(uint Modifiers, int Key)
{
    public string DisplayName()
    {
        var parts = new List<string>();
        if ((Modifiers & 2) != 0) parts.Add("Ctrl");
        if ((Modifiers & 1) != 0) parts.Add("Alt");
        if ((Modifiers & 4) != 0) parts.Add("Shift");
        uint scan = MapVirtualKeyW((uint)Key, 4);
        int flags = (int)((scan & 0xFF) << 16);
        if ((scan & 0xFF00) == 0xE000) flags |= 1 << 24;
        var name = new StringBuilder(64);
        parts.Add(GetKeyNameTextW(flags, name, name.Capacity) > 0 ? name.ToString() : $"Key 0x{Key:X2}");
        return string.Join(" + ", parts);
    }

    internal bool IsValid(out string error)
    {
        error = "";
        if ((Modifiers & ~7u) != 0) error = "Windows-key shortcuts are reserved for Windows. Use Ctrl or Alt instead.";
        else if ((Modifiers & 3) == 0) error = "Include Ctrl or Alt with a regular key.";
        else if (Key is < 8 or > 254 or 16 or 17 or 18 or 91 or 92 or 160 or 161 or 162 or 163 or 164 or 165) error = "Press a regular key together with Ctrl or Alt.";
        else if (Key is 123 or 44 || Key == 46 && (Modifiers & 3) == 3 || Key == 9 || Key == 27 || Key == 115 && (Modifiers & 1) != 0 || Key == 32 && (Modifiers & 1) != 0)
            error = "This combination is reserved for Windows. Choose another shortcut.";
        return error.Length == 0;
    }

    internal bool IsPressed() => Pressed(Key) || (Modifiers & 2) != 0 && Pressed(17)
        || (Modifiers & 1) != 0 && Pressed(18) || (Modifiers & 4) != 0 && Pressed(16);
    static bool Pressed(int key) => (GetAsyncKeyState(key) & 0x8000) != 0;

    [DllImport("user32.dll", ExactSpelling = true)] static extern uint MapVirtualKeyW(uint code, uint mapType);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)] static extern int GetKeyNameTextW(int flags, StringBuilder text, int size);
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int key);
}

// A message-only window keeps registrations alive when the editor's taskbar handle is recreated.
internal sealed class GlobalShortcuts : NativeWindow, IDisposable
{
    sealed record Registration(int Id, string Profile, ProfileShortcut Shortcut);
    readonly Dictionary<string, Registration> saved = new(StringComparer.OrdinalIgnoreCase);
    Registration? draft;
    int nextId = 1;
    internal event Action<string>? Activated;

    internal GlobalShortcuts() => CreateHandle(new CreateParams { Caption = "Device Switch shortcuts", Parent = new IntPtr(-3) });

    internal bool IsRegistered(string profile, ProfileShortcut shortcut) => saved.TryGetValue(profile, out var entry) && entry.Shortcut == shortcut;

    internal bool TryReserve(string profile, ProfileShortcut? shortcut, out string error)
    {
        error = "";
        if (shortcut == null) { DiscardDraft(); return true; }
        if (!shortcut.IsValid(out error)) return false;
        var conflict = saved.Values.FirstOrDefault(r => r.Shortcut == shortcut && !r.Profile.Equals(profile, StringComparison.OrdinalIgnoreCase));
        if (conflict == null && draft?.Shortcut == shortcut && !draft.Profile.Equals(profile, StringComparison.OrdinalIgnoreCase)) conflict = draft;
        if (conflict != null) { error = $"Already assigned to profile '{conflict.Profile}'. Choose a different shortcut."; return false; }
        if (IsRegistered(profile, shortcut)) { DiscardDraft(); return true; }
        if (draft?.Profile.Equals(profile, StringComparison.OrdinalIgnoreCase) == true && draft.Shortcut == shortcut) return true;
        if (nextId > 0xBFFF) { error = "Restart the tool before assigning more shortcuts."; return false; }
        int id = nextId++;
        if (!RegisterHotKey(Handle, id, shortcut.Modifiers | 0x4000, (uint)shortcut.Key))
        {
            int code = Marshal.GetLastWin32Error();
            error = code == 1409 ? "This shortcut is already in use by Windows or another application. Choose another."
                : $"Windows could not register this shortcut (error {code}). Choose another.";
            return false;
        }
        DiscardDraft();
        draft = new(id, profile, shortcut);
        return true;
    }

    internal void Commit(string profile, ProfileShortcut? shortcut)
    {
        if (shortcut != null && IsRegistered(profile, shortcut)) { DiscardDraft(); return; }
        if (shortcut != null && (draft == null || draft.Shortcut != shortcut || !draft.Profile.Equals(profile, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("The shortcut must be reserved before saving.");
        if (saved.Remove(profile, out var old)) UnregisterHotKey(Handle, old.Id);
        if (shortcut != null) { saved[profile] = draft!; draft = null; }
        else DiscardDraft();
    }

    internal void DiscardDraft()
    {
        if (draft != null) { UnregisterHotKey(Handle, draft.Id); draft = null; }
    }

    internal void Remove(string profile)
    {
        if (saved.Remove(profile, out var entry)) UnregisterHotKey(Handle, entry.Id);
        if (draft?.Profile.Equals(profile, StringComparison.OrdinalIgnoreCase) == true) DiscardDraft();
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x0312)
        {
            int id = m.WParam.ToInt32();
            var entry = saved.Values.FirstOrDefault(r => r.Id == id);
            if (entry != null) Activated?.Invoke(entry.Profile);
            return;
        }
        base.WndProc(ref m);
    }

    public void Dispose()
    {
        DiscardDraft();
        foreach (var entry in saved.Values) UnregisterHotKey(Handle, entry.Id);
        saved.Clear();
        DestroyHandle();
    }

    [DllImport("user32.dll", SetLastError = true)] static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
    [DllImport("user32.dll", SetLastError = true)] static extern bool UnregisterHotKey(IntPtr window, int id);
}

// Only active while the recorder has focus. Intercepts combinations before other hotkeys fire.
internal sealed class ShortcutRecorder : TextBox
{
    delegate IntPtr KeyboardCallback(int code, IntPtr message, IntPtr data);
    readonly KeyboardCallback callback;
    readonly HashSet<uint> swallowed = [];
    IntPtr hook;
    internal event Action<ProfileShortcut>? Captured;
    internal event Action<string>? CaptureFailed;
    internal event Action? CaptureStarted;

    internal ShortcutRecorder()
    {
        ReadOnly = true;
        ShortcutsEnabled = false;
        BorderStyle = BorderStyle.None;
        Font = EditorTheme.Font(11);
        BackColor = Color.FromArgb(250, 251, 254);
        Dock = DockStyle.Fill;
        Cursor = Cursors.Hand;
        callback = HandleKeyboard;
    }

    protected override void OnEnter(EventArgs e)
    {
        base.OnEnter(e);
        hook = SetWindowsHookExW(13, callback, GetModuleHandleW(null), 0);
        if (hook == IntPtr.Zero) { CaptureFailed?.Invoke("Windows could not start shortcut recording. Try Record again."); return; }
        CaptureStarted?.Invoke();
    }

    IntPtr HandleKeyboard(int code, IntPtr message, IntPtr data)
    {
        if (code >= 0)
        {
            uint key = (uint)Marshal.ReadInt32(data);
            int type = message.ToInt32();
            if (type is 0x101 or 0x105 && swallowed.Remove(key)) return new IntPtr(1);
            if (type is 0x100 or 0x104 && !IsModifier(key))
            {
                uint modifiers = (Pressed(17) ? 2u : 0) | (Pressed(18) ? 1u : 0) | (Pressed(16) ? 4u : 0) | (Pressed(91) || Pressed(92) ? 8u : 0);
                if (key == 9 && modifiers == 0) return CallNextHookEx(hook, code, message, data);
                if (!swallowed.Add(key)) return new IntPtr(1);
                if (key == 27 && modifiers == 0)
                {
                    BeginInvoke(new Action(() => FindForm()?.SelectNextControl(this, true, true, true, true)));
                    return new IntPtr(1);
                }
                var shortcut = new ProfileShortcut(modifiers, (int)key);
                BeginInvoke(new Action(() => { if (!IsDisposed && ContainsFocus) Captured?.Invoke(shortcut); }));
                return new IntPtr(1);
            }
        }
        return CallNextHookEx(hook, code, message, data);
    }

    static bool IsModifier(uint key) => key is 16 or 17 or 18 or 91 or 92 or 160 or 161 or 162 or 163 or 164 or 165;
    static bool Pressed(int key) => (GetAsyncKeyState(key) & 0x8000) != 0;
    void StopRecording() { if (hook != IntPtr.Zero) { UnhookWindowsHookEx(hook); hook = IntPtr.Zero; } swallowed.Clear(); }
    protected override void OnLeave(EventArgs e) { StopRecording(); base.OnLeave(e); }
    protected override void OnVisibleChanged(EventArgs e) { if (!Visible) StopRecording(); base.OnVisibleChanged(e); }
    protected override void Dispose(bool disposing) { StopRecording(); base.Dispose(disposing); }

    [DllImport("user32.dll", SetLastError = true, ExactSpelling = true)] static extern IntPtr SetWindowsHookExW(int id, KeyboardCallback callback, IntPtr module, uint threadId);
    [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int key);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)] static extern IntPtr GetModuleHandleW(string? module);
}
