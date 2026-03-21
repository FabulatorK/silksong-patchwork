using System.Collections.Generic;
using System.IO;

namespace Patchwork.Watchers;

public class TextFileWatcher
{
    public FileSystemWatcher TextWatcher;
    public List<FileSystemWatcher> PackWatchers = new();

    public static bool ReloadText = false;

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
        string sheet = new DirectoryInfo(Path.GetDirectoryName(e.FullPath)).Name;
        string lang = Path.GetFileNameWithoutExtension(e.FullPath);
        DialogueHandler.InvalidateCache(sheet, lang);
        ReloadText = true;
    }
}