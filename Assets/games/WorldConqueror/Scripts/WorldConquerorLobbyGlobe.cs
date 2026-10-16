using UnityEngine;
using UnityEngine.UI;

namespace WorldConqueror
{
    // Orthographic projection of rotating sphere geometry directly into the UI mesh.
    // This stays sharp at canvas resolution and needs no extra camera or render texture.
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class WorldConquerorLobbyGlobe : MaskableGraphic
    {
        private const int LongitudeSteps = 120;
        private const int LatitudeSteps = 60;
        private readonly bool[,] land = new bool[LongitudeSteps, LatitudeSteps];
        private float clock;
        private Quaternion rotation;
        private float radius;
        private Vector2 center;

        // Simplified continent coastlines, in longitude / latitude degrees.
        private static readonly Vector2[][] Continents =
        {
            Ring(-168,72,-135,70,-125,58,-130,50,-124,40,-117,32,-106,23,-97,16,-87,15,-82,9,-77,8,-86,21,-81,25,-80,32,-66,45,-55,53,-65,60,-85,66,-105,73),
            Ring(-81,12,-70,11,-60,7,-50,0,-35,-6,-40,-22,-50,-30,-57,-40,-68,-55,-74,-47,-75,-30,-81,-8),
            Ring(-17,36,0,37,12,33,25,32,34,30,43,12,51,11,43,-1,40,-15,32,-26,18,-35,12,-20,10,-5,-5,5,-17,15),
            Ring(-10,36,-9,44,0,49,7,55,20,60,27,71,40,68,48,57,65,55,85,73,110,75,140,70,180,66,170,52,145,48,135,35,122,24,110,20,106,8,100,1,96,17,88,22,80,7,72,20,60,25,51,12,43,14,35,30,26,40,18,40,12,45,4,43),
            Ring(113,-22,122,-14,134,-12,142,-10,153,-25,150,-37,135,-35,129,-31,115,-35),
            Ring(-53,60,-43,60,-20,74,-25,82,-50,83,-65,75),
            Ring(47,-13,51,-17,48,-26,44,-23),
            Ring(130,31,142,45,146,44,141,36,134,32),
            Ring(-10,50,-5,50,0,58,-5,59),
            Ring(166,-35,178,-38,173,-46,167,-47),
            Ring(95,5,106,-6,119,-8,130,-4,140,-7,150,-5,139,0,121,2,110,7)
        };

        // Representative locations across countries; the lobby is an ambient simulation.
        private static readonly Vector2[] Locations =
        {
            new(-100,38), new(-52,-14), new(2,47), new(30,27), new(78,23),
            new(105,35), new(135,-25), new(25,-28), new(138,37), new(60,56), new(-102,23), new(44,33)
        };

        protected override void Awake()
        {
            base.Awake();
            raycastTarget = false;
            for (int x = 0; x < LongitudeSteps; x++)
                for (int y = 0; y < LatitudeSteps; y++)
                {
                    Vector2 point = new(-180 + (x + 0.5f) * 360 / LongitudeSteps, -90 + (y + 0.5f) * 180 / LatitudeSteps);
                    foreach (Vector2[] continent in Continents)
                        if (Contains(continent, point)) { land[x, y] = true; break; }
                }
        }

        private void Update()
        {
            clock += Time.unscaledDeltaTime;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            Rect rect = rectTransform.rect;
            center = rect.center;
            radius = Mathf.Min(rect.width, rect.height) * 0.385f;
            rotation = Quaternion.Euler(0, clock * 5f - 35f, -12f);

            // Soft atmospheric halo and instrument rings.
            Disc(mesh, center, radius * 1.10f, Shade(0.65f, 0.025f), 120);
            Disc(mesh, center, radius * 1.055f, Shade(0.75f, 0.04f), 120);
            Circle(mesh, center, radius * 1.17f, 0.7f, Shade(0.55f, 0.28f));
            Circle(mesh, center, radius * 1.025f, 1.2f, Shade(0.8f, 0.35f));

            Vector3 light = new Vector3(-0.6f, 0.7f, -1).normalized;
            for (int x = 0; x < LongitudeSteps; x++)
                for (int y = 0; y < LatitudeSteps; y++)
                {
                    float lon = -180 + x * 360f / LongitudeSteps;
                    float lat = -90 + y * 180f / LatitudeSteps;
                    Vector3 a = rotation * Sphere(lon, lat);
                    Vector3 b = rotation * Sphere(lon + 3, lat);
                    Vector3 c = rotation * Sphere(lon + 3, lat + 3);
                    Vector3 d = rotation * Sphere(lon, lat + 3);
                    Vector3 normal = (a + b + c + d).normalized;
                    if (normal.z > 0) continue;
                    float lighting = 0.32f + 0.68f * Mathf.Max(0, Vector3.Dot(normal, light));
                    float value = (land[x, y] ? 0.58f : 0.16f) * lighting;
                    Quad(mesh, Project(a), Project(b), Project(c), Project(d), Shade(value));
                }

            for (int lat = -60; lat <= 60; lat += 30)
                for (int lon = -180; lon < 180; lon += 4)
                    SurfaceLine(mesh, Sphere(lon, lat) * 1.003f, Sphere(lon + 4, lat) * 1.003f, 0.6f, Shade(0.8f, 0.12f));
            for (int lon = -180; lon < 180; lon += 30)
                for (int lat = -87; lat < 87; lat += 3)
                    SurfaceLine(mesh, Sphere(lon, lat) * 1.003f, Sphere(lon, lat + 3) * 1.003f, 0.6f, Shade(0.8f, 0.12f));

            for (int i = 0; i < Locations.Length; i++)
            {
                Vector3 from = Sphere(Locations[i].x, Locations[i].y);
                Vector2 destination = Locations[(i + 3 + i % 3) % Locations.Length];
                Vector3 to = Sphere(destination.x, destination.y);
                float phase = Mathf.Repeat(clock * 0.18f + i * 0.173f, 1);
                for (int step = 0; step < 44; step++)
                {
                    Vector3 a = Arc(from, to, step / 44f);
                    Vector3 b = Arc(from, to, (step + 1) / 44f);
                    SurfaceLine(mesh, a, b, 5f, Shade(0.9f, 0.035f));
                    SurfaceLine(mesh, a, b, 1f, Shade(0.9f, 0.32f));
                }
                if (phase < 0.8f)
                {
                    float progress = phase / 0.8f;
                    for (int trail = 8; trail >= 0; trail--)
                    {
                        Vector3 position = rotation * Arc(from, to, Mathf.Max(0, progress - trail * 0.009f));
                        if (!Visible(position)) continue;
                        float alpha = 1 - trail / 9f;
                        Disc(mesh, Project(position), 7 * alpha + 1, Shade(1, 0.07f * alpha), 12);
                        Disc(mesh, Project(position), 2.4f * alpha + 0.5f, Shade(1, alpha), 10);
                    }
                }
                else
                {
                    Vector3 impact = rotation * (to * 1.015f);
                    if (!Visible(impact)) continue;
                    float age = (phase - 0.8f) / 0.2f;
                    Vector2 point = Project(impact);
                    Disc(mesh, point, 16 * (1 - age) + 3, Shade(1, (1 - age) * 0.22f), 24);
                    Circle(mesh, point, 3 + age * 24, 1.5f, Shade(1, 1 - age));
                    for (int spark = 0; spark < 7; spark++)
                    {
                        float angle = spark * Mathf.PI * 2 / 7 + i;
                        Vector2 direction = new(Mathf.Cos(angle), Mathf.Sin(angle));
                        Line(mesh, point + direction * age * 26, point + direction * (age * 26 + 4), 1.5f, Shade(1, 1 - age));
                    }
                }
                Vector3 marker = rotation * (from * 1.01f);
                if (Visible(marker))
                {
                    Disc(mesh, Project(marker), 5, Shade(1, 0.15f), 16);
                    Disc(mesh, Project(marker), 1.8f, Shade(0.95f), 12);
                }
            }
        }

        private void SurfaceLine(VertexHelper mesh, Vector3 a, Vector3 b, float width, Color color)
        {
            a = rotation * a;
            b = rotation * b;
            if (Visible(a) && Visible(b)) Line(mesh, Project(a), Project(b), width, color);
        }

        private static bool Visible(Vector3 point) => point.z <= 0 || point.x * point.x + point.y * point.y > 1.01f;
        private Vector2 Project(Vector3 point) => center + new Vector2(point.x, point.y) * radius;
        private static Vector3 Arc(Vector3 a, Vector3 b, float t) => Vector3.Slerp(a, b, t).normalized * (1.015f + Mathf.Sin(t * Mathf.PI) * 0.22f);
        private static Vector3 Sphere(float lon, float lat)
        {
            lon *= Mathf.Deg2Rad;
            lat *= Mathf.Deg2Rad;
            return new Vector3(Mathf.Cos(lat) * Mathf.Sin(lon), Mathf.Sin(lat), -Mathf.Cos(lat) * Mathf.Cos(lon));
        }
        private static Color Shade(float value, float alpha = 1) => new(value, value, value, alpha);
        private static Vector2[] Ring(params float[] coordinates)
        {
            Vector2[] points = new Vector2[coordinates.Length / 2];
            for (int i = 0; i < points.Length; i++) points[i] = new Vector2(coordinates[i * 2], coordinates[i * 2 + 1]);
            return points;
        }
        private static bool Contains(Vector2[] polygon, Vector2 point)
        {
            bool inside = false;
            for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
                if ((polygon[i].y > point.y) != (polygon[j].y > point.y) &&
                    point.x < (polygon[j].x - polygon[i].x) * (point.y - polygon[i].y) / (polygon[j].y - polygon[i].y) + polygon[i].x)
                    inside = !inside;
            return inside;
        }
        private static void Quad(VertexHelper mesh, Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color color)
        {
            int index = mesh.currentVertCount;
            mesh.AddVert(a, color, Vector2.zero); mesh.AddVert(b, color, Vector2.zero);
            mesh.AddVert(c, color, Vector2.zero); mesh.AddVert(d, color, Vector2.zero);
            mesh.AddTriangle(index, index + 1, index + 2); mesh.AddTriangle(index, index + 2, index + 3);
        }
        private static void Line(VertexHelper mesh, Vector2 a, Vector2 b, float width, Color color)
        {
            Vector2 delta = b - a;
            Vector2 normal = new Vector2(-delta.y, delta.x).normalized * width * 0.5f;
            Quad(mesh, a - normal, a + normal, b + normal, b - normal, color);
        }
        private static void Circle(VertexHelper mesh, Vector2 point, float size, float width, Color color)
        {
            for (int i = 0; i < 80; i++)
            {
                float a = i * Mathf.PI * 2 / 80, b = (i + 1) * Mathf.PI * 2 / 80;
                Line(mesh, point + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * size, point + new Vector2(Mathf.Cos(b), Mathf.Sin(b)) * size, width, color);
            }
        }
        private static void Disc(VertexHelper mesh, Vector2 point, float size, Color color, int segments)
        {
            int start = mesh.currentVertCount;
            mesh.AddVert(point, color, Vector2.zero);
            for (int i = 0; i <= segments; i++)
            {
                float angle = i * Mathf.PI * 2 / segments;
                mesh.AddVert(point + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * size, color, Vector2.zero);
                if (i > 0) mesh.AddTriangle(start, start + i, start + i + 1);
            }
        }
    }
}
