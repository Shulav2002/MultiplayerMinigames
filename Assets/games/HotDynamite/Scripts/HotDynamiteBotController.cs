using UnityEngine;

namespace HotDynamite
{
    public class HotDynamiteBotController : MonoBehaviour
    {
        private const float VectorEpsilonSqr = 0.0001f;

        [SerializeField, Range(0f, 1f)] private float randomChaseTargetChance = 0.3f;
        [SerializeField] private Vector2 chaseTargetHoldTime = new Vector2(0.35f, 0.9f);
        [SerializeField] private Vector2 interceptLookAhead = new Vector2(0.12f, 0.55f);
        [Header("Non-Bomb Behavior")]
        [SerializeField] private Vector2 preferredBombDistance = new Vector2(3.2f, 6.3f);
        [SerializeField, Range(0f, 1f)] private float orbitBlend = 0.65f;
        [SerializeField, Range(0f, 2f)] private float centerBias = 0.9f;
        [SerializeField, Range(0f, 2f)] private float cohesionBias = 0.7f;
        [SerializeField, Range(0f, 3f)] private float edgePatrolBias = 1.1f;
        [SerializeField] private float stuckSpeedThreshold = 0.12f;
        [SerializeField] private float stuckDirectionRefreshTime = 0.45f;

        private HotDynamiteGameManager manager;
        private HotDynamiteGameSettings settings;
        private HotDynamitePlayer owner;
        private float nextDecisionTime;
        private float nextTargetSelectionTime;
        private float nextUnstuckTime;
        private Vector2 moveVector;
        private HotDynamitePlayer chaseTarget;
        private float orbitSign = 1f;

        public Vector2 MoveVector => moveVector;

        public void Initialize(HotDynamiteGameManager manager, HotDynamiteGameSettings settings, HotDynamitePlayer owner)
        {
            this.manager = manager;
            this.settings = settings;
            this.owner = owner;
            orbitSign = Random.value < 0.5f ? -1f : 1f;
        }

        private void Update()
        {
            if (owner == null || manager == null || settings == null || !owner.IsAlive)
            {
                moveVector = Vector2.zero;
                return;
            }

            float now = Time.time;
            if (now < nextDecisionTime)
            {
                return;
            }

            float bombUrgency = manager.BombUrgency01;
            float decisionInterval = owner.HasBomb
                ? settings.botDecisionInterval * Mathf.Lerp(0.55f, 0.2f, bombUrgency)
                : settings.botDecisionInterval * Mathf.Lerp(0.8f, 0.55f, bombUrgency);
            nextDecisionTime = now + Mathf.Max(0.01f, decisionInterval);

            Vector2 desired = Vector2.zero;
            HotDynamitePlayer bombHolder = manager.BombHolder;

            if (owner.HasBomb)
            {
                if (chaseTarget == null
                    || !chaseTarget.IsAlive
                    || chaseTarget == owner
                    || now >= nextTargetSelectionTime)
                {
                    float effectiveRandomTargetChance = Mathf.Lerp(
                        randomChaseTargetChance,
                        randomChaseTargetChance * 0.25f,
                        bombUrgency);
                    bool pickRandomTarget = Random.value < effectiveRandomTargetChance;
                    chaseTarget = pickRandomTarget
                        ? manager.GetRandomAlivePlayer(owner)
                        : manager.GetClosestAlivePlayer(owner);

                    if (chaseTarget == null)
                    {
                        chaseTarget = manager.GetClosestAlivePlayer(owner);
                    }

                    float minHold = Mathf.Max(0.05f, chaseTargetHoldTime.x);
                    float maxHold = Mathf.Max(minHold, chaseTargetHoldTime.y);
                    float holdScale = Mathf.Lerp(1f, 0.45f, bombUrgency);
                    nextTargetSelectionTime = now + Random.Range(minHold, maxHold) * holdScale;
                }

                HotDynamitePlayer target = chaseTarget;
                if (target != null)
                {
                    desired = CalculateInterceptDirection(target, bombUrgency);
                }
            }
            else if (bombHolder != null)
            {
                chaseTarget = null;
                desired = CalculateNonBombDesiredDirection(bombHolder, bombUrgency);
            }

            Vector2 avoid = CalculateArenaAvoidance(owner.Position);

            float clampedWanderStrength = Mathf.Clamp(settings.botWanderStrength, 0f, 1f);
            float primaryWeight;
            float avoidWeight;
            float wanderWeight;

            if (owner.HasBomb)
            {
                primaryWeight = Mathf.Lerp(2.4f, 3.8f, bombUrgency);
                avoidWeight = Mathf.Lerp(settings.botAvoidanceStrength * 0.45f, settings.botAvoidanceStrength * 0.25f, bombUrgency);
                wanderWeight = 0f;
            }
            else
            {
                float danger = 0f;
                if (bombHolder != null)
                {
                    float safeDistance = Mathf.Max(0.1f, settings.botAvoidanceDistance * 4f);
                    float bombDistance = Vector2.Distance(owner.Position, bombHolder.Position);
                    danger = Mathf.Clamp01(1f - (bombDistance / safeDistance));
                }

                float urgencyBiasedDanger = Mathf.Clamp01(danger + bombUrgency * 0.35f);

                // Non-bomb bots should prioritize escape when bomber is nearby.
                primaryWeight = Mathf.Lerp(1.8f, 3.4f, urgencyBiasedDanger);
                avoidWeight = Mathf.Lerp(1.0f, 0.5f, urgencyBiasedDanger);
                wanderWeight = clampedWanderStrength * 0.1f * (1f - urgencyBiasedDanger);

                Vector2 center = (settings.arenaMin + settings.arenaMax) * 0.5f;
                Vector2 centerPull = (center - owner.Position);
                centerPull = centerPull.sqrMagnitude > 0.0001f ? centerPull.normalized : Vector2.zero;
                float centerWeight = centerBias * Mathf.Lerp(1.0f, 0.3f, urgencyBiasedDanger);

                Vector2 edgePatrol = CalculateEdgePatrolDirection(center);
                float edgeFactor = GetEdgeFactor(owner.Position);
                float edgePatrolWeight = edgePatrolBias * Mathf.Lerp(0.4f, 1.2f, edgeFactor);

                Vector2 cohesionPull = Vector2.zero;
                float cohesionWeight = 0f;
                if (manager.TryGetAliveCentroid(owner, includeBombHolder: false, out Vector2 centroid))
                {
                    cohesionPull = centroid - owner.Position;
                    cohesionPull = cohesionPull.sqrMagnitude > 0.0001f ? cohesionPull.normalized : Vector2.zero;
                    cohesionWeight = cohesionBias * Mathf.Lerp(0.9f, 0.25f, urgencyBiasedDanger);
                }

                Vector2 nonBombWander = Random.insideUnitCircle * wanderWeight;
                moveVector = desired * primaryWeight
                    + avoid * avoidWeight
                    + centerPull * centerWeight
                    + edgePatrol * edgePatrolWeight
                    + cohesionPull * cohesionWeight
                    + nonBombWander;

                // If forces cancel out, force a fresh tangential direction so bots keep moving.
                if (moveVector.sqrMagnitude < 0.04f || owner.EstimatedVelocity.magnitude < stuckSpeedThreshold)
                {
                    if (now >= nextUnstuckTime)
                    {
                        orbitSign *= -1f;
                        nextUnstuckTime = now + Mathf.Max(0.1f, stuckDirectionRefreshTime);
                    }

                    moveVector += edgePatrol * Mathf.Max(0.45f, edgePatrolWeight);
                }

                NormalizeMoveVector();
                return;
            }

            Vector2 wander = Random.insideUnitCircle * wanderWeight;
            moveVector = desired * primaryWeight + avoid * avoidWeight + wander;
            NormalizeMoveVector();
        }

        private Vector2 CalculateInterceptDirection(HotDynamitePlayer target, float urgency)
        {
            if (target == null)
            {
                return Vector2.zero;
            }

            Vector2 toTarget = target.Position - owner.Position;
            if (toTarget.sqrMagnitude <= VectorEpsilonSqr)
            {
                return Vector2.zero;
            }

            float ownerSpeed = Mathf.Max(0.1f, settings.moveSpeed * settings.bombHolderSpeedMultiplier);
            float targetSpeed = target.EstimatedVelocity.magnitude;
            float travelTime = toTarget.magnitude / Mathf.Max(0.1f, ownerSpeed + targetSpeed * 0.75f);

            float minLookAhead = Mathf.Max(0.02f, interceptLookAhead.x);
            float maxLookAhead = Mathf.Max(minLookAhead, interceptLookAhead.y);
            float urgencyBias = Mathf.Lerp(1f, 0.6f, urgency);
            float lookAhead = Mathf.Clamp(travelTime * urgencyBias, minLookAhead, maxLookAhead);

            Vector2 predictedTargetPosition = target.Position + target.EstimatedVelocity * lookAhead;
            Vector2 toPredicted = predictedTargetPosition - owner.Position;
            return toPredicted.sqrMagnitude > VectorEpsilonSqr ? toPredicted.normalized : toTarget.normalized;
        }

        private Vector2 CalculateNonBombDesiredDirection(HotDynamitePlayer bombHolder, float urgency)
        {
            if (bombHolder == null)
            {
                return Vector2.zero;
            }

            float lookAhead = Mathf.Lerp(0.08f, 0.30f, urgency);
            Vector2 predictedBombPosition = bombHolder.Position + bombHolder.EstimatedVelocity * lookAhead;
            Vector2 away = owner.Position - predictedBombPosition;
            float distance = away.magnitude;
            if (distance <= 0.01f)
            {
                return Random.insideUnitCircle.normalized;
            }

            Vector2 awayDir = away / distance;
            float minDistance = Mathf.Max(0.5f, preferredBombDistance.x);
            float maxDistance = Mathf.Max(minDistance + 0.25f, preferredBombDistance.y);

            if (distance < minDistance)
            {
                return awayDir;
            }

            Vector2 toBombDir = -awayDir;
            Vector2 orbitDir = new Vector2(-toBombDir.y, toBombDir.x) * orbitSign;
            if (Random.value < 0.02f)
            {
                orbitSign *= -1f;
            }

            if (distance > maxDistance)
            {
                Vector2 reEngage = Vector2.Lerp(toBombDir, orbitDir, 0.35f).normalized;
                return reEngage;
            }

            float bandT = Mathf.InverseLerp(minDistance, maxDistance, distance);
            float adjustedOrbitBlend = Mathf.Lerp(orbitBlend * 0.55f, orbitBlend, bandT);
            Vector2 bandMove = Vector2.Lerp(awayDir, orbitDir, adjustedOrbitBlend);
            return bandMove.sqrMagnitude > VectorEpsilonSqr ? bandMove.normalized : awayDir;
        }

        private Vector2 CalculateEdgePatrolDirection(Vector2 center)
        {
            Vector2 radial = owner.Position - center;
            if (radial.sqrMagnitude <= VectorEpsilonSqr)
            {
                radial = Random.insideUnitCircle;
            }

            radial.Normalize();
            Vector2 tangent = new Vector2(-radial.y, radial.x) * orbitSign;
            return tangent.sqrMagnitude > VectorEpsilonSqr ? tangent.normalized : Vector2.right;
        }

        private float GetEdgeFactor(Vector2 pos)
        {
            float distLeft = Mathf.Abs(pos.x - settings.arenaMin.x);
            float distRight = Mathf.Abs(settings.arenaMax.x - pos.x);
            float distBottom = Mathf.Abs(pos.y - settings.arenaMin.y);
            float distTop = Mathf.Abs(settings.arenaMax.y - pos.y);
            float minEdgeDistance = Mathf.Min(Mathf.Min(distLeft, distRight), Mathf.Min(distBottom, distTop));

            float edgeRange = Mathf.Max(0.1f, settings.botAvoidanceDistance * 2.2f);
            return Mathf.Clamp01(1f - (minEdgeDistance / edgeRange));
        }

        private Vector2 CalculateArenaAvoidance(Vector2 position)
        {
            Vector2 avoid = Vector2.zero;
            if (position.x < settings.arenaMin.x + settings.botAvoidanceDistance) { avoid.x += 1f; }
            if (position.x > settings.arenaMax.x - settings.botAvoidanceDistance) { avoid.x -= 1f; }
            if (position.y < settings.arenaMin.y + settings.botAvoidanceDistance) { avoid.y += 1f; }
            if (position.y > settings.arenaMax.y - settings.botAvoidanceDistance) { avoid.y -= 1f; }
            return avoid.sqrMagnitude > 1f ? avoid.normalized : avoid;
        }

        private void NormalizeMoveVector()
        {
            if (moveVector.sqrMagnitude > 1f)
            {
                moveVector.Normalize();
            }
        }

    }
}
