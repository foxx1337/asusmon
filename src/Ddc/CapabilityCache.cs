using System.Text.Json;
using System.Text.Json.Serialization;

namespace AsusMon.Ddc;

/// <summary>One cached capability string, keyed by the panel's PNP identity.</summary>
internal sealed class CapabilityCacheEntry
{
    /// <summary>PNP identity, e.g. <c>MONITOR\AUS32D6\{4d36e96e-...}\0004</c>.</summary>
    public string HardwareId { get; set; } = string.Empty;

    /// <summary>Kept only so the file is readable by a human.</summary>
    public string? Description { get; set; }

    /// <summary>
    /// The raw MCCS capability string, verbatim. Empty when the panel returned
    /// nothing, which is cached too so the slow read is not retried every run.
    /// </summary>
    public string Capabilities { get; set; } = string.Empty;

    public DateTimeOffset CachedUtc { get; set; }
}

internal sealed class CapabilityCacheFile
{
    public int Version { get; set; } = CapabilityCache.CurrentVersion;

    public List<CapabilityCacheEntry> Monitors { get; set; } = [];
}

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(CapabilityCacheFile))]
internal sealed partial class CapabilityCacheJsonContext : JsonSerializerContext;

/// <summary>
/// On-disk cache of MCCS capability strings.
/// </summary>
/// <remarks>
/// Fetching a capability string is by far the most expensive DDC/CI operation:
/// the reply is reassembled from ~32-byte I2C fragments with a mandated delay
/// between each, so a 1 KB string costs several seconds. The content is fixed
/// in the monitor's firmware and only changes with a firmware update, which
/// makes it ideal to cache.
/// <para>
/// Failures are swallowed throughout. A cache is an optimisation, and losing it
/// must never stop the tool from talking to a monitor.
/// </para>
/// </remarks>
internal sealed class CapabilityCache
{
    internal const int CurrentVersion = 1;

    private static readonly Lock Gate = new();

    private readonly string _path;
    private readonly bool _ignoreStored;
    private CapabilityCacheFile? _file;
    private bool _loaded;
    private bool _dirty;

    private CapabilityCache(string path, bool ignoreStored)
    {
        _path = path;
        _ignoreStored = ignoreStored;
    }

    /// <summary>
    /// The cache for this user, or <c>null</c> when caching is disabled or no
    /// writable location could be determined.
    /// </summary>
    /// <param name="enabled">False to bypass the cache entirely.</param>
    /// <param name="refresh">
    /// True to ignore what is stored and re-read from the monitors, while still
    /// writing the fresh results back.
    /// </param>
    public static CapabilityCache? Open(bool enabled, bool refresh = false)
    {
        if (!enabled)
        {
            return null;
        }

        string root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        return string.IsNullOrEmpty(root)
            ? null
            : new CapabilityCache(Path.Combine(root, "asusmon", "capabilities.json"), refresh);
    }

    public string FilePath => _path;

    /// <summary>
    /// Looks up a panel's stored capability string.
    /// </summary>
    /// <param name="capabilities">
    /// The stored string, or <c>null</c> when the panel is known not to publish
    /// one. Only meaningful when this method returns true.
    /// </param>
    /// <returns>True when an entry exists, negative results included.</returns>
    public bool TryGet(string hardwareId, out string? capabilities)
    {
        capabilities = null;

        if (_ignoreStored || string.IsNullOrEmpty(hardwareId))
        {
            return false;
        }

        lock (Gate)
        {
            Load();

            CapabilityCacheEntry? entry = _file?.Monitors
                .FirstOrDefault(m => string.Equals(m.HardwareId, hardwareId, StringComparison.OrdinalIgnoreCase));

            if (entry is null)
            {
                return false;
            }

            capabilities = string.IsNullOrEmpty(entry.Capabilities) ? null : entry.Capabilities;
            return true;
        }
    }

    /// <summary>
    /// Records the result of a capability read, replacing any older entry. Pass
    /// <c>null</c> to record that the panel publishes no capability string.
    /// </summary>
    public void Store(string hardwareId, string? description, string? capabilities)
    {
        if (string.IsNullOrEmpty(hardwareId))
        {
            return;
        }

        lock (Gate)
        {
            Load();
            _file ??= new CapabilityCacheFile();

            _file.Monitors.RemoveAll(
                m => string.Equals(m.HardwareId, hardwareId, StringComparison.OrdinalIgnoreCase));

            _file.Monitors.Add(new CapabilityCacheEntry
            {
                HardwareId = hardwareId,
                Description = description,
                Capabilities = capabilities ?? string.Empty,
                CachedUtc = DateTimeOffset.UtcNow,
            });

            _dirty = true;
        }
    }

    /// <summary>Writes pending changes. Safe to call when nothing changed.</summary>
    public void Flush()
    {
        lock (Gate)
        {
            if (!_dirty || _file is null)
            {
                return;
            }

            _dirty = false;

            try
            {
                string? directory = Path.GetDirectoryName(_path);

                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                // Write-then-replace so an interrupted run cannot leave a
                // half-written file that fails to parse on the next start.
                string temporary = _path + ".tmp";
                File.WriteAllText(
                    temporary,
                    JsonSerializer.Serialize(_file, CapabilityCacheJsonContext.Default.CapabilityCacheFile));
                File.Move(temporary, _path, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
            {
            }
        }
    }

    /// <summary>Discards every stored entry.</summary>
    public void Clear()
    {
        lock (Gate)
        {
            _loaded = true;
            _file = new CapabilityCacheFile();
            _dirty = true;
        }
    }

    public int Count
    {
        get
        {
            lock (Gate)
            {
                Load();
                return _file?.Monitors.Count ?? 0;
            }
        }
    }

    private void Load()
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;

        try
        {
            if (!File.Exists(_path))
            {
                return;
            }

            CapabilityCacheFile? loaded = JsonSerializer.Deserialize(
                File.ReadAllText(_path), CapabilityCacheJsonContext.Default.CapabilityCacheFile);

            // A file from a future version is left untouched rather than
            // rewritten, so downgrading does not destroy it.
            if (loaded is { Version: CurrentVersion })
            {
                _file = loaded;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
        }
    }
}
