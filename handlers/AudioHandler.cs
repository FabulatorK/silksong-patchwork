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
            if (!value.name.StartsWith("PATCHWORK_") || !LoadedClips.ContainsKey(value.name.Replace("PATCHWORK_", "")))
                LoadAudio(__instance);
        }
    }

    public static void Reload()
    {
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
                    LoadedClips[clipName] = replacement;
                    source.clip = replacement;
                }
                else if (source.clip.name.StartsWith("PATCHWORK_"))
                {
                    // No active pack covers this sound anymore — restore the vanilla clip.
                    if (_originalClips.TryGetValue(source.GetInstanceID(), out var orig) && orig != null)
                        source.clip = orig;
                }
            }

            AudioList.LogAudio(source);
        }
    }

    public static void LoadAudio(AudioSource source)
    {
        if (source == null || source.clip == null || string.IsNullOrEmpty(source.clip?.name))
            return;
        string clipName = source.clip.name.Replace("PATCHWORK_", "");

        // Remember the vanilla clip before we first overwrite it.
        int id = source.GetInstanceID();
        if (!source.clip.name.StartsWith("PATCHWORK_") && !_originalClips.ContainsKey(id))
            _originalClips[id] = source.clip;

        if (LoadedClips.ContainsKey(clipName))
        {
            source.clip = LoadedClips[clipName];
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
        if (clip == null || string.IsNullOrEmpty(clip?.name))
            return;
        string clipName = clip.name.Replace("PATCHWORK_", "");

        if (LoadedClips.ContainsKey(clipName))
        {
            clip = LoadedClips[clipName];
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
        var files = Directory.GetFiles(SoundFolder, $"{soundName}.*", SearchOption.AllDirectories);
        if (files.Length > 0)
            return files[0];

        foreach (var packPath in Plugin.PluginPackPaths)
        {
            string packSoundsDir = Path.Combine(packPath, "Sounds");
            if (!Directory.Exists(packSoundsDir))
                continue;
            var packFiles = Directory.GetFiles(packSoundsDir, $"{soundName}.*", SearchOption.AllDirectories);
            if (packFiles.Length > 0)
                return packFiles[0];
        }
        return null;
    }
}