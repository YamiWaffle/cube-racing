using UnityEngine;

namespace CubeRacing
{
    [CreateAssetMenu(fileName = "RaceConfig", menuName = "CubeRacing/RaceConfig")]
    public class RaceConfig : ScriptableObject
    {
        [Tooltip("Seconds per square when an NPC jumps one step")]
        public float stepDuration = 0.3f;

        [Tooltip("Height of each NPC cube (metres). Used for stacking offset calculations.")]
        public float npcHeight = 0.5f;
    }
}
