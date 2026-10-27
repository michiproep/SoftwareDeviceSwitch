# Device Switch - quick start

1. Install the .NET 10 Desktop Runtime for Windows x64: https://dotnet.microsoft.com/download/dotnet/10.0
2. Extract the ZIP to a writable folder and run `SoftwareDeviceSwitch.exe`. Keep `profile.json` beside it. When updating, retain your existing profile file.
3. Add a profile for the destination computer and choose its Logitech Easy-Switch channel. Devices must already be paired to that channel.
4. Enable DDC/CI in each monitor's menu. Use Find monitors, choose the appropriate connections and destination inputs, and open Input guide for help identifying ports.
5. Use Discover devices to find Logitech receiver settings. Test individual rows before testing the full profile, then save.

The Keyboard shortcut card records an optional global shortcut per profile. Save to activate it. Shortcut conflict checks cover Windows global registrations, not every application's local shortcuts.

Closing or minimizing hides the editor in the system tray. Double-click its icon to reopen it; use Exit to quit. The tray menu can run saved profiles. Start with Windows launches it hidden at sign-in.

To execute a profile directly, use `SoftwareDeviceSwitch.exe -ProfileName`. These command-line calls execute and close. `--tray` starts hidden; `--list-profiles` writes profile names and the configuration path to `run.log` without switching anything.

DDC/CI availability depends on the monitor and connection. A reported input may have no active signal. Physical input and Logitech channel buttons can restore access after a partial switch. This release supports USB receivers; Bluetooth switching and pairing are not included.
