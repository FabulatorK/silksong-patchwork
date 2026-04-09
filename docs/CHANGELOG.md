### Unreleased

#### UI visual identity
* Cassette-label colour palette: 13 design tokens replacing the original 8-colour blue-accent set. Primary accent is now orange (#E87530), confirmations are teal-green (#33AA88), danger is magenta-red (#CC3366), with specular green/blue reserved for rare pops
* Vertical cassette strip (14px, 5-band: 40% crimson / 10% orange / 5% green / 5% blue / 40% bone) drawn on Pack Manager left edge
* Horizontal cassette strip (2px, alpha-fading) on StatusOverlay bottom edge
* All hardcoded inline colours in Pack Manager and StatusOverlay migrated to design tokens

#### Pack Manager layout
* Top bar stripped to Rescan (left) + × close (right)
* ON/OFF toggle replaced with prominent ✓ square button; enabled packs get a full teal card border
* Packs with conditions get an orange card border; condition editor opened via ⚙ gear icon with separator divider
* Action bar moved to bottom of window (browse → decide → commit flow): status count + "unsaved changes" notice on layer 1, Discard + Apply on layer 2
* Footer: Status Overlay toggle takes ~70% width as primary banner; Dev Tools is a small ⚒ square
* Removed Pin to RAM (`PackRamCache`) — redundant with `FileCache`; saved only a `stat()` syscall per file while duplicating all PNG bytes in memory
* Procedural cassette strip textures: rounded caps, AA diagonal transitions, depth gradient — single `DrawTexture` call replaces dozens of IMGUI rects
* Borderless window: custom title bar, zero-padding style, strip flush with true window edge
* FabricUI Canvas toolkit (`gui/FabricUI.cs`): general-purpose factory for Canvas UI elements — panels, buttons, text, scroll views, input fields, layout groups, toggles, rounded-rect 9-slice sprites. Foundation for Canvas migration of all end-user UI surfaces
* Canvas Pack Manager (`gui/CanvasPackManager.cs`): retained-mode replacement for the IMGUI Pack Manager, built on FabricUI. Full feature parity: pack list with toggle/arrows/gear/conflict badge, DNF condition editor with AND-group brackets and type cycling, profiles, conflicts foldout, action bar with staging, footer, cassette strip as RawImage, draggable panel. Toggled via `UseCanvasPackManager` config (default: off, experimental)

#### T2D sprite loader
* Fixed T2D spritesheet revert failure after long sessions: `_originalTextureData.Clear()` was destroying vanilla backups for textures in unloaded scenes before they could be restored. Now protected by `_restoredNames` confirmation tracking
* Fixed T2D spritesheet clean-name fallback: dimension-based disambiguation allows multiple textures sharing a clean name (e.g. two Hornet atlases at different resolutions) to each match their correct override. Handles game-update hash changes in texture names
* Deduplicated SpriteLoader same-name collection warnings: fires once per name instead of every LoadCollection call

#### Audio
* Audio browser rewritten: `AudioHandler.GetClipInventory()` does a full `Resources.FindObjectsOfTypeAll<AudioClip>()` sweep so clips that never fire through the Harmony play path (ambient, music, pre-assigned) are discoverable without ever playing them
* `AudioClipEntry` model carries length, channel count, frequency, `HasReplacement` flag, and a list of live `AudioSource` game-object paths
* `AudioPillar` two-pane layout: left pane is a searchable clip browser with virtual scrolling (only visible rows rendered — fixes 200fps→40fps regression with large clip sets); right pane is the live play log
* Each log entry shows `clipName ← GameObjectPath` so creators know what action triggered a sound; clicking copies the name and focuses the browser
* `AudioList.Version` counter lets the browser cache its filtered list and rebuild only when the underlying data changes
* Removed `OnAudioSourceLoaded` event; `OnAudioPlayed` now carries `(AudioClip, AudioSource)` so source context flows through one path

#### tk2d sprite loader
* Fixed same-name collection collision: two `tk2dSpriteCollectionData` objects with identical `.name` values (e.g. "Slab Prisoner Cln Data") collided in all runtime dicts — now keyed by composite instance key `name\x00instanceID`
* Pack authors can target a specific same-named collection instance by using the vanilla texture name as the folder/file discriminator: `Sprites/{collName}/{vanillaTexName}/{spriteName}.png`
* Fixed Patchwork altering display even with no replacement files: `_collectionsWithFiles` gate skips the atlas RT swap when a collection has no pack files at all
* Fixed sprite-batch misses due to material instance drift: `def.materialId` (authoritative tk2d index) now used instead of `def.material` reference equality in both `SpriteLoader` and `SpriteDumper`
* `ResolveKey()` helper in `T2DLoader` centralises the `_spriteNameToKey` fallback used by both `CheckSprite` and `TryGetReplacement` — fixes uninit sweep missing atlas-qualified replacement keys

#### GUI
* IMGUI tooltip system: `GUIHelper.TT(label, tooltip)` + `DrawTooltip()` renders a semi-transparent tooltip box near the cursor; `BeginOnGUI` clears `GUI.tooltip` at the start of each Repaint pass to prevent stale Layout-pass values causing phantom tooltips
* `T2DTextureController` gains a sprite filter search field in the detail pane
* `T2DLog` deduplicates at atlas level (one entry per atlas, not per sprite); sprite name updated silently within a 2 s cooldown window before re-bumping to top
* Cursor is now shown automatically (`Cursor.visible = true`, `CursorLockMode.None`) in `LateUpdate` whenever DevHub or PackManager is open, overriding the game's per-frame cursor lock

#### Pack system
* Profile save/load now includes conditions and `ReloadTrigger`: `SaveProfile` appends trigger + serialised conditions tab-separated after each pack path; `StageProfile` restores them. Old profiles (no tab fields) load without change
* `TextFileWatcher.RebuildPackWatchers()` is called from `TriggerFullReload()` — packs enabled after startup have their `Text/` dirs watched correctly (previously a known gap)

#### Window persistence
* DevHub window position and active tab index are persisted via `ConfigEntry` and restored on next session
* Pack Manager window position is persisted the same way
* Positions are written to config in `Plugin.OnDestroy` (clean exit)

#### Video
* `VideoHandler.OnCinematicTriggered` event fires with `(name, hasReplacement)` whenever the game starts a cinematic
* `VideoPillar` now shows a live cinematic trigger log below the replacement table: each entry has clip name (click=copy), `[R]` badge if replaced, and `HH:mm:ss` timestamp

#### Earlier unreleased
* Removed legacy standalone window shim methods (`DevProfiler.Draw`, `DrawAnimationController`, `DrawAudioLog`, `DrawAudioList`, `DrawTextLog`) and dead `Plugin.ShowDialogueEditor` field — all content is served exclusively through the Dev Hub pillars
* Decoupled handlers from GUI: `AudioHandler` and `DialogueHandler` now fire static events (`OnAudioPlayed`, `OnAudioSourceLoaded`, `OnTextAccessed`); `Plugin.Awake` wires the GUI subscribers, satisfying Architecture Principle 4
* Dashboard text file list now shows `Sheet/LANG.yml` paths (relative to their Text/ root) with loaded-status badges
* TextPillar search now covers both in-session TextLog keys and all loaded YAML cache overrides; cache-only hits are blue-tinted
* TextPillar editor gains a direct Sheet / Key / Open row for editing any key without triggering it in-game first
* Added T2D Textures tab to the Graphics pillar — searchable list of all T2D atlases and standalone textures in the current scene, with live atlas preview, per-sprite UV highlight, and one-click edit/dump workflow
* AnimationController now shows an atlas thumbnail + yellow frame-highlight below the Edit buttons for the selected animator
* Fixed T2DDumper standalone leak: tk2d atlas textures no longer appear in `Dumps/T2D/_standalone/`
* Fixed T2DDumper writing duplicate individual sprite files
* Fixed T2D revert sweep breaking individual sprite priority
* Fixed T2D revert block being skipped when base-path T2D files kept `HasT2DReplacements` true with no active pack
* Fixed T2D spritesheet and audio revert cascade failures on pack disable
* Fixed T2D Inventory/UI sprites going black when unloading a pack
* Fixed `_confirmedSpriteNames` accumulating unboundedly across scene transitions
* Fixed `_originalTextureData` holding vanilla PNG bytes until next scene unload after pack disable
* Moved `CheckForUninitializedSprites` from a 30-frame Update timer to `sceneLoaded`

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