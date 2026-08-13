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
            _error.WriteLine("error: 'set' requires a mode. Run 'asusmon list' to see available modes.");
            return ExitCode.UsageError;
        }

        string token = cli.Args[0];

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
              set <mode>             Switch GameVisual preset, e.g. 'asusmon set fps'
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

            examples:
              asusmon list
              asusmon set fps
              asusmon set racing --monitor 0
              asusmon vcp 0xDC
              asusmon vcp 0x10 40          # brightness to 40
              asusmon status --json

            Capability strings take seconds to read over DDC/CI, so they are cached in
            %LOCALAPPDATA%\asusmon\capabilities.json. Use --refresh after a monitor
            firmware update.

            Started from Explorer this program shows a WinUI summary window instead.
            """);
    }
}
