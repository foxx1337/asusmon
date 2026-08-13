# asusmon

A small standalone replacement for parts of ASUS DisplayWidgetCenter.

It talks to the monitor exactly the way DisplayWidgetCenter does — plain DDC/CI
over the video cable via `dxva2.dll` — but as a single ~200 KB executable with no
services, no tray icon and no background processes.

Primary use case: list and switch **GameVisual** presets from the command line.

```
asusmon list
asusmon set fps
```

## Hybrid console / GUI

The executable has one entry point and two faces:

| Started from | Behaviour |
| --- | --- |
| A console (cmd, PowerShell, Terminal) | Acts as a CLI, writes to that console, and the shell blocks until it exits |
| Explorer, Start menu, a shortcut | Opens a WinUI 3 summary window, with no console flashing up |

This works because the binary is linked as `IMAGE_SUBSYSTEM_WINDOWS_CUI` *and*
declares `consoleAllocationPolicy` = `detached` in its manifest, a
[Windows 11 24H2 feature](https://learn.microsoft.com/en-us/windows/console/console-allocation-policy).
A CUI process normally gets a console allocated for it when none exists; the
`detached` policy suppresses that, so the process can tell the two cases apart at
runtime and pick a face.

`LaunchContext` makes the decision. It treats the launch as console use when any
of the following holds: a console window is attached, the standard handles are
bound to something usable (a pipe or file, so redirection and automation still
work), or any command-line argument was passed. `--gui` and `--console` override
it.

## Commands

```
asusmon [options] <command> [arguments]

  status                 Current settings for every monitor (default)
  list                   Monitors plus every GameVisual preset they advertise
  modes                  Bare list of preset ids, one per line (script friendly)
  set <mode>             Switch GameVisual preset, e.g. 'asusmon set fps'
  caps                   Dump the raw MCCS capability string
  vcp <code> [value]     Read, or write, a raw VCP feature code
  help                   Show this text

  -m, --monitor <n>      Target one monitor by index (see 'list')
      --json             Emit JSON instead of text
      --gui              Open the graphical summary instead of running a command
  -c, --console          Force console output even without an attached console
```

Exit codes: `0` success, `2` bad usage, `3` DDC/CI failure, `4` unknown mode,
`5` no monitor matched.

### Examples

```powershell
asusmon                       # status of every monitor
asusmon set racing            # switch the primary ASUS panel
asusmon set fps --monitor 0   # switch a specific monitor
asusmon modes                 # ids only, for scripting
asusmon vcp 0x10 40           # brightness to 40
asusmon status --json         # machine readable
```

## How it works

DisplayWidgetCenter uses no driver, no USB and no HID for this monitor — it is
ordinary MCCS 2.2 over the DDC/CI I²C channel (address 0x37) in the video cable,
reached through the Windows Monitor Configuration API:

```
EnumDisplayMonitors
  -> GetNumberOfPhysicalMonitorsFromHMONITOR
  -> GetPhysicalMonitorsFromHMONITOR
  -> CapabilitiesRequestAndCapabilitiesReply   (the capability string)
  -> GetVCPFeatureAndVCPFeatureReply           (read)
  -> SetVCPFeature                             (write)
```

Everything ASUS-specific is in *which* VCP codes are used. Vendor codes above
0xDF are undefined by the MCCS standard, so manufacturers are free to assign
them.

The bus has no flow control, and back-to-back transactions are silently dropped.
`DdcMonitor` therefore serialises every transaction behind a global lock with a
30 ms gap between them — the same thing DisplayWidgetCenter does internally.

### VCP codes used

| Code | Meaning |
| --- | --- |
| `0x10` | Brightness |
| `0x12` | Contrast |
| `0x60` | Input source |
| `0x62` | Audio volume |
| `0x87` | Sharpness |
| `0xDC` | **GameVisual preset** (SDR) |
| `0xE2` | **GameVisual preset** (HDR / Dolby Vision) |
| `0xE3` | GameVisual preset on ProArt panels |
| `0xEF` | ASUS vendor identity probe |

`0xEF` is how the app decides a panel is genuinely an ASUS display. Only a
specific set of replies counts (26, 85-88, 102-105, 119-122, 136-139, 153-156,
160-163, 177-180, 194-197, 211-214, 228-231, 245-248); `0` means not ASUS. A
ROG SWIFT PG32UCWM answers `177`. GameVisual is suppressed entirely for anything
that fails this check, because `0xDC` means something unrelated elsewhere.

### GameVisual presets

SDR presets live on `0xDC`:

| Value | Preset |
| --- | --- |
| `0x01` | Cinema |
| `0x02` | Scenery |
| `0x03` | sRGB |
| `0x04` | User |
| `0x05` | Racing |
| `0x06` | RTS/RPG |
| `0x07` | FPS |
| `0x08` | MOBA |
| `0x09` | Night Vision |
| `0x0A` | sRGB Cal |

On panels that advertise sRGB Calibration, `0x03` is replaced by `0x0A` — a
quirk reproduced from DisplayWidgetCenter.

HDR presets live on `0xE2` as a 16-bit value, high byte = family, low byte =
sub-mode:

| Value | Preset |
| --- | --- |
| `0x0101` | Cinema HDR |
| `0x0102` | Gaming HDR |
| `0x0103` | Console HDR |
| `0x0104` | DisplayHDR 400 True Black |
| `0x0108` | DisplayHDR 500 True Black |
| `0x0205` | Dolby Vision Bright |
| `0x0206` | Dolby Vision Dark |
| `0x0207` | Dolby Vision Gaming |
| `0x0208` | Dolby Vision Source-Only |

Which of these a given panel actually supports is read from its capability
string. Note that panels enumerate the two halves of a 16-bit value as separate
tokens — `E2(0000 0100 0200 01 02 03 04 05 06 07)` rather than as whole words —
so `GameVisualCatalog.IsValueDeclared` accepts a composite value when both of its
halves are declared.

A preset only takes effect while the panel is in the matching pipeline: `0xDC`
applies in SDR, `0xE2` in HDR. `set` warns when the two do not match.

## Requirements

- Windows 11 24H2 (build 26100) or newer, for the console allocation policy
- .NET 10 runtime
- [Windows App Runtime 1.8](https://learn.microsoft.com/windows/apps/windows-app-sdk/downloads), for the GUI
- A monitor with DDC/CI enabled in its OSD, connected by DisplayPort or HDMI.
  Docks, KVMs and USB-C adapters frequently drop the DDC channel.

## Building

```powershell
cd src\AsusMon
dotnet build -c Release
```

Output lands in `bin\Release\net10.0-windows10.0.26100.0\win-x64\`.

To publish a framework-dependent build:

```powershell
dotnet publish -c Release
```

Add `-p:WindowsAppSDKSelfContained=true -p:SelfContained=true` for a build that
carries both the .NET and Windows App Runtime dependencies. That removes every
prerequisite except the OS version, at the cost of a much larger output.

### Project notes

Two things about the project layout are load-bearing and easy to break:

- **`App` must live in the project root namespace** (`AsusMon`, not
  `AsusMon.Ui`). The XAML type-info generator only attaches its
  `IXamlMetadataProvider` implementation to an `App` class it finds there.
  Without that provider, no XAML type resolves at runtime and every
  `InitializeComponent` fails with a bare `XamlParseException`.
- **`DISABLE_XAML_GENERATED_MAIN` is a compiler constant, not an MSBuild
  property.** It has to be added to `DefineConstants`; setting it as a property
  silently does nothing and the generated `Main` collides with the custom one.

## Layout

```
src/AsusMon/
  Program.cs             Entry point; picks the console or GUI face
  LaunchContext.cs       Console-vs-GUI decision
  app.manifest           consoleAllocationPolicy, DPI awareness
  App.xaml[.cs]          WinUI application object (must be in root namespace)
  Ddc/                   Monitor Configuration API interop and DDC plumbing
  Monitors/              VCP codes, GameVisual catalog, high-level facade
  Cli/                   Argument parsing and commands
  Ui/                    WinUI window and view models
```
