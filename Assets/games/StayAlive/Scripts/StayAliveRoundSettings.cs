using UnityEngine;

namespace StayAlive
{
    [CreateAssetMenu(menuName = "StayAlive/Settings/Round Settings", fileName = "StayAliveRoundSettings")]
    public class StayAliveRoundSettings : ScriptableObject
    {
        [Min(0.1f)] public float spinDurationSeconds = 4f;
        [Min(0.02f)] public float symbolSwapIntervalSeconds = 0.04f;
        [Min(0.1f)] public float warningDurationSeconds = 4f;
        [Min(0.1f)] public float explosionDurationSeconds = 0.5f;
        [Min(0.1f)] public float collapseDurationSeconds = 0.5f;
        [Min(0f)] public float refillDelaySeconds = 0.25f;
        [Min(0.1f)] public float refillDurationSeconds = 0.35f;
        [Range(0f, 1f)] public float difficulty01 = 0.5f;
        [Min(1)] public int easyBombCellsPerRound = 16;
        [Min(1)] public int normalBombCellsPerRound = 22;
        [Min(1)] public int hardBombCellsPerRound = 30;
        [Min(0)] public int easyBombIncreasePerSpin = 5;
        [Min(0)] public int normalBombIncreasePerSpin = 7;
        [Min(0)] public int hardBombIncreasePerSpin = 10;
        public bool autoStartSpinOnPlay = false;

#if UNITY_EDITOR
        private void OnValidate()
        {
            spinDurationSeconds = Mathf.Max(0.1f, spinDurationSeconds);
            symbolSwapIntervalSeconds = Mathf.Max(0.02f, symbolSwapIntervalSeconds);
            warningDurationSeconds = Mathf.Max(0.1f, warningDurationSeconds);
            explosionDurationSeconds = Mathf.Max(0.1f, explosionDurationSeconds);
            collapseDurationSeconds = Mathf.Max(0.1f, collapseDurationSeconds);
            refillDelaySeconds = Mathf.Max(0f, refillDelaySeconds);
            refillDurationSeconds = Mathf.Max(0.1f, refillDurationSeconds);
            easyBombCellsPerRound = Mathf.Max(1, easyBombCellsPerRound);
            normalBombCellsPerRound = Mathf.Max(easyBombCellsPerRound, normalBombCellsPerRound);
            hardBombCellsPerRound = Mathf.Max(normalBombCellsPerRound, hardBombCellsPerRound);
            easyBombIncreasePerSpin = Mathf.Max(0, easyBombIncreasePerSpin);
            normalBombIncreasePerSpin = Mathf.Max(easyBombIncreasePerSpin, normalBombIncreasePerSpin);
            hardBombIncreasePerSpin = Mathf.Max(normalBombIncreasePerSpin, hardBombIncreasePerSpin);
            StayAliveGameSettings.NotifySplitSettingsChanged(this);
        }
#endif
    }
}
