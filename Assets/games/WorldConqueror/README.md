# WorldConqueror GIS Map Pipeline

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
