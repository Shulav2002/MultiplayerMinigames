using System;

namespace WorldConqueror
{
    // Match arithmetic kept independent of Unity's frame loop for deterministic checks.
    public static class WorldConquerorRules
    {
        public readonly struct ArrivalResult
        {
            public ArrivalResult(int ownerId, float defense, bool captured)
            {
                OwnerId = ownerId;
                Defense = defense;
                Captured = captured;
            }
            public int OwnerId { get; }
            public float Defense { get; }
            public bool Captured { get; }
        }

        public static int AvailableTroops(float garrison) =>
            Math.Max(0, (int)Math.Floor(garrison + 0.0001f));

        public static int SendCount(float garrison, int percent)
        {
            if (percent != 25 && percent != 50 && percent != 100) return 0;
            return (int)Math.Floor(AvailableTroops(garrison) * percent / 100f);
        }

        public static float RegenerateNeutral(float defense, float elapsed, float originalStrength) =>
            Math.Min(originalStrength, defense + Math.Max(0f, elapsed));

        public static int Produce(ref float carry, float rate, float elapsed)
        {
            carry += Math.Max(0f, rate) * Math.Max(0f, elapsed);
            int produced = (int)Math.Floor(carry + 0.000001f);
            carry -= produced;
            return produced;
        }

        public static ArrivalResult ResolveArrival(int ownerId, float defense, int attackerId)
        {
            if (ownerId == attackerId)
                return new ArrivalResult(ownerId, defense + 1f, false);
            if (defense > 0f)
                return new ArrivalResult(ownerId, Math.Max(0f, defense - 1f), false);
            return new ArrivalResult(attackerId, 1f, true);
        }

        public static float ArrivalTime(float orderTime, int troopIndex, float spacing, float distance, float speed) =>
            orderTime + troopIndex * Math.Max(0f, spacing) + distance / Math.Max(0.1f, speed);
    }
}
