using System;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using Patchwork.GUI;
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

    /// <summary>True when at least one audio replacement file exists across all active packs.</summary>
    public static bool HasAudioReplacements => _indexBuilt && _soundIndex.Count > 0;

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
            AudioLog.LogAudio(source.clip);
            LoadAudio(source);
        }
    }

    public static void PlayOneShotHelperPatch(AudioSource source, ref AudioClip clip, float volumeScale)
    {
        if (clip != null)
        {
            AudioLog.LogAudio(clip);
            LoadAudio(ref clip);
        }
    }

    public static void ClipSetterPatch(AudioSource __instance, AudioClip value)
    {
        if (value != null)
        {
            AudioLog.LogAudio(value);
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

        // Clear cache so clips are re-read from disk on this reload pass.
        LoadedClips.Clear();

        foreach (var source in Resources.FindObjectsOfTypeAll<AudioSource>())
        {
            if (source == null) continue;

            if (source.clip != null)
            {
                string clipName = source.clip.name.Replace("PATCHWORK_", "");
                AudioClip replacement = LoadAudioClip(clipName);
                if (replacement != null)
                {
                    // Save the original clip before we overwrite source.clip. Adding to LoadedClips
                    // first would cause the ClipSetterPatch guard (LoadedClips.ContainsKey) to block
                    // the Harmony postfix, so _originalClips would never receive this entry and the
                    // clip could not be restored when the pack is disabled.
                    int id = source.GetInstanceID();
                    if (source.clip != null && !source.clip.name.StartsWith("PATCHWORK_") && !_originalClips.ContainsKey(id))
                        _originalClips[id] = source.clip;
                    LoadedClips[clipName] = replacement;
                    source.clip = replacement;
                }
                else if (source.clip.name.StartsWith("PATCHWORK_"))
                {
                    // No active pack covers this sound anymore — restore the vanilla clip.
                    // _reverting prevents ClipSetterPatch from immediately re-applying a replacement.
                    int id = source.GetInstanceID();
                    bool hasOrig = _originalClips.TryGetValue(id, out var orig);
                    if (hasOrig && orig != null)
                    {
                        _reverting = true;
                        source.clip = orig;
                        _reverting = false;
                    }
                    else
                    {
                        Plugin.Logger.LogWarning($"[Audio] Reload: cannot revert '{clipName}' on '{source.gameObject.name}' — no original clip saved (cloned AudioSource?)");
                    }
                }
            }

            AudioList.LogAudio(source);
        }
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

        AudioClip loadedClip = LoadAudioClip(clipName);
        if (loadedClip != null)
        {
            LoadedClips[clipName] = loadedClip;
            source.clip = loadedClip;
        }
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

        AudioClip loadedClip = LoadAudioClip(clipName);
        if (loadedClip != null)
        {
            LoadedClips[clipName] = loadedClip;
            clip = loadedClip;
        }
    }

    public static void InvalidateCache(string soundName)
    {
        LoadedClips.Remove(soundName);
    }

    public static int CachedClipCount => LoadedClips.Count;

    public static AudioClip LoadAudioClip(string soundName)
    {
        string path = GetSoundPath(soundName);
        if (string.IsNullOrEmpty(path))
            return null;

        if (LoadedClips.TryGetValue(soundName, out var cachedClip))
            return cachedClip;

        string url = "file:///" + Uri.EscapeUriString(path.Replace("\\", "/"));
        var request = UnityWebRequestMultimedia.GetAudioClip(url, AudioType.UNKNOWN);
        var operation = request.SendWebRequest();
        while (!operation.isDone) { }
        if (request.result != UnityWebRequest.Result.Success)
        {
            Plugin.Logger.LogError($"[Patchwork] Failed to load audio clip from {path}: {request.error}");
            return null;
        }

        AudioClip clip = DownloadHandlerAudioClip.GetContent(request);
        clip.name = "PATCHWORK_" + soundName;
        LoadedClips[soundName] = clip;
        return clip;
    }

    static string GetSoundPath(string soundName)
    {
        if (!_indexBuilt) RebuildSoundIndex();
        return _soundIndex.TryGetValue(soundName, out var path) ? path : null;
    }
}