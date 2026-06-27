using UnityEngine;

namespace CubeRacing
{
    [CreateAssetMenu(fileName = "RaceConfig", menuName = "CubeRacing/RaceConfig")]
    public class RaceConfig : ScriptableObject
    {
        [Tooltip("Seconds per square when an NPC jumps one step")]
        public float stepDuration = 0.3f;
    }
}
