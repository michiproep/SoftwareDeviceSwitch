# Device Switch

A standalone Windows desktop app for switching Logitech devices and monitor inputs. It uses the Windows inbox DDC/CI, HID and SetupAPI APIs; it does not launch or bundle helper executables.

## Run

Build with `dotnet publish -c Release`. The single `SoftwareDeviceSwitch.exe` in `publish` uses the installed .NET 10 Desktop Runtime. Copy that file to the other Windows computer.

Choose a profile from the left menu, then edit the destination computer name, Logitech channel, monitor identifiers and destination inputs, and receiver device settings in the main view. The computer name labels the destination in confirmations and status messages; the profile name remains its menu entry and command-line name. Use **Find monitors** to list stable monitor Plug and Play IDs; these do not depend on Windows DISPLAY numbers. To identify monitor input values, select each input with the monitor's physical controls, then enter the corresponding value. Select a monitor row and use **Test selected monitor** to try it by itself.

Use **Discover devices** to probe receiver slots and ask responding devices for their runtime feature index for Change Host (`0x1814`). Compatible devices are added or updated in the table. The function byte is set to the standard function 1 / software ID E (`1E`). Use **List HID devices** to inspect receiver paths if multiple matching interfaces exist. Select a row and use **Test selected device** to switch only that device. Click **Save profile** to store the settings, or **Test and switch** to save and send all enabled commands. Add and delete profiles with the buttons under the left menu.

Enable DDC/CI in each monitor's menu. Devices must already be paired to the destination channel. This version uses the USB receiver report format present in the scripts; Bluetooth and pairing workflows are not included.

Named profiles are stored in `profile.json` beside the executable. The published file includes `NIQ` and `IMP`, seeded from the old PC and Laptop scripts respectively. Launch without an argument for the single-view profile editor: select a profile on the left, edit all settings on the right, then Save or Test. Add and Delete are below the profile list. `ActiveProfile` selects which profile opens first. The app migrates profiles created by previous versions on first launch.

To run a profile without opening the editor, call `SoftwareDeviceSwitch.exe -NIQ` (replace `NIQ` with its profile name). Names are case-insensitive. The process returns 0 when all commands are sent, 1 when execution fails, and 2 when the profile is not found. Run details are appended to `run.log` beside the executable.

Switching preflights every requested monitor and receiver before making changes. Monitor inputs are sent first, then Logitech device reports. A failure partway through can leave a partial switch, so keep physical input/channel controls available. Windows API success only confirms the command was accepted; check the destination computer.
