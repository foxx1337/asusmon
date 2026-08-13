using AsusMon.Ddc;

namespace AsusMon.Monitors;

/// <summary>Which VCP feature carries a given GameVisual preset.</summary>
internal enum GameVisualFamily
{
    /// <summary>SDR presets, written to 0xDC (0xE3 on ProArt).</summary>
    Sdr,

    /// <summary>HDR10 presets, written to 0xE2 with a 0x01 high byte.</summary>
    Hdr10,

    /// <summary>Dolby Vision presets, written to 0xE2 with a 0x02 high byte.</summary>
    DolbyVision,
}

/// <summary>A single selectable GameVisual preset.</summary>
/// <param name="Id">Stable, lowercase CLI token, e.g. <c>fps</c>.</param>
/// <param name="Name">Display name as shown in the monitor OSD.</param>
/// <param name="Value">Value written to <see cref="Code"/>.</param>
/// <param name="Code">VCP feature that carries this preset.</param>
internal sealed record GameVisualMode(
    string Id,
    string Name,
    uint Value,
    byte Code,
    GameVisualFamily Family,
    string[]? Aliases = null)
{
    public bool Matches(string token) =>
        Id.Equals(token, StringComparison.OrdinalIgnoreCase) ||
        Name.Equals(token, StringComparison.OrdinalIgnoreCase) ||
        (Aliases?.Any(a => a.Equals(token, StringComparison.OrdinalIgnoreCase)) ?? false);
}

/// <summary>
/// GameVisual / Splendid preset tables, transcribed from
/// DisplayWidgetCenter's <c>VCPAPI</c> and its <c>AppConfig\DisplayModeCapability_*</c>
/// data files.
/// </summary>
internal static class GameVisualCatalog
{
    private static readonly GameVisualMode[] Gaming =
    [
        new("cinema",      "Cinema",       0x01, Vcp.GameVisual, GameVisualFamily.Sdr),
        new("scenery",     "Scenery",      0x02, Vcp.GameVisual, GameVisualFamily.Sdr),
        new("srgb",        "sRGB",         0x03, Vcp.GameVisual, GameVisualFamily.Sdr),
        new("user",        "User",         0x04, Vcp.GameVisual, GameVisualFamily.Sdr),
        new("racing",      "Racing",       0x05, Vcp.GameVisual, GameVisualFamily.Sdr),
        new("rts",         "RTS/RPG",      0x06, Vcp.GameVisual, GameVisualFamily.Sdr, ["rpg", "rts-rpg"]),
        new("fps",         "FPS",          0x07, Vcp.GameVisual, GameVisualFamily.Sdr),
        new("moba",        "MOBA",         0x08, Vcp.GameVisual, GameVisualFamily.Sdr),
        new("nightvision", "Night Vision", 0x09, Vcp.GameVisual, GameVisualFamily.Sdr, ["night"]),
        new("srgbcal",     "sRGB Cal",     0x0A, Vcp.GameVisual, GameVisualFamily.Sdr, ["srgb-cal"]),
    ];

    private static readonly GameVisualMode[] MainStream =
    [
        new("theater",     "Theater",      0x01, Vcp.GameVisual, GameVisualFamily.Sdr),
        new("scenery",     "Scenery",      0x02, Vcp.GameVisual, GameVisualFamily.Sdr),
        new("srgb",        "sRGB",         0x03, Vcp.GameVisual, GameVisualFamily.Sdr),
        new("standard",    "Standard",     0x04, Vcp.GameVisual, GameVisualFamily.Sdr, ["std"]),
        new("game",        "Game",         0x05, Vcp.GameVisual, GameVisualFamily.Sdr),
        new("nightview",   "Night View",   0x06, Vcp.GameVisual, GameVisualFamily.Sdr),
        new("reading",     "Reading",      0x07, Vcp.GameVisual, GameVisualFamily.Sdr),
        new("darkroom",    "Darkroom",     0x08, Vcp.GameVisual, GameVisualFamily.Sdr, ["dark"]),
        new("nightvision", "Night Vision", 0x09, Vcp.GameVisual, GameVisualFamily.Sdr, ["night"]),
    ];

    private static readonly GameVisualMode[] Portable =
    [
        new("theater",   "Theater",    0x01, Vcp.GameVisual, GameVisualFamily.Sdr),
        new("scenery",   "Scenery",    0x02, Vcp.GameVisual, GameVisualFamily.Sdr),
        new("srgb",      "sRGB",       0x03, Vcp.GameVisual, GameVisualFamily.Sdr),
        new("standard",  "Standard",   0x04, Vcp.GameVisual, GameVisualFamily.Sdr, ["std"]),
        new("game",      "Game",       0x05, Vcp.GameVisual, GameVisualFamily.Sdr),
        new("nightview", "Night View", 0x06, Vcp.GameVisual, GameVisualFamily.Sdr),
        new("reading",   "Reading",    0x07, Vcp.GameVisual, GameVisualFamily.Sdr),
        new("darkroom",  "Darkroom",   0x08, Vcp.GameVisual, GameVisualFamily.Sdr, ["dark"]),
    ];

    /// <summary>
    /// ProArt writes the preset index to 0xE3 in the high byte. Untested here —
    /// transcribed from DisplayWidgetCenter for completeness.
    /// </summary>
    private static readonly GameVisualMode[] ProArt =
    [
        new("native",    "Native",      0x0100, Vcp.GameVisualProArt, GameVisualFamily.Sdr),
        new("srgb",      "sRGB",        0x0500, Vcp.GameVisualProArt, GameVisualFamily.Sdr),
        new("adobergb",  "Adobe RGB",   0x0800, Vcp.GameVisualProArt, GameVisualFamily.Sdr),
        new("rec2020",   "Rec.2020",    0x0900, Vcp.GameVisualProArt, GameVisualFamily.Sdr),
        new("dcip3",     "DCI-P3",      0x0A00, Vcp.GameVisualProArt, GameVisualFamily.Sdr, ["dci-p3"]),
        new("dicom",     "DICOM",       0x0B00, Vcp.GameVisualProArt, GameVisualFamily.Sdr),
        new("rec709",    "Rec.709",     0x0E00, Vcp.GameVisualProArt, GameVisualFamily.Sdr),
        new("user1",     "User 1",      0x1600, Vcp.GameVisualProArt, GameVisualFamily.Sdr),
        new("user2",     "User 2",      0x1700, Vcp.GameVisualProArt, GameVisualFamily.Sdr),
        new("user3",     "User 3",      0x1E00, Vcp.GameVisualProArt, GameVisualFamily.Sdr),
        new("displayp3", "Display P3",  0x1F00, Vcp.GameVisualProArt, GameVisualFamily.Sdr),
        new("mmodelp3",  "M Model P3",  0x2000, Vcp.GameVisualProArt, GameVisualFamily.Sdr),
    ];

    /// <summary>
    /// HDR presets live on 0xE2 as 16-bit values: high byte selects the family
    /// (0x01 HDR10, 0x02 Dolby Vision), low byte the preset.
    /// </summary>
    private static readonly GameVisualMode[] Hdr =
    [
        new("hdr-cinema",  "Cinema HDR",                 0x0101, Vcp.HdrMode, GameVisualFamily.Hdr10),
        new("hdr-gaming",  "Gaming HDR",                 0x0102, Vcp.HdrMode, GameVisualFamily.Hdr10),
        new("hdr-console", "Console HDR",                0x0103, Vcp.HdrMode, GameVisualFamily.Hdr10),
        new("hdr400",      "DisplayHDR 400 True Black",  0x0104, Vcp.HdrMode, GameVisualFamily.Hdr10),
        new("hdr500",      "DisplayHDR 500 True Black",  0x0108, Vcp.HdrMode, GameVisualFamily.Hdr10),
        new("dv-bright",   "Dolby Vision Bright",        0x0205, Vcp.HdrMode, GameVisualFamily.DolbyVision),
        new("dv-dark",     "Dolby Vision Dark",          0x0206, Vcp.HdrMode, GameVisualFamily.DolbyVision),
        new("dv-gaming",   "Dolby Vision Gaming",        0x0207, Vcp.HdrMode, GameVisualFamily.DolbyVision),
        new("dv-source",   "Dolby Vision (Source-Only)", 0x0208, Vcp.HdrMode, GameVisualFamily.DolbyVision),
    ];

    /// <summary>Every preset the given product line could theoretically expose.</summary>
    public static IReadOnlyList<GameVisualMode> ForProductLine(ProductLine line)
    {
        GameVisualMode[] sdr = line switch
        {
            ProductLine.Gaming => Gaming,
            ProductLine.MainStream => MainStream,
            ProductLine.Portable => Portable,
            ProductLine.ProArt => ProArt,
            _ => Gaming,
        };

        return [.. sdr, .. Hdr];
    }

    /// <summary>
    /// Narrows the catalog to what this particular panel advertises in its
    /// capability string.
    /// </summary>
    /// <remarks>
    /// When the capability string is unavailable the full table is returned, so
    /// a panel that refuses capability requests is still controllable. Callers
    /// are expected to have confirmed the panel is an ASUS one first.
    /// </remarks>
    public static IReadOnlyList<GameVisualMode> ForMonitor(ProductLine line, MonitorCapabilities? caps)
    {
        IReadOnlyList<GameVisualMode> all = ForProductLine(line);

        if (caps is null)
        {
            return all;
        }

        List<GameVisualMode> supported = [];

        foreach (GameVisualMode mode in all)
        {
            if (caps.Supports(mode.Code) && IsValueDeclared(caps.ValuesFor(mode.Code), mode.Value))
            {
                supported.Add(mode);
            }
        }

        return supported;
    }

    /// <summary>
    /// Tests a preset value against a capability value list.
    /// </summary>
    /// <remarks>
    /// Continuous features declare no values at all. For 16-bit ASUS features
    /// the panel enumerates the two halves separately — the family high bytes
    /// (<c>0000 0100 0200</c>) and the sub-mode low bytes (<c>01 02 ... 07</c>)
    /// appear as independent tokens rather than as combined words. A composite
    /// value is therefore supported when both of its halves are declared.
    /// </remarks>
    private static bool IsValueDeclared(IReadOnlyList<uint> declared, uint value)
    {
        if (declared.Count == 0)
        {
            return true;
        }

        if (declared.Contains(value))
        {
            return true;
        }

        if (value <= 0xFF)
        {
            return false;
        }

        uint family = value & 0xFF00;
        uint subMode = value & 0x00FF;

        return declared.Contains(family) && declared.Contains(subMode);
    }

    public static GameVisualMode? Resolve(IReadOnlyList<GameVisualMode> modes, string token)
    {
        foreach (GameVisualMode mode in modes)
        {
            if (mode.Matches(token))
            {
                return mode;
            }
        }

        // Allow a raw numeric value: "7", "0x07", "0x0102".
        if (TryParseNumber(token, out uint value))
        {
            foreach (GameVisualMode mode in modes)
            {
                if (mode.Value == value)
                {
                    return mode;
                }
            }
        }

        return null;
    }

    public static bool TryParseNumber(string token, out uint value)
    {
        if (token.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return uint.TryParse(token.AsSpan(2), System.Globalization.NumberStyles.HexNumber, null, out value);
        }

        return uint.TryParse(token, out value);
    }
}
