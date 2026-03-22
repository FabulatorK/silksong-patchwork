using System.IO;
using Patchwork.Handlers;

namespace Patchwork.Watchers;

public class AudioFileWatcher
{
    public FileSystemWatcher AudioWatcher;

    public static volatile bool ReloadAudio;

    public AudioFileWatcher()
    {
        AudioWatcher = new FileSystemWatcher();
        AudioWatcher.Path = AudioHandler.SoundFolder;
        AudioWatcher.IncludeSubdirectories = true;
        AudioWatcher.Filter = "*.*";
        AudioWatcher.NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.DirectoryName;
        AudioWatcher.Changed += OnAudioChanged;
        AudioWatcher.Created += OnAudioChanged;
        AudioWatcher.Deleted += OnAudioChanged;
        AudioWatcher.Renamed += OnAudioChanged;
        AudioWatcher.EnableRaisingEvents = true;
    }

    private void OnAudioChanged(object sender, FileSystemEventArgs e)
    {
        Plugin.Logger.LogInfo($"[FileWatcher] Audio file event: {e.ChangeType} — {e.FullPath}");
        // Cache invalidation deferred to Reload on the main thread
        // to avoid dictionary corruption from concurrent access.
        ReloadAudio = true;
    }
}