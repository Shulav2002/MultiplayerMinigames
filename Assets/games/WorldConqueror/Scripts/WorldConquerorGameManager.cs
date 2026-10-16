using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace WorldConqueror
{
    [DisallowMultipleComponent]
    public class WorldConquerorGameManager : MonoBehaviour
    {
        [Header("Starting Values")]
        [SerializeField] private int playerId = 0;
        [SerializeField] [Min(0)] private int startingTroops = 10;
        [SerializeField] [Min(0)] private int neutralStartingTroops = 10;
        [SerializeField] [Min(0)] private int startingPlayerBases = 1;
        [SerializeField] [Min(0)] private int startingEnemyBases = 1;
        [SerializeField] [Min(1)] private int maxTroopsPerTerritory = 99;

        [Header("Troop Speeds")]
        [Tooltip("How many troops each owned base generates per second.")]
        [SerializeField] [Min(0f)] private float troopsPerSecond = 1.4f;
        [Tooltip("World units per second for each launched troop bullet.")]
        [SerializeField] [Min(0.1f)] private float packetSpeed = 5.6f;
        [Tooltip("Delay between launching each troop bullet when a base sends a full wave.")]
        [SerializeField] [Min(0f)] private float packetLaunchSpacing = 0.035f;

        [Header("Enemy")]
        [SerializeField] [Min(0.1f)] private float botDecisionInterval = 1.35f;

        [Header("Colors")]
        [SerializeField] private Color playerColor = new(0.12f, 0.54f, 1f, 1f);
        [SerializeField] private Color enemyColor = new(0.95f, 0.25f, 0.22f, 1f);
        [SerializeField] private Color neutralColor = new(0.50f, 0.50f, 0.50f, 1f);
        [SerializeField] private Color selectedColor = new(0.0f, 0.80f, 1f, 1f);

        private readonly Dictionary<string, Territory> territories = new();
        private Territory selectedTerritory;
        private float botTimer;
        private bool initialized;
        private bool matchRunning;

        private void OnEnable()
        {
            WorldConquerorPolygonState.Pressed += HandleTerritoryPressed;
        }

        private void OnDisable()
        {
            WorldConquerorPolygonState.Pressed -= HandleTerritoryPressed;
        }

        public void BeginMatch()
        {
            InitializeFromScene();
            botTimer = 0f;
            matchRunning = initialized;
        }

        public void StopMatch()
        {
            matchRunning = false;
            selectedTerritory = null;
            foreach (WorldConquerorTroopPacket packet in GetComponentsInChildren<WorldConquerorTroopPacket>())
            {
                packet.gameObject.SetActive(false);
                Destroy(packet.gameObject);
            }
        }

        private void Update()
        {
            if (!matchRunning) return;
            if (!initialized)
            {
                InitializeFromScene();
            }

            ProduceTroops();
            UpdateBot();
            HandlePointerRelease();
            RefreshTerritoryViews();
        }

        [ContextMenu("Initialize State.io Game")]
        public void InitializeFromScene()
        {
            initialized = false;
            selectedTerritory = null;
            territories.Clear();
            WorldConquerorPolygonState[] fragments = FindObjectsByType<WorldConquerorPolygonState>(FindObjectsSortMode.None);
            if (fragments.Length == 0)
            {
                return;
            }

            foreach (IGrouping<string, WorldConquerorPolygonState> group in fragments.GroupBy(fragment => fragment.StateName))
            {
                Territory territory = new(group.Key, group.ToList());
                territories.Add(territory.Name, territory);
            }

            List<Territory> ordered = territories.Values
                .OrderBy(territory => territory.TargetPosition.x)
                .ToList();

            for (int i = 0; i < ordered.Count; i++)
            {
                Territory territory = ordered[i];
                territory.OwnerId = -1;
                territory.Troops = neutralStartingTroops;
            }

            int playerBasesToAssign = Mathf.Clamp(startingPlayerBases, 0, ordered.Count);
            for (int i = 0; i < playerBasesToAssign; i++)
            {
                ordered[i].OwnerId = playerId;
                ordered[i].Troops = startingTroops;
            }

            int enemyBasesToAssign = Mathf.Clamp(startingEnemyBases, 0, ordered.Count - playerBasesToAssign);
            for (int i = 0; i < enemyBasesToAssign; i++)
            {
                Territory enemyStart = ordered[ordered.Count - 1 - i];
                enemyStart.OwnerId = 1;
                enemyStart.Troops = startingTroops;
            }

            selectedTerritory = null;
            initialized = true;
            RefreshTerritoryViews();
        }

        public void ApplyIncomingTroops(string targetStateName, int ownerId, int troopCount)
        {
            if (!territories.TryGetValue(targetStateName, out Territory target))
            {
                return;
            }

            if (target.OwnerId == ownerId)
            {
                target.Troops = Mathf.Min(maxTroopsPerTerritory, target.Troops + troopCount);
            }
            else
            {
                target.Troops -= troopCount;
                if (target.Troops <= 0)
                {
                    target.OwnerId = ownerId;
                    target.Troops = Mathf.Abs(target.Troops);
                }
            }

            RefreshTerritoryViews();
        }

        private void HandleTerritoryPressed(WorldConquerorPolygonState fragment)
        {
            if (!matchRunning || !initialized || fragment == null || !territories.TryGetValue(fragment.StateName, out Territory clicked))
            {
                return;
            }

            if (selectedTerritory == null)
            {
                if (clicked.OwnerId == playerId && clicked.Troops > 0)
                {
                    selectedTerritory = clicked;
                }

                RefreshTerritoryViews();
                return;
            }

            if (clicked == selectedTerritory)
            {
                selectedTerritory = null;
                RefreshTerritoryViews();
                return;
            }

            SendTroops(selectedTerritory, clicked);
            selectedTerritory = null;
            RefreshTerritoryViews();
        }

        private void ProduceTroops()
        {
            foreach (Territory territory in territories.Values)
            {
                if (territory.OwnerId < 0 || territory.Troops >= maxTroopsPerTerritory)
                {
                    territory.ProductionCarry = 0f;
                    continue;
                }

                territory.ProductionCarry += troopsPerSecond * Time.deltaTime;
                int produced = Mathf.FloorToInt(territory.ProductionCarry);
                if (produced <= 0)
                {
                    continue;
                }

                territory.ProductionCarry -= produced;
                territory.Troops = Mathf.Min(maxTroopsPerTerritory, territory.Troops + produced);
            }
        }

        private void UpdateBot()
        {
            botTimer += Time.deltaTime;
            if (botTimer < botDecisionInterval)
            {
                return;
            }

            botTimer = 0f;
            Territory source = territories.Values
                .Where(territory => territory.OwnerId == 1 && territory.Troops >= 10)
                .OrderByDescending(territory => territory.Troops)
                .FirstOrDefault();

            if (source == null)
            {
                return;
            }

            Territory target = territories.Values
                .Where(territory => territory.OwnerId != 1)
                .OrderBy(territory => Vector3.Distance(source.TargetPosition, territory.TargetPosition))
                .ThenBy(territory => territory.Troops)
                .FirstOrDefault();

            if (target != null)
            {
                SendTroops(source, target);
            }
        }

        private void HandlePointerRelease()
        {
            if (selectedTerritory == null || !Input.GetMouseButtonUp(0) || Camera.main == null)
            {
                return;
            }

            Vector3 worldPosition = Camera.main.ScreenToWorldPoint(Input.mousePosition);
            Collider2D hit = Physics2D.OverlapPoint(worldPosition);
            if (hit == null || !hit.TryGetComponent(out WorldConquerorPolygonState targetFragment))
            {
                return;
            }

            if (!territories.TryGetValue(targetFragment.StateName, out Territory target))
            {
                return;
            }

            if (target == selectedTerritory)
            {
                selectedTerritory = null;
                return;
            }

            SendTroops(selectedTerritory, target);
            selectedTerritory = null;
        }

        private void SendTroops(Territory source, Territory target)
        {
            if (source == null || target == null || source.Troops <= 0)
            {
                return;
            }

            int troopCount = source.Troops;
            source.Troops = 0;

            for (int i = 0; i < troopCount; i++)
            {
                GameObject packetObject = new($"Bullet_{source.Name}_to_{target.Name}_{i + 1}");
                packetObject.transform.SetParent(transform, false);
                WorldConquerorTroopPacket packet = packetObject.AddComponent<WorldConquerorTroopPacket>();
                packet.Initialize(
                    this,
                    target.Name,
                    source.OwnerId,
                    1,
                    source.TargetPosition,
                    target.TargetPosition,
                    packetSpeed,
                    i * packetLaunchSpacing,
                    GetOwnerColor(source.OwnerId));
            }
        }

        private void RefreshTerritoryViews()
        {
            foreach (Territory territory in territories.Values)
            {
                Color baseColor = territory == selectedTerritory ? selectedColor : GetOwnerColor(territory.OwnerId);
                Color hoverColor = Color.Lerp(baseColor, Color.white, 0.22f);
                Color pressedColor = Color.Lerp(baseColor, Color.black, 0.18f);
                string label = territory.Troops.ToString();

                foreach (WorldConquerorPolygonState fragment in territory.Fragments)
                {
                    bool isAnchor = fragment == territory.Anchor;
                    fragment.SetRuntimeAnchorVisible(isAnchor);
                    fragment.ApplyRuntimeColors(baseColor, hoverColor, pressedColor, Color.Lerp(baseColor, Color.black, 0.35f));
                    fragment.SetTroopLabel(isAnchor ? label : string.Empty, Color.white);
                }
            }
        }

        private Color GetOwnerColor(int ownerId)
        {
            if (ownerId == playerId)
            {
                return playerColor;
            }

            if (ownerId == 1)
            {
                return enemyColor;
            }

            return neutralColor;
        }

        private sealed class Territory
        {
            public Territory(string name, List<WorldConquerorPolygonState> fragments)
            {
                Name = name;
                Fragments = fragments;
                Anchor = fragments.FirstOrDefault(fragment => fragment.IsGameplayAnchor) ?? fragments.First();
            }

            public string Name { get; }
            public List<WorldConquerorPolygonState> Fragments { get; }
            public WorldConquerorPolygonState Anchor { get; }
            public int OwnerId { get; set; }
            public int Troops { get; set; }
            public float ProductionCarry { get; set; }
            public Vector3 TargetPosition => Anchor.TargetWorldPosition;
        }
    }
}
