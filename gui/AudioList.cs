using System.Collections.Generic;
using Patchwork.Handlers;

namespace Patchwork.GUI;

/// <summary>
/// Clip inventory for the Audio browser.
/// Populated on demand via AudioHandler.GetClipInventory() — a full memory sweep.
/// Same approach as T2DLoader.GetSceneTextureEntries(): finds clips regardless of
/// whether they have ever fired through a harmony-patched play path.
/// </summary>
public static class AudioList
{
    private static List<AudioClipEntry> _entries = new();
    private static bool _needsRefresh = true;
    private static int  _version;

    /// <summary>
    /// Returns the cached entry list.  Stale until Refresh() is called or
    /// NeedsRefresh is true and the pillar calls Refresh() explicitly.
    /// </summary>
    public static IReadOnlyList<AudioClipEntry> Entries => _entries;

    public static bool NeedsRefresh => _needsRefresh;

    /// <summary>
    /// Incremented on every Refresh() or ClearList().
    /// AudioPillar compares against this to know when to rebuild its filtered view.
    /// </summary>
    public static int Version => _version;

    /// <summary>Schedules a refresh on the next pillar draw.</summary>
    public static void RequestRefresh() => _needsRefresh = true;

    /// <summary>
    /// Runs the full sweep. Only called when the Audio tab is visible
    /// (AudioHandler.IsAudioBrowserActive is true at that point).
    /// </summary>
    public static void Refresh()
    {
        _needsRefresh = false;
        _entries = AudioHandler.GetClipInventory();
        _version++;
    }

    public static void ClearList()
    {
        _entries.Clear();
        _needsRefresh = true;
        _version++;
    }
}
