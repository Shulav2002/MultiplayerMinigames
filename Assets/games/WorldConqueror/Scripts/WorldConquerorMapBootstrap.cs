using System.Collections.Generic;
using System.Linq;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace WorldConqueror
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public class WorldConquerorMapBootstrap : MonoBehaviour
    {
        [SerializeField] private TextAsset mapDefinitionJson;
        [SerializeField] private TextAsset[] lobbyMaps = System.Array.Empty<TextAsset>();
        [SerializeField] private float mapScale = 1.18f;
        [SerializeField] [Range(0.035f, 0.25f)] private float borderThickness = 0.08f;
        [SerializeField] private Color territoryColor = new(0.86f, 0.86f, 0.86f, 1f);
        [SerializeField] private Color hoverColor = new(0.74f, 0.74f, 0.74f, 1f);
        [SerializeField] private Color pressedColor = new(0.62f, 0.62f, 0.62f, 1f);
        [SerializeField] private Color borderColor = new(0.28f, 0.28f, 0.28f, 1f);
        [SerializeField] private WorldConquerorMergeProfile mergeProfile;
        [SerializeField] private bool regenerate;

        private const string GeneratedRootPrefix = "GeneratedMap_";

#if UNITY_EDITOR
        private bool editorRebuildQueued;
#endif

        private void OnEnable()
        {
            EnsureGameManager();

            if (Application.isPlaying && GetComponent<WorldConquerorLobby>() == null)
            {
                gameObject.AddComponent<WorldConquerorLobby>().Configure(this, lobbyMaps, mapDefinitionJson);
            }

            if (!HasGeneratedMap())
            {
                EnsureScene();
            }
        }

        public void LoadMap(TextAsset map)
        {
            mapDefinitionJson = map;
            EnsureScene();
        }

        private void OnValidate()
        {
            if (regenerate)
            {
                regenerate = false;
#if UNITY_EDITOR
                QueueEditorRebuild();
#endif
            }
        }

        [ContextMenu("Regenerate Map")]
        public void EnsureScene()
        {
            EnsureCamera();
            EnsureBackground();
            EnsureGameManager();
            GenerateStates();
        }

        private void EnsureCamera()
        {
            Camera camera = Camera.main;
            if (camera == null)
            {
                GameObject cameraObject = new("Main Camera");
                cameraObject.tag = "MainCamera";
                camera = cameraObject.AddComponent<Camera>();
                cameraObject.AddComponent<AudioListener>();
            }

            camera.orthographic = true;
            camera.orthographicSize = 7.6f;
            camera.transform.position = new Vector3(0f, 0f, -10f);
            camera.backgroundColor = new Color(0.93f, 0.93f, 0.93f, 1f);
            camera.clearFlags = CameraClearFlags.SolidColor;
        }

        private void EnsureBackground()
        {
            Transform background = transform.Find("Backdrop");
            if (background != null)
            {
                return;
            }

            GameObject backdrop = new("Backdrop");
            backdrop.transform.SetParent(transform, false);

            SpriteRenderer renderer = backdrop.AddComponent<SpriteRenderer>();
            renderer.sprite = CreateQuadSprite();
            renderer.drawMode = SpriteDrawMode.Sliced;
            renderer.size = new Vector2(24f, 16f);
            renderer.color = new Color(0.90f, 0.90f, 0.90f, 1f);
            renderer.sortingOrder = -10;
        }

        private void EnsureGameManager()
        {
            WorldConquerorGameManager manager = FindFirstObjectByType<WorldConquerorGameManager>(FindObjectsInactive.Include);
            if (manager != null)
            {
                return;
            }

            GameObject managerObject = new("WorldConquerorGameManager");
            managerObject.transform.SetParent(transform, false);
            managerObject.AddComponent<WorldConquerorGameManager>();
        }

        private void GenerateStates()
        {
            ClearGeneratedRoots();
            Transform root = CreateGeneratedRoot();

            List<StateDefinition> states = BuildStateDefinitions();
            Vector2 mapCenter = ComputeMapCenter(states);
            HashSet<string> usedNames = new();
            for (int i = 0; i < states.Count; i++)
            {
                StateDefinition state = states[i];
                GameObject stateObject = new(BuildUniqueName(state.Name, usedNames));
                stateObject.transform.SetParent(root, false);
                Transform stateTransform = stateObject.transform;

                ConfigureStateFragments(stateTransform, state);
            }

            root.localPosition = new Vector3(
                -mapCenter.x * root.localScale.x,
                -mapCenter.y * root.localScale.y,
                0f);
        }

        private List<StateDefinition> BuildStateDefinitions()
        {
            if (TryBuildImportedDefinitions(out List<StateDefinition> importedStates))
            {
                return importedStates;
            }

            return new List<StateDefinition>
            {
                new("Northwatch", SinglePolygon(
                    new Vector2(-8.6f, 3.6f), new Vector2(-6.9f, 5.7f), new Vector2(-3.9f, 5.2f),
                    new Vector2(-3.0f, 3.4f), new Vector2(-4.7f, 2.1f), new Vector2(-7.3f, 2.2f))),
                new("Ironvale", SinglePolygon(
                    new Vector2(-3.9f, 5.2f), new Vector2(-0.8f, 5.8f), new Vector2(1.0f, 4.7f),
                    new Vector2(0.3f, 2.9f), new Vector2(-2.1f, 2.5f), new Vector2(-3.0f, 3.4f))),
                new("Sunreach", SinglePolygon(
                    new Vector2(1.0f, 4.7f), new Vector2(4.3f, 5.4f), new Vector2(6.3f, 3.8f),
                    new Vector2(4.9f, 2.2f), new Vector2(2.0f, 2.4f), new Vector2(0.3f, 2.9f))),
                new("Frostford", SinglePolygon(
                    new Vector2(-7.3f, 2.2f), new Vector2(-4.7f, 2.1f), new Vector2(-4.0f, 0.1f),
                    new Vector2(-5.9f, -1.3f), new Vector2(-8.2f, -0.6f), new Vector2(-8.9f, 1.0f))),
                new("Cinder Steppe", SinglePolygon(
                    new Vector2(-4.7f, 2.1f), new Vector2(-2.1f, 2.5f), new Vector2(-0.5f, 1.4f),
                    new Vector2(-1.2f, -0.8f), new Vector2(-4.0f, 0.1f))),
                new("Highgarden", SinglePolygon(
                    new Vector2(-0.5f, 1.4f), new Vector2(2.0f, 2.4f), new Vector2(4.9f, 2.2f),
                    new Vector2(4.0f, 0.1f), new Vector2(1.4f, -0.9f), new Vector2(-1.2f, -0.8f))),
                new("Storm Coast", SinglePolygon(
                    new Vector2(4.9f, 2.2f), new Vector2(6.3f, 3.8f), new Vector2(8.8f, 2.1f),
                    new Vector2(8.2f, 0.1f), new Vector2(6.1f, -0.6f), new Vector2(4.0f, 0.1f))),
                new("Ashen Bay", SinglePolygon(
                    new Vector2(-8.2f, -0.6f), new Vector2(-5.9f, -1.3f), new Vector2(-5.2f, -3.8f),
                    new Vector2(-7.0f, -5.1f), new Vector2(-8.9f, -3.6f))),
                new("Goldcrest", SinglePolygon(
                    new Vector2(-5.9f, -1.3f), new Vector2(-4.0f, 0.1f), new Vector2(-1.2f, -0.8f),
                    new Vector2(-1.4f, -3.0f), new Vector2(-3.6f, -4.4f), new Vector2(-5.2f, -3.8f))),
                new("Heartland", SinglePolygon(
                    new Vector2(-1.2f, -0.8f), new Vector2(1.4f, -0.9f), new Vector2(3.7f, -1.2f),
                    new Vector2(3.2f, -3.8f), new Vector2(0.0f, -4.5f), new Vector2(-1.4f, -3.0f))),
                new("Red Mesa", SinglePolygon(
                    new Vector2(3.7f, -1.2f), new Vector2(6.1f, -0.6f), new Vector2(8.2f, 0.1f),
                    new Vector2(8.6f, -2.8f), new Vector2(6.4f, -4.6f), new Vector2(3.2f, -3.8f))),
                new("Southport", SinglePolygon(
                    new Vector2(-3.6f, -4.4f), new Vector2(0.0f, -4.5f), new Vector2(3.2f, -3.8f),
                    new Vector2(6.4f, -4.6f), new Vector2(4.5f, -6.3f), new Vector2(-1.0f, -6.5f),
                    new Vector2(-4.2f, -5.9f)))
            };
        }

        private bool TryBuildImportedDefinitions(out List<StateDefinition> states)
        {
            states = null;
            if (mapDefinitionJson == null || string.IsNullOrWhiteSpace(mapDefinitionJson.text))
            {
                return false;
            }

            WorldConquerorMapDefinition definition = JsonUtility.FromJson<WorldConquerorMapDefinition>(mapDefinitionJson.text);
            if (definition?.regions == null || definition.regions.Length == 0)
            {
                return false;
            }

            states = ApplyMergeRules(definition.regions)
                .Where(region => region != null)
                .Select(BuildImportedStateDefinition)
                .Where(state => state.Polygons.Length > 0)
                .ToList();

            return states.Count > 0;
        }

        private StateDefinition BuildImportedStateDefinition(ImportedRegion region)
        {
            return new StateDefinition(
                region.NameOrId,
                region.Polygons.ToArray(),
                region.hasLabelPosition,
                region.labelPosition);
        }

        private static Sprite CreateQuadSprite()
        {
            Texture2D texture = Texture2D.whiteTexture;
            return Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f), 1f);
        }

        private bool HasGeneratedMap()
        {
            for (int i = 0; i < transform.childCount; i++)
            {
                Transform child = transform.GetChild(i);
                if (IsGeneratedRoot(child) && child.childCount > 0)
                {
                    return true;
                }
            }

            return false;
        }

        private Transform CreateGeneratedRoot()
        {
            // Destroy is deferred in Play Mode. A same-name old root may still exist
            // this frame, so rebuilding must always use a fresh object.
            GameObject rootObject = new(GetGeneratedRootName());
            rootObject.transform.SetParent(transform, false);
            rootObject.transform.localScale = Vector3.one * Mathf.Max(0.1f, mapScale);
            return rootObject.transform;
        }

        private static string BuildUniqueName(string baseName, ISet<string> usedNames)
        {
            string safeName = string.IsNullOrWhiteSpace(baseName) ? "State" : baseName;
            if (!usedNames.Contains(safeName))
            {
                usedNames.Add(safeName);
                return safeName;
            }

            int suffix = 2;
            string candidate = $"{safeName} {suffix}";
            while (usedNames.Contains(candidate))
            {
                suffix++;
                candidate = $"{safeName} {suffix}";
            }

            usedNames.Add(candidate);
            return candidate;
        }

        private static void DestroySafely(GameObject target)
        {
            if (target == null)
            {
                return;
            }

#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                if (Selection.activeGameObject == target)
                {
                    Selection.activeGameObject = null;
                }

                Selection.objects = Selection.objects.Where(selected => selected != target).ToArray();
                DestroyImmediate(target);
                return;
            }
#endif

            Destroy(target);
        }

        private void ClearGeneratedRoots()
        {
            List<GameObject> rootsToRemove = new();
            for (int i = 0; i < transform.childCount; i++)
            {
                Transform child = transform.GetChild(i);
                if (IsGeneratedRoot(child))
                {
                    rootsToRemove.Add(child.gameObject);
                }
            }

            foreach (GameObject root in rootsToRemove)
            {
                root.SetActive(false);
                DestroySafely(root);
            }
        }

        private string GetGeneratedRootName()
        {
            return $"{GeneratedRootPrefix}{GetCountryRootLabel()}";
        }

        private string GetCountryRootLabel()
        {
            if (mapDefinitionJson == null || string.IsNullOrWhiteSpace(mapDefinitionJson.name))
            {
                return "Default";
            }

            string label = mapDefinitionJson.name
                .Replace("_simplified", string.Empty)
                .Replace('-', ' ')
                .Replace('_', ' ')
                .Trim();

            return string.IsNullOrWhiteSpace(label) ? "Default" : label;
        }

        private static bool IsGeneratedRoot(Transform candidate)
        {
            return candidate != null && candidate.name.StartsWith(GeneratedRootPrefix);
        }

#if UNITY_EDITOR
        private void QueueEditorRebuild()
        {
            if (Application.isPlaying || editorRebuildQueued)
            {
                return;
            }

            editorRebuildQueued = true;
            EditorApplication.delayCall += RunQueuedEditorRebuild;
        }

        private void RunQueuedEditorRebuild()
        {
            editorRebuildQueued = false;
            if (this == null)
            {
                return;
            }

            EnsureScene();
        }
#endif

        private void ConfigureStateFragments(Transform stateTransform, StateDefinition state)
        {
            Dictionary<string, Transform> existingFragments = new();
            for (int i = 0; i < stateTransform.childCount; i++)
            {
                Transform child = stateTransform.GetChild(i);
                existingFragments[child.name] = child;
            }

            HashSet<string> usedFragments = new();
            int labelFragmentIndex = FindLargestPolygonIndex(state.Polygons);

            for (int i = 0; i < state.Polygons.Length; i++)
            {
                string fragmentName = state.Polygons.Length == 1 ? "Shape" : $"Shape_{i + 1}";
                usedFragments.Add(fragmentName);

                Transform fragmentTransform;
                if (existingFragments.TryGetValue(fragmentName, out Transform existingFragment))
                {
                    fragmentTransform = existingFragment;
                }
                else
                {
                    GameObject fragmentObject = new(fragmentName);
                    fragmentObject.transform.SetParent(stateTransform, false);
                    fragmentTransform = fragmentObject.transform;
                }

                if (!fragmentTransform.TryGetComponent(out WorldConquerorPolygonState polygonState))
                {
                    polygonState = fragmentTransform.gameObject.AddComponent<WorldConquerorPolygonState>();
                }

                polygonState.Configure(
                    state.Name,
                    territoryColor,
                    hoverColor,
                    pressedColor,
                    borderColor,
                    state.Polygons[i],
                    borderThickness,
                    state.HasManualLabelPosition,
                    state.LabelPosition,
                    i == labelFragmentIndex);
            }

            List<Transform> fragmentsToRemove = new();
            for (int i = 0; i < stateTransform.childCount; i++)
            {
                Transform child = stateTransform.GetChild(i);
                if (!usedFragments.Contains(child.name))
                {
                    fragmentsToRemove.Add(child);
                }
            }

            foreach (Transform fragment in fragmentsToRemove)
            {
                DestroySafely(fragment.gameObject);
            }
        }

        private static int FindLargestPolygonIndex(IReadOnlyList<Vector2[]> polygons)
        {
            int largestIndex = 0;
            float largestArea = float.MinValue;

            for (int i = 0; i < polygons.Count; i++)
            {
                float area = Mathf.Abs(ComputePolygonArea(polygons[i]));
                if (area > largestArea)
                {
                    largestArea = area;
                    largestIndex = i;
                }
            }

            return largestIndex;
        }

        private static float ComputePolygonArea(IReadOnlyList<Vector2> points)
        {
            float area = 0f;
            for (int i = 0; i < points.Count; i++)
            {
                Vector2 current = points[i];
                Vector2 next = points[(i + 1) % points.Count];
                area += current.x * next.y - next.x * current.y;
            }

            return area * 0.5f;
        }

        private static Vector2 ComputeMapCenter(IReadOnlyList<StateDefinition> states)
        {
            if (states == null || states.Count == 0)
            {
                return Vector2.zero;
            }

            float minX = float.PositiveInfinity;
            float minY = float.PositiveInfinity;
            float maxX = float.NegativeInfinity;
            float maxY = float.NegativeInfinity;

            for (int stateIndex = 0; stateIndex < states.Count; stateIndex++)
            {
                Vector2[][] polygons = states[stateIndex].Polygons;
                for (int polygonIndex = 0; polygonIndex < polygons.Length; polygonIndex++)
                {
                    IReadOnlyList<Vector2> polygon = polygons[polygonIndex];
                    for (int pointIndex = 0; pointIndex < polygon.Count; pointIndex++)
                    {
                        Vector2 point = polygon[pointIndex];
                        minX = Mathf.Min(minX, point.x);
                        minY = Mathf.Min(minY, point.y);
                        maxX = Mathf.Max(maxX, point.x);
                        maxY = Mathf.Max(maxY, point.y);
                    }
                }
            }

            if (float.IsInfinity(minX) || float.IsInfinity(minY))
            {
                return Vector2.zero;
            }

            return new Vector2((minX + maxX) * 0.5f, (minY + maxY) * 0.5f);
        }

        private static Vector2[][] SinglePolygon(params Vector2[] points)
        {
            return new[] { points };
        }

        private IEnumerable<ImportedRegion> ApplyMergeRules(WorldConquerorStateDefinition[] sourceRegions)
        {
            List<ImportedRegion> importedRegions = sourceRegions
                .Where(region => region != null)
                .Select(CreateImportedRegion)
                .Where(region => region != null && region.Polygons.Count > 0)
                .ToList();

            MergeRule[] mergeRules = mergeProfile != null ? mergeProfile.MergeRules : System.Array.Empty<MergeRule>();
            if (mergeRules == null || mergeRules.Length == 0)
            {
                return importedRegions;
            }

            Dictionary<string, ImportedRegion> regionById = importedRegions
                .Where(region => !string.IsNullOrWhiteSpace(region.id))
                .GroupBy(region => region.id)
                .ToDictionary(group => group.Key, group => group.First());

            HashSet<string> mergedIds = new();
            List<ImportedRegion> mergedRegions = new();

            for (int i = 0; i < mergeRules.Length; i++)
            {
                MergeRule rule = mergeRules[i];
                if (rule == null || rule.sourceRegionIds == null || rule.sourceRegionIds.Length == 0)
                {
                    continue;
                }

                List<ImportedRegion> matchedRegions = new();
                for (int idIndex = 0; idIndex < rule.sourceRegionIds.Length; idIndex++)
                {
                    string sourceId = rule.sourceRegionIds[idIndex];
                    if (string.IsNullOrWhiteSpace(sourceId) || mergedIds.Contains(sourceId))
                    {
                        continue;
                    }

                    if (regionById.TryGetValue(sourceId, out ImportedRegion matchedRegion))
                    {
                        matchedRegions.Add(matchedRegion);
                    }
                }

                if (matchedRegions.Count < 2)
                {
                    continue;
                }

                ImportedRegion mergedRegion = BuildMergedRegion(rule, matchedRegions);
                mergedRegions.Add(mergedRegion);

                for (int matchedIndex = 0; matchedIndex < matchedRegions.Count; matchedIndex++)
                {
                    mergedIds.Add(matchedRegions[matchedIndex].id);
                }
            }

            List<ImportedRegion> finalRegions = importedRegions
                .Where(region => !mergedIds.Contains(region.id))
                .ToList();

            finalRegions.AddRange(mergedRegions);
            return finalRegions;
        }

        private static ImportedRegion CreateImportedRegion(WorldConquerorStateDefinition region)
        {
            List<Vector2[]> polygons = new();

            if (region.polygons != null && region.polygons.Length > 0)
            {
                foreach (SerializablePolygon polygon in region.polygons)
                {
                    if (polygon?.points == null || polygon.points.Length < 3)
                    {
                        continue;
                    }

                    polygons.Add(polygon.points.Select(point => point.ToVector2()).ToArray());
                }
            }
            else if (region.points != null && region.points.Length >= 3)
            {
                polygons.Add(region.points.Select(point => point.ToVector2()).ToArray());
            }

            return new ImportedRegion
            {
                id = region.id,
                displayName = region.displayName,
                Polygons = polygons,
                hasLabelPosition = region.hasLabelPosition,
                labelPosition = region.labelPosition.ToVector2(),
            };
        }

        private static ImportedRegion BuildMergedRegion(MergeRule rule, List<ImportedRegion> matchedRegions)
        {
            List<Vector2[]> mergedPolygons = new();
            ImportedRegion anchorRegion = matchedRegions[0];
            Vector2 anchorLabelPosition = anchorRegion.labelPosition;
            bool hasAnchorLabel = anchorRegion.hasLabelPosition;
            float largestPolygonArea = -1f;

            for (int i = 0; i < matchedRegions.Count; i++)
            {
                ImportedRegion region = matchedRegions[i];
                mergedPolygons.AddRange(region.Polygons);

                for (int polygonIndex = 0; polygonIndex < region.Polygons.Count; polygonIndex++)
                {
                    float area = Mathf.Abs(ComputePolygonArea(region.Polygons[polygonIndex]));
                    if (area > largestPolygonArea)
                    {
                        largestPolygonArea = area;
                        anchorRegion = region;
                        anchorLabelPosition = region.labelPosition;
                        hasAnchorLabel = region.hasLabelPosition;
                    }
                }
            }

            return new ImportedRegion
            {
                id = string.IsNullOrWhiteSpace(rule.mergedRegionId) ? anchorRegion.id : rule.mergedRegionId,
                displayName = string.IsNullOrWhiteSpace(rule.mergedRegionName) ? anchorRegion.NameOrId : rule.mergedRegionName,
                Polygons = mergedPolygons,
                hasLabelPosition = hasAnchorLabel,
                labelPosition = anchorLabelPosition,
            };
        }

        private readonly struct StateDefinition
        {
            public StateDefinition(string name, Vector2[][] polygons, bool hasManualLabelPosition = false, Vector2 labelPosition = default)
            {
                Name = name;
                Polygons = polygons;
                HasManualLabelPosition = hasManualLabelPosition;
                LabelPosition = labelPosition;
            }

            public string Name { get; }
            public Vector2[][] Polygons { get; }
            public bool HasManualLabelPosition { get; }
            public Vector2 LabelPosition { get; }
        }

        private class ImportedRegion
        {
            public string id;
            public string displayName;
            public List<Vector2[]> Polygons = new();
            public bool hasLabelPosition;
            public Vector2 labelPosition;

            public string NameOrId => string.IsNullOrWhiteSpace(displayName) ? id : displayName;
        }
    }
}
