using UnityEngine;

namespace WorldConqueror
{
    [DisallowMultipleComponent]
    public class WorldConquerorTroopPacket : MonoBehaviour
    {
        private static Sprite circleSprite;
        private SpriteRenderer packetRenderer;
        private Vector3 startPosition;
        private Vector3 targetPosition;
        private float departureTime;
        private float travelDuration;

        public void Initialize(
            Vector3 newStartPosition,
            Vector3 newTargetPosition,
            float newDepartureTime,
            float newTravelDuration,
            Color color)
        {
            startPosition = newStartPosition;
            targetPosition = newTargetPosition;
            transform.position = startPosition;
            departureTime = newDepartureTime;
            travelDuration = Mathf.Max(0.001f, newTravelDuration);
            packetRenderer = gameObject.AddComponent<SpriteRenderer>();
            if (circleSprite == null) circleSprite = CreateCircleSprite();
            packetRenderer.sprite = circleSprite;
            packetRenderer.color = color;
            packetRenderer.sortingOrder = 12;
            packetRenderer.enabled = false;
            transform.localScale = Vector3.one * 0.18f;
        }

        public void Render(float simulationTime)
        {
            packetRenderer.enabled = simulationTime >= departureTime;
            if (!packetRenderer.enabled) return;
            float t = Mathf.Clamp01((simulationTime - departureTime) / travelDuration);
            transform.position = Vector3.Lerp(startPosition, targetPosition, t);
        }

        private static Sprite CreateCircleSprite()
        {
            const int size = 32;
            Texture2D texture = new(size, size, TextureFormat.RGBA32, false);
            Vector2 center = new((size - 1) * 0.5f, (size - 1) * 0.5f);
            float radius = size * 0.42f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float distance = Vector2.Distance(new Vector2(x, y), center);
                    texture.SetPixel(x, y, distance <= radius ? Color.white : Color.clear);
                }
            }

            texture.Apply();
            return Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), size);
        }
    }
}
