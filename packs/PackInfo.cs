namespace Patchwork.Packs;

/// <summary>
/// Represents a single resource pack discovered on disk.
/// </summary>
public class PackInfo
{
    public string Path        { get; }
    public string Name        { get; }
    public string Author      { get; }
    public string Version     { get; }
    public string Description { get; }
    public bool   IsEnabled   { get; set; } = true;

    /// <summary>True when the pack lives under Patchwork/Packs/ (user-managed).
    /// False when discovered as a Thunderstore plugin pack.</summary>
    public bool IsLocal { get; }

    /// <summary>Cached asset-file counts, populated by PackManager after each Apply()
    /// and persisted to packs-stats.txt across sessions.</summary>
    public PackStats Stats { get; set; }

    public PackInfo(string path, string name, bool isLocal,
                    string author = null, string version = null, string description = null)
    {
        Path        = path;
        Name        = name;
        IsLocal     = isLocal;
        Author      = author;
        Version     = version;
        Description = description;
    }

    public PackInfo Clone() => new(Path, Name, IsLocal, Author, Version, Description)
    {
        IsEnabled = IsEnabled,
        Stats     = Stats,
    };
}
