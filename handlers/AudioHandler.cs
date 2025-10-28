using System;
using System.IO;
using HarmonyLib;
using UnityEngine;

namespace Patchwork.Handlers;

[HarmonyPatch]
public static class AudioSourcePatch
{
    public static void ApplyPatches(Harmony harmony)
    {
        // Play()
        var playMethod = AccessTools.Method(typeof(AudioSource), nameof(AudioSource.Play));
        harmony.Patch(playMethod, prefix: new HarmonyMethod(typeof(AudioSourcePatch), nameof(PlayPatch)));

        // PlayOneShot(AudioClip)
        var playOneShot1 = AccessTools.Method(typeof(AudioSource), nameof(AudioSource.PlayOneShot), new Type[] { typeof(AudioClip) });
        harmony.Patch(playOneShot1, prefix: new HarmonyMethod(typeof(AudioSourcePatch), nameof(PlayOneShotPatch)));

        // PlayOneShot(AudioClip, float)
        var playOneShot2 = AccessTools.Method(typeof(AudioSource), nameof(AudioSource.PlayOneShot), new Type[] { typeof(AudioClip), typeof(float) });
        harmony.Patch(playOneShot2, prefix: new HarmonyMethod(typeof(AudioSourcePatch), nameof(PlayOneShotVolumePatch)));
    }

    static void PlayPatch(AudioSource __instance)
    {
        var clip = __instance.clip;
        if (clip != null)
            Debug.Log($"Play called with clip: {clip.name}");
    }

    static void PlayOneShotPatch(AudioSource __instance, ref AudioClip clip)
    {
        if (clip != null)
        {
            clip = LoadWav(clip.name) ?? clip;
        }
    }

    static void PlayOneShotVolumePatch(AudioSource __instance, ref AudioClip clip, float volumeScale)
    {
        if (clip != null)
        {
            // TODO: Use volumeScale if needed
            clip = LoadWav(clip.name) ?? clip;
        }
    }

    static AudioClip LoadWav(string soundName)
    {
        string path = Path.Combine(Plugin.BasePath, "Sounds", soundName + ".wav");
        if (!File.Exists(path))
        {
            Debug.LogWarning($"Sound file not found: {path}");
            return null;
        }

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
            samples[i] = val / 32768f; // normalize to [-1,1]
            offset += 2; // 16-bit
        }

        AudioClip clip = AudioClip.Create(soundName, sampleCount / channels, channels, sampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }
}