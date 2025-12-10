using System.IO;

namespace Patchwork.Watchers;

public class TextFileWatcher
{
    public FileSystemWatcher TextWatcher;

    public TextFileWatcher()
    {
        TextWatcher = new FileSystemWatcher();
        TextWatcher.Path = DialogueHandler.TextLoadPath;
        TextWatcher.IncludeSubdirectories = true;
        TextWatcher.Filter = "*.yml";
        TextWatcher.NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.DirectoryName;
        TextWatcher.Changed += OnTextChanged;
        TextWatcher.Created += OnTextChanged;
        TextWatcher.Deleted += OnTextChanged;
        TextWatcher.Renamed += OnTextChanged;
        TextWatcher.EnableRaisingEvents = true;
    }

    private void OnTextChanged(object sender, FileSystemEventArgs e)
    {
        string sheet = new DirectoryInfo(Path.GetDirectoryName(e.FullPath)).Name;
        string lang = Path.GetFileNameWithoutExtension(e.FullPath);
        DialogueHandler.InvalidateCache(sheet, lang);
    }
}