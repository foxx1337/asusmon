using System.Runtime.InteropServices;

namespace AsusMon.Ddc;

/// <summary>
/// Walks <c>EnumDisplayMonitors</c> and expands each HMONITOR into the physical
/// monitors behind it.
/// </summary>
internal static unsafe class MonitorEnumerator
{
    public static List<DdcMonitor> Enumerate()
    {
        List<nint> handles = [];
        GCHandle state = GCHandle.Alloc(handles);

        try
        {
            NativeMethods.EnumDisplayMonitors(
                nint.Zero, nint.Zero, &Callback, GCHandle.ToIntPtr(state));
        }
        finally
        {
            state.Free();
        }

        List<DdcMonitor> monitors = [];

        foreach (nint hMonitor in handles)
        {
            (string deviceName, bool isPrimary) = DescribeAdapter(hMonitor);

            if (!NativeMethods.GetNumberOfPhysicalMonitorsFromHMONITOR(hMonitor, out uint count) || count == 0)
            {
                continue;
            }

            NativeMethods.PHYSICAL_MONITOR[] physical = new NativeMethods.PHYSICAL_MONITOR[count];

            fixed (NativeMethods.PHYSICAL_MONITOR* p = physical)
            {
                if (!NativeMethods.GetPhysicalMonitorsFromHMONITOR(hMonitor, count, p))
                {
                    continue;
                }

                for (int i = 0; i < count; i++)
                {
                    string description = new string(
                        p[i].szPhysicalMonitorDescription,
                        0,
                        NativeMethods.PhysicalMonitorDescriptionLength).TrimEnd('\0');

                    monitors.Add(new DdcMonitor(
                        new PhysicalMonitorHandle(p[i].hPhysicalMonitor),
                        description,
                        deviceName,
                        isPrimary,
                        DescribeMonitorDevice(deviceName, (uint)i)));
                }
            }
        }

        return monitors;
    }

    /// <summary>
    /// PNP identity of the panel behind an adapter output, e.g.
    /// <c>MONITOR\AUS32D6\{4d36e96e-...}\0004</c>.
    /// </summary>
    /// <remarks>
    /// Unlike <c>\\.\DISPLAY1</c>, which is just an ordinal that shifts when
    /// outputs are rearranged, this encodes the EDID manufacturer and product
    /// code, so it is stable enough to key a cache on.
    /// </remarks>
    private static string DescribeMonitorDevice(string adapterDeviceName, uint index)
    {
        if (string.IsNullOrEmpty(adapterDeviceName))
        {
            return string.Empty;
        }

        NativeMethods.DISPLAY_DEVICEW device = default;
        device.cb = (uint)sizeof(NativeMethods.DISPLAY_DEVICEW);

        return NativeMethods.EnumDisplayDevicesW(adapterDeviceName, index, &device, 0)
            ? new string(device.DeviceID, 0, 128).TrimEnd('\0')
            : string.Empty;
    }

    private static (string DeviceName, bool IsPrimary) DescribeAdapter(nint hMonitor)
    {
        NativeMethods.MONITORINFOEXW info = default;
        info.cbSize = (uint)sizeof(NativeMethods.MONITORINFOEXW);

        if (!NativeMethods.GetMonitorInfoW(hMonitor, &info))
        {
            return (string.Empty, false);
        }

        string device = new string(info.szDevice, 0, 32).TrimEnd('\0');
        return (device, (info.dwFlags & NativeMethods.MONITORINFOF_PRIMARY) != 0);
    }

    [UnmanagedCallersOnly]
    private static int Callback(nint hMonitor, nint hdc, NativeMethods.RECT* clip, nint data)
    {
        if (GCHandle.FromIntPtr(data).Target is List<nint> list)
        {
            list.Add(hMonitor);
        }

        return 1;
    }
}
