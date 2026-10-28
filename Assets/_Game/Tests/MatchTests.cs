using System.Collections.Generic;
using ArcaneGambit.Rules;
using NUnit.Framework;

namespace ArcaneGambit.Tests
{
    public sealed class MatchTests
    {
        static Coord C(string s) => Coord.Parse(s);

        /// <summary>Empty board, empty hands, full Morale: each test sets up only what it needs.</summary>
        static Match Empty()
        {
            var m = new Match(1, new List<CardId>(), new List<CardId>(), standardSetup: false);
            m.Get(Side.White).Morale = Match.MaxMorale;
            m.Get(Side.Black).Morale = Match.MaxMorale;
            return m;
        }

        [Test]
        public void StandardSetup_FollowsGdd()
        {
            var m = Match.CreateDefault(7);
            Assert.That(m.At(C("e1")).Type, Is.EqualTo(UnitType.Sovereign));
            Assert.That(m.At(C("d2")).Type, Is.EqualTo(UnitType.Infantry));
            Assert.That(m.At(C("f7")).Side, Is.EqualTo(Side.Black));
            Assert.That(m.Current, Is.EqualTo(Side.White));
            Assert.That(m.CurrentPlayer.Morale, Is.EqualTo(2));
            Assert.That(m.CurrentPlayer.Orders, Is.EqualTo(Match.OrdersPerTurn));
            Assert.That(m.CurrentPlayer.Hand.Count, Is.EqualTo(Match.StartingHand + 1));
            Assert.That(m.Get(Side.Black).Hand.Count, Is.EqualTo(Match.StartingHand));
        }

        [Test]
        public void Morale_IsCappedAtMaximum()
        {
            var m = Empty();
            m.EndTurn();
            m.EndTurn();
            Assert.That(m.Get(Side.White).Morale, Is.EqualTo(Match.MaxMorale));
        }

        [Test]
        public void Infantry_MovesForward_CapturesDiagonally()
        {
            var m = Empty();
            var infantry = m.Place(Side.White, UnitType.Infantry, C("d4"));
            m.Place(Side.Black, UnitType.Infantry, C("d5"));
            m.Place(Side.Black, UnitType.Cavalry, C("e5"));

            Assert.That(m.MoveTargets(infantry), Is.Empty, "blocked ahead");
            Assert.That(m.CaptureTargets(infantry), Is.EquivalentTo(new[] { C("e5") }));
        }

        [Test]
        public void Orders_LimitActionsPerTurn()
        {
            var m = Empty();
            var sovereign = m.Place(Side.White, UnitType.Sovereign, C("e1"));
            Assert.That(m.Move(sovereign, C("e2")), Is.True);
            Assert.That(m.Move(sovereign, C("e3")), Is.True);
            Assert.That(m.Move(sovereign, C("e4")), Is.False);
        }

        [Test]
        public void Recruit_CostsMoraleAndOrder_AndUnitWaitsOneTurn()
        {
            var m = Empty();
            Assert.That(m.Recruit(UnitType.Cavalry, C("b4")), Is.False, "outside the deployment zone");
            Assert.That(m.Recruit(UnitType.Cavalry, C("b1")), Is.True);
            var cavalry = m.At(C("b1"));
            Assert.That(m.CurrentPlayer.Morale, Is.EqualTo(Match.MaxMorale - 3));
            Assert.That(m.CurrentPlayer.Orders, Is.EqualTo(1));
            Assert.That(m.MoveTargets(cavalry), Is.Empty);
        }

        [Test]
        public void Capture_WithoutReaction_RemovesDefenderAndAdvances()
        {
            var m = Empty();
            var cavalry = m.Place(Side.White, UnitType.Cavalry, C("f3"));
            m.Place(Side.Black, UnitType.Infantry, C("e5"));

            Assert.That(m.DeclareCapture(cavalry, C("e5")), Is.True);
            Assert.That(m.Phase, Is.EqualTo(Phase.Maneuvers));
            Assert.That(cavalry.Pos, Is.EqualTo(C("e5")));
            Assert.That(m.Units.Count, Is.EqualTo(1));
        }

        [Test]
        public void RaiseShields_BlocksCapture_AndCostsMorale()
        {
            var m = Empty();
            var cavalry = m.Place(Side.White, UnitType.Cavalry, C("f3"));
            var infantry = m.Place(Side.Black, UnitType.Infantry, C("e5"));
            m.Get(Side.Black).Hand.Add(CardId.RaiseShields);

            m.DeclareCapture(cavalry, C("e5"));
            Assert.That(m.Phase, Is.EqualTo(Phase.Reaction));
            Assert.That(m.React(CardId.RaiseShields), Is.True);

            Assert.That(cavalry.Pos, Is.EqualTo(C("f3")));
            Assert.That(infantry.Pos, Is.EqualTo(C("e5")));
            Assert.That(m.Get(Side.Black).Morale, Is.EqualTo(Match.MaxMorale - 1));
            Assert.That(m.CurrentPlayer.Orders, Is.EqualTo(1), "attacker loses the Order");
        }

        [Test]
        public void OrganizedRetreat_DefenderRetreats_AttackerTakesSquare()
        {
            var m = Empty();
            var cavalry = m.Place(Side.White, UnitType.Cavalry, C("f3"));
            var infantry = m.Place(Side.Black, UnitType.Infantry, C("e5"));
            m.Get(Side.Black).Hand.Add(CardId.OrganizedRetreat);

            m.DeclareCapture(cavalry, C("e5"));
            m.React(CardId.OrganizedRetreat);

            Assert.That(infantry.Pos, Is.EqualTo(C("e6")));
            Assert.That(cavalry.Pos, Is.EqualTo(C("e5")));
            Assert.That(m.Units.Count, Is.EqualTo(2));
        }

        [Test]
        public void SovereignsGuard_InfantryIsCapturedInstead()
        {
            var m = Empty();
            var cavalry = m.Place(Side.White, UnitType.Cavalry, C("f6"));
            var sovereign = m.Place(Side.Black, UnitType.Sovereign, C("g8"));
            m.Place(Side.Black, UnitType.Infantry, C("f8"));
            m.Get(Side.Black).Hand.Add(CardId.SovereignsGuard);

            m.DeclareCapture(cavalry, C("g8"));
            m.React(CardId.SovereignsGuard);

            Assert.That(sovereign.Pos, Is.EqualTo(C("f8")));
            Assert.That(cavalry.Pos, Is.EqualTo(C("g8")));
            Assert.That(m.Winner, Is.Null);
            Assert.That(m.Units.Count, Is.EqualTo(2));
        }

        [Test]
        public void Protection_BlocksOnce_HammerBreaksIt()
        {
            var m = Empty();
            var engineers = m.Place(Side.White, UnitType.SiegeEngineers, C("d4"));
            var target = m.Place(Side.Black, UnitType.SiegeEngineers, C("d5"));
            target.Protection = 1;

            m.DeclareCapture(engineers, C("d5"));
            Assert.That(target.Protection, Is.EqualTo(0));
            Assert.That(m.At(C("d5")), Is.SameAs(target), "first attempt is blocked");

            target.Protection = 1;
            engineers.HasHammer = true;
            m.DeclareCapture(engineers, C("d5"));
            Assert.That(m.At(C("d5")), Is.SameAs(engineers));
            Assert.That(engineers.HasHammer, Is.False);
        }

        [Test]
        public void CrossbowVolley_RangedCapture_DoesNotMoveArcher_NorHitSovereign()
        {
            var m = Empty();
            var crossbowmen = m.Place(Side.White, UnitType.Crossbowmen, C("c4"));
            m.Place(Side.Black, UnitType.Cavalry, C("f7"));
            m.Place(Side.Black, UnitType.Sovereign, C("a6"));
            m.CurrentPlayer.Hand.Add(CardId.CrossbowVolley);

            var targets = m.CardTargets(CardId.CrossbowVolley, new[] { C("c4") });
            Assert.That(targets, Is.EquivalentTo(new[] { C("f7") }));

            Assert.That(m.PlayCard(CardId.CrossbowVolley, new[] { C("c4"), C("f7") }), Is.True);
            Assert.That(crossbowmen.Pos, Is.EqualTo(C("c4")));
            Assert.That(m.At(C("f7")), Is.Null);
            Assert.That(m.CurrentPlayer.Orders, Is.EqualTo(1));
        }

        [Test]
        public void OnlyOneProactiveCardPerTurn()
        {
            var m = Empty();
            m.Place(Side.White, UnitType.Infantry, C("d2"));
            m.CurrentPlayer.Hand.Add(CardId.ForcedMarch);
            m.CurrentPlayer.Hand.Add(CardId.ReinforcedShield);

            Assert.That(m.PlayCard(CardId.ForcedMarch, new Coord[0]), Is.True);
            Assert.That(m.CanPlay(CardId.ReinforcedShield), Is.False);
            Assert.That(m.CurrentPlayer.BonusMoves, Is.EqualTo(1));
        }

        [Test]
        public void Palisade_BlocksMovement_AndExpires()
        {
            var m = Empty();
            var infantry = m.Place(Side.White, UnitType.Infantry, C("d3"));
            m.Place(Side.Black, UnitType.Sovereign, C("h8"));
            m.EndTurn();
            m.CurrentPlayer.Hand.Add(CardId.ImprovisedPalisade);

            // Black may only build on its own half (ranks 5-8).
            Assert.That(m.PlayCard(CardId.ImprovisedPalisade, new[] { C("d4") }), Is.False);
            Assert.That(m.PlayCard(CardId.ImprovisedPalisade, new[] { C("d5") }), Is.True);
            m.EndTurn();
            Assert.That(m.Move(infantry, C("d4")), Is.True);
            Assert.That(m.CurrentPlayer.Orders, Is.EqualTo(1));
            Assert.That(m.MoveTargets(infantry), Is.Empty, "palisade blocks d5");
            m.EndTurn(); // start of Black's turn: the palisade falls
            Assert.That(m.HasPalisade(C("d5")), Is.False);
        }

        [Test]
        public void CapturingSovereign_EndsTheGame()
        {
            var m = Empty();
            var marshal = m.Place(Side.White, UnitType.Marshal, C("e6"));
            m.Place(Side.Black, UnitType.Sovereign, C("e8"));

            Assert.That(m.DeclareCapture(marshal, C("e8")), Is.True);
            Assert.That(m.Winner, Is.EqualTo(Side.White));
            Assert.That(m.Phase, Is.EqualTo(Phase.GameOver));
            Assert.That(m.EndTurn(), Is.False);
        }

        [Test]
        public void SeersOmen_DrawsChosenCard_RestGoToBottom()
        {
            var m = new Match(1, new List<CardId>(), new List<CardId>(), standardSetup: false);
            var p = m.CurrentPlayer;
            p.Deck.AddRange(new[] { CardId.RaiseShields, CardId.ForcedMarch, CardId.CrossbowVolley });
            p.Hand.Add(CardId.SeersOmen);
            p.Morale = Match.MaxMorale;

            Assert.That(m.PlayCard(CardId.SeersOmen, new Coord[0]), Is.True);
            Assert.That(m.Phase, Is.EqualTo(Phase.Foresight));
            Assert.That(m.ForesightOptions.Count, Is.EqualTo(3));
            var chosen = m.ForesightOptions[1];
            Assert.That(m.ChooseForesight(1), Is.True);
            Assert.That(p.Hand, Does.Contain(chosen));
            Assert.That(p.Deck.Count, Is.EqualTo(2));
        }

        [Test]
        public void Log_IsStructured_WithoutDisplayText()
        {
            var m = Empty();
            var sovereign = m.Place(Side.White, UnitType.Sovereign, C("e1"));
            m.Move(sovereign, C("e2"));
            var last = m.Log[m.Log.Count - 1];
            Assert.That(last.Kind, Is.EqualTo(LogKind.Moved));
            Assert.That(last.Unit, Is.EqualTo(UnitType.Sovereign));
            Assert.That(last.From, Is.EqualTo(C("e1")));
            Assert.That(last.To, Is.EqualTo(C("e2")));
        }

        [Test]
        public void Clone_IsIndependentCopy()
        {
            var m = Match.CreateDefault(3);
            var copy = m.Clone();
            var infantry = copy.At(C("d2"));
            Assert.That(copy.Move(infantry, C("d3")), Is.True);
            Assert.That(m.At(C("d2")), Is.Not.Null, "original untouched");
            Assert.That(m.CurrentPlayer.Orders, Is.EqualTo(Match.OrdersPerTurn));
            Assert.That(copy.CurrentPlayer.Hand, Is.EquivalentTo(m.CurrentPlayer.Hand));
        }

        [Test]
        public void ThreatSquares_IncludeEmptyAndFriendlySquares()
        {
            var m = Empty();
            var infantry = m.Place(Side.White, UnitType.Infantry, C("d4"));
            m.Place(Side.White, UnitType.Cavalry, C("e5"));
            Assert.That(m.ThreatSquares(infantry, infantry.Pos), Is.EquivalentTo(new[] { C("c5"), C("e5") }));
            Assert.That(m.ThreatSquares(infantry, C("d5")), Is.EquivalentTo(new[] { C("c6"), C("e6") }));
        }

        [Test]
        public void Ai_CapturesTheSovereignWhenItCan()
        {
            var m = Empty();
            m.Place(Side.White, UnitType.Sovereign, C("a1"));
            var marshal = m.Place(Side.White, UnitType.Marshal, C("e6"));
            m.Place(Side.Black, UnitType.Sovereign, C("e8"));
            m.Place(Side.Black, UnitType.Cavalry, C("h8"));

            var ai = new Ai(Side.White, 1);
            while (ai.HasDecision(m)) ai.Act(m);
            Assert.That(m.Winner, Is.EqualTo(Side.White));
            Assert.That(marshal.Pos, Is.EqualTo(C("e8")));
        }

        [Test]
        public void Ai_ReactsToSaveItsSovereign()
        {
            var m = Empty();
            m.Place(Side.White, UnitType.Sovereign, C("a1"));
            var marshal = m.Place(Side.White, UnitType.Marshal, C("e6"));
            m.Place(Side.Black, UnitType.Sovereign, C("e8"));
            m.Place(Side.Black, UnitType.Infantry, C("d8"));
            m.Get(Side.Black).Hand.Add(CardId.SovereignsGuard);

            Assert.That(m.DeclareCapture(marshal, C("e8")), Is.True);
            var ai = new Ai(Side.Black, 1);
            Assert.That(ai.HasDecision(m), Is.True);
            Assert.That(ai.Act(m), Is.True);
            Assert.That(m.Winner, Is.Null);
            Assert.That(m.At(C("d8")).Type, Is.EqualTo(UnitType.Sovereign));
        }

        [Test]
        public void Ai_FinishesItsTurnAndHandsOver()
        {
            var m = Match.CreateDefault(11);
            var ai = new Ai(Side.White, 11);
            for (int i = 0; i < 50 && ai.HasDecision(m); i++) Assert.That(ai.Act(m), Is.True);
            Assert.That(m.Current, Is.EqualTo(Side.Black));
            Assert.That(ai.HasDecision(m), Is.False);
        }
    }
}
