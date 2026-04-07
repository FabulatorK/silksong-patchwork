using System;
using System.Collections.Generic;
using System.IO;

namespace Patchwork.Util;

public static class IOUtil
{
    public static void EnsureDirectoryExists(string path)
    {
        if (!Directory.Exists(path))
            Directory.CreateDirectory(path);
    }

    // ── PNG dimension probe ───────────────────────────────────────────────────
    // Reads only the 8-byte IHDR block (bytes 16–23) — no full image decode.
    // Returns (0, 0) on any error or if the file is not a valid PNG.
    public static (int width, int height) ReadPngDimensions(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            if (fs.Length < 24) return (0, 0);
            fs.Seek(16, SeekOrigin.Begin);   // 8 sig + 4 chunk-len + 4 "IHDR"
            var buf = new byte[8];
            if (fs.Read(buf, 0, 8) < 8) return (0, 0);
            int w = (buf[0] << 24) | (buf[1] << 16) | (buf[2] << 8) | buf[3];
            int h = (buf[4] << 24) | (buf[5] << 16) | (buf[6] << 8) | buf[7];
            return (w, h);
        }
        catch { return (0, 0); }
    }

    // ── Anchor sidecar (anchors.txt) ─────────────────────────────────────────
    // File lives at: Sprites/{collName}/{matName}/anchors.txt
    // Format per line:  SpriteName = anchor-value
    // Valid values: top-center  left-center  right-center  center
    // Absence of an entry means the default (bottom-center).
    // Comment lines start with '#'.

    public static string AnchorSidecarPath(string spritesRoot, string collName, string matName)
        => Path.Combine(spritesRoot, collName, matName, "anchors.txt");

    /// <summary>
    /// Returns the saved anchor value for <paramref name="spriteName"/>, or
    /// <c>null</c> if no entry exists (caller should treat null as "bottom-center").
    /// </summary>
    public static string ReadAnchorEntry(string spritesRoot, string collName, string matName, string spriteName)
    {
        string path = AnchorSidecarPath(spritesRoot, collName, matName);
        if (!File.Exists(path)) return null;
        foreach (var line in File.ReadAllLines(path))
        {
            if (line.StartsWith("#", StringComparison.Ordinal)) continue;
            int eq = line.IndexOf('=');
            if (eq < 0) continue;
            if (line.Substring(0, eq).Trim() == spriteName)
                return line.Substring(eq + 1).Trim();
        }
        return null;
    }

    /// <summary>
    /// Writes or removes an anchor entry for <paramref name="spriteName"/>.
    /// Pass <c>null</c> for <paramref name="anchor"/> to delete the entry (revert to default).
    /// </summary>
    public static void WriteAnchorEntry(string spritesRoot, string collName, string matName,
        string spriteName, string anchor)
    {
        string path = AnchorSidecarPath(spritesRoot, collName, matName);
        var lines = File.Exists(path)
            ? new List<string>(File.ReadAllLines(path))
            : new List<string> { "# Patchwork anchor overrides — auto-generated, do not edit manually" };

        bool found = false;
        for (int i = 0; i < lines.Count; i++)
        {
            if (lines[i].StartsWith("#", StringComparison.Ordinal)) continue;
            int eq = lines[i].IndexOf('=');
            if (eq < 0) continue;
            if (lines[i].Substring(0, eq).Trim() != spriteName) continue;

            if (anchor == null)
                lines.RemoveAt(i);
            else
                lines[i] = $"{spriteName} = {anchor}";
            found = true;
            break;
        }

        if (!found && anchor != null)
            lines.Add($"{spriteName} = {anchor}");

        EnsureDirectoryExists(Path.GetDirectoryName(path));
        File.WriteAllLines(path, lines);
    }
}
