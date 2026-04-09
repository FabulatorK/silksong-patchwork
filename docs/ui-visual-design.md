# Patchwork — UI Visual Design

Living document. Tracks the visual identity, colour decisions, layout principles,
and future direction for all Patchwork UI surfaces.

---

## Identity: Cassette Label

The visual identity draws from cassette tape label design — a vertical colour
strip running the height of the main window, banded in proportions that evoke
both the medium and the game's protagonist:

| Band | Ratio | Colour | Hex | Intent |
|------|-------|--------|-----|--------|
| Top | 40% | Hornet crimson | `#CC2222` | Primary identity, warmth |
| | 10% | Orange | `#E87530` | Accent bridge, energy |
| | 5% | Transition green | `#3DBB7A` | Specular pop — reserved |
| | 5% | Transition blue | `#3D8EBB` | Cool counterpoint — reserved |
| Bottom | 40% | Bone off-white | `#DDD8D0` | Mask, porcelain, shell |

The strip's red and bone are **identity-only** — they brand the window but do
not appear on interactive elements. The transition green and blue are used
sparingly for rare highlights (Hades-style specular pops), never as broad
surfaces.

---

## Colour Tokens

All colours are defined in `GUIHelper.cs` as `static readonly Color` fields.
No hardcoded RGB values should appear in component code.

### Surfaces

| Token | Hex | Use |
|-------|-----|-----|
| `ColSurface` | `#19191F` | Window/panel background |
| `ColSurface1` | `#2B2B38` | Raised container (card rows) |
| `ColBorder` | `#474759` | Dividers, separators, inactive button bg |

### Interactive

| Token | Hex | Use |
|-------|-----|-----|
| `ColAccent` | `#E87530` | Primary interactive: selected, active, focus |
| `ColAccentSoft` | `#CC5522` | Hover, secondary highlight |
| `ColConfirm` | `#33AA88` | Apply, ON states, confirmations |
| `ColDanger` | `#CC3366` | Close, remove, destructive actions |
| `ColWarn` | `#FFBF33` | Warnings, conflicts, caution states |
| `ColMuted` | `#8C8CA6` | Secondary text, disabled elements |

### Rare accents

| Token | Hex | Use |
|-------|-----|-----|
| `ColBone` | `#DDD8D0` | Warm off-white — strip identity, possible tooltip bg |
| `ColPop1` | `#3DBB7A` | Specular green — condition add, rare highlight |
| `ColPop2` | `#3D8EBB` | Specular blue — Dev Tools, bracket lines, rare highlight |

### Semantic colour separation

Red is identity (strip) and danger (buttons) — these must not collide:
- **Strip red** (`#CC2222`) is deeper crimson, lives only on the strip.
- **Danger red** (`#CC3366`) is magenta-shifted, reads as "stop" without reading
  as "Hornet."
- **Confirm green** (`#33AA88`) is teal-shifted, cool enough to pop against the
  warm palette without clashing with `ColPop1` specular green.

---

## Layout Principles

### Pack Manager (end-user surface)

The window flow matches natural reading order: **browse -> decide -> commit**.

```
Top bar       Rescan (left) + x close (right). Clean, quiet.
Pack list     Core content. Scrollable. Each row:
              - Left: square toggle (check fill when ON, empty when OFF)
              - Centre: pack name, source chip, conflict badge
              - Right: priority arrows, gear icon for conditions
              Card border: teal when enabled, orange when conditions present.
Action bar    Layer 1: status count + "unsaved changes" in amber
              Layer 2: Discard + Apply side by side (Apply slightly heavier)
Footer        Status Overlay toggle (~70%) + Dev Tools square
```

### StatusOverlay (always-on badge)

Compact bottom-left badge. Horizontal cassette strip (2px) along bottom edge
with alpha fade. Rich-text segments coloured by token (teal for packs, amber
for conflicts, muted for stats). Click opens Pack Manager.

### Dev Hub (creator surface)

Not yet restyled under the cassette identity. Future work.

---

## Principles

1. **Contrast over decoration.** Every colour choice must preserve text
   readability. Tinted backgrounds stay dark enough that white text reads
   clearly. Border highlights are 60% alpha at most.

2. **Semantic instinct.** Green means yes. Red means stop. Orange means
   attention. Don't betray these. The cassette identity lives on the strip
   and in the token names, not by overloading red onto confirm buttons.

3. **Reserve the pops.** `ColPop1` and `ColPop2` are specular highlights —
   a flash of colour on a single badge or bracket, not broad surfaces.
   Their power comes from scarcity.

4. **Audience separation.** End users see Pack Manager and StatusOverlay.
   Creators see Dev Hub. Visual weight should match: Pack Manager is
   polished and restrained; Dev Hub can be denser and more technical.

---

## Future Directions

Areas to explore as the visual identity matures. These are open questions,
not commitments.

### Canvas Migration (IN PROGRESS)

The Pack Manager is being migrated from IMGUI to Unity's retained-mode Canvas
system. `FabricUI.cs` provides the foundation — a general-purpose toolkit for
creating Canvas UI elements programmatically (panels, buttons, text, scroll
views, input fields, layout groups, toggles, rounded-rect sprites via 9-slice).

**What's done:**
- `gui/FabricUI.cs` — complete Canvas UI factory (630 lines). Resolution-
  independent via `GUIHelper.Scale`. Hover transitions via Unity's built-in
  `ColorTint` with 0.08s fade. Rounded sprites with procedural generation
  and caching. All colour tokens flow from GUIHelper.
- Architecture decision: Canvas for end-user surfaces (Pack Manager,
  StatusOverlay), IMGUI stays for creator tools (Dev Hub).

**What's next:**
- `gui/CanvasPackManager.cs` — full Canvas Pack Manager replacing
  `PackManagerWindow.cs`. Must replicate all features: pack list with
  toggle/arrows/gear, condition editor, profiles, conflicts, action bar,
  footer, cassette strip, dragging, input blocking.
- Wire into `Plugin.cs` alongside the IMGUI version (flag-toggled during
  development, Canvas becomes default once proven).
- Cassette strip becomes a Canvas `RawImage` child — no more coordinate
  hacking or negative offsets.

**Canvas advantages over IMGUI:**
- Real pointer events (`PointerEnter/Exit/Down`) — hover states for free
- Retained state — widgets persist between frames, no rebuild every OnGUI
- Unity's `Button.ColorTint` transition — smooth hover/press without a
  custom animator
- `Image.sprite` with 9-slice — actual rounded rectangles, anti-aliased
- `ScrollRect` with inertia — smooth scrolling
- `CanvasGroup.alpha` — fade transitions
- Single draw call per batch — cheaper than dozens of IMGUI DrawTexture calls

**Design references:**
- DebugMod (hk-speedrunning/Silksong.DebugMod): built a retained-mode UI
  on Canvas from scratch. Catppuccin palette, hover borders, panel builder.
  Patchwork's FabricUI is more concise — uses Unity's built-in layout
  components directly instead of a custom scene graph.
- osu!lazer: custom framework, not directly portable, but the design
  principles apply — everything responds to interaction, rounded shapes,
  consistent spacing grid, blur backdrops, colour as information.

### Fork: Fabricwork

The project is forking from Patchwork to **Fabricwork**. The name carries
triple meaning: textile (silk, thread, Silksong), construction (a workshop,
*faber* = craftsman), and the underlying structure (the fabric of the game).

Rename plan:
- New name in UI, logs, window titles, manifest
- Backward compatibility: discover both `Fabricwork/` and `Patchwork/`
  subfolders for pack structure. Config reads from both paths.
- Namespace migration can be gradual — `Patchwork` internally is fine
  initially.
- `FabricUI` already fits the new name.

### Dev Hub

- Apply the cassette strip to the Dev Hub window.
- Retune tab bar from inline hardcoded colours to design tokens.
- Consider whether pillar content needs card-style containers or can stay
  denser (audience is creators, not end users).
- Canvas migration is lower priority — Dev Hub is creator-facing and density
  matters more than polish.

### StatusOverlay

- Migrate to Canvas — becomes a Canvas panel with the horizontal cassette
  strip as a child `RawImage`. No more IMGUI coordinate hacking.
- Explore whether the badge should grow/shrink based on content.
- Consider a compact mode (icon-only) vs expanded mode (current text).

### Pack row detail

- Condition indicator: the orange card border signals "has conditions" but
  doesn't communicate what they are. Consider a subtle inline summary
  (e.g. "Scene: Crossroads" in muted text) below the pack info.
- Pack thumbnail/icon: if packs ship a small icon, display it in the
  toggle square area.

### Animation and transitions

With Canvas, the barriers to animation are removed:
- `Button.ColorTint` gives hover/press transitions for free (already in FabricUI)
- `CanvasGroup.alpha` enables fade-in/out on panels
- Position lerping in Update() for slide transitions
- Spring-based easing for osu!-style bouncy feedback
- Consider a small `FabricAnimator` utility for keyed value lerping

### Blur backdrop

- Possible via a custom shader in `patchwork.assetbundle`
- Blur a small rect behind the Pack Manager panel only, downsampled 4x
- Only active when the panel is open — zero cost when closed
- Gives depth and the frosted-glass look (osu! / iOS style)

### Profiles section

- Currently a horizontal scrollbar of profile name buttons. Consider
  a cleaner list or dropdown treatment.

### Conflicts section

- Foldout with raw text. Consider card-style entries with colour-coded
  type badges.
