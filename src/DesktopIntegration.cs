using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace SoftwareDeviceSwitch;

internal static class WindowsStartup
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string ValueName = "SoftwareDeviceSwitch";

    internal static string Command(string executable)
    {
        string command = $"\"{executable}\" --tray";
        if (command.Length > 260) throw new InvalidOperationException("Move the executable to a shorter folder path before enabling startup.");
        return command;
    }

    internal static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return string.Equals(key?.GetValue(ValueName) as string, Command(Executable()), StringComparison.OrdinalIgnoreCase);
    }

    internal static void SetEnabled(bool enabled)
    {
        if (enabled)
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey, true);
            key.SetValue(ValueName, Command(Executable()), RegistryValueKind.String);
        }
        else
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, true);
            key?.DeleteValue(ValueName, false);
        }
    }

    static string Executable() => Environment.ProcessPath ?? throw new InvalidOperationException("Windows could not locate the executable.");
}

// Each installed copy has one editor/tray instance. Profile command-line runs stay independent.
internal sealed class DesktopInstance : IDisposable
{
    readonly Mutex mutex;
    readonly EventWaitHandle showRequested;
    RegisteredWaitHandle? listener;
    bool ownsMutex;

    internal DesktopInstance()
    {
        string path = Path.GetFullPath(Environment.ProcessPath ?? AppPaths.ExecutableDirectory).ToUpperInvariant();
        string key = "Local\\SoftwareDeviceSwitch-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path)))[..24];
        mutex = new Mutex(false, key);
        showRequested = new EventWaitHandle(false, EventResetMode.AutoReset, key + "-Show");
    }

    internal bool TryEnter()
    {
        try { ownsMutex = mutex.WaitOne(0); }
        catch (AbandonedMutexException) { ownsMutex = true; }
        return ownsMutex;
    }

    internal void ShowExisting() => showRequested.Set();

    internal void Listen(SwitchWindow window)
    {
        _ = window.Handle;
        listener = ThreadPool.RegisterWaitForSingleObject(showRequested, (_, _) =>
        {
            try
            {
                if (!window.IsDisposed && window.IsHandleCreated)
                    window.BeginInvoke(new Action(window.ShowFromTray));
            }
            catch (InvalidOperationException) { } // The editor may be closing as a request arrives.
        }, null, Timeout.Infinite, false);
    }

    public void Dispose()
    {
        listener?.Unregister(null);
        showRequested.Dispose();
        if (ownsMutex) mutex.ReleaseMutex();
        mutex.Dispose();
    }
}
