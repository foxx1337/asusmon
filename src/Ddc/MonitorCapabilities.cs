using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace AsusMon.Ddc;

/// <summary>
/// Parsed form of an MCCS capability string, e.g.
/// <c>(prot(monitor)type(LCD)model(PG32UCWM)...vcp(02 04 ... DC(01 02 04) ...))</c>.
/// </summary>
/// <remarks>
/// The <c>vcp(...)</c> section is the authoritative, per-unit list of supported
/// feature codes and — for non-continuous features — their legal values. Reading
/// it is what lets this tool adapt to a panel instead of hard-coding a model.
/// </remarks>
internal sealed class MonitorCapabilities
{
    private readonly Dictionary<byte, uint[]> _vcp;

    private MonitorCapabilities(string raw, string? model, string? mccsVersion, Dictionary<byte, uint[]> vcp)
    {
        Raw = raw;
        Model = model;
        MccsVersion = mccsVersion;
        _vcp = vcp;
    }

    public string Raw { get; }

    public string? Model { get; }

    public string? MccsVersion { get; }

    public IReadOnlyCollection<byte> SupportedCodes => _vcp.Keys;

    public bool Supports(byte code) => _vcp.ContainsKey(code);

    /// <summary>
    /// Values declared for <paramref name="code"/>. Empty for continuous
    /// features (brightness and friends), which declare a range instead.
    /// </summary>
    public IReadOnlyList<uint> ValuesFor(byte code) =>
        _vcp.TryGetValue(code, out uint[]? values) ? values : [];

    public static MonitorCapabilities Parse(string raw)
    {
        string? model = ExtractSection(raw, "model");
        string? mccs = ExtractSection(raw, "mccs_ver");
        string? vcpSection = ExtractSection(raw, "vcp");

        Dictionary<byte, uint[]> vcp = vcpSection is null
            ? []
            : ParseVcpSection(vcpSection);

        return new MonitorCapabilities(raw, model, mccs, vcp);
    }

    /// <summary>
    /// Pulls the balanced-parenthesis body of <c>name(...)</c>. The name must
    /// not be preceded by a hex digit, so that looking for "vcp" does not match
    /// the tail of some other token.
    /// </summary>
    private static string? ExtractSection(string raw, string name)
    {
        int search = 0;

        while (true)
        {
            int start = raw.IndexOf(name + "(", search, StringComparison.OrdinalIgnoreCase);
            if (start < 0)
            {
                return null;
            }

            bool boundary = start == 0 || !char.IsLetterOrDigit(raw[start - 1]) && raw[start - 1] != '_';

            if (!boundary)
            {
                search = start + 1;
                continue;
            }

            int open = start + name.Length;
            int depth = 0;

            for (int i = open; i < raw.Length; i++)
            {
                if (raw[i] == '(')
                {
                    depth++;
                }
                else if (raw[i] == ')')
                {
                    depth--;
                    if (depth == 0)
                    {
                        return raw[(open + 1)..i];
                    }
                }
            }

            return null;
        }
    }

    /// <summary>
    /// Tokenizes a vcp body: whitespace-separated hex codes, each optionally
    /// followed by a parenthesized list of legal values. Values may be 2 hex
    /// digits (byte) or 4 (word, as ASUS uses for its 16-bit features).
    /// </summary>
    private static Dictionary<byte, uint[]> ParseVcpSection(string body)
    {
        Dictionary<byte, uint[]> result = [];
        int i = 0;

        while (i < body.Length)
        {
            while (i < body.Length && !Uri.IsHexDigit(body[i]))
            {
                i++;
            }

            int start = i;
            while (i < body.Length && Uri.IsHexDigit(body[i]))
            {
                i++;
            }

            if (i == start)
            {
                break;
            }

            if (!TryParseHex(body.AsSpan(start, i - start), out uint code) || code > byte.MaxValue)
            {
                continue;
            }

            uint[] values = [];

            if (i < body.Length && body[i] == '(')
            {
                int close = body.IndexOf(')', i);
                if (close > i)
                {
                    values = ParseValueList(body.AsSpan(i + 1, close - i - 1));
                    i = close + 1;
                }
            }

            result[(byte)code] = values;
        }

        return result;
    }

    private static uint[] ParseValueList(ReadOnlySpan<char> span)
    {
        List<uint> values = [];

        foreach (Range range in span.Split(' '))
        {
            ReadOnlySpan<char> token = span[range].Trim();

            if (!token.IsEmpty && TryParseHex(token, out uint value))
            {
                values.Add(value);
            }
        }

        return [.. values];
    }

    private static bool TryParseHex(ReadOnlySpan<char> span, out uint value) =>
        uint.TryParse(span, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);

    public static bool TryParse(string? raw, [NotNullWhen(true)] out MonitorCapabilities? capabilities)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            capabilities = null;
            return false;
        }

        capabilities = Parse(raw);
        return true;
    }
}
