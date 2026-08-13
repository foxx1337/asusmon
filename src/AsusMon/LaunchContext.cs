using AsusMon.Ddc;

namespace AsusMon;

/// <summary>
/// Decides whether this launch is a command-line invocation or a shell
/// (Explorer, Start menu, shortcut) launch.
/// </summary>
/// <remarks>
/// Because the binary is CUI with a <c>detached</c> console allocation policy,
/// Windows gives it a console only when one already exists. The absence of both
/// a console and usable standard handles is therefore a reliable signal that a
/// human double-clicked the app rather than typed its name.
/// <para>
/// Standard handles are checked in addition to the console window because a
/// console host is not always present even for genuine CLI use: output may be
/// redirected to a pipe or file, and some automation hosts run child processes
/// with no console window at all.
/// </para>
/// </remarks>
internal static class LaunchContext
{
    /// <summary>True when the process can meaningfully write to stdout.</summary>
    public static bool HasConsoleWindow() => NativeMethods.GetConsoleWindow() != nint.Zero;

    /// <summary>
    /// True when stdout or stderr is bound to anything at all — a console
    /// screen buffer, a pipe, or a file.
    /// </summary>
    public static bool HasUsableStandardHandles() =>
        IsUsable(NativeMethods.STD_OUTPUT_HANDLE) || IsUsable(NativeMethods.STD_ERROR_HANDLE);

    private static bool IsUsable(int stdHandle)
    {
        nint handle = NativeMethods.GetStdHandle(stdHandle);

        if (handle == nint.Zero || handle == -1)
        {
            return false;
        }

        return NativeMethods.GetFileType(handle) != NativeMethods.FILE_TYPE_UNKNOWN;
    }

    /// <summary>
    /// Resolves the mode for this launch.
    /// </summary>
    /// <param name="hasArguments">Whether the user passed any arguments.</param>
    /// <param name="forceGui">Whether <c>--gui</c> was given.</param>
    /// <param name="forceConsole">Whether <c>--console</c> was given.</param>
    public static bool ShouldRunGui(bool hasArguments, bool forceGui, bool forceConsole)
    {
        if (forceGui)
        {
            return true;
        }

        if (forceConsole || hasArguments)
        {
            return false;
        }

        return !HasConsoleWindow() && !HasUsableStandardHandles();
    }
}
