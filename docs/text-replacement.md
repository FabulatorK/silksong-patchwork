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
 1. For each active pack (priority order, highest first):
      Read  [packPath]/Text/[sheet]/[LANG].yml
      First-wins: a higher-priority pack's entry is never overwritten.
 2. Read  Patchwork/Text/[sheet]/[LANG].yml   ← root, always wins
      Root entries unconditionally overwrite any pack entry.
 3. Cache the merged dict.
```

**Root is the highest priority.** This mirrors the sprite and audio pipelines where
`Patchwork/Sprites/` and `Patchwork/Sounds/` beat every pack.  A key present in the
root always shows in-game regardless of active packs — the Dialogue Editor's saves are
always reflected.  Among packs, the highest-priority pack wins and the conflict is
recorded in `ConflictTracker`.

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

## Hot-Reload Coverage

`TextFileWatcher.RebuildPackWatchers()` is called from `PackManager.TriggerFullReload()`
whenever the active pack set changes.  It disposes all existing pack watchers and
creates fresh ones for every currently-active pack that has a `Text/` directory.

```
TextWatcher      → watches  Patchwork/Text/          (always, created once)
PackWatchers[n]  → watches  [packPath]/Text/          (rebuilt on every pack change)
```

Any `.yml` change in any watched directory sets `ReloadText = true`; `Plugin.Update()`
picks this up on the next frame and calls `DialogueHandler.Reload()`.

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
