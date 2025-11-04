using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using Patchwork.GUI;
using UnityEngine;

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
            AudioLog.LogAudio(source.clip);
    }

    public static void PlayOneShotHelperPatch(AudioSource source, ref AudioClip clip, float volumeScale)
    {
        if (clip != null)
            AudioLog.LogAudio(clip);
    }

    public static void ClipSetterPatch(AudioSource __instance, AudioClip value)
    {
        if (value != null)
        {
            AudioLog.LogAudio(value);
            if (!value.name.StartsWith("PATCHWORK_") || !LoadedClips.ContainsKey(value.name.Substring("PATCHWORK_".Length)))
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

        if (LoadedClips.ContainsKey(source.clip.name))
        {
            source.clip = LoadedClips[source.clip.name];
            return;
        }

        AudioClip loadedClip = LoadWav(source.clip.name);
        if (loadedClip != null)
        {
            source.clip = loadedClip;
            LoadedClips[source.clip.name] = loadedClip;
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

        byte[] wavBytes = File.ReadAllBytes(path);
        int channels = BitConverter.ToInt16(wavBytes, 22);
        int sampleRate = BitConverter.ToInt32(wavBytes, 24);
        int bitsPerSample = BitConverter.ToInt16(wavBytes, 34);
        int dataStart = 44; // standard PCM WAV header
        int dataLength = wavBytes.Length - dataStart;

        int sampleCount = dataLength / (bitsPerSample / 8);
        float[] samples = new float[sampleCount];

        int offset = dataStart;
        for (int i = 0; i < sampleCount; i++)
        {
            short val = BitConverter.ToInt16(wavBytes, offset);
            samples[i] = val / 32768f;
            offset += 2;
        }
        samples[^1] = 0f; // ensure last sample is zero to avoid pops

        AudioClip clip = AudioClip.Create("PATCHWORK_" + soundName, sampleCount / channels, channels, sampleRate, false);
        clip.SetData(samples, 0);
        LoadedClips[soundName] = clip;
        return clip;
    }

    static string GetSoundPath(string soundName)
    {
        var files = Directory.GetFiles(SoundFolder, $"{soundName}.wav", SearchOption.AllDirectories);
        if (files.Any())
            return files.First();

        foreach (var packPath in Plugin.PluginPackPaths)
        {
            if (!Directory.Exists(Path.Combine(packPath, "Sounds")))
                continue;
            var packFiles = Directory.GetFiles(Path.Combine(packPath, "Sounds"), $"{soundName}.wav", SearchOption.AllDirectories);
            if (packFiles.Any())
                return packFiles.First();
        }
        return null;
    }
}