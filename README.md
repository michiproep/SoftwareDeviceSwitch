# Device Switch

A Windows desktop tool for switching monitor inputs and Logitech mouse/keyboard channels between computers. It uses Windows DDC/CI, HID and SetupAPI directly, without external switching tools or third-party drivers.

## Repository layout

```text
SoftwareDeviceSwitch.slnx       Solution for building from the repository root
.github/workflows/             Windows CI and tag-triggered releases
src/
  SoftwareDeviceSwitch.csproj  Windows Forms application
  *.cs                        Application code, UI and Windows integrations
tests/                        Checks that do not access hardware
scripts/                      Release packaging and GitHub publishing helpers
config/profile.example.json   Empty starter configuration
docs/                         Quick-start guide and release notes
publish/
  profile.json                Personal local profiles (ignored)
  SoftwareDeviceSwitch.exe    Generated single-file application
artifacts/                    Generated release assets (ignored)
old/                          Original scripts and utilities, for reference
```

Build output goes into the projects' `bin/` and `obj/` directories. Build folders, legacy utilities, personal profiles, logs and generated release assets are ignored by Git. The source tree includes an empty configuration example rather than machine-specific settings.

## Build and publish

Building requires Windows x64 and the .NET 10 SDK. Run these commands from the repository root:

```powershell
dotnet build -c Release
dotnet run --project tests/SoftwareDeviceSwitch.Checks -c Release --no-build
dotnet publish src/SoftwareDeviceSwitch.csproj -c Release -o publish
```

The published application is a single, framework-dependent executable. Each computer needs the **.NET 10 Desktop Runtime** installed. Copy `SoftwareDeviceSwitch.exe` and your `profile.json` together to a writable folder. For a fresh setup, use `config/profile.example.json` as `profile.json`, or let the app create its configuration on first launch. Keep your existing profile file when updating.

Open `publish/SoftwareDeviceSwitch.exe` and add profiles for your computers. The checks cover input-capability parsing, command planning, profile serialization, shortcut validation and startup command formatting; they do not send monitor or receiver commands or change Windows startup settings.

## Configure a profile

Choose a profile in the left sidebar, or use **Add** and **Delete** below the list. All settings are in one scrollable view, with **Save profile** and **Test and switch** fixed at the bottom. Unsaved changes are indicated and the app asks about them when changing profiles or exiting.

**Destination computer** is a label used in confirmations. It does not establish a network connection. **Easy-Switch channel** is the Logitech slot paired with the destination computer, normally 1, 2 or 3. Devices must already be paired to that slot. This version supports the USB receiver report format used by the original scripts; it does not implement Bluetooth switching or pairing.

### Monitors

Enable DDC/CI in each monitor's physical menu, then click **Find monitors**. Choose each screen by its saved connection. The tool uses stable Plug and Play identifiers instead of Windows DISPLAY numbers; hover over the Monitor column to see the full identifier. Discover and select the appropriate connections on each computer.

**Destination input** shows names such as **DisplayPort 1 (15)** and **HDMI 1 (17)**. Discovery reads the monitor's current input and asks it which input values it supports. The dropdown uses reported values when available, retaining any saved or current code missing from the report. A listed port can still have no cable or active signal.

Select a screen and open **Input guide** to distinguish reported inputs from unconfirmed standard suggestions. **Read inputs** refreshes that screen's information without switching it. Manufacturers sometimes use different codes, especially for USB-C. To identify a custom port, select it with the monitor's physical controls and read its current code while it remains reachable. The guide also accepts a manual decimal code. **Use input** updates the editor; **Test selected** sends a switch command for that screen after confirmation.

### Mouse and keyboard

Use **Discover devices** to find receiver slots and the runtime Change Host feature index (`0x1814`). Compatible devices are added or updated in the table. Discovery uses function 1 / software ID E (`1E`); existing configured function bytes are preserved for devices already in the table.

**Advanced fields** exposes feature, function and receiver-path settings. Leave **Receiver path** empty when the USB receiver ID identifies one interface. If multiple interfaces match, **Receiver details** lists their paths, and discovery can save a specific path automatically. The receiver slot identifies a device inside the receiver; the Easy-Switch channel identifies the destination pairing.

Use **Test selected** in the device card to switch just that row. **Activity** shows discovery results, profile location and command/error details.

### Keyboard shortcuts

The **Keyboard shortcut** card sits between destination settings and Monitors. Click **Record**, then press a combination such as **Ctrl + Alt + <** (**Strg + Alt + <** on a German keyboard). The actual key code is captured, so `<` remains distinct from comma. Include Ctrl or Alt; Shift is optional. Press Tab or Esc to finish recording, then **Save profile** to activate it. **Clear**, followed by Save, removes the shortcut.

The app checks for conflicts with other profiles and existing Windows global registrations. An accepted edit is reserved until saved or discarded, while the previous saved shortcut stays active until the replacement is saved. Common reserved Windows combinations, Windows-key combinations, Print Screen and F12 are rejected. Windows cannot enumerate shortcuts handled only inside another application's windows.

Saved shortcuts execute the saved profile immediately while Device Switch is running, including from the tray. The tool waits for the keys to be released before transferring the keyboard. Shortcuts are suspended during recording, another device operation or a modal dialog. If another app claims a shortcut while this tool is stopped, the next launch reports it as unavailable.

## System tray and startup

Closing or minimizing the editor hides it in the Windows notification area and keeps unsaved edits in memory. Double-click the icon, choose **Open Device Switch**, or launch the executable again to restore the same editor. **Exit** in the tray menu fully closes it and releases its shortcuts, asking about unsaved changes first. Windows shutdown and sign-out can close it normally.

The tray menu's **Switch to** submenu runs a saved profile immediately, without another confirmation. Unsaved editor changes are not used. A failed switch restores the editor with error details. Switching and Exit are disabled during an active device operation.

**Start with Windows** is available in both the sidebar and tray menu. It applies immediately and starts the app hidden at sign-in for the current Windows account. It is off by default and uses a per-user Windows Run entry, without administrator access or scheduled tasks. Unchecking removes the entry. If you move the executable, enable startup again from its new location.

## Profiles and command line

Profiles and their shortcuts are stored in `profile.json` beside the executable. `ActiveProfile` controls which editor profile opens first. Earlier configurations can be migrated on first launch. Monitor input values remain decimal numbers in the file.

```powershell
# Open the editor, or restore its running instance
.\publish\SoftwareDeviceSwitch.exe

# Start hidden in the tray
.\publish\SoftwareDeviceSwitch.exe --tray

# Execute a saved profile and close this command-line instance
.\publish\SoftwareDeviceSwitch.exe -NIQ
```

Profile names are case-insensitive. Command-line profile calls execute independently of the resident tray instance and return **0** when commands are sent, **1** on execution failure, or **2** when the profile is not found. Details are appended to `run.log` beside the executable. `--list-profiles` writes the loaded configuration path and profile names to that log without switching devices.

Switching checks that the requested monitors and receivers are available, sends monitor commands first, then sends Logitech reports. A failure during execution can leave a partial switch. Windows API success means commands were accepted; it does not confirm that the destination computer has a video signal. Physical monitor input and device channel controls remain useful for recovery.

## GitHub workflows and releases

**Build and check** runs on pushes to `main`/`master`, pull requests, and manual dispatch. It restores and builds the solution with .NET 10 on Windows, runs the hardware-free checks, then creates a downloadable portable artifact.

**Release** runs when a tag such as `v0.1` or `v0.1.1` is pushed. It repeats the build and checks, then publishes the executable, a portable ZIP containing an empty starter profile and quick-start guide, and `SHA256SUMS.txt`. Tags with two numbers are normalized to three-part application versions (`v0.1` becomes `0.1.0`). The release is made public only after assets are uploaded. A failed draft upload can be retried with the workflow's manual tag input; published releases are not overwritten.

To create the first public repository and push the current code and release tag, use an authenticated GitHub CLI and a terminal with Git writes, network access, and permission to run the repository's PowerShell scripts:

```powershell
gh auth login
powershell -NoProfile -File scripts/Publish-GitHub.ps1 -Tag v0.1
```

The helper defaults to `SoftwareDeviceSwitch` in the authenticated user's account. It validates the build and checks, keeps the personal `publish/profile.json` file on disk while removing it from the Git index, commits the current source, creates a public repository, pushes `main`, and pushes the tag to trigger Release. It rejects a different existing origin and does not force-push. Use `-Owner` or `-RepositoryName` to choose another destination.

For a later release after committing changes, push a new version tag:

```powershell
git tag -a v0.1.1 -m "Device Switch v0.1.1"
git push origin v0.1.1
```

Local packaging is available with `scripts/Build-Release.ps1 -Version 0.1.0`. It writes only under `artifacts/` and always uses the empty configuration example; it never reads your live profiles into a release.

## API references

- [Windows monitor capabilities](https://learn.microsoft.com/en-us/windows/win32/api/lowlevelmonitorconfigurationapi/nf-lowlevelmonitorconfigurationapi-capabilitiesrequestandcapabilitiesreply)
- [Standard monitor input-source values](https://www.ddcutil.com/cap_u3011_verbose_output/) and [manufacturer-specific input codes](https://www.ddcutil.com/faq/)
- [Windows global shortcut registration](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-registerhotkey)
- [Windows Run startup registration](https://learn.microsoft.com/en-us/windows/win32/setupapi/run-and-runonce-registry-keys)
