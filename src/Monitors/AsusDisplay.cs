using AsusMon.Ddc;

namespace AsusMon.Monitors;

/// <summary>A continuous VCP feature reading, e.g. brightness 0-100.</summary>
internal readonly record struct Reading(uint Current, uint Maximum)
{
    public override string ToString() => $"{Current} / {Maximum}";
}

/// <summary>Everything the CLI and the GUI display about one monitor.</summary>
internal sealed class DisplaySummary
{
    public required int Index { get; init; }

    public required string Description { get; init; }

    public required string DeviceName { get; init; }

    public required bool IsPrimary { get; init; }

    public string? Model { get; init; }

    public string? MccsVersion { get; init; }

    public ProductLine ProductLine { get; init; }

    /// <summary>Raw value of 0xEF, the vendor probe DisplayWidgetCenter uses.</summary>
    public uint? VendorCode { get; init; }

    public bool IsAsus { get; init; }

    public bool IsHdrActive { get; init; }

    public GameVisualMode? CurrentMode { get; init; }

    /// <summary>Raw current value of the feature that carries the preset.</summary>
    public uint? CurrentModeValue { get; init; }

    public IReadOnlyList<GameVisualMode> AvailableModes { get; init; } = [];

    public Reading? Brightness { get; init; }

    public Reading? Contrast { get; init; }

    public Reading? Sharpness { get; init; }

    public Reading? Volume { get; init; }

    public uint? InputSource { get; init; }

    public string? InputSourceName => InputSource is { } source ? InputSourceNames.Describe(source) : null;

    public string? Capabilities { get; init; }
}

/// <summary>
/// Reads and writes ASUS monitor state over DDC/CI.
/// </summary>
internal sealed class AsusDisplay : IDisposable
{
    private readonly DdcMonitor _monitor;
    private readonly CapabilityCache? _cache;
    private MonitorCapabilities? _capabilities;
    private bool _capabilitiesLoaded;
    private uint? _vendorCode;
    private bool _vendorCodeLoaded;
    private bool _hdrActive;
    private bool _hdrLoaded;

    public AsusDisplay(DdcMonitor monitor, int index, CapabilityCache? cache = null)
    {
        _monitor = monitor;
        Index = index;
        _cache = cache;
    }

    public int Index { get; }

    public string Description => _monitor.Description;

    public string DeviceName => _monitor.DeviceName;

    public bool IsPrimary => _monitor.IsPrimary;

    /// <summary>True when the capability string came from the on-disk cache.</summary>
    public bool CapabilitiesFromCache { get; private set; }

    /// <summary>
    /// The capability string, resolved once per process.
    /// </summary>
    /// <remarks>
    /// Reading this from the panel costs several seconds, so the on-disk cache
    /// is consulted first. The string is fixed in firmware, so a stale entry is
    /// only possible after a monitor firmware update — <c>--refresh</c> exists
    /// for that case.
    /// </remarks>
    public MonitorCapabilities? Capabilities
    {
        get
        {
            if (_capabilitiesLoaded)
            {
                return _capabilities;
            }

            _capabilitiesLoaded = true;

            string hardwareId = _monitor.HardwareId;

            if (_cache is { } cache && cache.TryGet(hardwareId, out string? cached))
            {
                CapabilitiesFromCache = true;
                MonitorCapabilities.TryParse(cached, out _capabilities);
                return _capabilities;
            }

            string? raw = _monitor.TryGetCapabilities();
            MonitorCapabilities.TryParse(raw, out _capabilities);
            _cache?.Store(hardwareId, Description, raw);

            return _capabilities;
        }
    }

    public string? Model => Capabilities?.Model ?? GuessModelFromDescription();

    public ProductLine ProductLine => ProductLineResolver.FromModel(Model);

    /// <summary>
    /// Vendor probe, cached. DisplayWidgetCenter reads 0xEF and matches the
    /// value against a whitelist of ASUS ranges before unlocking its feature
    /// pages; a non-ASUS panel answers 0 or fails the read outright.
    /// </summary>
    public uint? ReadVendorCode()
    {
        if (!_vendorCodeLoaded)
        {
            _vendorCodeLoaded = true;
            _vendorCode = _monitor.TryGetVcp(Vcp.VendorId, out VcpReading reading) ? reading.Current : null;
        }

        return _vendorCode;
    }

    /// <summary>
    /// True when the panel identifies itself as an ASUS display that speaks the
    /// vendor VCP dialect. GameVisual is only meaningful on these.
    /// </summary>
    public bool IsAsus => ReadVendorCode() is { } code && IsAsusVendorCode(code);

    public static bool IsAsusVendorCode(uint code) => code switch
    {
        0 => false,
        26 => true,
        >= 85 and <= 88 => true,
        >= 102 and <= 105 => true,
        >= 119 and <= 122 => true,
        >= 136 and <= 139 => true,
        >= 153 and <= 156 => true,
        >= 160 and <= 163 => true,
        >= 177 and <= 180 => true,
        >= 194 and <= 197 => true,
        >= 211 and <= 214 => true,
        >= 228 and <= 231 => true,
        >= 245 and <= 248 => true,
        _ => false,
    };

    public Reading? Read(byte code) =>
        _monitor.TryGetVcp(code, out VcpReading reading)
            ? new Reading(reading.Current, reading.Maximum)
            : null;

    public bool Write(byte code, uint value) => _monitor.TrySetVcp(code, value);

    /// <summary>
    /// GameVisual presets this panel actually offers. Empty for anything that
    /// is not an ASUS display, since 0xDC and 0xE2 are vendor-defined and mean
    /// something else — or nothing — elsewhere.
    /// </summary>
    public IReadOnlyList<GameVisualMode> AvailableModes =>
        IsAsus ? GameVisualCatalog.ForMonitor(ProductLine, Capabilities) : [];

    /// <summary>
    /// True when the panel reports an active HDR pipeline, in which case
    /// presets are carried by 0xE2 rather than 0xDC. 0xFE is the sentinel
    /// DisplayWidgetCenter treats as "no HDR support".
    /// </summary>
    public bool IsHdrActive
    {
        get
        {
            if (!_hdrLoaded)
            {
                _hdrLoaded = true;
                _hdrActive = IsAsus && Read(Vcp.HdrMode) is { Current: > 0 and not 0xFE };
            }

            return _hdrActive;
        }
    }

    /// <summary>Resolves the preset currently selected on the monitor.</summary>
    public (GameVisualMode? Mode, uint? RawValue) ReadCurrentMode()
    {
        IReadOnlyList<GameVisualMode> modes = AvailableModes;

        if (!IsAsus)
        {
            return (null, null);
        }

        byte code = IsHdrActive
            ? Vcp.HdrMode
            : ProductLine == ProductLine.ProArt ? Vcp.GameVisualProArt : Vcp.GameVisual;

        if (Read(code) is not { } reading)
        {
            return (null, null);
        }

        GameVisualMode? match = modes.FirstOrDefault(
            m => m.Code == code && m.Value == reading.Current);

        return (match, reading.Current);
    }

    /// <summary>
    /// Applies a preset and reads the value back to confirm the panel accepted
    /// it. DDC/CI writes are fire-and-forget, so read-back is the only proof.
    /// </summary>
    public bool ApplyMode(GameVisualMode mode, out uint readBack)
    {
        uint value = mode.Value;

        // DisplayWidgetCenter remaps sRGB to the calibrated preset when the
        // panel ships an sRGB Cal mode instead of plain sRGB.
        if (mode is { Family: GameVisualFamily.Sdr, Value: 0x03 } &&
            ProductLine == ProductLine.Gaming &&
            Capabilities is { } caps &&
            caps.ValuesFor(Vcp.GameVisual) is { Count: > 0 } declared &&
            !declared.Contains(0x03u) &&
            declared.Contains(0x0Au))
        {
            value = 0x0A;
        }

        if (!Write(mode.Code, value))
        {
            readBack = 0;
            return false;
        }

        Thread.Sleep(150);

        readBack = Read(mode.Code)?.Current ?? uint.MaxValue;
        return readBack == value;
    }

    /// <summary>
    /// Writes a continuous feature (brightness, contrast, ...) and reads it
    /// back. Panels settle faster on these than on preset switches, but the
    /// read-back is still the only confirmation DDC/CI offers.
    /// </summary>
    public bool ApplyLevel(byte code, uint value, out uint readBack)
    {
        if (!Write(code, value))
        {
            readBack = uint.MaxValue;
            return false;
        }

        Thread.Sleep(60);

        readBack = Read(code)?.Current ?? uint.MaxValue;
        return readBack == value;
    }

    public DisplaySummary Summarize(bool includeCapabilities = false)
    {
        uint? vendor = ReadVendorCode();
        (GameVisualMode? mode, uint? raw) = ReadCurrentMode();

        return new DisplaySummary
        {
            Index = Index,
            Description = Description,
            DeviceName = DeviceName,
            IsPrimary = IsPrimary,
            Model = Model,
            MccsVersion = Capabilities?.MccsVersion,
            ProductLine = ProductLine,
            VendorCode = vendor,
            IsAsus = IsAsus,
            IsHdrActive = IsHdrActive,
            CurrentMode = mode,
            CurrentModeValue = raw,
            AvailableModes = AvailableModes,
            Brightness = Read(Vcp.Brightness),
            Contrast = Read(Vcp.Contrast),
            Sharpness = Read(Vcp.Sharpness),
            Volume = Read(Vcp.AudioVolume),
            InputSource = Read(Vcp.InputSource)?.Current,
            Capabilities = includeCapabilities ? Capabilities?.Raw : null,
        };
    }

    /// <summary>
    /// Last resort when the panel refuses a capability request: derive a model
    /// token from the driver description, e.g. "ROG SWIFT PG32UCWM".
    /// </summary>
    private string? GuessModelFromDescription()
    {
        foreach (string part in Description.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (part.Length >= 4 &&
                char.IsLetter(part[0]) &&
                char.IsLetter(part[1]) &&
                part.Any(char.IsDigit))
            {
                return part;
            }
        }

        return null;
    }

    public void Dispose() => _monitor.Dispose();
}

/// <summary>MCCS input source names (feature 0x60).</summary>
internal static class InputSourceNames
{
    public static string Describe(uint value) => value switch
    {
        0x01 => "Analog 1",
        0x02 => "Analog 2",
        0x03 => "DVI 1",
        0x04 => "DVI 2",
        0x05 => "Composite 1",
        0x06 => "Composite 2",
        0x07 => "S-Video 1",
        0x08 => "S-Video 2",
        0x09 => "Tuner 1",
        0x0A => "Tuner 2",
        0x0B => "Tuner 3",
        0x0C => "Component 1",
        0x0D => "Component 2",
        0x0E => "Component 3",
        0x0F => "DisplayPort 1",
        0x10 => "DisplayPort 2",
        0x11 => "HDMI 1",
        0x12 => "HDMI 2",
        0x13 => "HDMI 3",
        0x14 => "HDMI 4",
        0x15 => "Thunderbolt 1",
        0x16 => "Thunderbolt 2",
        0x17 => "Thunderbolt 3",
        0x18 => "Thunderbolt 4",
        0x1A => "USB Type-C 1",
        0x1B => "USB Type-C 2",
        0x1C => "USB Type-C 3",
        0x1D => "USB Type-C 4",
        _ => $"0x{value:X2}",
    };
}

/// <summary>Opens every DDC/CI-capable monitor attached to the system.</summary>
internal static class DisplayCatalog
{
    public static List<AsusDisplay> Open(CapabilityCache? cache = null)
    {
        List<AsusDisplay> displays = [];
        int index = 0;

        foreach (DdcMonitor monitor in MonitorEnumerator.Enumerate())
        {
            displays.Add(new AsusDisplay(monitor, index++, cache));
        }

        return displays;
    }
}
