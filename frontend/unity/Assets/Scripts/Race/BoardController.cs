using System.Collections.Generic;
using UnityEngine;
using VContainer;

namespace CubeRacing
{
    public class BoardController : MonoBehaviour
    {
        [SerializeField] private GameObject _tilePrefab;
        [SerializeField] private GameObject _npcCubePrefab;
        [SerializeField] private float _tileSpacing = 2.2f;
        [SerializeField] private int _mapLength = 20;

        private NpcConfig _npcConfig;
        private RaceConfig _raceConfig;

        // Tile positions indexed 0-19
        private readonly Vector3[] _positions = new Vector3[20];

        public Dictionary<int, NpcCubeController> NpcCubes { get; } = new();

        [Inject]
        public void Construct(NpcConfig npcConfig, RaceConfig raceConfig)
        {
            _npcConfig = npcConfig;
            _raceConfig = raceConfig;
        }

        private void Awake()
        {
            BuildPositions();
            SpawnTiles();
        }

        private void Start()
        {
            SpawnNpcCubes();
        }

        // Snake layout: 5 columns × 4 rows (indices 1-20)
        // Row 0 (z=0): 1→2→3→4→5   (left to right)
        // Row 1 (z=1): 6→7→8→9→10  (right to left)
        // Row 2 (z=2): 11→12→13→14→15 (left to right)
        // Row 3 (z=3): 16→17→18→19→20 (right to left)
        private void BuildPositions()
        {
            float step = _tileSpacing;
            int cols = 5;

            for (int i = 0; i < _mapLength; i++)
            {
                int row = i / cols;
                int col = i % cols;
                float x = (row % 2 == 0) ? col * step : (cols - 1 - col) * step;
                float z = row * step;
                _positions[i] = new Vector3(x, 0f, z);
            }
        }

        public Vector3 GetSquarePosition(int squareIndex)
        {
            int clamped = Mathf.Clamp(squareIndex, 0, _mapLength - 1);
            return _positions[clamped];
        }

        private void SpawnTiles()
        {
            for (int i = 0; i < _mapLength; i++)
            {
                var tile = Instantiate(_tilePrefab, _positions[i], Quaternion.identity, transform);
                tile.name = $"Tile_{(i + 1):D2}";

                // Label tile number
                var label = tile.GetComponentInChildren<TMPro.TMP_Text>();
                if (label != null) label.text = (i + 1).ToString();

                // Color start/finish differently
                var renderer = tile.GetComponent<Renderer>();
                if (renderer != null)
                {
                    if (i == 0)
                        renderer.material.color = new Color(0.4f, 0.8f, 0.4f);  // green = start
                    else if (i == _mapLength - 1)
                        renderer.material.color = new Color(0.9f, 0.7f, 0.2f);  // gold = finish
                    else if (i % 2 == 0)
                        renderer.material.color = new Color(0.85f, 0.85f, 0.85f);
                }
            }
        }

        private void SpawnNpcCubes()
        {
            Vector3 startPos = _positions[0] - new Vector3(_tileSpacing, 0f, 0f);
            int count = _npcConfig.npcs.Length;
            float h = _raceConfig.npcHeight;

            for (int i = 0; i < count; i++)
            {
                var entry  = _npcConfig.npcs[i];
                Vector3 offset = new Vector3(
                    i * (h * 0.5f) - i * (h * 0.25f), 
                    i * h + h,
                    0f);
                var cube = Instantiate(_npcCubePrefab, startPos + offset, Quaternion.identity, transform);
                cube.name = $"Npc_{entry.id}";

                var ctrl = cube.GetComponent<NpcCubeController>();
                ctrl.Initialize(entry.id, entry.color);
                NpcCubes[entry.id] = ctrl;
            }
        }
    }
}
