using System;
using UnityEngine;

namespace WorldConqueror
{
    [CreateAssetMenu(
        fileName = "WorldConquerorMergeProfile",
        menuName = "World Conqueror/Merge Profile",
        order = 0)]
    public class WorldConquerorMergeProfile : ScriptableObject
    {
        [SerializeField] private MergeRule[] mergeRules = Array.Empty<MergeRule>();

        public MergeRule[] MergeRules => mergeRules;
    }

    [Serializable]
    public class MergeRule
    {
        public string mergedRegionId;
        public string mergedRegionName;
        public string[] sourceRegionIds = Array.Empty<string>();
    }
}
