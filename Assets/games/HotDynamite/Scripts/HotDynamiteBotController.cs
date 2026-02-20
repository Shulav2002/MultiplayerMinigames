using UnityEngine;

namespace HotDynamite
{
    public class HotDynamiteBotController : MonoBehaviour
    {
        private const float VectorEpsilonSqr = 0.0001f;
        private const float MinDecisionInterval = 0.01f;

        private const float DefaultBotRandomChaseTargetChance = 0.3f;
        private static readonly Vector2 DefaultBotChaseTargetHoldTime = new Vector2(0.35f, 0.9f);
        private static readonly Vector2 DefaultBotInterceptLookAhead = new Vector2(0.12f, 0.55f);
        private static readonly Vector2 DefaultBotPreferredBombDistance = new Vector2(3.2f, 6.3f);
        private const float DefaultBotOrbitBlend = 0.65f;
        private const float DefaultBotCenterBias = 0.9f;
        private const float DefaultBotCohesionBias = 0.7f;
        private const float DefaultBotEdgePatrolBias = 1.1f;
        private const float DefaultBotStuckSpeedThreshold = 0.12f;
        private const float DefaultBotStuckDirectionRefreshTime = 0.45f;
        private const float DefaultBotSteeringSmoothness = 12f;
        private const float DefaultBotOrbitFlipChance = 0.008f;

        private HotDynamiteGameManager manager;
        private HotDynamiteGameSettings settings;
        private HotDynamitePlayer owner;
        private float nextDecisionTime;
        private float nextTargetSelectionTime;
        private float nextUnstuckTime;
        private Vector2 moveVector;
        private Vector2 desiredMoveVector;
        private HotDynamitePlayer chaseTarget;
        private float orbitSign = 1f;

        public Vector2 MoveVector => moveVector;

        private float BotRandomChaseTargetChance => settings != null ? settings.BotRandomChaseTargetChance : DefaultBotRandomChaseTargetChance;
        private Vector2 BotChaseTargetHoldTime => settings != null ? settings.BotChaseTargetHoldTime : DefaultBotChaseTargetHoldTime;
        private Vector2 BotInterceptLookAhead => settings != null ? settings.BotInterceptLookAhead : DefaultBotInterceptLookAhead;
        private Vector2 BotPreferredBombDistance => settings != null ? settings.BotPreferredBombDistance : DefaultBotPreferredBombDistance;
        private float BotOrbitBlend => settings != null ? settings.BotOrbitBlend : DefaultBotOrbitBlend;
        private float BotCenterBias => settings != null ? settings.BotCenterBias : DefaultBotCenterBias;
        private float BotCohesionBias => settings != null ? settings.BotCohesionBias : DefaultBotCohesionBias;
        private float BotEdgePatrolBias => settings != null ? settings.BotEdgePatrolBias : DefaultBotEdgePatrolBias;
        private float BotStuckSpeedThreshold => settings != null ? settings.BotStuckSpeedThreshold : DefaultBotStuckSpeedThreshold;
        private float BotStuckDirectionRefreshTime => settings != null ? settings.BotStuckDirectionRefreshTime : DefaultBotStuckDirectionRefreshTime;
        private float BotSteeringSmoothness => settings != null ? settings.BotSteeringSmoothness : DefaultBotSteeringSmoothness;
        private float BotOrbitFlipChance => settings != null ? settings.BotOrbitFlipChance : DefaultBotOrbitFlipChance;

        public void Initialize(HotDynamiteGameManager manager, HotDynamiteGameSettings settings, HotDynamitePlayer owner)
        {
            this.manager = manager;
            this.settings = settings;
            this.owner = owner;

            orbitSign = Random.value < 0.5f ? -1f : 1f;
            moveVector = Vector2.zero;
            desiredMoveVector = Vector2.zero;
            chaseTarget = null;
        }

        private void Update()
        {
            if (!CanUpdateBot())
            {
                ResetMotion();
                return;
            }

            float now = Time.time;
            if (now < nextDecisionTime)
            {
                SmoothMoveVector(Time.deltaTime);
                return;
            }

            float bombUrgency = manager.BombUrgency01;
            ScheduleNextDecision(now, bombUrgency);

            desiredMoveVector = owner.HasBomb
                ? BuildBombHolderMove(now, bombUrgency)
                : BuildNonBombMove(now, bombUrgency);

            NormalizeDesiredMoveVector();
            SmoothMoveVector(Time.deltaTime);
        }

        private bool CanUpdateBot()
        {
            return owner != null && manager != null && settings != null && owner.IsAlive;
        }

        private void ResetMotion()
        {
            moveVector = Vector2.zero;
            desiredMoveVector = Vector2.zero;
        }

        private void ScheduleNextDecision(float now, float bombUrgency)
        {
            float decisionInterval = owner.HasBomb
                ? settings.BotDecisionInterval * Mathf.Lerp(0.55f, 0.2f, bombUrgency)
                : settings.BotDecisionInterval * Mathf.Lerp(0.8f, 0.55f, bombUrgency);
            nextDecisionTime = now + Mathf.Max(MinDecisionInterval, decisionInterval);
        }

        private Vector2 BuildBombHolderMove(float now, float bombUrgency)
        {
            Vector2 desired = GetBombHolderDesiredDirection(now, bombUrgency);
            Vector2 avoid = CalculateArenaAvoidance(owner.Position);

            float primaryWeight = Mathf.Lerp(2.4f, 3.8f, bombUrgency);
            float avoidWeight = Mathf.Lerp(settings.BotAvoidanceStrength * 0.45f, settings.BotAvoidanceStrength * 0.25f, bombUrgency);
            return desired * primaryWeight + avoid * avoidWeight;
        }

        private Vector2 GetBombHolderDesiredDirection(float now, float bombUrgency)
        {
            RefreshChaseTarget(now, bombUrgency);
            return chaseTarget != null ? CalculateInterceptDirection(chaseTarget, bombUrgency) : Vector2.zero;
        }

        private void RefreshChaseTarget(float now, float bombUrgency)
        {
            bool needsNewTarget = chaseTarget == null
                || !chaseTarget.IsAlive
                || chaseTarget == owner
                || now >= nextTargetSelectionTime;
            if (!needsNewTarget)
            {
                return;
            }

            float effectiveRandomTargetChance = Mathf.Lerp(
                BotRandomChaseTargetChance,
                BotRandomChaseTargetChance * 0.25f,
                bombUrgency);
            bool pickRandomTarget = Random.value < effectiveRandomTargetChance;
            chaseTarget = pickRandomTarget
                ? manager.GetRandomAlivePlayer(owner)
                : manager.GetClosestAlivePlayer(owner);

            if (chaseTarget == null)
            {
                chaseTarget = manager.GetClosestAlivePlayer(owner);
            }

            float minHold = Mathf.Max(0.05f, BotChaseTargetHoldTime.x);
            float maxHold = Mathf.Max(minHold, BotChaseTargetHoldTime.y);
            float holdScale = Mathf.Lerp(1f, 0.45f, bombUrgency);
            nextTargetSelectionTime = now + Random.Range(minHold, maxHold) * holdScale;
        }

        private Vector2 BuildNonBombMove(float now, float bombUrgency)
        {
            HotDynamitePlayer bombHolder = manager.BombHolder;
            if (bombHolder != null)
            {
                chaseTarget = null;
            }

            Vector2 desired = bombHolder != null
                ? CalculateNonBombDesiredDirection(bombHolder, bombUrgency)
                : Vector2.zero;
            Vector2 avoid = CalculateArenaAvoidance(owner.Position);

            float danger = CalculateBombDanger(bombHolder);
            float urgencyBiasedDanger = Mathf.Clamp01(danger + bombUrgency * 0.35f);
            float clampedWanderStrength = Mathf.Clamp(settings.BotWanderStrength, 0f, 1f);

            float primaryWeight = Mathf.Lerp(1.8f, 3.4f, urgencyBiasedDanger);
            float avoidWeight = Mathf.Lerp(1.0f, 0.5f, urgencyBiasedDanger);
            float wanderWeight = clampedWanderStrength * 0.1f * (1f - urgencyBiasedDanger);

            Vector2 center = (settings.ArenaMin + settings.ArenaMax) * 0.5f;
            Vector2 centerPull = center - owner.Position;
            centerPull = centerPull.sqrMagnitude > VectorEpsilonSqr ? centerPull.normalized : Vector2.zero;

            Vector2 edgePatrol = CalculateEdgePatrolDirection(center);
            float edgeFactor = GetEdgeFactor(owner.Position);
            desired = ConstrainDesiredDirectionAtEdge(desired, center, edgeFactor);

            float centerWeight = BotCenterBias
                * Mathf.Lerp(1.0f, 0.3f, urgencyBiasedDanger)
                * Mathf.Lerp(1.0f, 0.0f, edgeFactor);
            float edgePatrolWeight = BotEdgePatrolBias * Mathf.Lerp(0.5f, 1.9f, edgeFactor);

            Vector2 cohesionPull = Vector2.zero;
            float cohesionWeight = 0f;
            if (manager.TryGetAliveCentroid(owner, includeBombHolder: false, out Vector2 centroid))
            {
                cohesionPull = centroid - owner.Position;
                cohesionPull = cohesionPull.sqrMagnitude > VectorEpsilonSqr ? cohesionPull.normalized : Vector2.zero;
                cohesionWeight = BotCohesionBias
                    * Mathf.Lerp(0.9f, 0.25f, urgencyBiasedDanger)
                    * Mathf.Lerp(1.0f, 0.35f, edgeFactor);
            }

            Vector2 nonBombWander = Random.insideUnitCircle * wanderWeight;
            Vector2 move = desired * primaryWeight
                + avoid * avoidWeight
                + centerPull * centerWeight
                + edgePatrol * edgePatrolWeight
                + cohesionPull * cohesionWeight
                + nonBombWander;

            if (move.sqrMagnitude < 0.04f || owner.EstimatedVelocity.magnitude < BotStuckSpeedThreshold)
            {
                if (now >= nextUnstuckTime)
                {
                    orbitSign *= -1f;
                    nextUnstuckTime = now + Mathf.Max(0.1f, BotStuckDirectionRefreshTime);
                }

                move += edgePatrol * Mathf.Max(0.45f, edgePatrolWeight);
            }

            return move;
        }

        private float CalculateBombDanger(HotDynamitePlayer bombHolder)
        {
            if (bombHolder == null)
            {
                return 0f;
            }

            float safeDistance = Mathf.Max(0.1f, settings.BotAvoidanceDistance * 4f);
            float bombDistance = Vector2.Distance(owner.Position, bombHolder.Position);
            return Mathf.Clamp01(1f - (bombDistance / safeDistance));
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

            if (!manager.HardModeBots)
            {
                return toTarget.normalized;
            }

            float ownerSpeed = Mathf.Max(0.1f, settings.MoveSpeed * settings.BombHolderSpeedMultiplier);
            float targetSpeed = target.EstimatedVelocity.magnitude;
            float travelTime = toTarget.magnitude / Mathf.Max(0.1f, ownerSpeed + targetSpeed * 0.75f);

            float minLookAhead = Mathf.Max(0.02f, BotInterceptLookAhead.x);
            float maxLookAhead = Mathf.Max(minLookAhead, BotInterceptLookAhead.y);
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
            float minDistance = Mathf.Max(0.5f, BotPreferredBombDistance.x);
            float maxDistance = Mathf.Max(minDistance + 0.25f, BotPreferredBombDistance.y);

            if (distance < minDistance)
            {
                return awayDir;
            }

            Vector2 toBombDir = -awayDir;
            Vector2 orbitDir = new Vector2(-toBombDir.y, toBombDir.x) * orbitSign;
            if (Random.value < BotOrbitFlipChance)
            {
                orbitSign *= -1f;
            }

            if (distance > maxDistance)
            {
                return Vector2.Lerp(toBombDir, orbitDir, 0.35f).normalized;
            }

            float bandT = Mathf.InverseLerp(minDistance, maxDistance, distance);
            float adjustedOrbitBlend = Mathf.Lerp(BotOrbitBlend * 0.55f, BotOrbitBlend, bandT);
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

        private float GetEdgeFactor(Vector2 position)
        {
            Vector2 center = (settings.ArenaMin + settings.ArenaMax) * 0.5f;
            float halfWidth = Mathf.Max(0.1f, (settings.ArenaMax.x - settings.ArenaMin.x) * 0.5f);
            float halfHeight = Mathf.Max(0.1f, (settings.ArenaMax.y - settings.ArenaMin.y) * 0.5f);
            float arenaRadius = Mathf.Min(halfWidth, halfHeight);
            float distanceToCenter = Vector2.Distance(position, center);
            float distanceToEdge = Mathf.Max(0f, arenaRadius - distanceToCenter);

            float edgeRange = Mathf.Max(0.1f, settings.BotAvoidanceDistance * 2.2f);
            return Mathf.Clamp01(1f - (distanceToEdge / edgeRange));
        }

        private Vector2 CalculateArenaAvoidance(Vector2 position)
        {
            Vector2 avoid = Vector2.zero;
            if (position.x < settings.ArenaMin.x) { avoid.x += 1f; }
            if (position.x > settings.ArenaMax.x) { avoid.x -= 1f; }
            if (position.y < settings.ArenaMin.y) { avoid.y += 1f; }
            if (position.y > settings.ArenaMax.y) { avoid.y -= 1f; }
            return avoid.sqrMagnitude > 1f ? avoid.normalized : avoid;
        }

        private Vector2 ConstrainDesiredDirectionAtEdge(Vector2 desired, Vector2 center, float edgeFactor)
        {
            if (desired.sqrMagnitude <= VectorEpsilonSqr || edgeFactor <= 0.01f)
            {
                return desired;
            }

            Vector2 radial = owner.Position - center;
            if (radial.sqrMagnitude <= VectorEpsilonSqr)
            {
                return desired;
            }

            radial.Normalize();
            Vector2 tangent = new Vector2(-radial.y, radial.x) * orbitSign;
            float edgeBlend = Mathf.Clamp01(Mathf.InverseLerp(0.45f, 1f, edgeFactor));
            Vector2 blended = Vector2.Lerp(desired, tangent, edgeBlend);

            float outward = Vector2.Dot(blended, radial);
            if (outward > 0f)
            {
                blended -= radial * outward;
            }

            if (blended.sqrMagnitude <= VectorEpsilonSqr)
            {
                blended = tangent;
            }

            return blended.normalized;
        }

        private void NormalizeMoveVector()
        {
            if (moveVector.sqrMagnitude > 1f)
            {
                moveVector.Normalize();
            }
        }

        private void NormalizeDesiredMoveVector()
        {
            if (desiredMoveVector.sqrMagnitude > 1f)
            {
                desiredMoveVector.Normalize();
            }
        }

        private void SmoothMoveVector(float deltaTime)
        {
            float smooth = Mathf.Max(0f, BotSteeringSmoothness);
            if (smooth <= 0f)
            {
                moveVector = desiredMoveVector;
                return;
            }

            float interpolation = 1f - Mathf.Exp(-smooth * Mathf.Max(0f, deltaTime));
            moveVector = Vector2.Lerp(moveVector, desiredMoveVector, interpolation);
            NormalizeMoveVector();
        }
    }
}
