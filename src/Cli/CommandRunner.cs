using System.Text;
using System.Text.Json;
using AsusMon.Ddc;
using AsusMon.Monitors;

namespace AsusMon.Cli;

/// <summary>Exit codes surfaced to the shell.</summary>
internal static class ExitCode
{
    public const int Success = 0;
    public const int UsageError = 1;
    public const int NoMonitor = 2;
    public const int DdcFailure = 3;
    public const int UnknownMode = 4;
}

/// <summary>Executes the console-mode commands.</summary>
internal sealed class CommandRunner
{
    private readonly TextWriter _out;
    private readonly TextWriter _error;

    public CommandRunner(TextWriter output, TextWriter error)
    {
        _out = output;
        _error = error;
    }

    public int Run(CommandLine cli)
    {
        if (cli.Help)
        {
            PrintUsage();
            return ExitCode.Success;
        }

        if (cli.Error is not null)
        {
            _error.WriteLine($"error: {cli.Error}");
            return ExitCode.UsageError;
        }

        return cli.Command switch
        {
            "list" => List(cli),
            "modes" => Modes(cli),
            "status" or "get" => Status(cli),
            "set" => Set(cli),
            "osd" => RunOsd(cli),
            "caps" => Caps(cli),
            "vcp" => RawVcp(cli),
            "cache" => Cache(cli),
            "help" => Help(),
            _ => Unknown(cli.Command),
        };
    }

    private int Help()
    {
        PrintUsage();
        return ExitCode.Success;
    }

    private int Unknown(string command)
    {
        _error.WriteLine($"error: unknown command '{command}'.");
        _error.WriteLine();
        PrintUsage();
        return ExitCode.UsageError;
    }

    /// <summary>
    /// Opens the monitors with the capability cache configured for this run.
    /// Reading a capability string from a panel takes seconds, so every command
    /// shares the cache unless the user opted out.
    /// </summary>
    private static DisplaySet OpenDisplays(CommandLine cli) =>
        DisplaySet.Open(CapabilityCache.Open(!cli.NoCache, cli.Refresh));

    // ---------------------------------------------------------------- list

    private int List(CommandLine cli)
    {
        using DisplaySet displays = OpenDisplays(cli);

        if (displays.Count == 0)
        {
            _error.WriteLine("error: no DDC/CI capable monitors found.");
            return ExitCode.NoMonitor;
        }

        List<DisplaySummary> summaries = [];

        foreach (AsusDisplay display in displays.Where(cli.MonitorIndex))
        {
            summaries.Add(display.Summarize());
        }

        if (cli.Json)
        {
            WriteJson(summaries);
            return ExitCode.Success;
        }

        foreach (DisplaySummary summary in summaries)
        {
            WriteHeader(summary);

            IReadOnlyList<GameVisualMode> sdr =
                [.. summary.AvailableModes.Where(m => m.Family == GameVisualFamily.Sdr)];
            IReadOnlyList<GameVisualMode> hdr =
                [.. summary.AvailableModes.Where(m => m.Family != GameVisualFamily.Sdr)];

            if (sdr.Count > 0)
            {
                _out.WriteLine();
                _out.WriteLine("  GameVisual presets (SDR):");
                WriteModeTable(sdr, summary);
            }

            if (hdr.Count > 0)
            {
                _out.WriteLine();
                _out.WriteLine("  HDR presets (active only while the panel is in HDR):");
                WriteModeTable(hdr, summary);
            }

            if (sdr.Count == 0 && hdr.Count == 0)
            {
                _out.WriteLine();
                _out.WriteLine(summary.IsAsus
                    ? "  No GameVisual presets advertised by this monitor."
                    : "  Not an ASUS display - GameVisual is not available.");
            }

            _out.WriteLine();
        }

        return ExitCode.Success;
    }

    private void WriteModeTable(IReadOnlyList<GameVisualMode> modes, DisplaySummary summary)
    {
        int width = modes.Max(m => m.Id.Length);

        foreach (GameVisualMode mode in modes)
        {
            bool current = summary.CurrentMode is { } c && c.Id == mode.Id && c.Code == mode.Code;
            string marker = current ? "*" : " ";
            string value = mode.Value > 0xFF ? $"0x{mode.Value:X4}" : $"0x{mode.Value:X2}";

            _out.WriteLine(
                $"   {marker} {mode.Id.PadRight(width)}  {value} on 0x{mode.Code:X2}   {mode.Name}");
        }
    }

    // --------------------------------------------------------------- modes

    private int Modes(CommandLine cli)
    {
        using DisplaySet displays = OpenDisplays(cli);
        AsusDisplay? display = displays.Select(cli.MonitorIndex);

        if (display is null)
        {
            _error.WriteLine("error: no matching monitor.");
            return ExitCode.NoMonitor;
        }

        foreach (GameVisualMode mode in display.AvailableModes)
        {
            _out.WriteLine(mode.Id);
        }

        return ExitCode.Success;
    }

    // -------------------------------------------------------------- status

    private int Status(CommandLine cli)
    {
        using DisplaySet displays = OpenDisplays(cli);

        if (displays.Count == 0)
        {
            _error.WriteLine("error: no DDC/CI capable monitors found.");
            return ExitCode.NoMonitor;
        }

        List<DisplaySummary> summaries = [];

        foreach (AsusDisplay display in displays.Where(cli.MonitorIndex))
        {
            summaries.Add(display.Summarize());
        }

        if (cli.Json)
        {
            WriteJson(summaries);
            return ExitCode.Success;
        }

        foreach (DisplaySummary summary in summaries)
        {
            WriteHeader(summary);
            _out.WriteLine();

            if (summary.IsAsus)
            {
                WriteSetting("GameVisual", DescribeMode(summary));
                WriteSetting("Pipeline", summary.IsHdrActive ? "HDR" : "SDR");
            }

            WriteSetting("Input", summary.InputSourceName);
            WriteSetting("Brightness", Describe(summary.Brightness));
            WriteSetting("Contrast", Describe(summary.Contrast));
            WriteSetting("Sharpness", Describe(summary.Sharpness));
            WriteSetting("Volume", Describe(summary.Volume));
            _out.WriteLine();
        }

        return ExitCode.Success;
    }

    // ----------------------------------------------------------------- set

    private int Set(CommandLine cli)
    {
        if (cli.Args.Length == 0)
        {
            _error.WriteLine(
                "error: 'set' requires a mode or a level. " +
                "Run 'asusmon list' to see available modes, or use 'set brightness <0-100>'.");
            return ExitCode.UsageError;
        }

        string token = cli.Args[0];

        if (ShadowBoost.IsVerb(token))
        {
            return SetShadowBoost(cli);
        }

        if (LevelFeature.Resolve(token) is { } feature)
        {
            return SetLevel(cli, feature);
        }

        using DisplaySet displays = OpenDisplays(cli);
        AsusDisplay? display = displays.Select(cli.MonitorIndex);

        if (display is null)
        {
            _error.WriteLine("error: no matching monitor.");
            return ExitCode.NoMonitor;
        }

        IReadOnlyList<GameVisualMode> modes = display.AvailableModes;

        if (modes.Count == 0)
        {
            _error.WriteLine(display.IsAsus
                ? $"error: {display.Model ?? display.Description} advertises no GameVisual presets."
                : $"error: {display.Description} is not an ASUS display; GameVisual is not available.");
            return ExitCode.UnknownMode;
        }

        GameVisualMode? mode = GameVisualCatalog.Resolve(modes, token);

        if (mode is null)
        {
            _error.WriteLine($"error: '{token}' is not a mode supported by {display.Model ?? display.Description}.");
            _error.WriteLine($"       available: {string.Join(", ", modes.Select(m => m.Id))}");
            return ExitCode.UnknownMode;
        }

        if (mode.Family != GameVisualFamily.Sdr && !display.IsHdrActive)
        {
            _error.WriteLine(
                $"warning: '{mode.Id}' is an HDR preset but the panel is currently in SDR. " +
                "Enable HDR in Windows display settings first.");
        }

        if (!display.ApplyMode(mode, out uint readBack))
        {
            _error.WriteLine(
                readBack == uint.MaxValue
                    ? $"error: the monitor did not acknowledge 0x{mode.Code:X2}."
                    : $"error: wrote 0x{mode.Value:X2} to 0x{mode.Code:X2} but the monitor reports 0x{readBack:X2}.");
            return ExitCode.DdcFailure;
        }

        _out.WriteLine($"{display.Model ?? display.Description}: GameVisual -> {mode.Name}");
        return ExitCode.Success;
    }

    /// <summary>
    /// Drives a continuous feature, e.g. <c>set brightness 40</c>. A leading
    /// sign makes the value relative to the current reading, so
    /// <c>set brightness +10</c> works as a shortcut key would.
    /// </summary>
    private int SetLevel(CommandLine cli, LevelFeature feature)
    {
        if (cli.Args.Length < 2)
        {
            _error.WriteLine($"error: 'set {feature.Id}' requires a value, e.g. 'asusmon set {feature.Id} 40'.");
            return ExitCode.UsageError;
        }

        string valueToken = cli.Args[1];
        bool relative = valueToken.StartsWith('+') || valueToken.StartsWith('-');

        if (!int.TryParse(valueToken, out int requested))
        {
            _error.WriteLine($"error: '{valueToken}' is not a valid value. Use 0-100, or a relative +10 / -10.");
            return ExitCode.UsageError;
        }

        using DisplaySet displays = OpenDisplays(cli);
        AsusDisplay? display = displays.Select(cli.MonitorIndex);

        if (display is null)
        {
            _error.WriteLine("error: no matching monitor.");
            return ExitCode.NoMonitor;
        }

        if (display.Read(feature.Code) is not { } current)
        {
            _error.WriteLine(
                $"error: {display.Model ?? display.Description} does not report {feature.Id} (VCP 0x{feature.Code:X2}).");
            return ExitCode.DdcFailure;
        }

        int max = (int)current.Maximum;
        int target = relative ? (int)current.Current + requested : requested;
        int clamped = Math.Clamp(target, 0, max);

        if (clamped != target)
        {
            _error.WriteLine($"warning: {target} is outside 0-{max}; clamped to {clamped}.");
        }

        if (!display.ApplyLevel(feature.Code, (uint)clamped, out uint readBack))
        {
            if (readBack == uint.MaxValue)
            {
                _error.WriteLine($"error: the monitor did not accept a write to 0x{feature.Code:X2}.");
                return ExitCode.DdcFailure;
            }

            // Some panels quantize to their own step size; that is not a failure.
            _out.WriteLine(
                $"{display.Model ?? display.Description}: {feature.Name} -> {clamped}, monitor settled on {readBack}");
            return ExitCode.Success;
        }

        _out.WriteLine($"{display.Model ?? display.Description}: {feature.Name} -> {clamped} / {max}");
        return ExitCode.Success;
    }

    /// <summary>
    /// Sets Shadow Boost (VCP 0xE5). Vendor-specific, so the panel is checked
    /// for ASUS identity and for an 0xE5 declaration before writing.
    /// </summary>
    private int SetShadowBoost(CommandLine cli)
    {
        string options = string.Join(", ", ShadowBoost.Options.Select(o => o.Id));

        if (cli.Args.Length < 2)
        {
            _error.WriteLine($"error: 'set shadowboost' requires a level. One of: {options}.");
            return ExitCode.UsageError;
        }

        string token = cli.Args[1];

        if (ShadowBoost.Resolve(token) is not { } option)
        {
            _error.WriteLine($"error: '{token}' is not a Shadow Boost level.");
            _error.WriteLine($"       available: {options}");
            return ExitCode.UsageError;
        }

        using DisplaySet displays = OpenDisplays(cli);
        AsusDisplay? display = displays.Select(cli.MonitorIndex);

        if (display is null)
        {
            _error.WriteLine("error: no matching monitor.");
            return ExitCode.NoMonitor;
        }

        string name = display.Model ?? display.Description;

        if (!display.IsAsus)
        {
            _error.WriteLine($"error: {display.Description} is not an ASUS display; Shadow Boost is not available.");
            return ExitCode.UnknownMode;
        }

        // 0xFE is the sentinel DisplayWidgetCenter reads as "feature absent".
        // The panel reports it per-preset, so distinguish "this monitor never
        // has Shadow Boost" from "the active preset does not apply it".
        if (display.Read(ShadowBoost.Code) is not { } current || current.Current == 0xFE || current.Maximum == 0xFE)
        {
            GameVisualMode? active = display.ReadCurrentMode().Mode;

            if (display.IsHdrActive)
            {
                _error.WriteLine($"error: {name} does not apply Shadow Boost while the panel is in HDR.");
            }
            else if (active is { } preset &&
                     ShadowBoost.PresetsWithoutSupport.Contains(preset.Id, StringComparer.OrdinalIgnoreCase))
            {
                _error.WriteLine(
                    $"error: the {preset.Name} preset does not apply Shadow Boost. " +
                    "Switch to another GameVisual preset first.");
            }
            else
            {
                _error.WriteLine($"error: {name} does not support Shadow Boost (VCP 0x{ShadowBoost.Code:X2}).");
            }

            return ExitCode.DdcFailure;
        }

        if (display.Capabilities?.ValuesFor(ShadowBoost.Code) is { Count: > 0 } declared &&
            !declared.Contains(option.Value))
        {
            _error.WriteLine($"error: {name} does not advertise Shadow Boost '{option.Id}'.");
            _error.WriteLine(
                "       declared: " +
                string.Join(", ", declared.Select(v =>
                    ShadowBoost.Options.FirstOrDefault(o => o.Value == v)?.Id ?? $"0x{v:X2}")));
            return ExitCode.UnknownMode;
        }

        if (!display.ApplyLevel(ShadowBoost.Code, option.Value, out uint readBack))
        {
            _error.WriteLine(
                readBack == uint.MaxValue
                    ? $"error: the monitor did not accept a write to 0x{ShadowBoost.Code:X2}."
                    : $"error: wrote 0x{option.Value:X2} to 0x{ShadowBoost.Code:X2} but the monitor reports 0x{readBack:X2}.");
            return ExitCode.DdcFailure;
        }

        _out.WriteLine($"{name}: Shadow Boost -> {option.Name}");
        return ExitCode.Success;
    }

    // ----------------------------------------------------------------- osd

    /// <summary>
    /// Presses front-panel controls through VCP 0xEB. Accepts either a single
    /// action with a repeat count, or a sequence of actions to play in order.
    /// </summary>
    private int RunOsd(CommandLine cli)
    {
        if (cli.Args.Length == 0)
        {
            PrintOsdActions();
            return ExitCode.Success;
        }

        // 'osd down 3' repeats; anything else is a sequence of presses.
        int repeat = 1;
        string[] tokens = cli.Args;

        if (tokens.Length == 2 && int.TryParse(tokens[1], out int count))
        {
            if (count < 1 || count > 50)
            {
                _error.WriteLine($"error: repeat count must be between 1 and 50, got {count}.");
                return ExitCode.UsageError;
            }

            repeat = count;
            tokens = [tokens[0]];
        }

        List<EnumOption> sequence = [];

        foreach (string token in tokens)
        {
            if (Osd.Resolve(token) is not { } action)
            {
                _error.WriteLine($"error: '{token}' is not an OSD action.");
                _error.WriteLine($"       available: {string.Join(", ", Osd.Actions.Select(a => a.Id))}");
                return ExitCode.UsageError;
            }

            sequence.Add(action);
        }

        using DisplaySet displays = OpenDisplays(cli);
        AsusDisplay? display = displays.Select(cli.MonitorIndex);

        if (display is null)
        {
            _error.WriteLine("error: no matching monitor.");
            return ExitCode.NoMonitor;
        }

        string name = display.Model ?? display.Description;

        if (!display.IsAsus)
        {
            _error.WriteLine($"error: {display.Description} is not an ASUS display; VCP 0x{Osd.Code:X2} is vendor specific.");
            return ExitCode.UnknownMode;
        }

        IReadOnlyList<uint>? declared = display.Capabilities?.ValuesFor(Osd.Code);

        if (declared is { Count: > 0 })
        {
            foreach (EnumOption action in sequence)
            {
                if (declared.Contains(action.Value))
                {
                    continue;
                }

                _error.WriteLine($"error: {name} does not advertise the '{action.Id}' OSD action.");
                _error.WriteLine(
                    "       declared: " +
                    string.Join(", ", declared.Select(v =>
                        Osd.Actions.FirstOrDefault(a => a.Value == v)?.Id ?? $"0x{v:X2}")));
                return ExitCode.UnknownMode;
            }
        }

        // Write-only feature: there is no state to read back, so a failed
        // handshake is the only error the panel can report.
        foreach (EnumOption action in sequence)
        {
            for (int i = 0; i < repeat; i++)
            {
                if (!display.Write(Osd.Code, action.Value))
                {
                    _error.WriteLine($"error: the monitor did not accept 0x{action.Value:X2} on 0x{Osd.Code:X2}.");
                    return ExitCode.DdcFailure;
                }
            }
        }

        string played = string.Join(", ", sequence.Select(a => a.Name));
        _out.WriteLine(repeat > 1
            ? $"{name}: OSD -> {played} x{repeat}"
            : $"{name}: OSD -> {played}");

        return ExitCode.Success;
    }

    /// <summary>Lists the OSD actions, marking the ones this panel declares.</summary>
    private void PrintOsdActions()
    {
        _out.WriteLine($"OSD actions (VCP 0x{Osd.Code:X2}), usage: asusmon osd <action> [count]");
        _out.WriteLine();

        int width = Osd.Actions.Max(a => a.Id.Length);

        foreach (EnumOption action in Osd.Actions)
        {
            string aliases = action.Aliases is { Length: > 0 } list
                ? $"  also: {string.Join(", ", list)}"
                : string.Empty;

            _out.WriteLine($"  {action.Id.PadRight(width)}  0x{action.Value:X2}  {action.Name,-18}{aliases}");
        }

        _out.WriteLine();
        _out.WriteLine("  Buttons 1 and 2 press the panel's shortcut keys, whatever the OSD has");
        _out.WriteLine("  assigned to them. Not every panel offers every action; run 'asusmon caps'");
        _out.WriteLine($"  and look at the 0x{Osd.Code:X2} group to see which ones yours declares.");
    }

    // ---------------------------------------------------------------- caps

    private int Caps(CommandLine cli)
    {
        using DisplaySet displays = OpenDisplays(cli);
        AsusDisplay? display = displays.Select(cli.MonitorIndex);

        if (display is null)
        {
            _error.WriteLine("error: no matching monitor.");
            return ExitCode.NoMonitor;
        }

        if (display.Capabilities is not { } caps)
        {
            _error.WriteLine("error: the monitor did not return a capability string.");
            return ExitCode.DdcFailure;
        }

        _out.WriteLine(caps.Raw);
        return ExitCode.Success;
    }

    // ----------------------------------------------------------------- vcp

    private int RawVcp(CommandLine cli)
    {
        if (cli.Args.Length == 0)
        {
            _error.WriteLine("error: 'vcp' requires a feature code, e.g. 'asusmon vcp 0xDC'.");
            return ExitCode.UsageError;
        }

        if (!GameVisualCatalog.TryParseNumber(cli.Args[0], out uint code) || code > byte.MaxValue)
        {
            _error.WriteLine($"error: '{cli.Args[0]}' is not a valid VCP feature code.");
            return ExitCode.UsageError;
        }

        using DisplaySet displays = OpenDisplays(cli);
        AsusDisplay? display = displays.Select(cli.MonitorIndex);

        if (display is null)
        {
            _error.WriteLine("error: no matching monitor.");
            return ExitCode.NoMonitor;
        }

        // Two args means write, one means read.
        if (cli.Args.Length >= 2)
        {
            if (!GameVisualCatalog.TryParseNumber(cli.Args[1], out uint value))
            {
                _error.WriteLine($"error: '{cli.Args[1]}' is not a valid value.");
                return ExitCode.UsageError;
            }

            if (!display.Write((byte)code, value))
            {
                _error.WriteLine($"error: write to 0x{code:X2} failed.");
                return ExitCode.DdcFailure;
            }

            _out.WriteLine($"0x{code:X2} <- 0x{value:X4}");
            return ExitCode.Success;
        }

        if (display.Read((byte)code) is not { } reading)
        {
            _error.WriteLine($"error: read of 0x{code:X2} failed (feature likely unsupported).");
            return ExitCode.DdcFailure;
        }

        _out.WriteLine($"0x{code:X2}  current=0x{reading.Current:X4} ({reading.Current})  max=0x{reading.Maximum:X4} ({reading.Maximum})");
        return ExitCode.Success;
    }

    // --------------------------------------------------------------- cache

    private int Cache(CommandLine cli)
    {
        CapabilityCache? cache = CapabilityCache.Open(enabled: true);

        if (cache is null)
        {
            _error.WriteLine("error: no writable cache location available.");
            return ExitCode.DdcFailure;
        }

        string action = cli.Args.Length > 0 ? cli.Args[0].ToLowerInvariant() : "show";

        switch (action)
        {
            case "clear":
                cache.Clear();
                cache.Flush();
                _out.WriteLine($"cache cleared: {cache.FilePath}");
                return ExitCode.Success;

            case "path":
                _out.WriteLine(cache.FilePath);
                return ExitCode.Success;

            case "show":
                _out.WriteLine(cache.FilePath);
                _out.WriteLine($"{cache.Count} monitor(s) cached");
                return ExitCode.Success;

            default:
                _error.WriteLine($"error: unknown cache action '{action}'. Use show, path or clear.");
                return ExitCode.UsageError;
        }
    }

    // ------------------------------------------------------------- helpers

    private void WriteHeader(DisplaySummary summary)
    {
        StringBuilder tags = new();

        if (summary.IsPrimary)
        {
            tags.Append(" [primary]");
        }

        if (summary.IsAsus)
        {
            tags.Append(" [ASUS]");
        }

        _out.WriteLine($"[{summary.Index}] {summary.Description}{tags}");
        _out.WriteLine($"    device      {summary.DeviceName}");
        _out.WriteLine($"    model       {summary.Model ?? "unknown"}");
        _out.WriteLine($"    family      {summary.ProductLine}");
        _out.WriteLine($"    mccs        {summary.MccsVersion ?? "unknown"}");

        if (summary.VendorCode is { } vendor)
        {
            _out.WriteLine($"    vendor id   {vendor} (0x{vendor:X2}) via VCP 0xEF");
        }
    }

    private void WriteSetting(string label, string? value) =>
        _out.WriteLine($"    {label.PadRight(12)}{value ?? "n/a"}");

    private static string DescribeMode(DisplaySummary summary)
    {
        if (summary.CurrentMode is { } mode)
        {
            string raw = mode.Value > 0xFF ? $"0x{mode.Value:X4}" : $"0x{mode.Value:X2}";
            return $"{mode.Name} ({mode.Id}, {raw} on 0x{mode.Code:X2})";
        }

        return summary.CurrentModeValue is { } value
            ? $"unrecognized (0x{value:X4})"
            : "n/a";
    }

    private static string? Describe(Reading? reading) =>
        reading is { } r ? $"{r.Current} / {r.Maximum}" : null;

    private void WriteJson<T>(T value) =>
        _out.WriteLine(JsonSerializer.Serialize(value, AsusMonJsonContext.Default.Options));

    private void PrintUsage()
    {
        _out.WriteLine("""
            asusmon - control ASUS monitors over DDC/CI

            usage:
              asusmon [options] <command> [arguments]

            commands:
              status                 Current settings for every monitor (default)
              list                   Monitors plus every GameVisual preset they advertise
              modes                  Bare list of preset ids, one per line (script friendly)
              set <setting> [value]  Change a setting, see 'settings' below
              osd [action] [count]   Press a front panel control, see 'osd actions' below
              caps                   Dump the raw MCCS capability string
              vcp <code> [value]     Read, or write, a raw VCP feature code
              cache [show|path|clear] Inspect or discard the capability cache
              help                   Show this text

            options:
              -m, --monitor <n>      Target one monitor by index (see 'list')
                  --json             Emit JSON instead of text
                  --gui              Open the graphical summary instead of running a command
              -c, --console          Force console output even without an attached console
                  --refresh          Re-read capability strings instead of using the cache
                  --no-cache         Bypass the capability cache entirely
            """);

        PrintSettings();
        PrintOsdSummary();

        _out.WriteLine("""

            examples:
              asusmon list
              asusmon set fps
              asusmon set racing --monitor 0
              asusmon set brightness 40
              asusmon set contrast +5
              asusmon set shadowboost level2
              asusmon set sb dynamic
              asusmon osd show
              asusmon osd down 3           # three presses of the joystick
              asusmon osd show down enter  # a sequence, played in order
              asusmon osd button2
              asusmon vcp 0xDC
              asusmon vcp 0x10 40          # brightness to 40
              asusmon status --json

            Capability strings take seconds to read over DDC/CI, so they are cached in
            %LOCALAPPDATA%\asusmon\capabilities.json. Use --refresh after a monitor
            firmware update.

            Started from Explorer this program shows a WinUI summary window instead.
            """);
    }

    /// <summary>
    /// Documents the <c>osd</c> verb inside the main help. Generated from the
    /// same table the parser uses.
    /// </summary>
    private void PrintOsdSummary()
    {
        _out.WriteLine();
        _out.WriteLine($"osd actions (VCP 0x{Osd.Code:X2}, one write per press):");

        int width = Osd.Actions.Max(a => a.Id.Length);

        foreach (EnumOption action in Osd.Actions)
        {
            string aliases = action.Aliases is { Length: > 0 } list
                ? $"  also: {string.Join(", ", list)}"
                : string.Empty;

            _out.WriteLine($"  {action.Id.PadRight(width)}  {action.Name,-18}{aliases}");
        }

        _out.WriteLine();
        _out.WriteLine("  'osd <action> <count>' repeats one action; several actions are played");
        _out.WriteLine("  in order. Run 'asusmon osd' alone to list them with their raw values.");
        _out.WriteLine("  button1 and button2 press the panel's shortcut keys, firing whatever");
        _out.WriteLine("  function the OSD has assigned to them.");
    }

    /// <summary>
    /// Documents everything <c>set</c> accepts. Generated from the feature
    /// tables so the help can never drift away from what the parser allows.
    /// </summary>
    private void PrintSettings()
    {
        _out.WriteLine();
        _out.WriteLine("settings for 'set':");
        _out.WriteLine("  <preset>               A GameVisual preset id, name or alias.");
        _out.WriteLine("                         Run 'asusmon modes' for the ones your panel offers.");

        foreach (LevelFeature feature in LevelFeature.All)
        {
            _out.WriteLine($"  {feature.Id,-22} 0 to the panel maximum, or relative (+10, -10).");
            _out.WriteLine($"                         also: {string.Join(", ", feature.Aliases)}");
        }

        _out.WriteLine($"  {"shadowboost <level>",-22} ASUS only, VCP 0x{ShadowBoost.Code:X2}. Levels below.");
        _out.WriteLine($"                         also: {string.Join(", ", ShadowBoost.VerbAliases.Skip(1))}");
        _out.WriteLine();
        _out.WriteLine("  shadow boost levels:");

        int width = ShadowBoost.Options.Max(o => o.Id.Length);

        foreach (EnumOption option in ShadowBoost.Options)
        {
            string aliases = option.Aliases is { Length: > 0 } list
                ? $"  also: {string.Join(", ", list)}"
                : string.Empty;

            _out.WriteLine($"    {option.Id.PadRight(width)}  {option.Name,-20}{aliases}");
        }

        _out.WriteLine();
        _out.WriteLine("  Level names match with or without spaces, e.g. \"Level 2\" or level2.");
        _out.WriteLine("  All setting and level names are case insensitive.");
        _out.WriteLine(
            "  Shadow Boost is reported as unavailable by the sRGB, sRGB Cal and MOBA");
        _out.WriteLine(
            "  presets, and while the panel is in HDR.");
    }
}
