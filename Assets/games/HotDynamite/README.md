# HotDynamite (Short Technical Notes)

## Core Loop
- `HotDynamiteGameManager` spawns players from fixed `spawnPoints`.
- One alive player is chosen as bomber.
- Bomb fuse is fixed to `10` seconds per round.
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

## Recent Refactor Notes
- Removed unused manager state to reduce redundancy.
- Simplified repeated movement and bomb-visual logic in `HotDynamitePlayer`.
- Refactored bot update internals to reduce repeated calculations and duplicated normalization.
- Reduced joystick texture generation overhead in `HotDynamitePlayerInput`.
