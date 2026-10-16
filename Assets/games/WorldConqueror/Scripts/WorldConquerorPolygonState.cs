using System.Collections.Generic;
using System;
using UnityEngine;

namespace WorldConqueror
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public class WorldConquerorPolygonState : MonoBehaviour
    {
        [SerializeField] private string stateName = "State";
        [SerializeField] private Color fillColor = new(0.86f, 0.86f, 0.86f, 1f);
        [SerializeField] private Color hoverColor = new(0.74f, 0.74f, 0.74f, 1f);
        [SerializeField] private Color pressedColor = new(0.62f, 0.62f, 0.62f, 1f);
        [SerializeField] private Color borderColor = new(0.28f, 0.28f, 0.28f, 1f);
        [SerializeField] private Color targetPointColor = new(0.18f, 0.18f, 0.18f, 1f);
        [SerializeField] private Vector2[] points = new Vector2[0];
        [SerializeField] private float borderWidth = 0.08f;
        private const int SortingOrderFill = 0;
        private const int SortingOrderBorder = 1;
        private const int SortingOrderTargetPoint = 2;
        private const int SortingOrderLabel = 3;
        private const float TargetPointRadius = 0.18f;
        private const float TroopLabelCharacterSize = 0.06f;
        private const int TargetPointSegments = 18;
        private MeshFilter meshFilter;
        private MeshRenderer meshRenderer;
        private PolygonCollider2D polygonCollider;
        private LineRenderer lineRenderer;
        private MeshFilter targetPointMeshFilter;
        private MeshRenderer targetPointMeshRenderer;
        private bool hasManualLabelPosition;
        private Vector2 manualLabelPosition;
        private bool showTargetPoint = true;
        private TextMesh troopLabel;
        private bool isHovered;
        private bool isPressed;

        public static event Action<WorldConquerorPolygonState> Pressed;

        public string StateName => stateName;
        public bool IsGameplayAnchor => showTargetPoint;
        public Vector3 TargetWorldPosition => transform.TransformPoint(GetTargetLocalPosition());

        public void Configure(
            string newStateName,
            Color newFillColor,
            Color newHoverColor,
            Color newPressedColor,
            Color newBorderColor,
            IReadOnlyList<Vector2> newPoints,
            float newBorderWidth,
            bool useLabelOverride = false,
            Vector2 labelOverride = default,
            bool shouldShowLabel = false)
        {
            stateName = newStateName;
            fillColor = newFillColor;
            hoverColor = newHoverColor;
            pressedColor = newPressedColor;
            borderColor = newBorderColor;
            borderWidth = Mathf.Max(0.035f, newBorderWidth);
            hasManualLabelPosition = useLabelOverride;
            manualLabelPosition = labelOverride;
            showTargetPoint = shouldShowLabel;
            points = new Vector2[newPoints.Count];

            for (int i = 0; i < newPoints.Count; i++)
            {
                points[i] = newPoints[i];
            }

            Rebuild();
        }

        private void OnEnable()
        {
            Rebuild();
        }

        private void OnValidate()
        {
            Rebuild();
        }

        public void Rebuild()
        {
            if (points == null || points.Length < 3)
            {
                return;
            }

            EnsureComponents();
            ApplyMesh();
            ApplyCollider();
            ApplyBorder();
            ApplyTargetPoint();
            ApplyTroopLabel();
            RefreshVisualState();
        }

        private void EnsureComponents()
        {
            if (!TryGetComponent(out meshFilter))
            {
                meshFilter = gameObject.AddComponent<MeshFilter>();
            }

            if (!TryGetComponent(out meshRenderer))
            {
                meshRenderer = gameObject.AddComponent<MeshRenderer>();
            }

            if (!TryGetComponent(out polygonCollider))
            {
                polygonCollider = gameObject.AddComponent<PolygonCollider2D>();
            }

            if (!TryGetComponent(out lineRenderer))
            {
                lineRenderer = gameObject.AddComponent<LineRenderer>();
            }

            Transform targetPointTransform = transform.Find("TargetPoint");
            if (targetPointTransform == null)
            {
                GameObject targetPointObject = new("TargetPoint");
                targetPointObject.transform.SetParent(transform, false);
                targetPointTransform = targetPointObject.transform;
            }

            Transform labelTransform = transform.Find("TroopLabel");
            if (labelTransform == null)
            {
                GameObject labelObject = new("TroopLabel");
                labelObject.transform.SetParent(transform, false);
                labelTransform = labelObject.transform;
            }

            if (!labelTransform.TryGetComponent(out troopLabel))
            {
                troopLabel = labelTransform.gameObject.AddComponent<TextMesh>();
            }

            if (!targetPointTransform.TryGetComponent(out targetPointMeshFilter))
            {
                targetPointMeshFilter = targetPointTransform.gameObject.AddComponent<MeshFilter>();
            }

            if (!targetPointTransform.TryGetComponent(out targetPointMeshRenderer))
            {
                targetPointMeshRenderer = targetPointTransform.gameObject.AddComponent<MeshRenderer>();
            }
        }

        private void ApplyMesh()
        {
            Mesh mesh = new()
            {
                name = $"{stateName}_Mesh"
            };

            Vector3[] vertices = new Vector3[points.Length];
            for (int i = 0; i < points.Length; i++)
            {
                vertices[i] = points[i];
            }

            mesh.vertices = vertices;
            mesh.triangles = Triangulate(points);
            mesh.RecalculateBounds();
            mesh.RecalculateNormals();
            meshFilter.sharedMesh = mesh;

            Material material = new(ResolveShader())
            {
                color = fillColor
            };

            meshRenderer.sharedMaterial = material;
            meshRenderer.sortingOrder = SortingOrderFill;
        }

        private void ApplyCollider()
        {
            polygonCollider.pathCount = 1;
            polygonCollider.SetPath(0, points);
        }

        private void ApplyBorder()
        {
            lineRenderer.loop = true;
            lineRenderer.useWorldSpace = false;
            lineRenderer.widthMultiplier = borderWidth;
            lineRenderer.positionCount = points.Length;
            lineRenderer.numCornerVertices = 2;
            lineRenderer.numCapVertices = 2;
            lineRenderer.sortingOrder = SortingOrderBorder;
            lineRenderer.material = new Material(ResolveShader())
            {
                color = borderColor
            };

            for (int i = 0; i < points.Length; i++)
            {
                lineRenderer.SetPosition(i, points[i]);
            }
        }

        private void ApplyTargetPoint()
        {
            Transform targetPointTransform = targetPointMeshFilter.transform;
            targetPointTransform.localPosition = GetTargetLocalPosition();
            targetPointTransform.localRotation = Quaternion.identity;
            targetPointTransform.localScale = Vector3.one;

            Mesh targetMesh = BuildTargetPointMesh();
            targetPointMeshFilter.sharedMesh = targetMesh;

            Material material = new(ResolveShader())
            {
                color = targetPointColor
            };

            targetPointMeshRenderer.sharedMaterial = material;
            targetPointMeshRenderer.sortingOrder = SortingOrderTargetPoint;
            targetPointTransform.gameObject.SetActive(showTargetPoint);
        }

        private void ApplyTroopLabel()
        {
            if (troopLabel == null)
            {
                return;
            }

            Transform labelTransform = troopLabel.transform;
            labelTransform.localPosition = GetTargetLocalPosition() + new Vector3(0f, 0f, -0.03f);
            labelTransform.localRotation = Quaternion.identity;
            labelTransform.localScale = Vector3.one;

            troopLabel.anchor = TextAnchor.MiddleCenter;
            troopLabel.alignment = TextAlignment.Center;
            // TextMesh fontSize controls glyph resolution; characterSize controls world size.
            // Keep two-digit troop counts compact enough to sit on the territory marker.
            troopLabel.characterSize = TroopLabelCharacterSize;
            troopLabel.fontSize = 42;
            troopLabel.color = Color.white;
            troopLabel.text = string.Empty;

            MeshRenderer labelRenderer = troopLabel.GetComponent<MeshRenderer>();
            if (labelRenderer != null)
            {
                labelRenderer.sortingOrder = SortingOrderLabel;
            }

            labelTransform.gameObject.SetActive(showTargetPoint);
        }

        private void OnMouseEnter()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            isHovered = true;
            RefreshVisualState();
        }

        private void OnMouseExit()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            isHovered = false;
            isPressed = false;
            RefreshVisualState();
        }

        private void OnMouseDown()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            isPressed = true;
            RefreshVisualState();
            Pressed?.Invoke(this);
        }

        private void OnMouseUpAsButton()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            isPressed = false;
            RefreshVisualState();
        }

        private void OnMouseUp()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            isPressed = false;
            RefreshVisualState();
        }

        private void RefreshVisualState()
        {
            if (meshRenderer == null || meshRenderer.sharedMaterial == null)
            {
                return;
            }

            Color targetColor = fillColor;
            if (isPressed)
            {
                targetColor = pressedColor;
            }
            else if (isHovered)
            {
                targetColor = hoverColor;
            }

            meshRenderer.sharedMaterial.color = targetColor;
        }

        public void ApplyRuntimeColors(Color normalColor, Color newHoverColor, Color newPressedColor, Color newTargetPointColor)
        {
            fillColor = normalColor;
            hoverColor = newHoverColor;
            pressedColor = newPressedColor;
            targetPointColor = newTargetPointColor;

            if (targetPointMeshRenderer != null && targetPointMeshRenderer.sharedMaterial != null)
            {
                targetPointMeshRenderer.sharedMaterial.color = targetPointColor;
            }

            RefreshVisualState();
        }

        public void SetTroopLabel(string value, Color color)
        {
            if (troopLabel == null)
            {
                return;
            }

            troopLabel.text = showTargetPoint ? value : string.Empty;
            troopLabel.color = color;
            // Also update existing scene labels when refreshing their runtime values.
            if (troopLabel.characterSize != TroopLabelCharacterSize)
            {
                troopLabel.characterSize = TroopLabelCharacterSize;
            }
        }

        public void SetRuntimeAnchorVisible(bool isVisible)
        {
            showTargetPoint = isVisible;

            if (targetPointMeshFilter != null)
            {
                targetPointMeshFilter.gameObject.SetActive(showTargetPoint);
            }

            if (troopLabel != null)
            {
                troopLabel.gameObject.SetActive(showTargetPoint);
                if (!showTargetPoint)
                {
                    troopLabel.text = string.Empty;
                }
            }
        }

        private Vector3 GetTargetLocalPosition()
        {
            Vector2 center = hasManualLabelPosition ? manualLabelPosition : ComputeCentroid(points);
            return new Vector3(center.x, center.y, -0.05f);
        }

        private static Mesh BuildTargetPointMesh()
        {
            Mesh mesh = new()
            {
                name = "TargetPointMesh"
            };

            Vector3[] vertices = new Vector3[TargetPointSegments + 1];
            int[] triangles = new int[TargetPointSegments * 3];
            vertices[0] = Vector3.zero;

            for (int i = 0; i < TargetPointSegments; i++)
            {
                float angle = (Mathf.PI * 2f * i) / TargetPointSegments;
                vertices[i + 1] = new Vector3(
                    Mathf.Cos(angle) * TargetPointRadius,
                    Mathf.Sin(angle) * TargetPointRadius,
                    0f);

                int triangleIndex = i * 3;
                triangles[triangleIndex] = 0;
                triangles[triangleIndex + 1] = i + 1;
                triangles[triangleIndex + 2] = i == TargetPointSegments - 1 ? 1 : i + 2;
            }

            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
            mesh.RecalculateNormals();
            return mesh;
        }

        private static Shader ResolveShader()
        {
            return Shader.Find("Sprites/Default")
                   ?? Shader.Find("Universal Render Pipeline/Unlit")
                   ?? Shader.Find("Unlit/Color");
        }

        private static int[] Triangulate(IReadOnlyList<Vector2> polygon)
        {
            List<int> indices = new();
            List<int> vertexIndices = new();

            for (int i = 0; i < polygon.Count; i++)
            {
                vertexIndices.Add(i);
            }

            if (SignedArea(polygon) < 0f)
            {
                vertexIndices.Reverse();
            }

            int guard = 0;
            while (vertexIndices.Count > 3 && guard < 512)
            {
                bool earFound = false;
                for (int i = 0; i < vertexIndices.Count; i++)
                {
                    int previous = vertexIndices[(i - 1 + vertexIndices.Count) % vertexIndices.Count];
                    int current = vertexIndices[i];
                    int next = vertexIndices[(i + 1) % vertexIndices.Count];

                    if (!IsEar(previous, current, next, vertexIndices, polygon))
                    {
                        continue;
                    }

                    indices.Add(previous);
                    indices.Add(current);
                    indices.Add(next);
                    vertexIndices.RemoveAt(i);
                    earFound = true;
                    break;
                }

                if (!earFound)
                {
                    break;
                }

                guard++;
            }

            if (vertexIndices.Count == 3)
            {
                indices.Add(vertexIndices[0]);
                indices.Add(vertexIndices[1]);
                indices.Add(vertexIndices[2]);
            }

            return indices.ToArray();
        }

        private static bool IsEar(int previous, int current, int next, IReadOnlyList<int> polygonIndices, IReadOnlyList<Vector2> polygon)
        {
            Vector2 a = polygon[previous];
            Vector2 b = polygon[current];
            Vector2 c = polygon[next];

            if (Cross(a, b, c) <= 0f)
            {
                return false;
            }

            for (int i = 0; i < polygonIndices.Count; i++)
            {
                int pointIndex = polygonIndices[i];
                if (pointIndex == previous || pointIndex == current || pointIndex == next)
                {
                    continue;
                }

                if (PointInTriangle(polygon[pointIndex], a, b, c))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool PointInTriangle(Vector2 point, Vector2 a, Vector2 b, Vector2 c)
        {
            float cross1 = Cross(point, a, b);
            float cross2 = Cross(point, b, c);
            float cross3 = Cross(point, c, a);

            bool hasNegative = cross1 < 0f || cross2 < 0f || cross3 < 0f;
            bool hasPositive = cross1 > 0f || cross2 > 0f || cross3 > 0f;
            return !(hasNegative && hasPositive);
        }

        private static float Cross(Vector2 a, Vector2 b, Vector2 c)
        {
            return (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
        }

        private static float SignedArea(IReadOnlyList<Vector2> polygon)
        {
            float area = 0f;

            for (int i = 0; i < polygon.Count; i++)
            {
                Vector2 current = polygon[i];
                Vector2 next = polygon[(i + 1) % polygon.Count];
                area += (current.x * next.y) - (next.x * current.y);
            }

            return area * 0.5f;
        }

        private static Vector2 ComputeCentroid(IReadOnlyList<Vector2> polygon)
        {
            float areaFactor = 0f;
            float centroidX = 0f;
            float centroidY = 0f;

            for (int i = 0; i < polygon.Count; i++)
            {
                Vector2 current = polygon[i];
                Vector2 next = polygon[(i + 1) % polygon.Count];
                float cross = (current.x * next.y) - (next.x * current.y);
                areaFactor += cross;
                centroidX += (current.x + next.x) * cross;
                centroidY += (current.y + next.y) * cross;
            }

            if (Mathf.Abs(areaFactor) < 0.0001f)
            {
                Vector2 average = Vector2.zero;
                for (int i = 0; i < polygon.Count; i++)
                {
                    average += polygon[i];
                }

                return average / polygon.Count;
            }

            float safeArea = areaFactor * 0.5f;
            return new Vector2(centroidX / (6f * safeArea), centroidY / (6f * safeArea));
        }
    }
}
