using System;
using System.Collections.Generic;

namespace CubeRacing
{
    // --- REST response types ---
    [Serializable]
    public class CreatePlayerResponse
    {
        public Guid playerId;
        public Guid token;
        public int chipsBalance;
    }

    [Serializable]
    public class NpcOddsDto
    {
        public int npcId;
        public double odds;
    }

    [Serializable]
    public class CurrentSessionResponse
    {
        public Guid sessionId;
        public string status;
        public int? bettingSecondsRemaining;
        public List<NpcOddsDto> npcOdds;
        public int mapLength;
        public DateTime? raceStartsAt;
    }

    [Serializable]
    public class LeaderboardEntry
    {
        public string nickname;
        public int correctBets;
        public int totalChipsWon;
    }

    // --- SignalR event payloads ---
    [Serializable]
    public class RoundActionDto
    {
        public int npcId;
        public int diceRoll;
        public int fromSquare;
        public int toSquare;
        public List<int> carriedNpcIds;
    }

    [Serializable]
    public class RoundExecutedPayload
    {
        public Guid sessionId;
        public int roundNumber;
        public List<RoundActionDto> actions;
        public Dictionary<string, List<int>> squareStacks;
        public int? winner;
    }

    [Serializable]
    public class PlayerResultDto
    {
        public Guid playerId;
        public string nickname;
        public int winAmount;
    }

    [Serializable]
    public class SettlementDonePayload
    {
        public int winnerNpcId;
        public List<PlayerResultDto> playerResults;
        public List<LeaderboardEntry> topLeaderboard;
    }
}
