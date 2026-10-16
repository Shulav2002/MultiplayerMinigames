using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;

namespace WorldConqueror
{
    [DisallowMultipleComponent]
    public class WorldConquerorGameManager : MonoBehaviour
    {
        public enum BotDifficulty { Easy, Normal, Hard }
        public enum BotPersonality { Balanced, Aggressive, Defensive, Opportunistic }

        [Header("Starting Values")]
        [SerializeField] private int playerId = 0;
        [SerializeField, Min(0)] private int startingTroops = 10;
        [SerializeField, Min(0)] private int neutralStartingTroops = 10;

        [Header("Troop Speeds")]
        [SerializeField, Min(0f)] private float troopsPerSecond = 1f;
        [SerializeField, Min(0.1f)] private float packetSpeed = 2f;
        [SerializeField, Min(0f)] private float packetLaunchSpacing = 0.10f;

        [Header("Enemy")]
        [SerializeField, Min(0.1f)] private float botDecisionInterval = 1.35f;
        [SerializeField] private BotDifficulty botDifficulty = BotDifficulty.Normal;
        [SerializeField] private BotPersonality botPersonality = BotPersonality.Balanced;

        [Header("Colors")]
        [SerializeField] private Color playerColor = new(0.12f, 0.54f, 1f, 1f);
        [SerializeField] private Color enemyColor = new(0.95f, 0.25f, 0.22f, 1f);
        [SerializeField] private Color neutralColor = new(0.50f, 0.50f, 0.50f, 1f);
        [SerializeField] private Color selectedColor = new(0.0f, 0.80f, 1f, 1f);

        private readonly Dictionary<string, Territory> territories = new();
        private readonly List<Arrival> arrivals = new();
        private Territory selectedTerritory;
        private float simulationTime;
        private float botTimer;
        private long nextSequence;
        private int sendPercent = 100;
        private int activeTouchId = -1;
        private bool dragging;
        private bool initialized;
        private bool matchRunning;

        public int SendPercent => sendPercent;
        public BotDifficulty Difficulty => botDifficulty;
        public BotPersonality Personality => botPersonality;
        public event Action<int> SendPercentChanged;

        public void SetSendPercent(int percent)
        {
            if (percent != 25 && percent != 50 && percent != 100) return;
            sendPercent = percent;
            SendPercentChanged?.Invoke(sendPercent);
        }
        public void SetBotDifficulty(BotDifficulty value) => botDifficulty = value;
        public void SetBotPersonality(BotPersonality value) => botPersonality = value;

        public void BeginMatch()
        {
            StopMatch();
            InitializeFromScene();
            botTimer = 0f;
            matchRunning = initialized;
            SendPercentChanged?.Invoke(sendPercent);
        }

        public void StopMatch()
        {
            matchRunning = false;
            dragging = false;
            selectedTerritory = null;
            foreach (Arrival arrival in arrivals)
                if (arrival.Packet != null) Destroy(arrival.Packet.gameObject);
            arrivals.Clear();
        }

        private void Update()
        {
            if (!matchRunning) return;
            HandlePointerInput();
            AdvanceSimulation(simulationTime + Time.deltaTime);
            UpdateBot();
            foreach (Arrival arrival in arrivals)
                if (arrival.Packet != null) arrival.Packet.Render(simulationTime);
            RefreshTerritoryViews();
        }

        [ContextMenu("Initialize State.io Game")]
        public void InitializeFromScene()
        {
            initialized = false;
            selectedTerritory = null;
            simulationTime = 0f;
            nextSequence = 0;
            territories.Clear();
            WorldConquerorPolygonState[] fragments = FindObjectsByType<WorldConquerorPolygonState>(FindObjectsSortMode.None);
            foreach (IGrouping<string, WorldConquerorPolygonState> group in fragments.GroupBy(fragment => fragment.StateName))
            {
                Territory territory = new(group.Key, group.ToList());
                territories.Add(territory.Name, territory);
            }
            if (territories.Count == 0) return;

            List<Territory> playable = SelectPlayableTerritories(territories.Values.ToList());
            foreach (Territory territory in territories.Values)
            {
                territory.Active = playable.Contains(territory);
                territory.OwnerId = -1;
                territory.Garrison = territory.Active ? neutralStartingTroops : 0f;
                territory.ProductionCarry = 0f;
            }
            if (playable.Count < 2) return;
            (Territory playerStart, Territory enemyStart) = ChooseStartingPair(playable);
            playerStart.OwnerId = playerId;
            playerStart.Garrison = startingTroops;
            enemyStart.OwnerId = 1;
            enemyStart.Garrison = startingTroops;
            initialized = true;
            RefreshTerritoryViews();
        }

        // One arrival represents one troop; retained for scene-level regression checks.
        public void ApplyIncomingTroops(string targetStateName, int ownerId, int troopCount)
        {
            if (!territories.TryGetValue(targetStateName, out Territory target) || !target.Active) return;
            for (int i = 0; i < troopCount; i++) ApplyArrival(target, ownerId);
            RefreshTerritoryViews();
        }

        private void AdvanceSimulation(float targetTime)
        {
            while (arrivals.Count > 0 && arrivals[0].Time <= targetTime)
            {
                Arrival arrival = arrivals[0];
                AdvanceEconomy(arrival.Time - simulationTime);
                simulationTime = arrival.Time;
                arrivals.RemoveAt(0);
                if (arrival.Packet != null) Destroy(arrival.Packet.gameObject);
                if (ApplyArrival(arrival.Target, arrival.OwnerId))
                    botTimer = botDecisionInterval;
            }
            AdvanceEconomy(targetTime - simulationTime);
            simulationTime = targetTime;
        }

        private void AdvanceEconomy(float elapsed)
        {
            if (elapsed <= 0f) return;
            foreach (Territory territory in territories.Values)
            {
                if (!territory.Active) continue;
                if (territory.OwnerId < 0)
                {
                    territory.Garrison = WorldConquerorRules.RegenerateNeutral(
                        territory.Garrison, elapsed, neutralStartingTroops);
                    continue;
                }
                float carry = territory.ProductionCarry;
                territory.Garrison += WorldConquerorRules.Produce(ref carry, troopsPerSecond, elapsed);
                territory.ProductionCarry = carry;
            }
        }

        private static bool ApplyArrival(Territory target, int ownerId)
        {
            WorldConquerorRules.ArrivalResult result =
                WorldConquerorRules.ResolveArrival(target.OwnerId, target.Garrison, ownerId);
            target.OwnerId = result.OwnerId;
            target.Garrison = result.Defense;
            if (result.Captured) target.ProductionCarry = 0f;
            return result.Captured;
        }

        private void HandlePointerInput()
        {
            if (Input.touchCount > 0)
            {
                for (int i = 0; i < Input.touchCount; i++)
                {
                    Touch touch = Input.GetTouch(i);
                    if (touch.phase == TouchPhase.Began && !dragging)
                        BeginDrag(touch.position, touch.fingerId);
                    else if (touch.fingerId == activeTouchId &&
                             (touch.phase == TouchPhase.Ended || touch.phase == TouchPhase.Canceled))
                        EndDrag(touch.position, touch.phase == TouchPhase.Canceled);
                }
                return;
            }
            if (Input.GetMouseButtonDown(0)) BeginDrag(Input.mousePosition, -1);
            if (Input.GetMouseButtonUp(0)) EndDrag(Input.mousePosition, false);
        }

        private void BeginDrag(Vector2 screenPosition, int pointerId)
        {
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject(pointerId)) return;
            Territory source = TerritoryAt(screenPosition);
            if (source == null || source.OwnerId != playerId || SourceTroops(source) <= 0) return;
            selectedTerritory = source;
            activeTouchId = pointerId;
            dragging = true;
        }

        private void EndDrag(Vector2 screenPosition, bool canceled)
        {
            if (!dragging) return;
            Territory source = selectedTerritory;
            Territory target = canceled ? null : TerritoryAt(screenPosition);
            dragging = false;
            selectedTerritory = null;
            activeTouchId = -1;
            if (source != null && target != null && source != target && source.OwnerId == playerId)
                SendTroops(source, target, sendPercent);
        }

        private Territory TerritoryAt(Vector2 screenPosition)
        {
            if (Camera.main == null) return null;
            Vector3 worldPosition = Camera.main.ScreenToWorldPoint(screenPosition);
            Collider2D hit = Physics2D.OverlapPoint(worldPosition);
            if (hit == null || !hit.TryGetComponent(out WorldConquerorPolygonState fragment)) return null;
            return territories.TryGetValue(fragment.StateName, out Territory territory) && territory.Active ? territory : null;
        }

        private static int SourceTroops(Territory territory) => WorldConquerorRules.AvailableTroops(territory.Garrison);

        private bool SendTroops(Territory source, Territory target, int percent)
        {
            if (source == null || target == null || source == target || !source.Active || !target.Active || source.OwnerId < 0)
                return false;
            int count = WorldConquerorRules.SendCount(source.Garrison, percent);
            if (count <= 0) return false;
            int ownerId = source.OwnerId;
            source.Garrison -= count; // Commit the full order immediately.
            if (ownerId == playerId && target.OwnerId == 1)
                botTimer = botDecisionInterval;
            float distance = Vector3.Distance(source.TargetPosition, target.TargetPosition);
            float travelTime = distance / Mathf.Max(0.1f, packetSpeed);
            for (int i = 0; i < count; i++)
            {
                float departure = simulationTime + i * packetLaunchSpacing;
                GameObject packetObject = new($"Bullet_{source.Name}_to_{target.Name}_{nextSequence}");
                packetObject.transform.SetParent(transform, false);
                WorldConquerorTroopPacket packet = packetObject.AddComponent<WorldConquerorTroopPacket>();
                packet.Initialize(source.TargetPosition, target.TargetPosition, departure, travelTime, GetOwnerColor(ownerId));
                float arrivalTime = WorldConquerorRules.ArrivalTime(
                    simulationTime, i, packetLaunchSpacing, distance, packetSpeed);
                arrivals.Add(new Arrival(arrivalTime, nextSequence++, target, ownerId, packet));
            }
            arrivals.Sort((a, b) =>
            {
                int timeOrder = a.Time.CompareTo(b.Time);
                return timeOrder != 0 ? timeOrder : a.Sequence.CompareTo(b.Sequence);
            });
            return true;
        }

        private void UpdateBot()
        {
            float speedFactor = botDifficulty == BotDifficulty.Easy ? 1.5f : botDifficulty == BotDifficulty.Hard ? 0.7f : 1f;
            botTimer += Time.deltaTime;
            if (botTimer < botDecisionInterval * speedFactor) return;
            botTimer = 0f;
            BotAction best = null;
            List<Territory> active = territories.Values.Where(t => t.Active).ToList();
            if (active.All(t => t.OwnerId == 1)) return;
            foreach (Territory source in active.Where(t => t.OwnerId == 1 && SourceTroops(t) > 0))
            {
                int threat = IncomingThreat(source);
                int reserve = threat > 0 ? Mathf.CeilToInt(threat * 0.75f) : 0;
                if (botPersonality == BotPersonality.Defensive && threat > 0) reserve += 2;
                foreach (Territory target in active.Where(t => t != source))
                {
                    float distance = Vector3.Distance(source.TargetPosition, target.TargetPosition);
                    float firstArrival = distance / Mathf.Max(0.1f, packetSpeed);
                    int required;
                    float score;
                    if (target.OwnerId == 1)
                    {
                        int incoming = IncomingThreat(target);
                        if (incoming <= target.Garrison + troopsPerSecond * firstArrival) continue;
                        float firstThreat = arrivals.Where(a => a.Target == target && a.OwnerId != 1)
                            .Min(a => a.Time - simulationTime);
                        if (firstArrival > firstThreat + 0.5f) continue;
                        required = Mathf.CeilToInt(incoming - target.Garrison - troopsPerSecond * firstArrival + 2);
                        score = 15f + incoming * 0.2f - distance;
                    }
                    else
                    {
                        float streamDuration = SourceTroops(source) * packetLaunchSpacing;
                        float streamWeight = botDifficulty == BotDifficulty.Easy ? 0.5f :
                            botDifficulty == BotDifficulty.Normal ? 0.75f : 1f;
                        float forecastHorizon = firstArrival + streamDuration * streamWeight;
                        float forecast = target.OwnerId < 0
                            ? Mathf.Min(neutralStartingTroops, target.Garrison + firstArrival) +
                              streamDuration * streamWeight
                            : target.Garrison + troopsPerSecond * forecastHorizon;
                        if (target.OwnerId >= 0)
                            forecast += arrivals.Count(a => a.Target == target && a.OwnerId == target.OwnerId &&
                                                           a.Time - simulationTime <= forecastHorizon);
                        float margin = botDifficulty == BotDifficulty.Hard ? 3f : botDifficulty == BotDifficulty.Normal ? 2f : 1f;
                        required = Mathf.CeilToInt(forecast + 1f + margin);
                        score = 12f - required * 0.25f - distance * 0.4f;
                        if (target.OwnerId >= 0) score += 1.5f;
                        if (botPersonality == BotPersonality.Aggressive) score += 2f;
                        if (botPersonality == BotPersonality.Opportunistic) score += Mathf.Max(0f, 10f - target.Garrison) * 0.3f;
                        if (botPersonality == BotPersonality.Defensive) score -= 2f;
                    }
                    int chosenPercent = 0;
                    foreach (int percent in new[] { 25, 50, 100 })
                    {
                        int count = Mathf.FloorToInt(SourceTroops(source) * percent / 100f);
                        if (count >= required && SourceTroops(source) - count >= reserve)
                        {
                            chosenPercent = percent;
                            break;
                        }
                    }
                    if (chosenPercent == 0) continue;
                    if (best == null || score > best.Score)
                        best = new BotAction(source, target, chosenPercent, score);
                }
            }
            if (best == null)
            {
                foreach (Territory source in active.Where(t => t.OwnerId == 1 && SourceTroops(t) >= 8))
                {
                    float sourceFront = NearestOpponentDistance(source, active);
                    foreach (Territory target in active.Where(t => t.OwnerId == 1 && t != source))
                    {
                        float targetFront = NearestOpponentDistance(target, active);
                        if (targetFront >= sourceFront - 1f || IncomingThreat(source) > 0) continue;
                        float score = sourceFront - targetFront - Vector3.Distance(source.TargetPosition, target.TargetPosition) * 0.2f;
                        if (best == null || score > best.Score)
                            best = new BotAction(source, target, 50, score);
                    }
                }
            }
            if (best != null) SendTroops(best.Source, best.Target, best.Percent);
        }

        private static float NearestOpponentDistance(Territory source, List<Territory> active)
        {
            return active.Where(t => t.OwnerId != 1)
                .Min(t => Vector3.Distance(source.TargetPosition, t.TargetPosition));
        }

        private int IncomingThreat(Territory target) =>
            arrivals.Count(a => a.Target == target && a.OwnerId != target.OwnerId && a.Time - simulationTime <= 8f);

        private static List<Territory> SelectPlayableTerritories(List<Territory> all)
        {
            // Geography remains visible; larger maps use well-spaced base locations.
            int wanted = all.Count <= 12 ? all.Count : all.Count <= 40 ? 18 : 26;
            if (all.Count <= wanted) return all;
            Vector3 center = all.Aggregate(Vector3.zero, (sum, t) => sum + t.TargetPosition) / all.Count;
            List<Territory> selected = new() { all.OrderBy(t => Vector3.Distance(t.TargetPosition, center)).ThenBy(t => t.Name).First() };
            while (selected.Count < wanted)
            {
                Territory next = all.Except(selected)
                    .OrderByDescending(t => selected.Min(s => Vector3.Distance(t.TargetPosition, s.TargetPosition)))
                    .ThenBy(t => t.Name).First();
                selected.Add(next);
            }
            return selected;
        }

        private static (Territory, Territory) ChooseStartingPair(List<Territory> active)
        {
            Territory first = active[0], second = active[1];
            float bestScore = float.NegativeInfinity;
            for (int i = 0; i < active.Count; i++)
                for (int j = i + 1; j < active.Count; j++)
                {
                    Territory a = active[i], b = active[j];
                    float separation = Vector3.Distance(a.TargetPosition, b.TargetPosition);
                    List<Territory> neutralStarts = active.Where(t => t != a && t != b).ToList();
                    float nearA = neutralStarts.Count == 0 ? 0f :
                        neutralStarts.Min(t => Vector3.Distance(a.TargetPosition, t.TargetPosition));
                    float nearB = neutralStarts.Count == 0 ? 0f :
                        neutralStarts.Min(t => Vector3.Distance(b.TargetPosition, t.TargetPosition));
                    float score = separation - Mathf.Abs(nearA - nearB) * 3f;
                    if (score <= bestScore) continue;
                    bestScore = score;
                    first = a;
                    second = b;
                }
            return (first, second);
        }

        private void RefreshTerritoryViews()
        {
            foreach (Territory territory in territories.Values)
            {
                Color baseColor = !territory.Active ? neutralColor : territory == selectedTerritory ? selectedColor : GetOwnerColor(territory.OwnerId);
                Color hoverColor = Color.Lerp(baseColor, Color.white, 0.22f);
                Color pressedColor = Color.Lerp(baseColor, Color.black, 0.18f);
                string label = Mathf.RoundToInt(territory.Garrison).ToString();
                foreach (WorldConquerorPolygonState fragment in territory.Fragments)
                {
                    bool isAnchor = territory.Active && fragment == territory.Anchor;
                    fragment.SetRuntimeAnchorVisible(isAnchor);
                    fragment.ApplyRuntimeColors(baseColor, hoverColor, pressedColor, Color.Lerp(baseColor, Color.black, 0.35f));
                    fragment.SetTroopLabel(isAnchor ? label : string.Empty, Color.white);
                }
            }
        }

        private Color GetOwnerColor(int ownerId) => ownerId == playerId ? playerColor : ownerId == 1 ? enemyColor : neutralColor;

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
            public Vector3 TargetPosition => Anchor.TargetWorldPosition;
            public bool Active { get; set; }
            public int OwnerId { get; set; }
            public float Garrison { get; set; }
            public float ProductionCarry { get; set; }
        }

        private sealed class Arrival
        {
            public Arrival(float time, long sequence, Territory target, int ownerId, WorldConquerorTroopPacket packet)
            { Time = time; Sequence = sequence; Target = target; OwnerId = ownerId; Packet = packet; }
            public float Time { get; }
            public long Sequence { get; }
            public Territory Target { get; }
            public int OwnerId { get; }
            public WorldConquerorTroopPacket Packet { get; }
        }

        private sealed class BotAction
        {
            public BotAction(Territory source, Territory target, int percent, float score)
            { Source = source; Target = target; Percent = percent; Score = score; }
            public Territory Source { get; }
            public Territory Target { get; }
            public int Percent { get; }
            public float Score { get; }
        }
    }
}
