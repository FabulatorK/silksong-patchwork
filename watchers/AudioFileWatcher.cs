using System.IO;
using Patchwork.Handlers;

namespace Patchwork.Watchers;

public class AudioFileWatcher
{
    public FileSystemWatcher AudioWatcher;

    public static bool ReloadAudio = false;

    public AudioFileWatcher()
    {
        AudioWatcher = new FileSystemWatcher();
        AudioWatcher.Path = AudioHandler.SoundFolder;
        AudioWatcher.IncludeSubdirectories = true;
        AudioWatcher.Filter = "*.wav";
        AudioWatcher.NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.DirectoryName;
        AudioWatcher.Changed += OnAudioChanged;
        AudioWatcher.Created += OnAudioChanged;
        AudioWatcher.Deleted += OnAudioChanged;
        AudioWatcher.Renamed += OnAudioChanged;
        AudioWatcher.EnableRaisingEvents = true;
    }

    private void OnAudioChanged(object sender, FileSystemEventArgs e)
    {
        string filename = Path.GetFileNameWithoutExtension(e.FullPath);
        AudioHandler.InvalidateCache(filename);
        ReloadAudio = true;
    }
}