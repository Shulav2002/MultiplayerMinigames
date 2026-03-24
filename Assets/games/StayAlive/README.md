# StayAlive

`StayAlive` has its own local player/input stack and focuses on a slot-board spin test:

- The board is an `11 x 8` grid.
- The current symbol set is `3` safe outcomes plus `1` bomb outcome.
- Fallback colors are:
  - safe: `white`, `green`, `yellow`
  - bomb: `red`
- Difficulty uses a `0..1` slider:
  - `0` = easy
  - `0.5` = normal
  - `1` = hard
- Bomb count now ramps up after each completed spin.
- Players move on top of the cells with the same general movement, collision, and animation feel, but the scripts live inside `StayAlive`.
- A visible `SPIN` button starts the reel effect.
- During spin, each column shifts symbols top-to-bottom like a slot reel.
- The board spins for `4` seconds, warns for `4` seconds, explodes bomb cells, then bomb cells shrink to zero, stay vanished briefly, and replacement safe cells grow back in with a tweened refill.

## Current Setup

- Add `StayAliveGameManager` to a GameObject in `Assets/games/StayAlive/Scene/StayAlive.unity`.
- The scene should already contain `Board` and `Players` child objects under the manager.
- The manager does not create board hierarchy at runtime anymore.
- In the editor, use `Rebuild Board In Editor` on the manager if the `Board` child does not already contain all `88` `StayAliveCell` children.
- Player art assets live inside `Assets/games/StayAlive`, and the player prefab uses only `StayAlive` scripts.
- Gameplay tuning now lives in `Assets/games/StayAlive/Data/StayAliveGameSettings.asset`, with optional split assets available under the `StayAlive/Settings/*` create menu.
- Default local play now starts with `4` total players and `1` human, so `3` bot seats are active without extra setup.
- `symbolSprites` and `bombSprite` are optional. If left empty, the board uses colored fallback tiles.
- `autoStartSpinOnPlay` is off by default, so spinning starts from the on-screen button.
- While spinning, the visible bomb frequency scales with the current round bomb target, so later spins look more dangerous before the board stops.
- Warning only highlights bomb cells, explosion only affects bomb cells, and collapse/refill only rebuilds those exploded cells.
- Bomb count is driven by difficulty with scene defaults:
  - easy: `16`
  - normal: `22`
  - hard: `30`
- Bomb increase per completed spin with scene defaults:
  - easy: `+5`
  - normal: `+7`
  - hard: `+10`

## Files

- `Scripts/StayAliveGameManager.cs`
- `Scripts/StayAlivePlayer.cs`
- `Scripts/StayAliveBotController.cs`
- `Scripts/StayAliveCell.cs`
