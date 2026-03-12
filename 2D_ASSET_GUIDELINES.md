# 2D Asset Guidelines for Designers

Use this document before delivering any 2D art for upcoming games in this Unity project.

The goal is simple:
- keep imports predictable
- reduce setup time in Unity
- avoid renaming, re-slicing, and pivot fixes after delivery

## 1. General Rules

- Deliver production-ready exports only. Do not place source `.psd`, `.ai`, `.aseprite`, `.zip`, or backup files inside the Unity asset folders.
- Keep source files in a separate shared source-art location outside the Unity project.
- Use PNG for sprites and UI unless a different format is explicitly requested.
- All sprites must have transparent backgrounds unless the asset is intentionally opaque.
- Trim empty space only when asked. For characters, VFX, and animation sequences, keep canvas size consistent across frames.
- Do not upscale small art using export software. Native resolution only.
- Avoid baked shadows, outlines, or glows unless they are part of the approved style.

## 2. Naming Convention

Use lowercase `snake_case` for files and folders.

Good examples:
- `player_red_idle_0000.png`
- `player_red_run_0007.png`
- `bomb_fuse_0012.png`
- `ui_button_play.png`
- `bg_island_far.png`

Bad examples:
- `Stickman 2_00005.png`
- `1 Pink_Monster`
- `Play-game.png`
- `final_final2.png`

Rules:
- No spaces in file or folder names.
- No leading numbers unless the number is part of a frame index.
- No special characters except underscore.
- Use 4-digit frame numbers for sequences: `0000`, `0001`, `0002`.
- Use clear prefixes by asset type:
  - `player_`
  - `enemy_`
  - `bomb_`
  - `fx_`
  - `ui_`
  - `bg_`
  - `prop_`
  - `icon_`

## 3. Folder Structure

Each game should follow a consistent folder structure under its own game folder.

Example:

```text
Assets/games/<game_name>/
  Sprites/
    Characters/
    Props/
    Environment/
    UI/
    FX/
  Anims/
  Prefabs/
  Audio/
  Data/
```

Rules:
- Put exported sprite images in `Sprites/`.
- Put Unity animation assets in `Anims/`. 
- Do not nest duplicate folders like `Sprites/pack_name/pack_name/...`.
- Do not mix lobby UI, gameplay sprites, VFX, and environment art in the same folder.

## 4. Canvas, Size, and Scale

Every asset set must declare its target size before production starts.

For each new game, confirm these values first:
- target art style: pixel art or non-pixel art
- base character height in pixels
- reference resolution
- pixels per unit
- expected camera framing

Default convention if not otherwise specified:
- non-pixel art gameplay sprites: export at final intended screen size, usually 1x
- animation frames in the same set must use the same canvas size
- character variants must match the same bounds and feet position
- mirrored color variants must not shift silhouette or pose

Design requirement:
- Put the character's contact point with the ground in a consistent position across idle, run, hit, and death animations.
- If the asset rotates in gameplay, keep the visual centered consistently around its intended pivot.

## 5. Pivots and Alignment

Every delivered asset set must include pivot intent.

Use these defaults unless told otherwise:
- characters: bottom center
- standing enemies: bottom center
- projectiles and bombs: center
- UI icons: center
- buttons and panels: center
- floor decals and shadows: center

Rules:
- Do not guess pivots differently across variants.
- Character color variants must share identical alignment.
- Animation frames must not drift unless motion is intentional.
- If an asset needs a custom pivot, include it in the handoff notes.

## 6. Animation Delivery

For frame-by-frame sprite animation:
- export one PNG per frame
- keep frame dimensions identical
- keep frame origin aligned
- use 4-digit numbering
- sort frames in playback order

Example:
- `player_blue_run_0000.png`
- `player_blue_run_0001.png`
- `player_blue_run_0002.png`

Required note per animation:
- animation name
- loop or one-shot
- target fps
- total frame count

If multiple color variants share the same motion:
- keep the same frame count
- keep the same pose timing
- keep the same canvas and pivot

## 7. UI Asset Rules

- Export UI elements at the exact intended aspect and shape.
- Separate text from button art unless the text is decorative and non-localized.
- Deliver normal, pressed, disabled, and highlighted states only if the feature actually uses them.
- Keep scalable panels clean for slicing; avoid details too close to panel edges.
- Icons should sit on a consistent canvas size per set.

For UI handoff, include:
- intended use
- expected size on screen
- whether the sprite should be sliced
- whether the element must support localization

## 8. Backgrounds and Environment

- Split backgrounds into logical layers when parallax or depth is expected.
- Name layers by depth, not by vague labels.

Good examples:
- `bg_sky_far.png`
- `bg_mountains_mid.png`
- `bg_trees_near.png`

- Keep tileable assets truly tileable on the exported edges.
- Deliver repeating ground, walls, or water as clean loops without visible seams.
- Mark decorative-only assets versus collision-relevant assets.

## 9. FX and Special Assets

- VFX sprites must be named by effect and sequence.
- Keep additive/glow elements on transparent backgrounds.
- Avoid downloaded asset-store filenames or website watermarks in final exports.
- Do not include vendor names, pack names, or random unicode symbols in filenames.

Example:
- `fx_explosion_smoke_0000.png`
- `fx_explosion_flash_0003.png`

## 10. Handoff Package

Each delivery should include:
- exported asset files
- a short `readme.txt` or `notes.md`
- preview sheet or contact sheet for animations
- source-file link or source location outside Unity

The notes file must state:
- asset pack name
- intended game or feature
- sprite type: gameplay, UI, environment, or FX
- pixel dimensions
- pivot rule
- frame rate for animations
- any custom import requirement

## 11. Designer Impact on Optimization

Designers affect runtime performance more than it usually seems. Most avoidable 2D performance issues start with oversized textures, inconsistent exports, or too many unique assets.

### Resolution and Texture Size

- Do not export assets larger than their intended on-screen use.
- If a button displays at roughly `256x128`, do not deliver it as `2048x1024` unless there is a real scaling requirement.
- Character and prop sprites should be sized for the actual gameplay camera, not for infinite zoom.
- Reuse a shared target size for similar assets so one set is not much larger than the rest.
- Prefer powers of two for large standalone textures when practical: `256`, `512`, `1024`, `2048`.
- Avoid using `4K`-sized textures for mobile-targeted 2D art unless explicitly approved.

### Overdraw and Transparency

- Large transparent canvases cost memory and can increase overdraw.
- For static props and UI, trim excess transparent space unless nine-slicing or consistent alignment requires fixed bounds.
- Avoid stacking many semi-transparent glow, smoke, shadow, and blur layers unless the effect is necessary.
- Keep full-screen transparent overlays to a minimum.

### Animation Cost

- More frames are not always better. Use the fewest frames that still look good.
- Avoid creating multiple near-identical animation sets when recolors or shared rigs can solve the same need.
- Keep color variants on identical dimensions and timing so engineers can reuse the same animation setup.
- Separate optional cosmetic variants from required gameplay assets.

### Atlas and Batching Friendliness

- Deliver related sprites in clean groups so they can be packed into atlases together.
- Keep UI assets grouped with UI, gameplay sprites with gameplay, and FX with FX.
- Avoid tiny one-off textures when they can be grouped into an existing set.
- Reuse common shapes and icons instead of exporting slightly different duplicates.

### UI Optimization

- Build reusable panels, icons, and frames instead of unique art for every screen.
- Use nine-slice-friendly panels where possible so one asset can scale to multiple layouts.
- Separate text from art to avoid making a new texture for every language or label change.
- Keep decorative UI textures smaller than gameplay-critical textures when possible.

### Submission Notes That Help Engineering

For each asset batch, designers should state:
- intended max on-screen size
- whether the asset must stay crisp when scaled up
- whether it belongs in a shared atlas
- whether transparent padding is required
- whether the asset is gameplay-critical, cosmetic, or optional

This helps engineers choose import settings, compression, atlas packing, and memory budgets faster.

## 12. Hard Rules for This Project

These are non-negotiable because they directly reduce Unity import and maintenance problems:

- No spaces in file names.
- No zip files inside `Assets/`.
- No duplicate nested pack folders.
- No renamed exports like `final`, `final2`, or `new`.
- No mixed naming styles in the same asset set.
- No inconsistent frame numbering.
- No source-art files committed into gameplay asset folders unless explicitly requested.

## 13. Delivery Checklist

Before handing off assets, confirm:
- filenames use lowercase `snake_case`
- folders follow the agreed structure
- frames are consistently aligned
- pivots are defined
- character variants match size and footing
- animation fps is documented
- transparent backgrounds are correct
- no source archives are inside the Unity asset path
- no unnecessary duplicate files are included

## 14. Recommended Approval Flow

Before full production, designers should submit:
1. one style sample
2. one character or prop sample export
3. one animation test
4. one UI sample if the game includes UI-heavy screens

Only after those are approved should the full asset batch be produced.

This prevents full-set rework caused by wrong scale, pivot, or naming.
