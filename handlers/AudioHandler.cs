using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        foreach (var source in Resources.FindObjectsOfTypeAll<AudioSource>())
        {
            LoadAudio(source);
            AudioList.LogAudio(source);
        }
    }

    public static void LoadAudio(AudioSource source)
    {
        if (source == null || source.clip == null || string.IsNullOrEmpty(source.clip?.name))
            return;
        string clipName = source.clip.name.Replace("PATCHWORK_", "");

        if (LoadedClips.ContainsKey(clipName))
        {
            source.clip = LoadedClips[clipName];
            return;
        }

        AudioClip loadedClip = LoadWav(clipName);
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

        AudioClip loadedClip = LoadWav(clipName);
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

    public static AudioClip LoadWav(string soundName)
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
        if (files.Any())
            return files.First();

        foreach (var packPath in Plugin.PluginPackPaths)
        {
            if (!Directory.Exists(Path.Combine(packPath, "Sounds")))
                continue;
            var packFiles = Directory.GetFiles(Path.Combine(packPath, "Sounds"), $"{soundName}.*", SearchOption.AllDirectories);
            if (packFiles.Any())
                return packFiles.First();
        }
        return null;
    }
}