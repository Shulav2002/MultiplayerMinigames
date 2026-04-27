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
        [SerializeField] private bool regenerate;

        private const string RootName = "GeneratedMap";

        private readonly Color[] palette =
        {
            new(0.73f, 0.84f, 0.92f, 1f),
            new(0.84f, 0.89f, 0.67f, 1f),
            new(0.95f, 0.80f, 0.62f, 1f),
            new(0.90f, 0.69f, 0.69f, 1f),
            new(0.70f, 0.83f, 0.76f, 1f),
            new(0.79f, 0.74f, 0.92f, 1f)
        };

        private void OnEnable()
        {
            if (!HasGeneratedMap())
            {
                EnsureScene();
            }
        }

        private void OnValidate()
        {
            if (regenerate)
            {
                regenerate = false;
                EnsureScene();
            }
        }

        [ContextMenu("Regenerate Map")]
        public void EnsureScene()
        {
            EnsureCamera();
            EnsureBackground();
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
            camera.backgroundColor = new Color(0.92f, 0.95f, 0.98f, 1f);
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
            renderer.color = new Color(0.88f, 0.92f, 0.97f, 1f);
            renderer.sortingOrder = -10;
        }

        private void GenerateStates()
        {
            Transform root = GetOrCreateRoot();

            List<StateDefinition> states = BuildStateDefinitions();
            Dictionary<string, Transform> existingChildren = new();
            for (int i = 0; i < root.childCount; i++)
            {
                Transform child = root.GetChild(i);
                existingChildren[child.name] = child;
            }

            HashSet<string> usedNames = new();
            for (int i = 0; i < states.Count; i++)
            {
                StateDefinition state = states[i];
                string uniqueName = BuildUniqueName(state.Name, usedNames);
                usedNames.Add(uniqueName);

                Transform stateTransform;
                if (existingChildren.TryGetValue(uniqueName, out Transform existingChild))
                {
                    stateTransform = existingChild;
                }
                else
                {
                    GameObject stateObject = new(uniqueName);
                    stateObject.transform.SetParent(root, false);
                    stateTransform = stateObject.transform;
                }

                stateTransform.name = uniqueName;

                if (!stateTransform.TryGetComponent(out WorldConquerorPolygonState polygonState))
                {
                    polygonState = stateTransform.gameObject.AddComponent<WorldConquerorPolygonState>();
                }

                polygonState.Configure(
                    state.Name,
                    palette[i % palette.Length],
                    state.Points,
                    state.HasManualLabelPosition,
                    state.LabelPosition);
            }

            List<Transform> toRemove = new();
            for (int i = 0; i < root.childCount; i++)
            {
                Transform child = root.GetChild(i);
                if (!usedNames.Contains(child.name))
                {
                    toRemove.Add(child);
                }
            }

            foreach (Transform child in toRemove)
            {
                DestroySafely(child.gameObject);
            }
        }

        private List<StateDefinition> BuildStateDefinitions()
        {
            if (TryBuildImportedDefinitions(out List<StateDefinition> importedStates))
            {
                return importedStates;
            }

            return new List<StateDefinition>
            {
                new("Northwatch", new[]
                {
                    new Vector2(-8.6f, 3.6f), new Vector2(-6.9f, 5.7f), new Vector2(-3.9f, 5.2f),
                    new Vector2(-3.0f, 3.4f), new Vector2(-4.7f, 2.1f), new Vector2(-7.3f, 2.2f)
                }),
                new("Ironvale", new[]
                {
                    new Vector2(-3.9f, 5.2f), new Vector2(-0.8f, 5.8f), new Vector2(1.0f, 4.7f),
                    new Vector2(0.3f, 2.9f), new Vector2(-2.1f, 2.5f), new Vector2(-3.0f, 3.4f)
                }),
                new("Sunreach", new[]
                {
                    new Vector2(1.0f, 4.7f), new Vector2(4.3f, 5.4f), new Vector2(6.3f, 3.8f),
                    new Vector2(4.9f, 2.2f), new Vector2(2.0f, 2.4f), new Vector2(0.3f, 2.9f)
                }),
                new("Frostford", new[]
                {
                    new Vector2(-7.3f, 2.2f), new Vector2(-4.7f, 2.1f), new Vector2(-4.0f, 0.1f),
                    new Vector2(-5.9f, -1.3f), new Vector2(-8.2f, -0.6f), new Vector2(-8.9f, 1.0f)
                }),
                new("Cinder Steppe", new[]
                {
                    new Vector2(-4.7f, 2.1f), new Vector2(-2.1f, 2.5f), new Vector2(-0.5f, 1.4f),
                    new Vector2(-1.2f, -0.8f), new Vector2(-4.0f, 0.1f)
                }),
                new("Highgarden", new[]
                {
                    new Vector2(-0.5f, 1.4f), new Vector2(2.0f, 2.4f), new Vector2(4.9f, 2.2f),
                    new Vector2(4.0f, 0.1f), new Vector2(1.4f, -0.9f), new Vector2(-1.2f, -0.8f)
                }),
                new("Storm Coast", new[]
                {
                    new Vector2(4.9f, 2.2f), new Vector2(6.3f, 3.8f), new Vector2(8.8f, 2.1f),
                    new Vector2(8.2f, 0.1f), new Vector2(6.1f, -0.6f), new Vector2(4.0f, 0.1f)
                }),
                new("Ashen Bay", new[]
                {
                    new Vector2(-8.2f, -0.6f), new Vector2(-5.9f, -1.3f), new Vector2(-5.2f, -3.8f),
                    new Vector2(-7.0f, -5.1f), new Vector2(-8.9f, -3.6f)
                }),
                new("Goldcrest", new[]
                {
                    new Vector2(-5.9f, -1.3f), new Vector2(-4.0f, 0.1f), new Vector2(-1.2f, -0.8f),
                    new Vector2(-1.4f, -3.0f), new Vector2(-3.6f, -4.4f), new Vector2(-5.2f, -3.8f)
                }),
                new("Heartland", new[]
                {
                    new Vector2(-1.2f, -0.8f), new Vector2(1.4f, -0.9f), new Vector2(3.7f, -1.2f),
                    new Vector2(3.2f, -3.8f), new Vector2(0.0f, -4.5f), new Vector2(-1.4f, -3.0f)
                }),
                new("Red Mesa", new[]
                {
                    new Vector2(3.7f, -1.2f), new Vector2(6.1f, -0.6f), new Vector2(8.2f, 0.1f),
                    new Vector2(8.6f, -2.8f), new Vector2(6.4f, -4.6f), new Vector2(3.2f, -3.8f)
                }),
                new("Southport", new[]
                {
                    new Vector2(-3.6f, -4.4f), new Vector2(0.0f, -4.5f), new Vector2(3.2f, -3.8f),
                    new Vector2(6.4f, -4.6f), new Vector2(4.5f, -6.3f), new Vector2(-1.0f, -6.5f),
                    new Vector2(-4.2f, -5.9f)
                })
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

            states = definition.regions
                .Where(region => region != null && region.points != null && region.points.Length >= 3)
                .Select(region => new StateDefinition(
                    region.NameOrId,
                    region.points.Select(point => point.ToVector2()).ToArray(),
                    region.hasLabelPosition,
                    region.labelPosition.ToVector2()))
                .ToList();

            return states.Count > 0;
        }

        private static Sprite CreateQuadSprite()
        {
            Texture2D texture = Texture2D.whiteTexture;
            return Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f), 1f);
        }

        private bool HasGeneratedMap()
        {
            Transform root = transform.Find(RootName);
            return root != null && root.childCount > 0;
        }

        private Transform GetOrCreateRoot()
        {
            Transform root = transform.Find(RootName);
            if (root != null)
            {
                return root;
            }

            GameObject rootObject = new(RootName);
            rootObject.transform.SetParent(transform, false);
            return rootObject.transform;
        }

        private static string BuildUniqueName(string baseName, ISet<string> usedNames)
        {
            string safeName = string.IsNullOrWhiteSpace(baseName) ? "State" : baseName;
            if (!usedNames.Contains(safeName))
            {
                return safeName;
            }

            int suffix = 2;
            string candidate = $"{safeName} {suffix}";
            while (usedNames.Contains(candidate))
            {
                suffix++;
                candidate = $"{safeName} {suffix}";
            }

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

        private readonly struct StateDefinition
        {
            public StateDefinition(string name, Vector2[] points, bool hasManualLabelPosition = false, Vector2 labelPosition = default)
            {
                Name = name;
                Points = points;
                HasManualLabelPosition = hasManualLabelPosition;
                LabelPosition = labelPosition;
            }

            public string Name { get; }
            public Vector2[] Points { get; }
            public bool HasManualLabelPosition { get; }
            public Vector2 LabelPosition { get; }
        }
    }
}
