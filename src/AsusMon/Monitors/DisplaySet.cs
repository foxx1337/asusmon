using AsusMon.Ddc;

namespace AsusMon.Monitors;

/// <summary>
/// Owns the set of open monitor handles for the lifetime of one command and
/// provides index-based selection.
/// </summary>
internal sealed class DisplaySet : IDisposable
{
    private readonly List<AsusDisplay> _displays;

    private DisplaySet(List<AsusDisplay> displays) => _displays = displays;

    public int Count => _displays.Count;

    public IReadOnlyList<AsusDisplay> All => _displays;

    public static DisplaySet Open() => new(DisplayCatalog.Open());

    /// <summary>
    /// The monitor at <paramref name="index"/>, or the first ASUS panel when no
    /// index was given (falling back to the first monitor of any kind).
    /// </summary>
    public AsusDisplay? Select(int? index)
    {
        if (index is { } i)
        {
            return i >= 0 && i < _displays.Count ? _displays[i] : null;
        }

        foreach (AsusDisplay display in _displays)
        {
            if (display.ReadVendorCode() is { } code && AsusDisplay.IsAsusVendorCode(code))
            {
                return display;
            }
        }

        return _displays.FirstOrDefault();
    }

    /// <summary>
    /// All monitors, or just the indexed one when a filter was supplied.
    /// </summary>
    public IEnumerable<AsusDisplay> Where(int? index)
    {
        if (index is null)
        {
            return _displays;
        }

        AsusDisplay? single = Select(index);
        return single is null ? [] : [single];
    }

    public void Dispose()
    {
        foreach (AsusDisplay display in _displays)
        {
            display.Dispose();
        }
    }
}
