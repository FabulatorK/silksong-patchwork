using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Networking;

namespace Patchwork.Handlers;

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

    /// <summary>Fired on the main thread whenever an AudioClip is about to play (vanilla or replaced).</summary>
    public static event Action<AudioClip> OnAudioPlayed;
    /// <summary>Fired during Reload() for every live AudioSource — lets GUI inventory the loaded clip set.</summary>
    public static event Action<AudioSource> OnAudioSourceLoaded;

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
    }

    public static void PlayHelperPatch(AudioSource source, ulong delay)
    {
        if (source.clip != null)
        {
            OnAudioPlayed?.Invoke(source.clip);
            LoadAudio(source);
        }
    }

    public static void PlayOneShotHelperPatch(AudioSource source, ref AudioClip clip, float volumeScale)
    {
        if (clip != null)
        {
            OnAudioPlayed?.Invoke(clip);
            LoadAudio(ref clip);
        }
    }

    public static void ClipSetterPatch(AudioSource __instance, AudioClip value)
    {
        if (value != null)
        {
            OnAudioPlayed?.Invoke(value);
            if (!_reverting &&
                (!value.name.StartsWith("PATCHWORK_") || !LoadedClips.ContainsKey(value.name.Replace("PATCHWORK_", ""))))
                LoadAudio(__instance);
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

            OnAudioSourceLoaded?.Invoke(source);
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
}