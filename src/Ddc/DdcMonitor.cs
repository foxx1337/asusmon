using System.Runtime.InteropServices;
using System.Text;

namespace AsusMon.Ddc;

/// <summary>Result of a VCP read.</summary>
internal readonly record struct VcpReading(uint Current, uint Maximum);

/// <summary>
/// A single physical monitor reachable over DDC/CI.
/// </summary>
/// <remarks>
/// DDC/CI is an unacknowledged I2C protocol with no flow control, so every
/// transaction in the process is serialized behind one gate and paced apart.
/// Back-to-back requests are silently dropped by most panels; ASUS' own
/// DisplayWidgetCenter does exactly the same (a global lock plus a 20 ms sleep
/// before each call).
/// </remarks>
internal sealed class DdcMonitor : IDisposable
{
    /// <summary>Serializes access to the I2C bus across all monitors.</summary>
    private static readonly Lock BusGate = new();

    /// <summary>Minimum spacing between consecutive DDC/CI transactions.</summary>
    private static readonly TimeSpan Pacing = TimeSpan.FromMilliseconds(30);

    private static long _lastTransactionTicks;

    private readonly PhysicalMonitorHandle _handle;

    internal DdcMonitor(
        PhysicalMonitorHandle handle,
        string description,
        string deviceName,
        bool isPrimary,
        string hardwareId)
    {
        _handle = handle;
        Description = description;
        DeviceName = deviceName;
        IsPrimary = isPrimary;
        HardwareId = hardwareId;
    }

    /// <summary>Driver-supplied description, e.g. <c>ROG SWIFT PG32UCWM</c>.</summary>
    public string Description { get; }

    /// <summary>GDI device name, e.g. <c>\\.\DISPLAY1</c>.</summary>
    public string DeviceName { get; }

    /// <summary>
    /// Stable PNP identity of the panel, used to key the capability cache.
    /// Empty when it could not be determined.
    /// </summary>
    public string HardwareId { get; }

    public bool IsPrimary { get; }

    public bool TryGetVcp(byte code, out VcpReading reading)
    {
        using (EnterBus())
        {
            if (NativeMethods.GetVCPFeatureAndVCPFeatureReply(
                    _handle.DangerousGetHandle(), code, nint.Zero, out uint current, out uint max))
            {
                reading = new VcpReading(current, max);
                return true;
            }
        }

        reading = default;
        return false;
    }

    public bool TrySetVcp(byte code, uint value)
    {
        using (EnterBus())
        {
            return NativeMethods.SetVCPFeature(_handle.DangerousGetHandle(), code, value);
        }
    }

    /// <summary>
    /// Retrieves the raw MCCS capability string. This is a slow transaction
    /// (the reply is reassembled from many small I2C reads) and can take a
    /// second or more.
    /// </summary>
    public unsafe string? TryGetCapabilities()
    {
        using (EnterBus())
        {
            nint h = _handle.DangerousGetHandle();

            if (!NativeMethods.GetCapabilitiesStringLength(h, out uint length) || length == 0)
            {
                return null;
            }

            byte[] buffer = new byte[length];
            fixed (byte* p = buffer)
            {
                if (!NativeMethods.CapabilitiesRequestAndCapabilitiesReply(h, p, length))
                {
                    return null;
                }
            }

            int end = Array.IndexOf(buffer, (byte)0);
            return Encoding.ASCII.GetString(buffer, 0, end < 0 ? buffer.Length : end);
        }
    }

    /// <summary>
    /// Takes the bus gate and pauses long enough that the previous transaction
    /// has drained before this one is issued.
    /// </summary>
    private static BusLease EnterBus()
    {
        BusGate.Enter();

        long elapsedMs = Environment.TickCount64 - Interlocked.Read(ref _lastTransactionTicks);
        long remainingMs = (long)Pacing.TotalMilliseconds - elapsedMs;

        if (remainingMs > 0)
        {
            Thread.Sleep((int)remainingMs);
        }

        return default;
    }

    private readonly struct BusLease : IDisposable
    {
        public void Dispose()
        {
            Interlocked.Exchange(ref _lastTransactionTicks, Environment.TickCount64);
            BusGate.Exit();
        }
    }

    public void Dispose() => _handle.Dispose();
}
