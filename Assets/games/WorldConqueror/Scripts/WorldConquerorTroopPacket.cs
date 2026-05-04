using UnityEngine;

namespace WorldConqueror
{
    [DisallowMultipleComponent]
    public class WorldConquerorTroopPacket : MonoBehaviour
    {
        private WorldConquerorGameManager manager;
        private string targetStateName;
        private int ownerId;
        private int troopCount;
        private Vector3 startPosition;
        private Vector3 targetPosition;
        private float travelDuration = 1f;
        private float launchDelay;
        private float elapsed;

        public void Initialize(
            WorldConquerorGameManager newManager,
            string newTargetStateName,
            int newOwnerId,
            int newTroopCount,
            Vector3 newStartPosition,
            Vector3 newTargetPosition,
            float speed,
            float newLaunchDelay,
            Color color)
        {
            manager = newManager;
            targetStateName = newTargetStateName;
            ownerId = newOwnerId;
            troopCount = newTroopCount;
            startPosition = newStartPosition;
            targetPosition = newTargetPosition;
            transform.position = startPosition;

            float distance = Vector3.Distance(startPosition, targetPosition);
            travelDuration = Mathf.Max(0.12f, distance / Mathf.Max(0.1f, speed));
            launchDelay = Mathf.Max(0f, newLaunchDelay);
            elapsed = 0f;

            SpriteRenderer renderer = gameObject.AddComponent<SpriteRenderer>();
            renderer.sprite = CreateCircleSprite();
            renderer.color = color;
            renderer.sortingOrder = 12;
            transform.localScale = Vector3.one * Mathf.Clamp(0.12f + troopCount * 0.01f, 0.18f, 0.42f);
        }

        private void Update()
        {
            if (launchDelay > 0f)
            {
                launchDelay -= Time.deltaTime;
                return;
            }

            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / travelDuration);
            float eased = t * t * (3f - 2f * t);
            transform.position = Vector3.Lerp(startPosition, targetPosition, eased);

            if (t < 1f)
            {
                return;
            }

            manager?.ApplyIncomingTroops(targetStateName, ownerId, troopCount);
            Destroy(gameObject);
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
