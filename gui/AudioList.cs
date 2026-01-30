using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Patchwork.Handlers;
using UnityEngine;

namespace Patchwork.GUI;

public static class AudioList
{
    private static readonly HashSet<string> LoadedAudioClips = new();

    private static Vector2 scrollPosition = Vector2.zero;
    private static Rect windowRect;
    private static bool initialized = false;
    public static void DrawAudioList()
    {
        if (!initialized || windowRect.width < 1)
        {
            windowRect = GUIHelper.ScaledRect(10, 10, 300, 400);
            initialized = true;
        }
        
        windowRect = GUILayout.Window(
            6970, 
            windowRect, 
            AudioLogWindow, 
            "Loaded Sounds",
            GUIHelper.WindowStyle
        );
    }

    private static void AudioLogWindow(int windowID)
    {   
        GUIHelper.Space(16); 
        
        int shown = 0;
        List<string> sortedEntries = LoadedAudioClips.ToList();
        sortedEntries.Sort();

        scrollPosition = GUILayout.BeginScrollView(scrollPosition);
        GUILayout.BeginVertical();
        foreach (var entry in sortedEntries)
        {
            GUILayout.Label(entry, GUIHelper.LabelStyle);
            shown++;
        }
        if (shown == 0)
        {
            UnityEngine.GUI.contentColor = Color.yellow;
            GUILayout.Label("No audio clips loaded.", GUIHelper.LabelStyle);
        }
        UnityEngine.GUI.contentColor = Color.white;
        GUILayout.EndVertical();
        GUILayout.EndScrollView();

        UnityEngine.GUI.DragWindow(GUIHelper.DragRect);
    }

    public static void LogAudio(AudioSource source)
    {
        if (source == null || string.IsNullOrEmpty(source.clip?.name))
            return;

        string soundName = source.clip.name.Replace("PATCHWORK_", "");
        LoadedAudioClips.Add(soundName);
    }
    
    public static void ClearList()
    {
        LoadedAudioClips.Clear();
    }
}