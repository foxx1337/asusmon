using System.Runtime.InteropServices;

namespace AsusMon.Ddc;

/// <summary>
/// P/Invoke surface for the Windows Monitor Configuration API. Every monitor
/// command in this app funnels through <c>dxva2.dll</c>, which speaks MCCS over
/// the DDC/CI I2C channel embedded in the DisplayPort/HDMI link.
/// </summary>
internal static unsafe partial class NativeMethods
{
    internal const int PhysicalMonitorDescriptionLength = 128;

    [StructLayout(LayoutKind.Sequential)]
    internal struct PHYSICAL_MONITOR
    {
        public nint hPhysicalMonitor;
        public fixed char szPhysicalMonitorDescription[PhysicalMonitorDescriptionLength];
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MONITORINFOEXW
    {
        public uint cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
        public fixed char szDevice[32];
    }

    internal const uint MONITORINFOF_PRIMARY = 1;

    [StructLayout(LayoutKind.Sequential)]
    internal struct DISPLAY_DEVICEW
    {
        public uint cb;
        public fixed char DeviceName[32];
        public fixed char DeviceString[128];
        public uint StateFlags;
        public fixed char DeviceID[128];
        public fixed char DeviceKey[128];
    }

    [LibraryImport("user32.dll", EntryPoint = "EnumDisplayDevicesW", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool EnumDisplayDevicesW(
        string? lpDevice,
        uint iDevNum,
        DISPLAY_DEVICEW* lpDisplayDevice,
        uint dwFlags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool EnumDisplayMonitors(
        nint hdc,
        nint lprcClip,
        delegate* unmanaged<nint, nint, RECT*, nint, int> lpfnEnum,
        nint dwData);

    [LibraryImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetMonitorInfoW(nint hMonitor, MONITORINFOEXW* lpmi);

    [LibraryImport("kernel32.dll")]
    internal static partial nint GetConsoleWindow();

    [LibraryImport("user32.dll", EntryPoint = "MessageBoxW", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial int MessageBox(nint hWnd, string lpText, string lpCaption, uint uType);

    [LibraryImport("user32.dll")]
    internal static partial uint GetDpiForWindow(nint hwnd);

    /// <summary>
    /// Pulls an icon group out of a PE image. Used to hand the window the icon
    /// already embedded in this executable by <c>ApplicationIcon</c>, which
    /// avoids shipping a loose .ico beside the binary.
    /// </summary>
    [LibraryImport("shell32.dll", EntryPoint = "ExtractIconExW", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial uint ExtractIconExW(
        string lpszFile,
        int nIconIndex,
        nint* phiconLarge,
        nint* phiconSmall,
        uint nIcons);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool DestroyIcon(nint hIcon);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    internal static partial nint GetStdHandle(int nStdHandle);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    internal static partial uint GetFileType(nint hFile);

    internal const int STD_OUTPUT_HANDLE = -11;
    internal const int STD_ERROR_HANDLE = -12;
    internal const uint FILE_TYPE_UNKNOWN = 0x0000;

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool AllocConsole();

    [LibraryImport("dxva2.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetNumberOfPhysicalMonitorsFromHMONITOR(
        nint hMonitor,
        out uint pdwNumberOfPhysicalMonitors);

    [LibraryImport("dxva2.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetPhysicalMonitorsFromHMONITOR(
        nint hMonitor,
        uint dwPhysicalMonitorArraySize,
        PHYSICAL_MONITOR* pPhysicalMonitorArray);

    [LibraryImport("dxva2.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool DestroyPhysicalMonitor(nint hMonitor);

    [LibraryImport("dxva2.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetVCPFeatureAndVCPFeatureReply(
        nint hMonitor,
        uint dwVCPCode,
        nint pvct,
        out uint pdwCurrentValue,
        out uint pdwMaximumValue);

    [LibraryImport("dxva2.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetVCPFeature(nint hMonitor, uint dwVCPCode, uint dwNewValue);

    [LibraryImport("dxva2.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetCapabilitiesStringLength(nint hMonitor, out uint pdwLength);

    [LibraryImport("dxva2.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool CapabilitiesRequestAndCapabilitiesReply(
        nint hMonitor,
        byte* pszASCIICapabilitiesString,
        uint dwCapabilitiesStringLengthInCharacters);
}
