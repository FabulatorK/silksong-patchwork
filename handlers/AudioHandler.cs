using System;
using System.Collections.Generic;
using System.IO;
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
        // PlayOneShot(AudioClip, float)
        var playOneShot2 = AccessTools.Method(typeof(AudioSource), nameof(AudioSource.PlayOneShot), [typeof(AudioClip), typeof(float)]);
        harmony.Patch(playOneShot2, prefix: new HarmonyMethod(typeof(AudioHandler), nameof(PlayOneShotVolumePatch)));

        // Clip setter
        var clipSetter = AccessTools.PropertySetter(typeof(AudioSource), nameof(AudioSource.clip));
        harmony.Patch(clipSetter, prefix: new HarmonyMethod(typeof(AudioHandler), nameof(SetterPatch)));
    }

    public static void InvalidateCache(string soundName)
    {
        LoadedClips.Remove(soundName);
    }

    static void SetterPatch(ref AudioClip value)
    {
        if (value != null)
        {
            AudioGUI.LogAudio(value, true);
            value = LoadWav(value.name) ?? value;
        }
    }

    static void PlayOneShotVolumePatch(ref AudioClip clip, float volumeScale)
    {
        if (clip != null)
        {
            AudioGUI.LogAudio(clip);
            clip = LoadWav(clip.name) ?? clip;
        }
    }

    static AudioClip LoadWav(string soundName)
    {
        string path = Path.Combine(SoundFolder, soundName + ".wav");
        if (!File.Exists(path))
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
}