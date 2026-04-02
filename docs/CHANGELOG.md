### Unreleased
* Removed legacy standalone window shim methods (`DevProfiler.Draw`, `DrawAnimationController`, `DrawAudioLog`, `DrawAudioList`, `DrawTextLog`) and dead `Plugin.ShowDialogueEditor` field — all content is served exclusively through the Dev Hub pillars
* Decoupled handlers from GUI: `AudioHandler` and `DialogueHandler` now fire static events (`OnAudioPlayed`, `OnAudioSourceLoaded`, `OnTextAccessed`); `Plugin.Awake` wires the GUI subscribers, satisfying Architecture Principle 4
* Dashboard text file list now shows `Sheet/LANG.yml` paths (relative to their Text/ root) with loaded-status badges
* TextPillar search now covers both in-session TextLog keys and all loaded YAML cache overrides; cache-only hits are blue-tinted
* TextPillar editor gains a direct Sheet / Key / Open row for editing any key without triggering it in-game first
* Fixed text hot-reload: Patchwork root `Text/` is now highest priority (matches sprite/audio pipeline); pack `Text/` watchers are rebuilt when active packs change
* Added T2D Textures tab to the Graphics pillar — searchable list of all T2D atlases and standalone textures in the current scene, with live atlas preview, per-sprite UV highlight, and one-click edit/dump workflow (mirrors Animation Controller for T2D)
* AnimationController now shows an atlas thumbnail + yellow frame-highlight below the Edit buttons for the selected animator
* Fixed T2DDumper standalone leak: tk2d atlas textures no longer appear in `Dumps/T2D/_standalone/` even when their texture names don't match the compression-based filter
* Fixed T2DDumper writing duplicate individual sprite files when multiple Sprite objects reference the same atlas
* Fixed T2D revert sweep breaking individual sprite priority — revert sweep now sets `_enforcing` and explicitly prefers `_loadedSprites` over vanilla, preventing the Harmony setter chain from corrupting priority during pack reload
* Fixed T2D revert block being skipped when base-path T2D files kept `HasT2DReplacements` true with no active pack
* Fixed T2D spritesheet and audio revert cascade failures on pack disable
* Fixed T2D Inventory/UI sprites going black when unloading a pack
* Fixed spurious spritesheet size-mismatch warnings across scene transitions
* Removed eager byte-promotion Pass 2 and `WarmSprites` coroutine (accepted one-frame vanilla flash trade-off)
* Fixed `_confirmedSpriteNames` accumulating unboundedly across scene transitions — now cleared on `sceneUnloaded`
* Fixed `_originalTextureData` holding vanilla PNG bytes until next scene unload after pack disable — freed immediately when no spritesheet overrides remain
* Fixed `AudioHandler` using `ContainsKey`+indexer pattern instead of `TryGetValue`
* Fixed `FindSprite` enumerating its file list twice; added warning log for ambiguous multi-file matches in a pack
* Moved `CheckForUninitializedSprites` from a 30-frame Update timer to `sceneLoaded` — eliminates a recurring `FindObjectsByType` sweep every 0.5 s


* Added text replacement
* Massive update to Texture2D handling, adding support for more textures & sprites

### v2.4.0
* Implemented cutscene replacement support

### v2.3.3
* Fixed custom sprites not appearing for some enemy sprites that include Hornet

### v2.3.2
* Fixed some custom sprites reverting to default sprites after death
* Removed `LoadSprites` config option

### v2.3.1
* Fixed sprite loading breaking in certain areas
* Fixed non-statically loaded audio not being replaced

### v2.3.0
* Implemented the Patchwork Animation Controller:
  * Freeze the in-world locations of game objects for easier testing
  * Pause animations and step through them frame by frame ingame
  * Force-play animations ingame, selectable through a dropdown menu
  * "Edit Current Sprite" button, which automatically dumps the currently shown sprite and opens it for editing

### v2.2.4
* Added support for all Unity-supported audio formats

### v2.2.3
* Fixed crash on sound file change

### v2.2.2
* Added support for even more sounds

### v2.2.1
* Fix occasional pop sound at the end of custom sounds
* Added support for more sounds

### v2.2.0
* Added support for AudioClip replacement
* Implemented seamless sprite reloading (Sprite changes no longer reload the entire area)

### v2.1.2
* Fixed crashes when auto-reloading certain sprites

### v2.1.1
* Fixed plugin crash when using plugin pack with missing folders

### v2.1.0
* Support for plugin packs
* Thunderstore release

### v2.0.1
* Fixed an issue with dumping of certain rotated sprites

### v2.0.0
* Massive performance improvements by moving texture workload to GPU
* Texture2D replacement support
* Moved Patchwork folder to `BepInEx/plugins` for future mod manager support

### v1.0.2
* Added automatic resizing + warning log messages for incorrectly sized sprites

### v1.0.1
* Fixed a plugin crash during initialization if Patchwork folders didn't already exist

### v1.0.0
* Initial release! 🎉