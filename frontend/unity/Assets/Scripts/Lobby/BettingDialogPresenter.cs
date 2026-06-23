// STUB — full implementation in Task 8.
// This file exists only so LobbyPresenter compiles before Task 8 is complete.
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace CubeRacing
{
    public class BettingDialogPresenter : MonoBehaviour
    {
        /// <summary>Show the betting dialog for the given NPC.</summary>
        public UniTask Show(int npcId, CancellationToken ct) => UniTask.CompletedTask;
    }
}
