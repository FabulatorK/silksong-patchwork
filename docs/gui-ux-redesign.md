# GUI / UX Redesign Plan

## Status: IMPLEMENTED — all 8 steps shipped

---

## Problem Statement

The current GUI is a flat list of 8 independent windows, each behind its own
keybind (Alpha1–Alpha8).  There is no distinction between tools that end users
need and tools that asset creators need.  Several windows are semantically paired
but structurally separate (Audio Log + Audio List; Text Log + Dialogue Editor),
and Video replacements have no dedicated window at all.

### Current window inventory

| Key | Window | Target | R/W |
|-----|--------|--------|-----|
| Alpha1 | Audio Log — live playback log | Users & creators | Read |
| Alpha2 | Audio List — loaded clips inventory | Users & creators | Read |
| Alpha3 | Animation Controller — tk2d frame inspector | Creators | Write |
| Alpha4 | Text Log — accessed dialogue keys | Users & creators | Read |
| Alpha5 | Skin Status — asset load overview | Users & creators | Read |
| Alpha6 | Dev Profiler — performance metrics + actions | Creators | Write |
| Alpha7 | Dialogue Editor — in-game text editor | Creators | Write |
| Alpha8 | Pack Manager — pack enable/priority/conditions | Users & creators | Write |

### Core problems

- 8 keybinds with no mental model
- No end-user / creator split — same keymap for both audiences
- Audio Log and Audio List are two halves of one view
- Text Log and Dialogue Editor are two halves of one workflow
- Animation Controller is orphaned from other graphics tools
- Video has no home (tracked nowhere, glimpsed only as a count)

---

## Target Architecture

Two entry points, cleanly separated by audience:

```
End users:   [Pack Manager]  ←→  [Status Overlay]
Creators:    [Pack Manager]  →   [Dev Hub: Graphics | Audio | Text | Perf | Video]
```

### Entry point 1 — Pack Manager  *(unchanged for users)*

Already the correct end-user tool.  Gains one addition: a **"Dev Tools →"** button
in its footer, visible only in dev mode, that opens the Dev Hub.

### Entry point 2 — Status Overlay  *(new, end users)*

Always-on, corner-mounted, non-intrusive.  Replaces the need for end users to open
any other window.

```
[■ 2 packs active]  [! 1 conflict]  [42 sprites  |  8 clips]
```

- Clicking opens Pack Manager
- Warning badge appears when conflicts exist
- Replaces Skin Status for end-user verification needs

### Entry point 3 — Dev Hub  *(new, creators)*

Single tabbed window.  One keybind opens it; Alpha keys 2–5 open it directly to
the matching pillar.

```
┌─ Patchwork Dev Hub ──────────────────────────────────────┐
│  [Graphics]  [Audio]  [Text]  [Performance]  [Video]      │
├──────────────────────────────────────────────────────────┤
│  (pillar content)                                         │
└──────────────────────────────────────────────────────────┘
```

---

## Keybind Consolidation

| Key | Before | After |
|-----|--------|-------|
| Alpha1 | Audio Log | Pack Manager |
| Alpha2 | Audio List | Dev Hub → Graphics |
| Alpha3 | Animation Controller | Dev Hub → Audio |
| Alpha4 | Text Log | Dev Hub → Text |
| Alpha5 | Skin Status | Dev Hub → Performance |
| Alpha6 | Dev Profiler | *(freed)* |
| Alpha7 | Dialogue Editor | *(freed)* |
| Alpha8 | Pack Manager | *(freed)* |

Six keybinds freed.  Alpha2–Alpha5 open the Dev Hub and jump directly to the
labelled tab so muscle memory from the old layout is partially preserved.

---

## Dev Hub Pillars

### Graphics

Absorbs: **SkinStatus**, **AnimationController**

```
[Summary: 142 sprites | 3 spritesheets | 28 T2D | 6 animated]

Tabs: [Sprites] [Spritesheets] [T2D] [Animated]

Sprites tab:
  Search: [_______________]
  name                  pack        atlas            status
  knight_idle_01        MyPack      HeroMaterial     ● loaded
  shockwave_hit         —           —                ○ on-disk only
  [Dump All]  [Dump Selected]

Animated tab:
  → Animation Controller content, contextually docked here
  Currently animating: 4 objects
  [animator list + frame stepper]
```

**Sources:** `SpriteLoader`, `T2DLoader`, `AudioHandler` (for asset counts),
`AnimationController` (frame inspection logic moved here as a sub-tab)

### Audio

Absorbs: **AudioLog**, **AudioList**

```
[Summary: 8 clips loaded | last played: MenuTheme 0.3s ago]

Left — Loaded Clips (searchable):        Right — Live Log (fade, configurable):
  MenuTheme.ogg      MyPack  ● ready       0.3s  MenuTheme       [replaced]
  HitEffect_01.wav   MyPack  ● ready       1.1s  HitEffect_01    [replaced]
                                           2.4s  FootstepGrass   [vanilla]
```

Two panes, one window.  Config options (log duration, max entries, hide-modded)
surface as a small settings row at the top of the pillar.

**Sources:** `AudioHandler.LoadedClips`, `AudioLog` callback feed

### Text

Absorbs: **TextLog**, **DialogueEditor**

```
Left — Accessed Keys (clickable):        Right — Editor (selected entry):
  UI.MenuStart   "Press any button…"       Sheet: UI    Key: MenuStart
  ENEMIES.Crawler "Crawler enemy desc"     [________________________]
  …                                        Tags: <page> <br> <player>
                                           [Save]  [Revert]  [Delete]
                                           Status: ✓ saved
```

Clicking a key in the left pane populates the right pane.  This unifies the
TextLog → DialogueEditor navigation that currently requires opening two separate
windows and clicking across them.

**Sources:** `DialogueHandler`, `TextLog` callback feed

### Performance

Rehouses: **DevProfiler** — no content changes, just relocated

```
FPS: 240  Frame: 0.41ms  Update: 0.12ms
[FPS graph]
Mono heap: 48 MB  |  GC: 3 collections this session
T2D: 0 setter calls/frame  |  Tracked renderers: 12
[Force GC]  [Reload Sprites]  [Reload Audio]  [Reload Text]  [Log Hierarchy]
```

### Video  *(stub — activates when VideoHandler has content)*

```
[No video replacements loaded]
```

When active, follows the same table pattern as the Graphics pillar:
`name | pack | resolution | status`.

---

## File Structure After Redesign

```
gui/
  PackManagerWindow.cs        ← unchanged
  StatusOverlay.cs            ← new
  DevHub.cs                   ← new shell (tab bar + pillar dispatch)
  pillars/
    GraphicsPillar.cs         ← absorbs SkinStatus.cs + AnimationController.cs
    AudioPillar.cs            ← absorbs AudioLog.cs + AudioList.cs
    TextPillar.cs             ← absorbs TextLog.cs + DialogueEditor.cs
    PerformancePillar.cs      ← absorbs DevProfiler.cs
    VideoPillar.cs            ← new stub

  [deleted after migration]
  AudioLog.cs
  AudioList.cs
  AnimationController.cs
  TextLog.cs
  SkinStatus.cs
  DevProfiler.cs
  DialogueEditor.cs
```

`Plugin.cs` goes from 7 `ShowX` booleans + a long `OnGUI` dispatch block to:
```csharp
public static bool ShowPackManager   = false;
public static bool ShowStatusOverlay = true;   // on by default
public static bool ShowDevHub        = false;
public static int  DevHubTab         = 0;      // 0=Graphics … 4=Video
```

---

## Build Sequence

Each step is independently shippable.  Merge order matters: later steps depend on
earlier ones being in place, but each step leaves the mod in a working state.

### Step 1 — StatusOverlay  *(new file, no deletions)*
- Create `gui/StatusOverlay.cs`
- Wire into `Plugin.OnGUI` alongside existing windows
- Reads from `PackManager`, `ConflictTracker`, `SpriteLoader`, `AudioHandler`
- Always visible by default; toggled by same key as Pack Manager (opens PM on click)

### Step 2 — DevHub shell  *(new file, no deletions)*
- Create `gui/DevHub.cs` with tab bar rendering and empty pillar stubs
- Add `ShowDevHub` / `DevHubTab` to `Plugin.cs`
- Wire Alpha2–Alpha5 to open Dev Hub at the correct tab
- All old windows still exist and still work — nothing broken yet

### Step 3 — PerformancePillar  *(first migration, lowest risk)*
- Create `gui/pillars/PerformancePillar.cs`
- Move `DevProfiler.Draw()` content into `PerformancePillar.Draw()`
- `DevProfiler.cs` becomes a thin shim calling `PerformancePillar` (kept for one
  release to avoid hard breakage), then deleted
- Alpha6 still works via the shim during transition

### Step 4 — AudioPillar
- Create `gui/pillars/AudioPillar.cs`
- Move `AudioLog` feed + `AudioList` content into two panes in one pillar
- Config row at pillar top replaces the separate config entries in PatchworkConfig
- Delete `AudioLog.cs` and `AudioList.cs`
- Alpha1 and Alpha2 now map to Pack Manager and Dev Hub → Audio respectively

### Step 5 — TextPillar
- Create `gui/pillars/TextPillar.cs`
- Left pane: TextLog feed (clickable rows)
- Right pane: DialogueEditor edit surface
- Wire the existing `TextLog → DialogueEditor.SelectEntry()` call into pillar-internal
  navigation (left click → right pane populates)
- Delete `TextLog.cs` and `DialogueEditor.cs`

### Step 6 — GraphicsPillar
- Create `gui/pillars/GraphicsPillar.cs`
- Sub-tabs: Sprites | Spritesheets | T2D | Animated
- Move `SkinStatus` content into Sprites/T2D sub-tabs
- Move `AnimationController` content into Animated sub-tab; its Harmony patches and
  Update/LateUpdate hooks remain in `AnimationController.cs` (data layer stays
  separate from view)
- Delete `SkinStatus.cs`; retain `AnimationController.cs` as a pure data/patch layer
  with no `Draw()` method

### Step 7 — VideoPillar stub
- Create `gui/pillars/VideoPillar.cs`
- Reads from `VideoHandler` (existing, not yet surfaced in any window)
- Shows placeholder until VideoHandler has content

### Step 8 — Pack Manager footer + keybind cleanup
- Add "Dev Tools →" button to Pack Manager footer (visible only when dev mode is on)
- Remove Alpha6–Alpha8 keybind entries from `PatchworkConfig`
- Update keybind config section doc comment

---

## What Does Not Change

- Pack Manager internals (conditions, profiles, conflict view, ghost add row,
  far-left AND bracket) — all preserved exactly
- All creator functionality — nothing is removed, only relocated and consolidated
- Data layer — handlers, loaders, `PlayerDataCatalog`, `PackManager`, etc.
- Packed bundle format work (`.pwpk`) — orthogonal to this plan

