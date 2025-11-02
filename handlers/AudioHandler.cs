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
    }

    public static void PlayHelperPatch(AudioSource source, ulong delay)
    {
        if (source.clip != null)
        {
            AudioGUI.LogAudio(source.clip);
            source.clip = LoadWav(source.clip.name) ?? source.clip;
        }
    }

    public static void PlayOneShotHelperPatch(AudioSource source, ref AudioClip clip, float volumeScale)
    {
        if (clip != null)
        {
            AudioGUI.LogAudio(clip);
            clip = LoadWav(clip.name) ?? clip;
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

        AudioClip clip = AudioClip.Create(soundName, sampleCount / channels, channels, sampleRate, false);
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