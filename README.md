# asusmon

A small standalone replacement for parts of ASUS DisplayWidgetCenter.

It talks to the monitor exactly the way DisplayWidgetCenter does — plain DDC/CI
over the video cable via `dxva2.dll` — but as a single ~200 KB executable with no
services, no tray icon and no background processes.

Primary use case: list and switch **GameVisual** presets from the command line,
plus brightness, contrast and Shadow Boost.

```
asusmon list
asusmon set fps
asusmon set brightness 40
```

## Hybrid console / GUI

The executable has one entry point and two faces:

| Started from | Behaviour |
| --- | --- |
| A console (cmd, PowerShell, Terminal) | Acts as a CLI, writes to that console, and the shell blocks until it exits |
| Explorer, Start menu, a shortcut | Opens a WinUI 3 summary window, with no console flashing up |

The window lists every monitor with its GameVisual preset in a drop-down and
brightness and contrast on sliders, all live. Slider writes are debounced by
200 ms so that dragging one does not flood the DDC channel, and the value is
re-read afterwards so the slider shows what the panel actually accepted.
Shadow Boost is command-line only.

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
  set <setting> [value]  Change a setting, see below
  osd [action] [count]   Press a front panel control, see below
  caps                   Dump the raw MCCS capability string
  vcp <code> [value]     Read, or write, a raw VCP feature code
  cache [show|path|clear] Inspect or discard the capability cache
  help                   Show this text

  -m, --monitor <n>      Target one monitor by index (see 'list')
      --json             Emit JSON instead of text
      --gui              Open the graphical summary instead of running a command
  -c, --console          Force console output even without an attached console
      --refresh          Re-read capability strings instead of using the cache
      --no-cache         Bypass the capability cache entirely
```

### Settings

`set` takes one of three kinds of setting. Names and values are case
insensitive, and `asusmon help` prints the full alias list — that help text is
generated from the same tables the parser uses, so it cannot drift.

| Setting | Values | Aliases |
| --- | --- | --- |
| *preset* | any id from `asusmon modes` | preset name, e.g. `"Night Vision"` |
| `brightness` | `0`-max, or relative `+10` / `-10` | `bright`, `lum`, `luminance` |
| `contrast` | `0`-max, or relative `+10` / `-10` | `cont` |
| `shadowboost` | `off`, `level1`, `level2`, `level3`, `dynamic` | `shadow-boost`, `shadow`, `sb` |

Shadow Boost levels also accept the bare digits `0`-`4`, their display names
(`"Level 2"`, `"Dynamic Adjustment"`), and `none`, `low`, `medium`/`mid`, `high`,
`dyn`/`auto`.

The maximum for brightness and contrast is read from the panel rather than
assumed to be 100. Out-of-range values are clamped, with a warning. If a panel
quantises a value to its own step size, the accepted value is reported instead
of being treated as a failure.

Exit codes: `0` success, `1` bad usage, `2` no monitor matched, `3` DDC/CI
failure, `4` unknown mode or setting value.

### OSD control

`osd` presses the monitor's own controls. Everything the front panel does —
joystick, its click, the back key and both shortcut buttons — is carried by a
single write-only register, VCP `0xEB`, one write per press.

| Action | Value | Physical control | Aliases |
| --- | --- | --- | --- |
| `close` | `0x00` | dismisses the OSD | `dismiss`, `esc` |
| `show` | `0x01` | opens the OSD | `open`, `menu` |
| `up` | `0x02` | joystick up | `u` |
| `down` | `0x03` | joystick down | `d` |
| `right` | `0x04` | joystick right | `r` |
| `left` | `0x05` | joystick left | `l` |
| `enter` | `0x06` | **joystick press** | `press`, `select`, `ok` |
| `back` | `0x07` | back key | `cancel` |
| `input` | `0x08` | input select key | `source`, `inputselect` |
| `quickfit` | `0x09` | QuickFit key | `qf` |
| `button1` | `0x0A` | **shortcut button 1** | `shortcut1`, `key1`, `b1` |
| `button2` | `0x0B` | **shortcut button 2** | `shortcut2`, `key2`, `b2` |
| `selfcal` | `0x0C` | self calibration (ProArt) | `selfcalibration` |

`asusmon osd` on its own lists the actions. One action followed by a number
repeats it; several actions are played in order. Actions the panel does not
declare in its capability string are rejected before anything is written — a
PG32UCWM offers everything above except `quickfit` and `selfcal`.

The two buttons fire whatever function the OSD has assigned to them (GamePlus,
GameVisual, Shadow Boost, and so on, per `ShortcutType` in the ASUS app). These
press the key; they do not choose the function.

Nothing here reads back: the register has no state, so a press either reaches
the panel or the DDC/CI handshake fails.

### Examples

```powershell
asusmon                       # status of every monitor
asusmon set racing            # switch the primary ASUS panel
asusmon set fps --monitor 0   # switch a specific monitor
asusmon set brightness 40     # absolute
asusmon set contrast +5       # relative
asusmon set shadowboost level2
asusmon set sb dynamic        # same thing, short form
asusmon modes                 # ids only, for scripting
asusmon osd show              # open the OSD
asusmon osd down 3            # three joystick presses down
asusmon osd show down enter   # a sequence, played in order
asusmon osd button2           # press shortcut button 2
asusmon vcp 0x10 40           # brightness to 40, the raw way
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

## The capability cache

Reading the capability string is by far the most expensive thing this tool does.
On a PG32UCWM it costs **~5.6 seconds**, which is most of the runtime of any
command:

| Step | Time |
| --- | --- |
| `GetCapabilitiesStringLength` | 5595 ms |
| `CapabilitiesRequestAndCapabilitiesReply` | 2 ms |
| A single VCP read or write | ~57 ms |

`GetCapabilitiesStringLength` is not a cheap length query. The driver performs
the entire transfer in order to answer it, then serves the follow-up reply from
its own cache — hence the 2 ms. The panel returns 1143 bytes, MCCS moves that in
~32-byte fragments with a mandated delay between each, so it is roughly 36 round
trips over a 100 kHz I²C link.

`set` needs the capability string three times over: to derive the model, and
from it the product line and therefore which VCP code carries the preset; to
narrow the preset catalog to what the panel actually declares; and to decide the
sRGB→sRGB Cal remap.

Since the string is fixed in the monitor's firmware, it is cached in

```
%LOCALAPPDATA%\asusmon\capabilities.json
```

keyed by the panel's PNP identity (`MONITOR\AUS32D6\{4d36e96e-...}\0009`), which
comes from the EDID manufacturer and product code and so survives being moved to
another port. Panels that publish no capability string are recorded as such, so
that slow failed read is not repeated either.

The effect:

| Command | Cold | Cached |
| --- | --- | --- |
| `set fps -m 0` | 6100 ms | **485 ms** |
| `list` (3 monitors) | 14400 ms | **1700 ms** |

What remains is irreducible: each live VCP read costs ~57 ms plus the 30 ms bus
pacing, and `ApplyMode` waits 150 ms after a write before reading the value back
to confirm the panel accepted it.

The file is plain JSON and safe to delete at any time. Use `--refresh` to
re-read and overwrite it (needed only after a monitor firmware update),
`--no-cache` to bypass it completely, or `asusmon cache clear` to empty it.

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
| `0xE5` | **Shadow Boost** |
| `0xEB` | **OSD instruction** (front panel keys) |
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

### Shadow Boost

Shadow Boost lifts detail in dark content without washing out midtones. It lives
on `0xE5`:

| Value | Level |
| --- | --- |
| `0x00` | Off |
| `0x01` | Level 1 |
| `0x02` | Level 2 |
| `0x03` | Level 3 |
| `0x04` | Dynamic Adjustment |

Availability is **per GameVisual preset**, not per model. In a preset that does
not apply it — sRGB, sRGB Cal and MOBA on the Gaming line, matching the
`ShadowBoost: "False"` rows in DisplayWidgetCenter's
`AppConfig\DisplayModeCapability_Gaming` — the panel answers a read of `0xE5`
with `current=0xFE max=0xFE`, the same sentinel DisplayWidgetCenter treats as
"feature absent". `set shadowboost` checks for that and names the preset
responsible rather than reporting a generic failure.

### Where the code map comes from

Codes below `0xE0` are VESA MCCS standard, so brightness at `0x10` and contrast
at `0x12` are documented and vendor-neutral; Windows' own `GetMonitorBrightness`
is a wrapper over `0x10`.

The vendor range has no such backing. `0xDC`, `0xE2`, `0xE5`, `0xEB` and `0xEF`
were taken from DisplayWidgetCenter's own `VCPAPI` class, which ships
unobfuscated and passes each code as a literal in a named method
(`SetShadowBoost` → `SetVCPFeatureInternal(hMonitor, 229u, ...)`, `SetEzOSD` →
`SetVCPFeatureInternalForEzOSD(hMonitor, 235u, ...)`), cross-checked against its
`AirVisionVCPCode` enum and its retained log strings, then confirmed against the
live panel by writing each value and reading it back. The OSD action ordinals
come from the `VCPAPI.OSDOperate` enum in the same class.

## Requirements

- Windows 11 24H2 (build 26100) or newer, for the console allocation policy
- .NET 10 runtime
- [Windows App Runtime 1.8](https://learn.microsoft.com/windows/apps/windows-app-sdk/downloads), for the GUI
- A monitor with DDC/CI enabled in its OSD, connected by DisplayPort or HDMI.
  Docks, KVMs and USB-C adapters frequently drop the DDC channel.

## Building

```powershell
cd src
dotnet build -c Release
```

Output lands in `src\bin\Release\net10.0-windows10.0.26100.0\win-x64\`.

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
src/
  AsusMon.csproj
  Program.cs             Entry point; picks the console or GUI face
  LaunchContext.cs       Console-vs-GUI decision
  app.manifest           consoleAllocationPolicy, DPI awareness
  App.xaml[.cs]          WinUI application object (must be in root namespace)
  Ddc/                   Monitor Configuration API interop and DDC plumbing
    CapabilityCache.cs   %LOCALAPPDATA% cache of capability strings
  Monitors/              VCP codes, GameVisual catalog, high-level facade
  Cli/                   Argument parsing and commands
  Ui/                    WinUI window and view models
```
