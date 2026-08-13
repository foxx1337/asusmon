namespace AsusMon.Monitors;

/// <summary>
/// VCP feature codes used by this tool. Codes below 0xE0 are standard MCCS;
/// 0xE0 and above are the ASUS vendor-specific range.
/// </summary>
internal static class Vcp
{
    // Standard MCCS.
    public const byte Brightness = 0x10;
    public const byte Contrast = 0x12;
    public const byte ColorPreset = 0x14;
    public const byte RedGain = 0x16;
    public const byte GreenGain = 0x18;
    public const byte BlueGain = 0x1A;
    public const byte InputSource = 0x60;
    public const byte AudioVolume = 0x62;
    public const byte Gamma = 0x72;
    public const byte Sharpness = 0x87;
    public const byte Saturation = 0x8A;
    public const byte Hue = 0x90;
    public const byte PowerMode = 0xD6;

    // ASUS vendor range.
    public const byte GameVisual = 0xDC;
    public const byte OverDrive = 0xE0;
    public const byte HdrMode = 0xE2;
    public const byte GameVisualProArt = 0xE3;
    public const byte ShadowBoost = 0xE5;
    public const byte BlueLightFilter = 0xE6;
    public const byte VendorId = 0xEF;
    public const byte AuraSync = 0xF2;
    public const byte Kvm = 0xF3;
    public const byte PxpMode = 0xF4;
    public const byte ToggleSettings1 = 0xFC;
    public const byte ToggleSettings2 = 0xFD;
}

/// <summary>ASUS monitor families. Each has its own GameVisual code table.</summary>
internal enum ProductLine
{
    Unknown,
    Gaming,
    MainStream,
    Portable,
    ProArt,
}

internal static class ProductLineResolver
{
    /// <summary>
    /// Mirrors DisplayWidgetCenter's model-prefix classification.
    /// </summary>
    public static ProductLine FromModel(string? model)
    {
        if (string.IsNullOrWhiteSpace(model) || model.Length < 2)
        {
            return ProductLine.Unknown;
        }

        ReadOnlySpan<char> prefix = model.AsSpan(0, 2);

        if (prefix.Equals("MB", StringComparison.OrdinalIgnoreCase) ||
            prefix.Equals("MQ", StringComparison.OrdinalIgnoreCase))
        {
            return ProductLine.Portable;
        }

        if (prefix.Equals("PA", StringComparison.OrdinalIgnoreCase) ||
            prefix.Equals("PQ", StringComparison.OrdinalIgnoreCase))
        {
            return ProductLine.ProArt;
        }

        if (prefix.Equals("VG", StringComparison.OrdinalIgnoreCase) ||
            prefix.Equals("PG", StringComparison.OrdinalIgnoreCase) ||
            prefix.Equals("XG", StringComparison.OrdinalIgnoreCase))
        {
            return ProductLine.Gaming;
        }

        foreach (string mainstream in (string[])["VX", "VP", "VY", "VZ", "VU", "VA", "VT", "BE"])
        {
            if (prefix.Equals(mainstream, StringComparison.OrdinalIgnoreCase))
            {
                return ProductLine.MainStream;
            }
        }

        return ProductLine.Unknown;
    }
}
