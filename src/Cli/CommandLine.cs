using AsusMon.Monitors;

namespace AsusMon.Cli;

/// <summary>
/// Hand-rolled argument model. Grammar:
/// <c>asusmon [--monitor N] [--json] &lt;command&gt; [args]</c>
/// </summary>
internal sealed class CommandLine
{
    public string Command { get; private init; } = "status";

    public string[] Args { get; private init; } = [];

    public int? MonitorIndex { get; private init; }

    public bool Json { get; private init; }

    public bool ForceGui { get; private init; }

    public bool ForceConsole { get; private init; }

    /// <summary>Ignore stored capability strings and re-read them from the panels.</summary>
    public bool Refresh { get; private init; }

    /// <summary>Bypass the capability cache entirely, neither reading nor writing it.</summary>
    public bool NoCache { get; private init; }

    public bool Help { get; private init; }

    public string? Error { get; private init; }

    public static CommandLine Parse(string[] argv)
    {
        List<string> positional = [];
        int? monitor = null;
        bool json = false;
        bool gui = false;
        bool console = false;
        bool refresh = false;
        bool noCache = false;
        bool help = false;

        for (int i = 0; i < argv.Length; i++)
        {
            string arg = argv[i];

            switch (arg.ToLowerInvariant())
            {
                case "-m":
                case "--monitor":
                    if (i + 1 >= argv.Length || !int.TryParse(argv[++i], out int parsed))
                    {
                        return new CommandLine { Error = "--monitor requires an integer index." };
                    }

                    monitor = parsed;
                    break;

                case "--json":
                    json = true;
                    break;

                case "--gui":
                    gui = true;
                    break;

                case "--console":
                case "-c":
                    console = true;
                    break;

                case "--refresh":
                    refresh = true;
                    break;

                case "--no-cache":
                    noCache = true;
                    break;

                case "-h":
                case "--help":
                case "-?":
                case "/?":
                    help = true;
                    break;

                default:
                    positional.Add(arg);
                    break;
            }
        }

        return new CommandLine
        {
            Command = positional.Count > 0 ? positional[0].ToLowerInvariant() : "status",
            Args = positional.Count > 1 ? [.. positional[1..]] : [],
            MonitorIndex = monitor,
            Json = json,
            ForceGui = gui,
            ForceConsole = console,
            Refresh = refresh,
            NoCache = noCache,
            Help = help,
        };
    }
}
