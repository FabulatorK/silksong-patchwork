# Text Replacement — Tech Sheet

How `DialogueHandler` and `TextFileWatcher` work, known issues, and fix guidance.

---

## How It Works

`DialogueHandler` patches `Language.Get(key, sheet)` with a postfix. On every text
request the postfix checks `TextCache[lang][sheet][key]`; on a cache miss it calls
`LoadTextSheet` to read YAML from disk, then caches and returns the result.

### Load order (per sheet, per language)

```
LoadTextSheet(sheet, lang)
 1. Read  Patchwork/Text/[sheet]/[LANG].yml          ← root / base overrides
 2. For each active pack (priority order):
      Read  [packPath]/Text/[sheet]/[LANG].yml
      Each pack entry overwrites earlier entries.
 3. Cache the merged dict.
```

**Pack priority is higher than root.** Root entries survive for any key not present in
any pack. If two packs define the same key, the higher-priority pack wins and
`ConflictTracker` records the collision.

### YAML format

```yaml
# Text/UI/EN.yml
KEY_NAME: "Replacement text"
ANOTHER_KEY: "Supports \"escaped quotes\""
```

Lines starting with `#` and blank lines are skipped. Keys are case-sensitive.

### Reload

`DialogueHandler.Reload()` clears `TextCache` and calls `Language.SwitchLanguage`
(current language) so the game re-requests all text keys. Active dialogue boxes
update only when re-triggered; static UI labels update immediately.

### Stale key detection

Keys loaded from disk but never requested by the game are reported as stale after
each scene load (`CheckForStaleKeys()`). Stale = key was likely renamed by a game
update; the override is silently not applied. Re-dump text to find the new key name.

---

## File Watching

`TextFileWatcher` is constructed once in `Plugin.Awake()` after `InitializeFolders()`
ensures `TextLoadPath` exists.

```
TextWatcher      → watches  Patchwork/Text/          (always)
PackWatchers[n]  → watches  [packPath]/Text/          (packs active at startup,
                                                        if directory exists)
```

Any change fires `ReloadText = true`. `Plugin.Update()` picks this up and calls
`DialogueHandler.Reload()`.

---

## Known Bug — Watcher Not Updated on Pack Change

**Symptom**: After enabling or disabling a pack via the Pack Manager, edits to files
inside that pack's `Text/` directory do not trigger hot reload. The root `Text/`
folder is unaffected — it is always watched — but the read path (`LoadTextSheet`)
also always reads from the root, so root content is present in every reload regardless
of what triggered it.

**Root cause**: `TextFileWatcher` is never reconstructed when the active pack list
changes. `PackManager.TriggerFullReload()` only sets `ReloadText = true`; it does not
update `PackWatchers`. Packs enabled after startup have no watcher on their `Text/`
dir.

**Fix**: Reconstruct `PackWatchers` (dispose old watchers, re-create for current
`Plugin.PluginPackPaths`) whenever `TriggerFullReload` fires, or expose a
`TextFileWatcher.RebuildPackWatchers()` method and call it from `PackManager.Apply()`.

```csharp
// TextFileWatcher — add this method:
public void RebuildPackWatchers()
{
    foreach (var w in PackWatchers) { w.EnableRaisingEvents = false; w.Dispose(); }
    PackWatchers.Clear();
    foreach (var packPath in Plugin.PluginPackPaths)
    {
        string textDir = Path.Combine(packPath, "Text");
        if (Directory.Exists(textDir))
            PackWatchers.Add(CreateWatcher(textDir));
    }
}

// PackManager.TriggerFullReload — add:
Plugin.TextFileWatcher?.RebuildPackWatchers();
```

**Note**: `LoadTextSheet` already reads from the correct set of paths on every reload
(lazy evaluation of `Plugin.PluginPackPaths`). The bug only affects watcher coverage —
file changes in newly-enabled packs go undetected. The text content is correct as soon
as any reload fires.

---

## Asset Layout Reference

```
PackRoot/
  Text/
    [SheetName]/
      EN.yml
      ZH.yml
      (any SupportedLanguages enum value)
```

Sheet names and language codes must match exactly what `Language.GetSheets()` and
`Language.CurrentLanguage()` return. Use the text dump (`DumpText` config flag, F6) to
discover valid sheet and key names.
