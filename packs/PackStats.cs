using System.IO;

namespace Patchwork.Packs;

/// <summary>
/// Cached file counts for each asset category in a single pack.
/// Used to display a rough "load footprint" badge in the pack manager UI.
/// Default (all-zero + IsScanned=false) means the pack has not been scanned yet.
/// </summary>
public readonly struct PackStats
{
    /// <summary>False until <see cref="Scan"/> has been called for this pack.</summary>
    public bool IsScanned { get; init; }
    public int Sprites    { get; init; }   // files under Sprites/
    public int Sheets     { get; init; }   // files under Spritesheets/
    public int Audio      { get; init; }   // files under Sounds/
    public int Video      { get; init; }   // files under Videos/
    public int Text       { get; init; }   // files under Text/

    public int Total => Sprites + Sheets + Audio + Video + Text;

    // ================================================================
    //  Scanning
    // ================================================================

    /// <summary>
    /// Counts asset files in each known subdirectory of <paramref name="packPath"/>.
    /// Never throws — returns an all-zero scanned result on any error.
    /// </summary>
    public static PackStats Scan(string packPath)
    {
        try
        {
            return new PackStats
            {
                IsScanned = true,
                Sprites   = CountFiles(packPath, "Sprites",      "*.png"),
                Sheets    = CountFiles(packPath, "Spritesheets", "*.png"),
                Audio     = CountFiles(packPath, "Sounds",       "*.*",
                                extensions: new[] { ".wav", ".ogg", ".mp3" }),
                Video     = CountFiles(packPath, "Videos",       "*.*"),
                Text      = CountFiles(packPath, "Text",         "*.*",
                                extensions: new[] { ".tsv", ".csv", ".txt" }),
            };
        }
        catch
        {
            return new PackStats { IsScanned = true };
        }
    }

    private static int CountFiles(string packPath, string subdir, string pattern,
                                  string[] extensions = null)
    {
        string dir = System.IO.Path.Combine(packPath, subdir);
        if (!Directory.Exists(dir)) return 0;
        var files = Directory.GetFiles(dir, pattern, SearchOption.AllDirectories);
        if (extensions == null) return files.Length;
        int count = 0;
        foreach (var f in files)
            if (System.Array.IndexOf(extensions,
                    System.IO.Path.GetExtension(f).ToLowerInvariant()) >= 0)
                count++;
        return count;
    }

    // ================================================================
    //  Cache serialization  (tab-separated:  path \t sp \t sh \t au \t vi \t tx)
    // ================================================================

    public string Serialize(string packPath) =>
        $"{packPath}\t{Sprites}\t{Sheets}\t{Audio}\t{Video}\t{Text}";

    public static bool TryDeserialize(string line, out string path, out PackStats stats)
    {
        stats = default;
        path  = null;
        if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#")) return false;

        var parts = line.Split('\t');
        if (parts.Length < 6) return false;

        if (!int.TryParse(parts[1], out int sp) ||
            !int.TryParse(parts[2], out int sh) ||
            !int.TryParse(parts[3], out int au) ||
            !int.TryParse(parts[4], out int vi) ||
            !int.TryParse(parts[5], out int tx))
            return false;

        path  = parts[0];
        stats = new PackStats { IsScanned = true,
            Sprites = sp, Sheets = sh, Audio = au, Video = vi, Text = tx };
        return true;
    }

    // ================================================================
    //  Display
    // ================================================================

    /// <summary>
    /// Human-readable badge, e.g. "12 sprites  3 sheets  5 sfx".
    /// Returns null when not yet scanned or all counts are zero.
    /// </summary>
    public string Badge
    {
        get
        {
            if (!IsScanned) return null;
            var parts = new System.Collections.Generic.List<string>();
            if (Sprites > 0) parts.Add($"{Sprites} sprite{(Sprites == 1 ? "" : "s")}");
            if (Sheets  > 0) parts.Add($"{Sheets} sheet{(Sheets  == 1 ? "" : "s")}");
            if (Audio   > 0) parts.Add($"{Audio} sfx");
            if (Video   > 0) parts.Add($"{Video} video{(Video   == 1 ? "" : "s")}");
            if (Text    > 0) parts.Add($"{Text} text");
            return parts.Count > 0 ? string.Join("  ", parts) : null;
        }
    }
}
