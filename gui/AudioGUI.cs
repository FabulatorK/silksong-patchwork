using System;
using System.Collections.Generic;
using UnityEngine;

namespace Patchwork.GUI;

public static class AudioGUI
{
    private static List<AudioPlayEntry> audioPlayLog = new();

    public static void DrawAudioLog()
    {
        if (audioPlayLog.Count == 0)
            return;

        GUILayout.BeginVertical();

        UnityEngine.GUI.contentColor = Color.yellow;
        GUILayout.Label("Audio Play Log:");
        
        for (int i = audioPlayLog.Count - 1; i >= 0; i--)
        {
            var entry = audioPlayLog[i];
            if (entry.IsExpired())
            {
                audioPlayLog.RemoveAt(i);
                continue;
            }

            var opacity = entry.GetOpacity();
            var color = new Color(1.0f, 1.0f, 1.0f, opacity);
            UnityEngine.GUI.contentColor = color;
            GUILayout.Label($"{entry.ClipName}");
        }
        UnityEngine.GUI.contentColor = Color.white;
        GUILayout.EndVertical();
    }

    public static void LogAudio(AudioClip clip)
    {
        audioPlayLog.Add(new AudioPlayEntry
        {
            ClipName = clip.name,
            StartTime = DateTime.Now,
            EndTime = DateTime.Now.AddSeconds(clip.length)
        });
    }
    
    public static void ClearLog()
    {
        audioPlayLog.Clear();
    }

    internal class AudioPlayEntry
    {
        public string ClipName;
        public DateTime StartTime;
        public DateTime EndTime;

        public float GetOpacity()
        {
            var totalDuration = (EndTime - StartTime).TotalSeconds;
            if (totalDuration < 2.0)
                totalDuration = 2.0;
            var elapsed = (DateTime.Now - StartTime).TotalSeconds;
            return 1.0f - (float)(elapsed / totalDuration);
        }

        public bool IsExpired()
        {
            var endTime = (EndTime - StartTime).TotalSeconds < 2.0 ? StartTime.AddSeconds(2.0) : EndTime;
            return DateTime.Now >= endTime;
        }
    }
}