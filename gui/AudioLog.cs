using System;
using System.Collections.Generic;
using System.IO;
using Patchwork.Handlers;
using UnityEngine;

namespace Patchwork.GUI;

public static class AudioLog
{
    private static readonly Dictionary<string, AudioPlayEntry> AudioPlayLog = new();

    private static Rect windowRect = new Rect(Screen.width - 310, 10, 300, 400);

    public static void DrawAudioLog()
    {
        windowRect = GUILayout.Window(6969, windowRect, AudioLogWindow, "Patchwork Audio Log");
    }

    private static void AudioLogWindow(int windowID)
    {
        int shown = 0;
        List<AudioPlayEntry> sortedEntries = new(AudioPlayLog.Values);
        sortedEntries.Sort((a, b) => a.ClipName.CompareTo(b.ClipName));

        GUILayout.BeginVertical();
        foreach (var entry in sortedEntries)
        {
            if (entry.IsExpired())
            {
                AudioPlayLog.Remove(entry.ClipName);
                continue;
            }

            if (Plugin.Config.HideModdedAudioInLog && File.Exists(Path.Combine(AudioHandler.SoundFolder, entry.ClipName + ".wav")))
                continue;

            var opacity = entry.GetOpacity();
            var color = new Color(1.0f, 1.0f, 1.0f, opacity);
            UnityEngine.GUI.contentColor = color;
            GUILayout.Label($"{entry.ClipName}");
            shown++;
        }
        if (shown == 0)
        {
            UnityEngine.GUI.contentColor = Color.yellow;
            GUILayout.Label("No audio played recently.");
        }
        UnityEngine.GUI.contentColor = Color.white;
        GUILayout.EndVertical();

        UnityEngine.GUI.DragWindow(new Rect(0, 0, 10000, 20));
    }

    public static void LogAudio(AudioClip clip)
    {
        AudioPlayLog[clip.name] = new AudioPlayEntry
        {
            ClipName = clip.name,
            StartTime = DateTime.Now
        };
    }
    
    public static void ClearLog()
    {
        AudioPlayLog.Clear();
    }

    internal class AudioPlayEntry
    {
        public string ClipName;
        public DateTime StartTime;

        public float GetOpacity() => 1.0f - (float)((DateTime.Now - StartTime).TotalSeconds / Plugin.Config.LogAudioDuration);
        public bool IsExpired() => DateTime.Now >= StartTime.AddSeconds(Plugin.Config.LogAudioDuration);
    }
}