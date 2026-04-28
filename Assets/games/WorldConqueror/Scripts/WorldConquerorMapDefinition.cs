using System;
using UnityEngine;

namespace WorldConqueror
{
    [Serializable]
    public class WorldConquerorMapDefinition
    {
        public WorldConquerorStateDefinition[] regions = Array.Empty<WorldConquerorStateDefinition>();
    }

    [Serializable]
    public class WorldConquerorStateDefinition
    {
        public string id;
        public string displayName;
        public SerializableVector2[] points = Array.Empty<SerializableVector2>();
        public SerializablePolygon[] polygons = Array.Empty<SerializablePolygon>();
        public SerializableVector2 labelPosition;
        public bool hasLabelPosition;

        public string NameOrId => string.IsNullOrWhiteSpace(displayName) ? id : displayName;
    }

    [Serializable]
    public class SerializablePolygon
    {
        public SerializableVector2[] points = Array.Empty<SerializableVector2>();
    }

    [Serializable]
    public struct SerializableVector2
    {
        public float x;
        public float y;

        public SerializableVector2(float xValue, float yValue)
        {
            x = xValue;
            y = yValue;
        }

        public readonly Vector2 ToVector2()
        {
            return new Vector2(x, y);
        }

        public static SerializableVector2 FromVector2(Vector2 value)
        {
            return new SerializableVector2(value.x, value.y);
        }
    }

}
