using System.Collections.Generic;
using UnityEngine;

namespace WorldConqueror
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public class WorldConquerorPolygonState : MonoBehaviour
    {
        [SerializeField] private string stateName = "State";
        [SerializeField] private Color fillColor = new(0.58f, 0.68f, 0.75f, 1f);
        [SerializeField] private Color borderColor = new(0.14f, 0.19f, 0.25f, 1f);
        [SerializeField] private Vector2[] points = new Vector2[0];

        private const float BorderWidth = 0.16f;
        private const int SortingOrderFill = 0;
        private const int SortingOrderBorder = 1;
        private const int SortingOrderLabel = 2;

        private MeshFilter meshFilter;
        private MeshRenderer meshRenderer;
        private PolygonCollider2D polygonCollider;
        private LineRenderer lineRenderer;
        private TextMesh labelMesh;
        private bool hasManualLabelPosition;
        private Vector2 manualLabelPosition;

        public void Configure(
            string newStateName,
            Color newFillColor,
            IReadOnlyList<Vector2> newPoints,
            bool useLabelOverride = false,
            Vector2 labelOverride = default)
        {
            stateName = newStateName;
            fillColor = newFillColor;
            hasManualLabelPosition = useLabelOverride;
            manualLabelPosition = labelOverride;
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
            ApplyLabel();
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

            Transform labelTransform = transform.Find("Label");
            if (labelTransform == null)
            {
                GameObject labelObject = new("Label");
                labelObject.transform.SetParent(transform, false);
                labelTransform = labelObject.transform;
            }

            if (!labelTransform.TryGetComponent(out labelMesh))
            {
                labelMesh = labelTransform.gameObject.AddComponent<TextMesh>();
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
            lineRenderer.widthMultiplier = BorderWidth;
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

        private void ApplyLabel()
        {
            labelMesh.text = stateName;
            labelMesh.fontSize = 44;
            labelMesh.characterSize = 0.12f;
            labelMesh.anchor = TextAnchor.MiddleCenter;
            labelMesh.alignment = TextAlignment.Center;
            labelMesh.color = new Color(0.06f, 0.08f, 0.11f, 1f);
            labelMesh.GetComponent<MeshRenderer>().sortingOrder = SortingOrderLabel;

            Vector2 centroid = hasManualLabelPosition ? manualLabelPosition : ComputeCentroid(points);
            labelMesh.transform.localPosition = new Vector3(centroid.x, centroid.y, -0.1f);
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
