using UnityEngine;

namespace StayAlive
{
    public class StayAliveBotController : MonoBehaviour
    {
        private const float VectorEpsilonSqr = 0.0001f;
        private const float TargetArrivalStopDistance = 0.18f;
        private const float TargetArrivalSlowDistance = 0.65f;

        private StayAliveGameManager manager;
        private StayAlivePlayer owner;
        private float nextDecisionAt;
        private float targetRefreshAt;
        private Vector2 moveVector;
        private Vector2 desiredMoveVector;
        private Vector2 currentTarget;

        public Vector2 MoveVector => moveVector;

        public void Initialize(StayAliveGameManager manager, StayAlivePlayer owner)
        {
            this.manager = manager;
            this.owner = owner;
            nextDecisionAt = 0f;
            targetRefreshAt = 0f;
            moveVector = Vector2.zero;
            desiredMoveVector = Vector2.zero;
            currentTarget = owner != null ? owner.Position : Vector2.zero;
        }

        private void Update()
        {
            if (owner == null || manager == null || !owner.IsAlive)
            {
                moveVector = Vector2.zero;
                desiredMoveVector = Vector2.zero;
                return;
            }

            if (!manager.AreBotsAllowedToMove)
            {
                moveVector = Vector2.zero;
                desiredMoveVector = Vector2.zero;
                currentTarget = owner.Position;
                nextDecisionAt = 0f;
                targetRefreshAt = 0f;
                return;
            }

            float now = Time.time;
            if (now >= nextDecisionAt)
            {
                nextDecisionAt = now + Mathf.Max(0.04f, manager.BotDecisionInterval);
                RefreshTarget(now);
                BuildDesiredMove();
            }

            SmoothMoveVector(Time.deltaTime);
        }

        private void RefreshTarget(float now)
        {
            bool needsTarget = now >= targetRefreshAt || (currentTarget - owner.Position).sqrMagnitude < 0.04f;
            if (!needsTarget)
            {
                return;
            }

            if (!manager.TryGetBestSafePoint(owner, out currentTarget))
            {
                currentTarget = manager.GetBoardCenter();
            }

            targetRefreshAt = now + Mathf.Max(0.15f, manager.BotTargetRefreshInterval);
        }

        private void BuildDesiredMove()
        {
            Vector2 toTarget = currentTarget - owner.Position;
            float distanceToTarget = toTarget.magnitude;
            float targetPull = Mathf.InverseLerp(TargetArrivalStopDistance, TargetArrivalSlowDistance, distanceToTarget);
            Vector2 towardTarget = distanceToTarget > VectorEpsilonSqr
                ? toTarget.normalized * targetPull
                : Vector2.zero;

            Vector2 edgeAvoidance = manager.GetEdgeAvoidance(owner.Position);
            Vector2 dangerAvoidance = manager.GetBombAvoidance(owner.Position);

            float dangerWeight = manager.IsExplosionActive ? 2.8f : manager.IsWarningActive ? 2f : 0.7f;
            float edgeWeight = manager.IsExplosionActive ? 1.1f : 0.75f;

            desiredMoveVector = towardTarget
                + (dangerAvoidance * dangerWeight)
                + (edgeAvoidance * edgeWeight);

            if (distanceToTarget <= TargetArrivalStopDistance
                && dangerAvoidance.sqrMagnitude <= 0.01f
                && edgeAvoidance.sqrMagnitude <= 0.01f)
            {
                desiredMoveVector = Vector2.zero;
            }

            if (desiredMoveVector.sqrMagnitude > 1f)
            {
                desiredMoveVector.Normalize();
            }
        }

        private void SmoothMoveVector(float deltaTime)
        {
            float smoothness = Mathf.Max(0f, manager != null ? manager.BotSteeringSmoothness : 8f);
            if (smoothness <= 0f)
            {
                moveVector = desiredMoveVector;
                return;
            }

            float interpolation = 1f - Mathf.Exp(-smoothness * Mathf.Max(0f, deltaTime));
            moveVector = Vector2.Lerp(moveVector, desiredMoveVector, interpolation);
            if (moveVector.sqrMagnitude > 1f)
            {
                moveVector.Normalize();
            }
        }
    }
}
