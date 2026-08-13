using Microsoft.UI.Xaml;

namespace AsusMon.Ui;

/// <summary>
/// Last-resort reporting for failures on the GUI path.
/// </summary>
/// <remarks>
/// A shell launch has no console to print to, so an unhandled exception would
/// otherwise surface only as a silent crash. The detail is written next to the
/// executable and shown in a message box so the user has something actionable.
/// </remarks>
internal static class GuiFailure
{
    private const uint MB_OK = 0x0000;
    private const uint MB_ICONERROR = 0x0010;

    /// <summary>Coarse marker of how far startup got, for diagnosing failures.</summary>
    public static string Stage { get; set; } = "start";

    public static void Report(Exception exception)
    {
        string detail = $"stage: {Stage}{Environment.NewLine}{Environment.NewLine}{exception}";

        try
        {
            File.WriteAllText(LogPath, $"{DateTimeOffset.Now:u}{Environment.NewLine}{detail}");
        }
        catch (IOException)
        {
            // Reporting must never be the reason the process dies.
        }
        catch (UnauthorizedAccessException)
        {
        }

        Ddc.NativeMethods.MessageBox(
            nint.Zero,
            $"asusmon could not start its window.{Environment.NewLine}{Environment.NewLine}{detail}",
            "asusmon",
            MB_OK | MB_ICONERROR);

        Application.Current?.Exit();
    }

    private static string LogPath =>
        Path.Combine(AppContext.BaseDirectory, "asusmon-crash.log");
}
