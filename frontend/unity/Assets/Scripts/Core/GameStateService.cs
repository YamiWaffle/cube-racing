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
        public ReactiveProperty<DateTime?>     RaceStartsAt     { get; } = new(null);
        public ReactiveProperty<DateTime?>     BettingStartsAt  { get; } = new(null);
        public ReactiveProperty<int?>          BetNpcId         { get; } = new(null);
        public ReactiveProperty<int?>          BetAmount        { get; } = new(null);
        public Guid CurrentSessionId { get; private set; }
        public int  MapLength        { get; private set; }

        private readonly ISubscriber<OddsUpdatedMessage>     _oddsSubscriber;
        private readonly ISubscriber<BettingEndedMessage>    _bettingEndedSubscriber;
        private readonly ISubscriber<RaceCompletedMessage>   _raceCompletedSubscriber;
        private readonly ISubscriber<SettlementDoneMessage>  _settlementSubscriber;
        private readonly ISubscriber<RaceStartingMessage>    _raceStartingSubscriber;
        private readonly ISubscriber<BettingStartedMessage>   _bettingStartedSubscriber;
        private readonly ISubscriber<WaitingStartedMessage>  _waitingStartedSubscriber;
        private readonly CompositeDisposable _bag = new();

        public GameStateService(
            ISubscriber<OddsUpdatedMessage>    oddsSubscriber,
            ISubscriber<BettingEndedMessage>   bettingEndedSubscriber,
            ISubscriber<RaceCompletedMessage>  raceCompletedSubscriber,
            ISubscriber<SettlementDoneMessage> settlementSubscriber,
            ISubscriber<RaceStartingMessage>   raceStartingSubscriber,
            ISubscriber<BettingStartedMessage>  bettingStartedSubscriber,
            ISubscriber<WaitingStartedMessage>  waitingStartedSubscriber)
        {
            _oddsSubscriber            = oddsSubscriber;
            _bettingEndedSubscriber    = bettingEndedSubscriber;
            _raceCompletedSubscriber   = raceCompletedSubscriber;
            _settlementSubscriber      = settlementSubscriber;
            _raceStartingSubscriber    = raceStartingSubscriber;
            _bettingStartedSubscriber  = bettingStartedSubscriber;
            _waitingStartedSubscriber  = waitingStartedSubscriber;
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

            _raceStartingSubscriber.Subscribe(m =>
                RaceStartsAt.Value = m.RaceStartsAt).AddTo(_bag);

            _waitingStartedSubscriber.Subscribe(m =>
                BettingStartsAt.Value = m.BettingStartsAt).AddTo(_bag);

            _bettingStartedSubscriber.Subscribe(_ =>
            {
                RaceStartsAt.Value    = null;
                BettingStartsAt.Value = null;
                BetNpcId.Value        = null;
                BetAmount.Value       = null;
            }).AddTo(_bag);
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
            BettingStartsAt.Value  = session.bettingStartsAt;
            RaceStartsAt.Value     = session.raceStartsAt;
            Status.Value           = session.status;
        }

        public void SetBet(int npcId, int amount)
        {
            BetAmount.Value = amount;
            BetNpcId.Value  = npcId;
        }

        public void Dispose() => _bag.Dispose();
    }
}
