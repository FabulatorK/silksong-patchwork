using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Patchwork.GUI;

public static class AudioList
{
    private static readonly HashSet<string> LoadedAudioClips = new();

    /// <summary>Returns a sorted copy of the loaded clip name set.</summary>
    public static List<string> GetClipNames()
    {
        var list = new List<string>(LoadedAudioClips);
        list.Sort();
        return list;
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