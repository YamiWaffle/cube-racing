using System;
using System.Linq;
using UnityEngine;

namespace CubeRacing
{
    [Serializable]
    public class NpcEntry
    {
        public int    id;
        public string npcName;
        public Color  color;
    }

    [CreateAssetMenu(fileName = "NpcConfig", menuName = "CubeRacing/NpcConfig")]
    public class NpcConfig : ScriptableObject
    {
        public NpcEntry[] npcs = new[]
        {
            new NpcEntry { id = 1, npcName = "Red Cube",    color = new Color(0.898f, 0.243f, 0.243f) },
            new NpcEntry { id = 2, npcName = "Blue Cube",   color = new Color(0.192f, 0.506f, 0.808f) },
            new NpcEntry { id = 3, npcName = "Yellow Cube", color = new Color(0.839f, 0.620f, 0.180f) },
            new NpcEntry { id = 4, npcName = "Green Cube",  color = new Color(0.220f, 0.631f, 0.412f) },
        };

        public NpcEntry GetById(int id) => npcs.FirstOrDefault(n => n.id == id);
    }
}
