using System;
using System.Collections.Generic;
using MessagePipe;
using R3;
using VContainer.Unity;

namespace CubeRacing
{
    public class GameStateService : IDisposable
    {
        public ReactiveProperty<string>        Status           { get; } = new("Waiting");
        public ReactiveProperty<int?>          SecondsRemaining { get; } = new(null);
        public ReactiveProperty<List<NpcOddsDto>> NpcOdds      { get; } = new(new());
        public ReactiveProperty<bool>          HasPlacedBet     { get; } = new(false);
        public ReactiveProperty<int?>          WinnerNpcId      { get; } = new(null);
        public ReactiveProperty<bool>          IsConnected      { get; } = new(false);
        public Guid CurrentSessionId { get; private set; }
        public int  MapLength        { get; private set; }

        private readonly ISubscriber<OddsUpdatedMessage>    _oddsSubscriber;
        private readonly ISubscriber<BettingEndedMessage>   _bettingEndedSubscriber;
        private readonly ISubscriber<RaceCompletedMessage>  _raceCompletedSubscriber;
        private readonly ISubscriber<SettlementDoneMessage> _settlementSubscriber;
        private readonly CompositeDisposable _bag = new();

        public GameStateService(
            ISubscriber<OddsUpdatedMessage>    oddsSubscriber,
            ISubscriber<BettingEndedMessage>   bettingEndedSubscriber,
            ISubscriber<RaceCompletedMessage>  raceCompletedSubscriber,
            ISubscriber<SettlementDoneMessage> settlementSubscriber)
        {
            _oddsSubscriber          = oddsSubscriber;
            _bettingEndedSubscriber  = bettingEndedSubscriber;
            _raceCompletedSubscriber = raceCompletedSubscriber;
            _settlementSubscriber    = settlementSubscriber;
        }

        public void Initialize()
        {
            _oddsSubscriber.Subscribe(m =>
                NpcOdds.Value = m.Odds).AddTo(_bag);

            _bettingEndedSubscriber.Subscribe(_ =>
                Status.Value = "Racing").AddTo(_bag);

            _raceCompletedSubscriber.Subscribe(m => {
                Status.Value      = "Settling";
                WinnerNpcId.Value = m.WinnerNpcId;
            }).AddTo(_bag);

            _settlementSubscriber.Subscribe(_ =>
                Status.Value = "Completed").AddTo(_bag);
        }

        public void ApplySession(CurrentSessionResponse session)
        {
            CurrentSessionId       = session.sessionId;
            MapLength              = session.mapLength;
            // Set all data before Status so that OnStatusChanged subscribers read correct values
            SecondsRemaining.Value = session.bettingSecondsRemaining;
            NpcOdds.Value          = session.npcOdds ?? new();
            HasPlacedBet.Value     = false;
            WinnerNpcId.Value      = null;
            Status.Value           = session.status;
        }

        public void Dispose() => _bag.Dispose();
    }
}
