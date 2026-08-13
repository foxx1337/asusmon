using System.Runtime.InteropServices;

namespace AsusMon.Ddc;

/// <summary>
/// Owns a physical monitor handle produced by
/// <c>GetPhysicalMonitorsFromHMONITOR</c> and releases it through
/// <c>DestroyPhysicalMonitor</c>.
/// </summary>
internal sealed class PhysicalMonitorHandle : SafeHandle
{
    public PhysicalMonitorHandle(nint handle)
        : base(nint.Zero, ownsHandle: true)
    {
        SetHandle(handle);
    }

    public override bool IsInvalid => handle == nint.Zero;

    protected override bool ReleaseHandle() => NativeMethods.DestroyPhysicalMonitor(handle);
}
