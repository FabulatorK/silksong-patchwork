using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using TeamCherry.Cinematics;
using UnityEngine;

namespace Patchwork.Handlers;

public class VideoHandler
{
    public static string VideoLoadPath { get { return Path.Combine(Plugin.BasePath, "Videos"); } }
    public static Dictionary<string, string> VideoFileMap = new Dictionary<string, string>();
    public static bool ReloadVideos = false;

    private static readonly string[] SupportedExtensions =
        { ".mp4", ".webm", ".ogv", ".mov", ".avi", ".m4v", ".mpg", ".mpeg", ".wmv" };

    /// <summary>
    /// Eagerly scans all Videos directories (base + active packs) and pre-populates
    /// VideoFileMap so the Video Pillar can display replacements without waiting for
    /// the game to actually trigger a cinematic.
    /// </summary>
    public static void Reload()
    {
        VideoFileMap.Clear();

        void ScanDir(string dir)
        {
            if (!Directory.Exists(dir)) return;
            foreach (var file in Directory.GetFiles(dir, "*.*", SearchOption.AllDirectories))
            {
                string ext = Path.GetExtension(file).ToLowerInvariant();
                if (System.Array.IndexOf(SupportedExtensions, ext) < 0) continue;
                string name = Path.GetFileNameWithoutExtension(file);
                if (!VideoFileMap.ContainsKey(name))
                    VideoFileMap[name] = "file:///" + file;
            }
        }

        ScanDir(VideoLoadPath);
        foreach (var packPath in Plugin.PluginPackPaths)
            ScanDir(Path.Combine(packPath, "Videos"));

        Plugin.Logger.LogInfo($"[VideoHandler] Reload: {VideoFileMap.Count} video replacement(s) found");
    }
    
    public static void ApplyPatches(Harmony harmony)
    {
        harmony.Patch(
            original: AccessTools.Constructor(typeof(EmbeddedCinematicVideoPlayer), new[] { typeof(CinematicVideoPlayerConfig) }),
            postfix: new HarmonyMethod(typeof(VideoHandler), nameof(ConstructorPostfix))
        );
        harmony.Patch(
            original: AccessTools.Method(typeof(EmbeddedCinematicVideoPlayer), nameof(EmbeddedCinematicVideoPlayer.PlayVideo)),
            postfix: new HarmonyMethod(typeof(VideoHandler), nameof(PlayVideoPostfix))
        );
        harmony.Patch(
            original: AccessTools.Method(typeof(EmbeddedCinematicVideoPlayer), nameof(EmbeddedCinematicVideoPlayer.Update)),
            postfix: new HarmonyMethod(typeof(VideoHandler), nameof(UpdatePostfix))
        );
        harmony.Patch(
            original: AccessTools.Method(typeof(CinematicPlayer), nameof(CinematicPlayer.StartVideo)),
            prefix: new HarmonyMethod(typeof(VideoHandler), nameof(StartVideoPrefix))
        );
    }

    private static string FindVideoFile(string name)
    {
        if (VideoFileMap.ContainsKey(name))
            return VideoFileMap[name];

        var files = Directory.GetFiles(VideoLoadPath, $"{name}.*", SearchOption.AllDirectories);
        if (files.Any())
        {
            string path = "file:///" + files[0];
            VideoFileMap[name] = path;
            return path;
        }

        foreach (var packPath in Plugin.PluginPackPaths)
        {
            if (!Directory.Exists(Path.Combine(packPath, "Videos")))
                continue;
            var packFiles = Directory.GetFiles(Path.Combine(packPath, "Videos"), $"{name}.*", SearchOption.AllDirectories);
            if (packFiles.Any())
            {
                string path = "file:///" + packFiles[0];
                VideoFileMap[name] = path;
                return path;
            }
        }

        VideoFileMap[name] = null;
        return null;
    }

    private static void StartVideoPrefix(CinematicPlayer __instance)
    {
        if (__instance.videoClip.VideoFileName != null && FindVideoFile(__instance.videoClip.VideoFileName) != null)
            __instance.additionalAudio = null;
    }

    private static void UpdatePostfix(EmbeddedCinematicVideoPlayer __instance)
    {
        if (FindVideoFile(__instance.Config.VideoReference.VideoFileName) != null)
            Platform.Current.RestoreFrameRate();
    }

    private static void ConstructorPostfix(EmbeddedCinematicVideoPlayer __instance, CinematicVideoPlayerConfig config)
    {
        string customVideoPath = FindVideoFile(config.VideoReference.VideoFileName);
        if (customVideoPath == null)
            return;
        
        __instance.videoPlayer.url = customVideoPath;
        __instance.videoPlayer.clip = null;
        __instance.videoPlayer.playbackSpeed = 1f;

        if (config.AudioSource)
        {
            AudioSource audioSource = config.AudioSource.gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            __instance.videoPlayer.audioOutputMode = UnityEngine.Video.VideoAudioOutputMode.AudioSource;
            __instance.videoPlayer.SetTargetAudioSource(0, audioSource);
            __instance.videoPlayer.controlledAudioTrackCount = 1;
            audioSource.volume = 1.0f;
        } else
        {
            Plugin.Logger.LogWarning($"Using custom video file '{config.VideoReference.VideoFileName}' with direct audio output. This may result in no audio being played.");
            __instance.videoPlayer.audioOutputMode = UnityEngine.Video.VideoAudioOutputMode.Direct;
        }
        
        __instance.videoPlayer.Prepare();
    }

    private static void PlayVideoPostfix(EmbeddedCinematicVideoPlayer __instance)
    {
        if (FindVideoFile(__instance.Config.VideoReference.VideoFileName) != null)
        {
            if(!__instance.isSeeking)
                __instance.StopAudio();
        }
    }
}