using System.Collections.Generic;
using System.IO;

namespace Patchwork.Watchers;

public class TextFileWatcher
{
    public FileSystemWatcher TextWatcher;
    public List<FileSystemWatcher> PackWatchers = new();

    public static volatile bool ReloadText;

    public TextFileWatcher()
    {
        TextWatcher = CreateWatcher(DialogueHandler.TextLoadPath);

        foreach (var packPath in Plugin.PluginPackPaths)
        {
            string textDir = Path.Combine(packPath, "Text");
            if (Directory.Exists(textDir))
                PackWatchers.Add(CreateWatcher(textDir));
        }
    }

    private FileSystemWatcher CreateWatcher(string path)
    {
        var watcher = new FileSystemWatcher();
        watcher.Path = path;
        watcher.IncludeSubdirectories = true;
        watcher.Filter = "*.yml";
        watcher.NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.DirectoryName;
        watcher.Changed += OnTextChanged;
        watcher.Created += OnTextChanged;
        watcher.Deleted += OnTextChanged;
        watcher.Renamed += OnTextChanged;
        watcher.EnableRaisingEvents = true;
        return watcher;
    }

    private void OnTextChanged(object sender, FileSystemEventArgs e)
    {
        Plugin.Logger.LogInfo($"[FileWatcher] Text file event: {e.ChangeType} — {e.FullPath}");
        // Cache invalidation deferred to Reload on the main thread
        // to avoid dictionary corruption from concurrent access.
        ReloadText = true;
    }
}