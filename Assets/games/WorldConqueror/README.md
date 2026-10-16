# WorldConqueror GIS Map Pipeline

## Current match controls and rules

- Pick a map, bot difficulty, and bot style in the lobby. During a match, choose
  25%, 50%, or 100% and drag from one owned base to one destination. The choice
  stays selected until changed; dropping outside a playable base cancels.
- Sending deducts the whole selected amount immediately. Troops depart at
  0.10-second intervals and travel at a constant world speed. Arrivals resolve
  in time and send-sequence order.
- Neutral bases begin with 10 defenders and regenerate one defender per second
  up to that original strength. Owned bases produce continuously without a
  troop cap. Reducing defense to exactly zero does not capture a base; one more
  attacking arrival is required.
- Nepal and Africa use all 7 and 9 regions respectively. India and China use
  18 well-spaced playable bases; USA uses 26. Other regions remain visible as
  map geography. Starting bases are chosen to balance access to nearby neutral
  bases. Nepal remains a compact seven-base exception to the GDD's small-map
  test baseline.
- The bot forecasts neutral regeneration and owned production, reserves troops
  for incoming attacks, reinforces threatened bases when it can arrive in time,
  and repositions troops toward the front. Difficulty changes reaction time
  and safety margins; style changes action weights. Upgrades, special base
  types, and abilities are separate later phases of the GDD.
- The scene-level batch checks in `Assets/Editor/WorldConquerorPhaseOneChecks.cs`
  cover capture thresholds, regeneration, uncapped production, allocation
  rounding, and dispatch spacing.

Use a real map source such as US state GeoJSON, simplify it outside Unity, then assign the generated JSON to the `WorldConquerorMapBootstrap` component in the scene.

## Recommended source

- Use a GeoJSON or shapefile export where each state is a `Feature`.
- For the USA, start with a states dataset and exclude `AK`, `HI`, and territories if you want a tighter gameplay board.

## Conversion flow

1. Export or download a USA states GeoJSON file.
2. Run:

```powershell
python Tools/convert_geojson_to_worldconqueror_map.py `
  --input path\to\usa_states.geojson `
  --output Assets\games\WorldConqueror\Data\usa_states_simplified.json `
  --name-property NAME `
  --id-property STUSPS `
  --exclude AK HI PR GU VI MP AS `
  --target-width 18 `
  --target-height 12 `
  --tolerance 0.08
```

3. In Unity, select the `WorldConquerorBootstrap` object in `WorldConqueror.unity`.
4. Assign the generated `usa_states_simplified.json` `TextAsset` to `Map Definition Json`.
5. Toggle `Regenerate` or use the component context menu to rebuild the map.

## Notes

- The converter keeps only the largest landmass for multipolygon states by default. That is intentional so the game map stays clean and lightweight.
- Increase `--tolerance` to simplify more aggressively.
- Lower `--tolerance` if states start losing important shape identity.
- If you eventually want Alaska, Hawaii, or islands, we can add inset placement later instead of keeping them in geographic scale.
