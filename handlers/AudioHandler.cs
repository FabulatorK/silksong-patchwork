using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Networking;

namespace Patchwork.Handlers;

/// <summary>
/// One entry in the audio clip browser — produced by AudioHandler.GetClipInventory().
/// </summary>
public sealed class AudioClipEntry
{
    public readonly string       ClipName;
    public readonly float        LengthSeconds;
    public readonly int          Channels;
    public readonly int          Frequency;
    /// <summary>True when a replacement file exists in the active pack's sound index.</summary>
    public readonly bool         HasReplacement;
    /// <summary>Game-object paths of live AudioSources currently holding this clip.</summary>
    public readonly List<string> SourcePaths = new();

    public AudioClipEntry(string name, float length, int channels, int frequency, bool hasReplacement)
    {
        ClipName       = name;
        LengthSeconds  = length;
        Channels       = channels;
        Frequency      = frequency;
        HasReplacement = hasReplacement;
    }
}

[HarmonyPatch]
public static class AudioHandler
{
    public static readonly string SoundFolder = Path.Combine(Plugin.BasePath, "Sounds");

    private static readonly Dictionary<string, AudioClip> LoadedClips = new();

    // Original game clips keyed by AudioSource instance ID, stored before we first replace them.
    // Never cleared — lets Reload() restore the vanilla clip when a pack is disabled.
    private static readonly Dictionary<int, AudioClip> _originalClips = new();

    // Reverse index: sound name (no extension, case-insensitive) → full file path.
    // Built lazily on first use and rebuilt at the start of every Reload().
    // Turns per-clip Directory.GetFiles scans into O(1) lookups.
    private static readonly Dictionary<string, string> _soundIndex =
        new(StringComparer.OrdinalIgnoreCase);
    private static bool _indexBuilt;

    // Set to true while Reload() is writing vanilla clips back to AudioSources.
    // Blocks ClipSetterPatch from immediately re-applying a replacement and undoing the revert.
    private static bool _reverting;

    // Tracks sounds currently being loaded by a coroutine — prevents duplicate launches.
    private static readonly HashSet<string> _loadingInProgress =
        new(StringComparer.OrdinalIgnoreCase);
    // Incremented at the start of each Reload() so stale in-flight coroutines discard their result.
    private static int _reloadGeneration;

    /// <summary>True when at least one audio replacement file exists across all active packs.</summary>
    public static bool HasAudioReplacements => _indexBuilt && _soundIndex.Count > 0;

    /// <summary>
    /// Fired on the main thread whenever an AudioClip is about to play.
    /// Second arg is the AudioSource that owns the clip (null for one-shot paths that lack a persistent source).
    /// </summary>
    public static event Action<AudioClip, AudioSource> OnAudioPlayed;

    /// <summary>
    /// Set by AudioPillar when the Audio tab is visible.
    /// Gates the full clip/source sweep in GetClipInventory — zero cost when closed.
    /// </summary>
    public static bool IsAudioBrowserActive;

    public static void ApplyPatches(Harmony harmony)
    {
        harmony.Patch(
            AccessTools.Method(typeof(AudioSource), nameof(AudioSource.PlayHelper), [typeof(AudioSource), typeof(ulong)]),
            prefix: new HarmonyMethod(typeof(AudioHandler), nameof(PlayHelperPatch))
        );

        harmony.Patch(
            AccessTools.Method(typeof(AudioSource), nameof(AudioSource.PlayOneShotHelper), [typeof(AudioSource), typeof(AudioClip), typeof(float)]),
            prefix: new HarmonyMethod(typeof(AudioHandler), nameof(PlayOneShotHelperPatch))
        );

        harmony.Patch(
            AccessTools.PropertySetter(typeof(AudioSource), nameof(AudioSource.clip)),
            postfix: new HarmonyMethod(typeof(AudioHandler), nameof(ClipSetterPatch))
        );

        // ── Needolin-specific patches ─────────────────────────────────────────
        // The Needolin loop uses OverrideNeedolinLoop.StartSyncedAudio(AudioSource, AudioClip)
        // instead of the standard AudioSource.Play() family.  Our PlayHelperPatch never fires
        // for it, so the synced system starts with the vanilla clip and schedules its next cycle
        // based on that clip's length — causing the ~10s cut-and-restart when the replacement
        // has a different length.  A prefix lets us swap synchronously before the scheduler sees it.
        var needolinLoopType = AccessTools.TypeByName("OverrideNeedolinLoop");
        if (needolinLoopType != null)
        {
            var startSyncedAudio = AccessTools.Method(needolinLoopType, "StartSyncedAudio",
                [typeof(AudioSource), typeof(AudioClip)]);
            if (startSyncedAudio != null)
                harmony.Patch(startSyncedAudio,
                    prefix: new HarmonyMethod(typeof(AudioHandler), nameof(StartSyncedAudioPatch)));
            else
                Plugin.Logger.LogWarning("[Audio] OverrideNeedolinLoop.StartSyncedAudio(AudioSource, AudioClip) not found — Needolin loop replacement disabled");
        }
        else
        {
            Plugin.Logger.LogWarning("[Audio] OverrideNeedolinLoop type not found — Needolin loop replacement disabled");
        }

        // PlayMaker SetAudioClip.OnEnter covers the up/down Needolin variants
        // (needolin_bell_beast_v2, needolin_alt_melodies_deep) and any other PM audio
        // assignment.  ClipSetterPatch already fires downstream, but it's async; a prefix
        // here makes the swap synchronous when the clip is already in LoadedClips.
        var setAudioClipType = AccessTools.TypeByName("HutongGames.PlayMaker.Actions.SetAudioClip");
        if (setAudioClipType != null)
        {
            var onEnter = AccessTools.Method(setAudioClipType, "OnEnter");
            if (onEnter != null)
                harmony.Patch(onEnter,
                    prefix: new HarmonyMethod(typeof(AudioHandler), nameof(SetAudioClipOnEnterPatch)));
        }
    }

    public static void PlayHelperPatch(AudioSource source, ulong delay)
    {
        if (source.clip != null)
        {
            OnAudioPlayed?.Invoke(source.clip, source);
            LoadAudio(source);
        }
    }

    public static void PlayOneShotHelperPatch(AudioSource source, ref AudioClip clip, float volumeScale)
    {
        if (clip != null)
        {
            OnAudioPlayed?.Invoke(clip, source);
            LoadAudio(ref clip);
        }
    }

    public static void ClipSetterPatch(AudioSource __instance, AudioClip value)
    {
        if (value != null)
        {
            OnAudioPlayed?.Invoke(value, __instance);
            if (!_reverting &&
                (!value.name.StartsWith("PATCHWORK_") || !LoadedClips.ContainsKey(value.name.Replace("PATCHWORK_", ""))))
                LoadAudio(__instance);
        }
    }

    /// <summary>
    /// Prefix for OverrideNeedolinLoop.StartSyncedAudio(AudioSource, AudioClip).
    /// The synced loop passes the clip into a scheduler that reads its length to time
    /// the next cycle — swapping after the fact (async) produces the wrong cycle length,
    /// causing the cut-and-restart at ~10 s.  Swapping here (before the scheduler sees
    /// the clip) fixes both the replacement and the loop timing in one step.
    /// </summary>
    public static void StartSyncedAudioPatch(AudioSource targetSource, ref AudioClip defaultClip)
    {
        if (defaultClip == null || string.IsNullOrEmpty(defaultClip.name)) return;
        string clipName = defaultClip.name.Replace("PATCHWORK_", "");
        if (!_soundIndex.ContainsKey(clipName)) return;

        int id = targetSource != null ? targetSource.GetInstanceID() : -1;
        if (id >= 0 && !defaultClip.name.StartsWith("PATCHWORK_") && !_originalClips.ContainsKey(id))
            _originalClips[id] = defaultClip;

        if (LoadedClips.TryGetValue(clipName, out var loaded))
            defaultClip = loaded;
        else
            EnqueueLoad(clipName);
        // If not yet loaded, vanilla plays this cycle; on the next StartSyncedAudio call
        // the replacement will be in LoadedClips (eager preload runs at pack Apply time).
    }

    /// <summary>
    /// Prefix for HutongGames.PlayMaker.Actions.SetAudioClip.OnEnter.
    /// Covers the Needolin up/down variants and any other PlayMaker audio assignment.
    /// ClipSetterPatch fires downstream regardless; this gives a synchronous fast-path
    /// when the replacement is already cached, eliminating the one-trigger delay.
    /// </summary>
    public static void SetAudioClipOnEnterPatch(object __instance)
    {
        try
        {
            // Access audioClip field via reflection — avoids a hard compile-time reference
            // to the PlayMaker assembly (which may not be present in all build configs).
            var field = __instance.GetType().GetField("audioClip",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
            if (field == null) return;

            var fsmObj = field.GetValue(__instance);
            if (fsmObj == null) return;

            var valProp = fsmObj.GetType().GetProperty("Value");
            if (valProp == null) return;

            var clip = valProp.GetValue(fsmObj) as AudioClip;
            if (clip == null || string.IsNullOrEmpty(clip.name)) return;

            string clipName = clip.name;
            if (!_soundIndex.ContainsKey(clipName)) return;

            if (LoadedClips.TryGetValue(clipName, out var loaded))
                valProp.SetValue(fsmObj, loaded);
            else
                EnqueueLoad(clipName);
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogWarning($"[Audio] SetAudioClipOnEnterPatch: {ex.Message}");
        }
    }

    public static void Reload()
    {
        RebuildSoundIndex();

        Plugin.Logger.LogInfo($"[Audio-Trace] Reload: soundIndex={_soundIndex.Count} LoadedClips={LoadedClips.Count} _originalClips={_originalClips.Count}");

        // If there are no replacement files and no previously-loaded clips to restore,
        // skip the expensive FindObjectsOfTypeAll pass entirely.
        if (!HasAudioReplacements && LoadedClips.Count == 0)
        {
            Plugin.Logger.LogInfo("[Audio-Trace] Reload: early exit — no replacements and no cached clips to restore");
            return;
        }

        // Clear cache so clips are re-fetched on this reload pass.
        // Invalidate any in-flight coroutines from a previous reload.
        LoadedClips.Clear();
        _loadingInProgress.Clear();
        _reloadGeneration++;

        foreach (var source in Resources.FindObjectsOfTypeAll<AudioSource>())
        {
            if (source == null) continue;

            if (source.clip != null)
            {
                string clipName = source.clip.name.Replace("PATCHWORK_", "");
                if (_soundIndex.ContainsKey(clipName))
                {
                    // Save the vanilla clip now (synchronous) so the async completion
                    // sweep can restore it when a pack is disabled.
                    int id = source.GetInstanceID();
                    if (!source.clip.name.StartsWith("PATCHWORK_") && !_originalClips.ContainsKey(id))
                        _originalClips[id] = source.clip;
                    EnqueueLoad(clipName);
                }
                else if (source.clip.name.StartsWith("PATCHWORK_"))
                {
                    // No active pack covers this sound anymore — restore the vanilla clip.
                    // _reverting prevents ClipSetterPatch from immediately re-applying a replacement.
                    int id = source.GetInstanceID();
                    bool hasOrig = _originalClips.TryGetValue(id, out var orig);

                    if (!hasOrig || orig == null)
                    {
                        // Cloned AudioSources inherit the PATCHWORK_ clip from the prefab and were
                        // never saved in _originalClips. The vanilla clip is still alive — either
                        // under a different instance ID in _originalClips, or as a game asset.
                        // Search _originalClips.Values first (cheap, already in memory), then fall
                        // back to a full Resources scan.
                        foreach (var saved in _originalClips.Values)
                        {
                            if (saved != null && string.Equals(saved.name, clipName, System.StringComparison.OrdinalIgnoreCase))
                            {
                                orig = saved;
                                break;
                            }
                        }

                        if (orig == null)
                        {
                            foreach (var ac in Resources.FindObjectsOfTypeAll<AudioClip>())
                            {
                                if (ac != null && !ac.name.StartsWith("PATCHWORK_") &&
                                    string.Equals(ac.name, clipName, System.StringComparison.OrdinalIgnoreCase))
                                {
                                    orig = ac;
                                    break;
                                }
                            }
                        }

                        // Cache the found vanilla clip so subsequent reloads don't re-scan.
                        if (orig != null)
                            _originalClips[id] = orig;
                    }

                    if (orig != null)
                    {
                        _reverting = true;
                        source.clip = orig;
                        _reverting = false;
                    }
                    else
                    {
                        Plugin.Logger.LogWarning($"[Audio] Reload: cannot revert '{clipName}' on '{source.gameObject.name}' — vanilla clip not found in memory");
                    }
                }
            }

        }

        // Eagerly preload every replacement clip in the index, even those not currently
        // assigned to any live AudioSource. Coroutines start in the background; by the time
        // the player triggers the action (silk spear cast, etc.), the clip is already cached
        // in LoadedClips and LoadAudio() will apply it instantly instead of queuing a load
        // that forces vanilla to play first.
        foreach (var clipName in _soundIndex.Keys)
            EnqueueLoad(clipName);
    }

    private static void RebuildSoundIndex()
    {
        _soundIndex.Clear();
        _indexBuilt = true;

        void ScanDir(string dir)
        {
            if (!Directory.Exists(dir)) return;
            foreach (var file in Directory.GetFiles(dir, "*.*", SearchOption.AllDirectories))
            {
                string ext = Path.GetExtension(file).ToLowerInvariant();
                if (ext != ".ogg" && ext != ".wav" && ext != ".mp3" &&
                    ext != ".aiff" && ext != ".aif")
                    continue;
                string name = Path.GetFileNameWithoutExtension(file);
                _soundIndex.TryAdd(name, file);
            }
        }

        ScanDir(SoundFolder);
        foreach (var packPath in Plugin.PluginPackPaths)
            ScanDir(Path.Combine(packPath, "Sounds"));
    }

    public static void LoadAudio(AudioSource source)
    {
        if (source == null || source.clip == null || string.IsNullOrEmpty(source.clip.name))
            return;
        string clipName = source.clip.name.Replace("PATCHWORK_", "");

        // Remember the vanilla clip before we first overwrite it.
        int id = source.GetInstanceID();
        if (!source.clip.name.StartsWith("PATCHWORK_") && !_originalClips.ContainsKey(id))
            _originalClips[id] = source.clip;

        if (LoadedClips.TryGetValue(clipName, out var cachedForSource))
        {
            source.clip = cachedForSource;
            return;
        }

        // Not loaded yet — enqueue background load; vanilla plays this trigger.
        EnqueueLoad(clipName);
    }

    public static void LoadAudio(ref AudioClip clip)
    {
        if (clip == null || string.IsNullOrEmpty(clip.name))
            return;
        string clipName = clip.name.Replace("PATCHWORK_", "");

        if (LoadedClips.TryGetValue(clipName, out var cachedForClip))
        {
            clip = cachedForClip;
            return;
        }

        // Not loaded yet — enqueue background load; vanilla plays this trigger.
        EnqueueLoad(clipName);
    }

    public static void InvalidateCache(string soundName)
    {
        LoadedClips.Remove(soundName);
    }

    public static int CachedClipCount => LoadedClips.Count;

    /// <summary>
    /// Returns the cached clip for <paramref name="soundName"/>, or null if not yet loaded.
    /// Use <see cref="EnqueueLoad"/> to start a background load; the clip will be available
    /// on the next call once the coroutine completes.
    /// </summary>
    public static AudioClip LoadAudioClip(string soundName)
    {
        LoadedClips.TryGetValue(soundName, out var clip);
        return clip;
    }

    private static void EnqueueLoad(string soundName)
    {
        if (LoadedClips.ContainsKey(soundName) || _loadingInProgress.Contains(soundName)) return;
        string path = GetSoundPath(soundName);
        if (path == null) return;
        _loadingInProgress.Add(soundName);
        Plugin.Instance.StartCoroutine(LoadAudioClipAsync(soundName, path, _reloadGeneration));
    }

    private static IEnumerator LoadAudioClipAsync(string soundName, string path, int gen)
    {
        string url = "file:///" + Uri.EscapeUriString(path.Replace("\\", "/"));
        var request = UnityWebRequestMultimedia.GetAudioClip(url, AudioType.UNKNOWN);
        yield return request.SendWebRequest();   // non-blocking: returns control each frame

        _loadingInProgress.Remove(soundName);

        // Discard result if a newer Reload() has already superseded this load.
        if (_reloadGeneration != gen) yield break;

        if (request.result != UnityWebRequest.Result.Success)
        {
            Plugin.Logger.LogError($"[Audio] Failed to load '{path}': {request.error}");
            yield break;
        }

        var clip = DownloadHandlerAudioClip.GetContent(request);
        clip.name = "PATCHWORK_" + soundName;
        LoadedClips[soundName] = clip;

        // Sweep live AudioSources and apply the clip immediately on the completion frame.
        // Unity coroutines resume on the main thread so no concurrency concern here.
        foreach (var src in Resources.FindObjectsOfTypeAll<AudioSource>())
        {
            if (src == null || src.clip == null) continue;
            string cn = src.clip.name.Replace("PATCHWORK_", "");
            if (!cn.Equals(soundName, StringComparison.OrdinalIgnoreCase)) continue;
            int id = src.GetInstanceID();
            if (!src.clip.name.StartsWith("PATCHWORK_") && !_originalClips.ContainsKey(id))
                _originalClips[id] = src.clip;
            src.clip = clip;
        }
    }

    static string GetSoundPath(string soundName)
    {
        if (!_indexBuilt) RebuildSoundIndex();
        return _soundIndex.TryGetValue(soundName, out var path) ? path : null;
    }

    // ── Clip inventory for the browser ──────────────────────────────────────

    /// <summary>
    /// Full sweep of every AudioClip in memory paired with the AudioSources that hold it.
    /// Mirrors the approach used by T2DLoader.GetSceneTextureEntries():
    /// finds clips that never fire through a harmony-patched play path.
    /// Only call when IsAudioBrowserActive is true.
    /// </summary>
    public static List<AudioClipEntry> GetClipInventory()
    {
        if (!_indexBuilt) RebuildSoundIndex();

        // clip instance-ID → entry
        var byId = new Dictionary<int, AudioClipEntry>();

        // Primary: all clips loaded in memory
        foreach (var clip in Resources.FindObjectsOfTypeAll<AudioClip>())
        {
            if (clip == null) continue;
            string name = clip.name.Replace("PATCHWORK_", "");
            int id = clip.GetInstanceID();
            if (!byId.ContainsKey(id))
                byId[id] = new AudioClipEntry(name, clip.length, clip.channels, clip.frequency,
                    _soundIndex.ContainsKey(name));
        }

        // Secondary: scan live AudioSources to build clip → source-path associations
        foreach (var src in Resources.FindObjectsOfTypeAll<AudioSource>())
        {
            if (src == null || src.clip == null) continue;
            int id = src.clip.GetInstanceID();
            if (!byId.TryGetValue(id, out var entry)) continue;
            string path = GetGameObjectPath(src);
            if (!entry.SourcePaths.Contains(path))
                entry.SourcePaths.Add(path);
        }

        var result = new List<AudioClipEntry>(byId.Values);
        result.Sort((a, b) => string.Compare(a.ClipName, b.ClipName, StringComparison.OrdinalIgnoreCase));
        return result;
    }

    private static string GetGameObjectPath(AudioSource src)
    {
        if (src == null) return "";
        // Walk up two levels max — enough context without becoming unwieldy
        var t = src.transform;
        string name = t.name;
        if (t.parent != null) name = t.parent.name + "/" + name;
        return name;
    }
}