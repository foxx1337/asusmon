using System.Runtime.InteropServices;
using AsusMon.Cli;
using AsusMon.Ddc;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace AsusMon;

/// <summary>
/// Single entry point for both faces of the app.
/// </summary>
/// <remarks>
/// The executable is built as IMAGE_SUBSYSTEM_WINDOWS_CUI and carries a
/// <c>consoleAllocationPolicy</c> of <c>detached</c> in its manifest. That
/// combination means:
/// <list type="bullet">
///   <item>Run from cmd/PowerShell, it inherits that console and the shell
///   blocks until it exits — so it behaves like a normal CLI tool.</item>
///   <item>Run from Explorer, Windows allocates no console at all, so no black
///   window flashes up and it can present a GUI instead.</item>
/// </list>
/// <c>GetConsoleWindow()</c> therefore tells the two cases apart at runtime.
/// Requires Windows 11 24H2 (build 26100) or later.
/// </remarks>
internal static class Program
{
    [STAThread]
    private static int Main(string[] argv)
    {
        CommandLine cli = CommandLine.Parse(argv);

        if (LaunchContext.ShouldRunGui(argv.Length > 0, cli.ForceGui, cli.ForceConsole))
        {
            return RunGui();
        }

        // --console from a launch that has no console yet needs one allocated.
        if (cli.ForceConsole && !LaunchContext.HasConsoleWindow() && !LaunchContext.HasUsableStandardHandles())
        {
            NativeMethods.AllocConsole();
            ReopenStandardStreams();
        }

        return RunConsole(cli);
    }

    /// <summary>
    /// After <c>AllocConsole</c> the runtime's cached Console streams still point
    /// at the old, invalid handles, so they are rebound to the new console.
    /// </summary>
    private static void ReopenStandardStreams()
    {
        StreamWriter output = new(Console.OpenStandardOutput()) { AutoFlush = true };
        StreamWriter error = new(Console.OpenStandardError()) { AutoFlush = true };
        Console.SetOut(output);
        Console.SetError(error);
    }

    private static int RunConsole(CommandLine cli)
    {
        try
        {
            return new CommandRunner(Console.Out, Console.Error).Run(cli);
        }
        catch (DllNotFoundException ex)
        {
            Console.Error.WriteLine($"error: required Windows component missing ({ex.Message}).");
            return ExitCode.DdcFailure;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return ExitCode.DdcFailure;
        }
    }

    private static int RunGui()
    {
        // WinUI 3 needs COM wrappers plus a DispatcherQueue-backed synchronization
        // context on the UI thread before Application.Start hands over control.
        WinRT.ComWrappersSupport.InitializeComWrappers();

        try
        {
            Application.Start(p =>
            {
                DispatcherQueueSynchronizationContext context = new(DispatcherQueue.GetForCurrentThread());
                SynchronizationContext.SetSynchronizationContext(context);
                _ = new App();
            });
        }
        catch (Exception ex)
        {
            Ui.GuiFailure.Report(ex);
            return ExitCode.DdcFailure;
        }

        return ExitCode.Success;
    }
}
