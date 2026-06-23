// STUB — full implementation in Task 9.
// This file exists only so LobbyPresenter compiles before Task 9 is complete.
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace CubeRacing
{
    public class LeaderboardPresenter : MonoBehaviour
    {
        /// <summary>Show the leaderboard panel.</summary>
        public UniTask Show(CancellationToken ct) => UniTask.CompletedTask;
    }
}
