using System.Collections.Generic;

namespace CubeRacing
{
    public readonly struct OddsUpdatedMessage
    {
        public readonly List<NpcOddsDto> Odds;
        public OddsUpdatedMessage(List<NpcOddsDto> odds) => Odds = odds;
    }

    public readonly struct BettingEndedMessage { }

    public readonly struct RoundExecutedMessage
    {
        public readonly RoundExecutedPayload Payload;
        public RoundExecutedMessage(RoundExecutedPayload payload) => Payload = payload;
    }

    public readonly struct RaceCompletedMessage
    {
        public readonly int WinnerNpcId;
        public RaceCompletedMessage(int winnerNpcId) => WinnerNpcId = winnerNpcId;
    }

    public readonly struct SettlementDoneMessage
    {
        public readonly SettlementDonePayload Payload;
        public SettlementDoneMessage(SettlementDonePayload payload) => Payload = payload;
    }
}
