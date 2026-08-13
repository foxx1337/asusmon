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
    public const byte OsdInstruction = 0xEB;
    public const byte VendorId = 0xEF;
    public const byte AuraSync = 0xF2;
    public const byte Kvm = 0xF3;
    public const byte PxpMode = 0xF4;
    public const byte ToggleSettings1 = 0xFC;
    public const byte ToggleSettings2 = 0xFD;
}

/// <summary>One named value of a discrete VCP feature, e.g. Shadow Boost Level 2.</summary>
internal sealed record EnumOption(string Id, string Name, uint Value, string[]? Aliases = null)
{
    public bool Matches(string token) =>
        Id.Equals(token, StringComparison.OrdinalIgnoreCase) ||
        Name.Equals(token, StringComparison.OrdinalIgnoreCase) ||
        Name.Replace(" ", string.Empty).Equals(token, StringComparison.OrdinalIgnoreCase) ||
        (Aliases?.Any(a => a.Equals(token, StringComparison.OrdinalIgnoreCase)) ?? false);
}

/// <summary>
/// Shadow Boost, VCP 0xE5. Lifts shadow detail without washing out midtones.
/// Names come from the combo box DisplayWidgetCenter builds in
/// <c>GameVisualPage</c>; the value mapping is confirmed by
/// <c>CapabilityConfigManager.GetShadowBoostConfig</c>, which keys on "e5".
/// </summary>
internal static class ShadowBoost
{
    public const byte Code = Vcp.ShadowBoost;

    /// <summary>Tokens accepted in place of <c>shadowboost</c> after <c>set</c>.</summary>
    public static readonly string[] VerbAliases = ["shadowboost", "shadow-boost", "shadow", "sb"];

    public static bool IsVerb(string token) =>
        VerbAliases.Contains(token, StringComparer.OrdinalIgnoreCase);

    public static readonly EnumOption[] Options =
    [
        new("off",     "Off",                 0x00, ["0", "none"]),
        new("level1",  "Level 1",             0x01, ["1", "l1", "low"]),
        new("level2",  "Level 2",             0x02, ["2", "l2", "medium", "mid"]),
        new("level3",  "Level 3",             0x03, ["3", "l3", "high"]),
        new("dynamic", "Dynamic Adjustment",  0x04, ["4", "dyn", "auto"]),
    ];

    /// <summary>
    /// GameVisual presets in which the panel reports Shadow Boost as absent,
    /// matching <c>AppConfig\DisplayModeCapability_Gaming</c>, where the
    /// <c>ShadowBoost</c> flag is False. Used only to explain the 0xFE reply.
    /// </summary>
    public static readonly string[] PresetsWithoutSupport = ["srgb", "srgbcal", "moba"];

    public static EnumOption? Resolve(string token) =>
        Options.FirstOrDefault(o => o.Matches(token));
}

/// <summary>
/// OSD Instruction, VCP 0xEB. A write-only command register: the panel acts on
/// each write as though the matching front-panel control had been pressed, so
/// there is nothing to read back. Names and ordinals come from
/// <c>VCPAPI.OSDOperate</c>, which <c>VCPAPI.SetEzOSD</c> writes to 235.
/// </summary>
internal static class Osd
{
    public const byte Code = Vcp.OsdInstruction;

    /// <summary>
    /// Buttons 1 and 2 fire whatever the OSD has assigned to them (GamePlus,
    /// GameVisual, Shadow Boost and so on); these press the key, they do not
    /// choose the function.
    /// </summary>
    public static readonly EnumOption[] Actions =
    [
        new("close",    "Close",             0x00, ["dismiss", "esc"]),
        new("show",     "Show",              0x01, ["open", "menu"]),
        new("up",       "Up",                0x02, ["u"]),
        new("down",     "Down",              0x03, ["d"]),
        new("right",    "Right",             0x04, ["r"]),
        new("left",     "Left",              0x05, ["l"]),
        new("enter",    "Enter",             0x06, ["press", "select", "ok"]),
        new("back",     "Back",              0x07, ["cancel"]),
        new("input",    "Input Select",      0x08, ["source", "inputselect"]),
        new("quickfit", "QuickFit",          0x09, ["qf"]),
        new("button1",  "Shortcut 1",        0x0A, ["shortcut1", "key1", "b1"]),
        new("button2",  "Shortcut 2",        0x0B, ["shortcut2", "key2", "b2"]),
        new("selfcal",  "Self Calibration",  0x0C, ["selfcalibration"]),
    ];

    public static EnumOption? Resolve(string token) =>
        Actions.FirstOrDefault(a => a.Matches(token));
}

/// <summary>A continuous 0..max VCP feature that <c>set</c> can drive directly.</summary>
internal sealed record LevelFeature(string Id, string Name, byte Code, string[] Aliases)
{
    public static readonly LevelFeature Brightness =
        new("brightness", "Brightness", Vcp.Brightness, ["bright", "lum", "luminance"]);

    public static readonly LevelFeature Contrast =
        new("contrast", "Contrast", Vcp.Contrast, ["cont"]);

    public static readonly IReadOnlyList<LevelFeature> All = [Brightness, Contrast];

    /// <summary>Matches a command-line token against the id or any alias.</summary>
    public static LevelFeature? Resolve(string token)
    {
        foreach (LevelFeature feature in All)
        {
            if (string.Equals(feature.Id, token, StringComparison.OrdinalIgnoreCase) ||
                feature.Aliases.Contains(token, StringComparer.OrdinalIgnoreCase))
            {
                return feature;
            }
        }

        return null;
    }
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
