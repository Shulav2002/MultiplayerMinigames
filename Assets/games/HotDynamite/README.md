# HotDynamite (Short Technical Notes)

## Core Loop
- `HotDynamiteGameManager` spawns players from fixed `spawnPoints`.
- One alive player is chosen as bomber.
- Bomb fuse defaults to `10` seconds per round (from bomb settings asset).
- Bomb can be passed on collision, but pass does **not** reset the fuse.
- When fuse reaches zero, current bomber is eliminated and a new round starts (until one player remains).

## Main Scripts
- `Scripts/HotDynamiteGameManager.cs`
  - Round state, bomb ownership, pass cooldown, elimination.
- `Scripts/HotDynamitePlayer.cs`
  - Movement, collision-based bomb pass requests, facing flip, bomb visual child object.
- `Scripts/HotDynamiteBotController.cs`
  - Bot decision logic (chase/intercept when bomber, evade/orbit/cohesion when non-bomber).
- `Scripts/HotDynamitePlayerInput.cs`
  - Mobile circular joystick + legacy keyboard fallback.
- `Scripts/FixedAspectCamera.cs`
  - Optional fixed-aspect viewport letterbox/pillarbox handling.

## Gameplay Tuning (Game Settings)
Use `HotDynamiteGameSettings` to tune:
- Player speed and bomb-holder speed multiplier.
- Pass cooldown.
- Pass effects:
  - `bombPassSpeedEffectSeconds`
  - `bombPasserSpeedMultiplier`
  - `bombReceiverSpeedMultiplier`
- Bot steering/avoidance behavior.

## Split ScriptableObjects
`HotDynamiteGameSettings` now supports split config assets:
- `Data/HotDynamitePlayerSettings.asset`
- `Data/HotDynamiteBombSettings.asset`
- `Data/HotDynamiteArenaSettings.asset`
- `Data/HotDynamiteBotSettings.asset`

`Data/HotDynamiteGameSettings.asset` references these assets, and falls back to legacy inline values if a split asset reference is missing.
You can keep values synced with `keepSettingsSynced` and choose sync direction using `syncMode`.

## Recent Refactor Notes
- Removed unused manager state to reduce redundancy.
- Simplified repeated movement and bomb-visual logic in `HotDynamitePlayer`.
- Refactored bot update internals to reduce repeated calculations and duplicated normalization.
- Reduced joystick texture generation overhead in `HotDynamitePlayerInput`.
