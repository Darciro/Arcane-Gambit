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
        public void Palisade_NextToOwnTroops_BlocksMovement_ForTwoRounds()
        {
            var m = Empty();
            var infantry = m.Place(Side.White, UnitType.Infantry, C("d3"));
            m.Place(Side.Black, UnitType.Infantry, C("e6"));
            m.EndTurn();
            m.CurrentPlayer.Hand.Add(CardId.ImprovisedPalisade);

            Assert.That(m.PlayCard(CardId.ImprovisedPalisade, new[] { C("d4") }), Is.False, "not next to Black troops");
            Assert.That(m.PlayCard(CardId.ImprovisedPalisade, new[] { C("d5") }), Is.True);
            m.EndTurn();
            Assert.That(m.Move(infantry, C("d4")), Is.True);
            Assert.That(m.MoveTargets(infantry), Is.Empty, "palisade blocks d5");
            m.EndTurn();
            m.EndTurn();
            Assert.That(m.HasMarker(C("d5"), MarkerKind.Palisade), Is.True);
            m.EndTurn();
            Assert.That(m.HasMarker(C("d5"), MarkerKind.Palisade), Is.False);
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

        [Test]
        public void Catalog_CoversEveryCard_AndBothDecksUseThemAll()
        {
            var used = new HashSet<CardId>(CardCatalog.Vanguard());
            used.UnionWith(CardCatalog.Bastion());
            foreach (CardId id in System.Enum.GetValues(typeof(CardId)))
            {
                Assert.That(CardCatalog.Get(id).Id, Is.EqualTo(id));
                Assert.That(used, Does.Contain(id), id.ToString());
            }
            Assert.That(CardCatalog.Vanguard().Count, Is.EqualTo(24));
            Assert.That(CardCatalog.Bastion().Count, Is.EqualTo(24));
        }

        [Test]
        public void BracedSpears_IsFree_WithAnotherInfantryAlongside()
        {
            var m = Empty();
            var cavalry = m.Place(Side.White, UnitType.Cavalry, C("f3"));
            m.Place(Side.Black, UnitType.Infantry, C("e5"));
            m.Place(Side.Black, UnitType.Infantry, C("d5"));
            m.Get(Side.Black).Hand.Add(CardId.BracedSpears);

            m.DeclareCapture(cavalry, C("e5"));
            Assert.That(m.ReactionCost(CardId.BracedSpears), Is.EqualTo(0));
            Assert.That(m.React(CardId.BracedSpears), Is.True);
            Assert.That(m.Get(Side.Black).Morale, Is.EqualTo(Match.MaxMorale));
        }

        [Test]
        public void CloseRanks_BlocksCapture_AndLocksMovementBonus()
        {
            var m = Empty();
            var cavalry = m.Place(Side.White, UnitType.Cavalry, C("f3"));
            var infantry = m.Place(Side.Black, UnitType.Infantry, C("e5"));
            var partner = m.Place(Side.Black, UnitType.Infantry, C("d5"));
            m.Get(Side.Black).Hand.Add(CardId.CloseRanks);

            m.DeclareCapture(cavalry, C("e5"));
            Assert.That(m.React(CardId.CloseRanks), Is.True);
            Assert.That(m.At(C("e5")), Is.SameAs(infantry));

            m.EndTurn();
            m.CurrentPlayer.Hand.Add(CardId.ForcedMarch);
            Assert.That(m.PlayCard(CardId.ForcedMarch, new Coord[0]), Is.True);
            Assert.That(m.Move(partner, C("d4")), Is.True);
            Assert.That(m.CurrentPlayer.BonusMoves, Is.EqualTo(1), "the locked unit spends an Order instead");
            Assert.That(m.CurrentPlayer.Orders, Is.EqualTo(1));
        }

        [Test]
        public void Countercharge_CavalryJumpsAway_AttackerStays()
        {
            var m = Empty();
            var marshal = m.Place(Side.White, UnitType.Marshal, C("d4"));
            var cavalry = m.Place(Side.Black, UnitType.Cavalry, C("e5"));
            m.Get(Side.Black).Hand.Add(CardId.Countercharge);

            m.DeclareCapture(marshal, C("e5"));
            Assert.That(m.React(CardId.Countercharge), Is.True);
            int dx = System.Math.Abs(cavalry.Pos.X - 4), dy = System.Math.Abs(cavalry.Pos.Y - 4);
            Assert.That(dx * dy, Is.EqualTo(2), "an L-jump from e5");
            Assert.That(marshal.Pos, Is.EqualTo(C("d4")));
            Assert.That(m.Units.Count, Is.EqualTo(2));
        }

        [Test]
        public void SmokeScreen_CancelsShot_AndHidesTheSquareUntilRoundEnd()
        {
            var m = Empty();
            m.Place(Side.White, UnitType.Crossbowmen, C("c4"));
            var target = m.Place(Side.Black, UnitType.Cavalry, C("e6"));
            m.CurrentPlayer.Hand.Add(CardId.CrossbowVolley);
            m.Get(Side.Black).Hand.Add(CardId.SmokeScreen);

            Assert.That(m.PlayCard(CardId.CrossbowVolley, new[] { C("c4"), C("e6") }), Is.True);
            Assert.That(m.React(CardId.SmokeScreen), Is.True);
            Assert.That(m.At(C("e6")), Is.SameAs(target));
            Assert.That(m.CardTargets(CardId.CrossbowVolley, new[] { C("c4") }), Is.Empty);
            m.EndTurn();
            m.EndTurn();
            Assert.That(m.HasMarker(C("e6"), MarkerKind.Smoke), Is.False);
        }

        [Test]
        public void HeroicInterposition_InfantryTakesTheCapture()
        {
            var m = Empty();
            var marshal = m.Place(Side.White, UnitType.Marshal, C("d4"));
            var archers = m.Place(Side.Black, UnitType.Crossbowmen, C("e5"));
            var guard = m.Place(Side.Black, UnitType.Infantry, C("f6"));
            m.Get(Side.Black).Hand.Add(CardId.HeroicInterposition);

            m.DeclareCapture(marshal, C("e5"));
            Assert.That(m.React(CardId.HeroicInterposition), Is.True);
            Assert.That(archers.Pos, Is.EqualTo(C("f6")));
            Assert.That(marshal.Pos, Is.EqualTo(C("e5")));
            Assert.That(m.Units, Has.No.Member(guard));
        }

        [Test]
        public void Fireball_CapturesFromAfar_AndIgnoresInterposition()
        {
            var m = Empty();
            var caster = m.Place(Side.White, UnitType.Infantry, C("c3"));
            m.Place(Side.Black, UnitType.Infantry, C("d5"));
            m.Place(Side.Black, UnitType.Infantry, C("e6"));
            m.Place(Side.Black, UnitType.Sovereign, C("c5"));
            m.CurrentPlayer.Hand.Add(CardId.Fireball);
            m.Get(Side.Black).Hand.Add(CardId.HeroicInterposition);

            Assert.That(m.CardTargets(CardId.Fireball, new[] { C("c3") }), Is.EquivalentTo(new[] { C("d5") }));
            Assert.That(m.PlayCard(CardId.Fireball, new[] { C("c3"), C("d5") }), Is.True);
            Assert.That(m.Phase, Is.EqualTo(Phase.Maneuvers), "no reaction applies to an arcane capture");
            Assert.That(m.At(C("d5")), Is.Null);
            Assert.That(caster.Pos, Is.EqualTo(C("c3")));
            Assert.That(m.CurrentPlayer.Orders, Is.EqualTo(Match.OrdersPerTurn));
        }

        [Test]
        public void ReinforcedArmor_StopsTwoShots_ButNotMelee()
        {
            var m = Empty();
            m.Place(Side.White, UnitType.Crossbowmen, C("c4"));
            var marshal = m.Place(Side.White, UnitType.Marshal, C("d6"));
            var engineers = m.Place(Side.Black, UnitType.SiegeEngineers, C("e6"));
            m.EndTurn();
            m.CurrentPlayer.Hand.Add(CardId.ReinforcedArmor);
            Assert.That(m.PlayCard(CardId.ReinforcedArmor, new[] { C("e6") }), Is.True);
            Assert.That(engineers.RangedProtection, Is.EqualTo(2));
            m.EndTurn();

            m.CurrentPlayer.Hand.Add(CardId.CrossbowVolley);
            Assert.That(m.PlayCard(CardId.CrossbowVolley, new[] { C("c4"), C("e6") }), Is.True);
            Assert.That(m.At(C("e6")), Is.SameAs(engineers));
            Assert.That(engineers.RangedProtection, Is.EqualTo(1));
            Assert.That(m.DeclareCapture(marshal, C("e6")), Is.True);
            Assert.That(m.At(C("e6")), Is.SameAs(marshal));
        }

        [Test]
        public void WarHammer_OnlyEquipsCavalryOrMarshal()
        {
            var m = Empty();
            m.Place(Side.White, UnitType.Infantry, C("a2"));
            m.Place(Side.White, UnitType.Cavalry, C("b1"));
            m.Place(Side.White, UnitType.Marshal, C("d1"));
            Assert.That(m.CardTargets(CardId.WarHammer, new Coord[0]), Is.EquivalentTo(new[] { C("b1"), C("d1") }));
        }

        [Test]
        public void CampaignHorseshoes_FreeStepAfterAJump()
        {
            var m = Empty();
            var cavalry = m.Place(Side.White, UnitType.Cavalry, C("b1"));
            cavalry.HasHorseshoes = true;

            Assert.That(m.Move(cavalry, C("c3")), Is.True);
            Assert.That(m.Move(cavalry, C("d4")), Is.True);
            Assert.That(m.CurrentPlayer.Orders, Is.EqualTo(1));
            Assert.That(cavalry.HasHorseshoes, Is.False);
        }

        [Test]
        public void JoustingLance_AdvancesBeforeTheJump()
        {
            var m = Empty();
            var cavalry = m.Place(Side.White, UnitType.Cavalry, C("b1"));
            cavalry.HasLance = true;
            m.Place(Side.Black, UnitType.Infantry, C("d4"));

            Assert.That(m.CaptureTargets(cavalry), Does.Contain(C("d4")));
            Assert.That(m.DeclareCapture(cavalry, C("d4")), Is.True);
            Assert.That(cavalry.Pos, Is.EqualTo(C("d4")));
            Assert.That(cavalry.HasLance, Is.False);
            Assert.That(m.CurrentPlayer.Orders, Is.EqualTo(1));
        }

        [Test]
        public void VigilSeal_StopsALanceChargeOnTheSovereign()
        {
            var m = Empty();
            var cavalry = m.Place(Side.White, UnitType.Cavalry, C("b1"));
            cavalry.HasLance = true;
            m.Place(Side.Black, UnitType.Sovereign, C("d4"));
            m.Get(Side.Black).Hand.Add(CardId.VigilSeal);

            m.DeclareCapture(cavalry, C("d4"));
            Assert.That(m.ReactionOptions(), Does.Contain(CardId.VigilSeal));
            Assert.That(m.React(CardId.VigilSeal), Is.True);
            Assert.That(m.Winner, Is.Null);
            Assert.That(m.At(C("d4")).Type, Is.EqualTo(UnitType.Sovereign));
        }

        [Test]
        public void MarshalsBanner_MakesLineSwapFree()
        {
            var m = Empty();
            var marshal = m.Place(Side.White, UnitType.Marshal, C("d2"));
            marshal.HasBanner = true;
            var a = m.Place(Side.White, UnitType.Infantry, C("d3"));
            var b = m.Place(Side.White, UnitType.Infantry, C("e3"));
            m.CurrentPlayer.Morale = 0;
            m.CurrentPlayer.Hand.Add(CardId.LineSwap);

            Assert.That(m.CardCost(CardId.LineSwap, new[] { C("d3") }), Is.EqualTo(0));
            Assert.That(m.PlayCard(CardId.LineSwap, new[] { C("d3"), C("e3") }), Is.True);
            Assert.That(a.Pos, Is.EqualTo(C("e3")));
            Assert.That(b.Pos, Is.EqualTo(C("d3")));
            Assert.That(m.CurrentPlayer.Orders, Is.EqualTo(1));
        }

        [Test]
        public void CavalryFeint_FirstReactionCostsOneMore()
        {
            var m = Empty();
            var cavalry = m.Place(Side.White, UnitType.Cavalry, C("f3"));
            m.Place(Side.Black, UnitType.Infantry, C("e5"));
            m.CurrentPlayer.Hand.Add(CardId.CavalryFeint);
            m.Get(Side.Black).Hand.Add(CardId.RaiseShields);

            Assert.That(m.PlayCard(CardId.CavalryFeint, new[] { C("f3") }), Is.True);
            m.DeclareCapture(cavalry, C("e5"));
            Assert.That(m.ReactionCost(CardId.RaiseShields), Is.EqualTo(2));
            m.React(CardId.RaiseShields);
            Assert.That(m.Get(Side.Black).Morale, Is.EqualTo(Match.MaxMorale - 2));
        }

        [Test]
        public void CoordinatedAdvance_MovesTwoInfantry_ForOneOrder()
        {
            var m = Empty();
            var rear = m.Place(Side.White, UnitType.Infantry, C("d2"));
            var front = m.Place(Side.White, UnitType.Infantry, C("d3"));
            m.CurrentPlayer.Hand.Add(CardId.CoordinatedAdvance);

            Assert.That(m.PlayCard(CardId.CoordinatedAdvance, new[] { C("d3"), C("d4"), C("d2"), C("d3") }), Is.True);
            Assert.That(front.Pos, Is.EqualTo(C("d4")));
            Assert.That(rear.Pos, Is.EqualTo(C("d3")));
            Assert.That(m.CurrentPlayer.Orders, Is.EqualTo(1));
        }

        [Test]
        public void SiegeOrder_ShootsInAStraightLine_NotTheSovereign()
        {
            var m = Empty();
            var engineers = m.Place(Side.White, UnitType.SiegeEngineers, C("d2"));
            m.Place(Side.Black, UnitType.Cavalry, C("d5"));
            m.Place(Side.Black, UnitType.Sovereign, C("g2"));
            m.CurrentPlayer.Hand.Add(CardId.SiegeOrder);

            Assert.That(m.CardTargets(CardId.SiegeOrder, new[] { C("d2") }), Is.EquivalentTo(new[] { C("d5") }));
            Assert.That(m.PlayCard(CardId.SiegeOrder, new[] { C("d2"), C("d5") }), Is.True);
            Assert.That(engineers.Pos, Is.EqualTo(C("d2")));
            Assert.That(m.At(C("d5")), Is.Null);
        }

        [Test]
        public void Weather_ShortensAndBlindsRangedAttacks()
        {
            var m = Empty();
            m.Place(Side.White, UnitType.Crossbowmen, C("a1"));
            m.Place(Side.Black, UnitType.Cavalry, C("d4"));
            var volley = new[] { C("a1") };
            Assert.That(m.CardTargets(CardId.CrossbowVolley, volley), Does.Contain(C("d4")));

            m.CurrentPlayer.Hand.Add(CardId.Gale);
            Assert.That(m.PlayCard(CardId.Gale, new Coord[0]), Is.True);
            Assert.That(m.CardTargets(CardId.CrossbowVolley, volley), Is.Empty, "Gale");

            m.EndTurn();
            m.EndTurn();
            m.CurrentPlayer.Hand.Add(CardId.MoonlessNight);
            Assert.That(m.PlayCard(CardId.MoonlessNight, new Coord[0]), Is.True);
            Assert.That(m.Weather, Is.EqualTo(CardId.MoonlessNight));
            Assert.That(m.CardTargets(CardId.CrossbowVolley, volley), Is.Empty, "no observer");
            m.Place(Side.White, UnitType.Infantry, C("c4"));
            Assert.That(m.CardTargets(CardId.CrossbowVolley, volley), Does.Contain(C("d4")), "observer next to the target");
        }

        [Test]
        public void Blizzard_And_Mud_DenyExtraMovement()
        {
            var m = Empty();
            var infantry = m.Place(Side.White, UnitType.Infantry, C("d2"));
            m.CurrentPlayer.Hand.Add(CardId.Blizzard);
            Assert.That(m.PlayCard(CardId.Blizzard, new Coord[0]), Is.True);
            m.EndTurn();
            m.EndTurn();
            m.CurrentPlayer.Hand.Add(CardId.ForcedMarch);
            Assert.That(m.CanPlay(CardId.ForcedMarch), Is.False);

            m.CurrentPlayer.Hand.Add(CardId.Mud);
            Assert.That(m.PlayCard(CardId.Mud, new Coord[0]), Is.True);
            m.EndTurn();
            m.EndTurn();
            m.Move(infantry, C("d3"));
            m.CurrentPlayer.Hand.Add(CardId.ExtraRations);
            Assert.That(m.CanPlay(CardId.ExtraRations), Is.False);
        }

        [Test]
        public void LightningStorm_MarksASquare_ThenStrikesIt()
        {
            var m = Empty();
            m.CurrentPlayer.Hand.Add(CardId.LightningStorm);
            Assert.That(m.PlayCard(CardId.LightningStorm, new Coord[0]), Is.True);
            m.EndTurn();
            m.EndTurn();
            Marker mark = null;
            foreach (var marker in m.Markers) if (marker.Kind == MarkerKind.Lightning) mark = marker;
            Assert.That(mark, Is.Not.Null, "marked at the start of the round");

            var victim = m.Place(Side.Black, UnitType.Infantry, mark.Pos);
            m.EndTurn();
            m.EndTurn();
            Assert.That(m.Units, Has.No.Member(victim));
        }

        [Test]
        public void AntiCavalryStakes_KeepEnemyCavalryOff_UntilNextRoundEnds()
        {
            var m = Empty();
            m.Place(Side.White, UnitType.Infantry, C("d3"));
            var cavalry = m.Place(Side.Black, UnitType.Cavalry, C("d6"));
            m.CurrentPlayer.Hand.Add(CardId.AntiCavalryStakes);
            Assert.That(m.PlayCard(CardId.AntiCavalryStakes, new[] { C("c4"), C("e4") }), Is.True);

            m.EndTurn();
            Assert.That(m.MoveTargets(cavalry), Has.No.Member(C("c4")).And.No.Member(C("e4")));
            m.EndTurn();
            m.EndTurn();
            Assert.That(m.MoveTargets(cavalry), Has.No.Member(C("c4")));
            m.EndTurn();
            m.EndTurn();
            Assert.That(m.HasMarker(C("c4"), MarkerKind.Stakes), Is.False);
        }

        [Test]
        public void ShallowDitch_StopsSliders_ButCavalryJumpsOver()
        {
            var m = Empty();
            m.Place(Side.White, UnitType.SiegeEngineers, C("d2"));
            var marshal = m.Place(Side.White, UnitType.Marshal, C("c3"));
            var cavalry = m.Place(Side.White, UnitType.Cavalry, C("b2"));
            m.CurrentPlayer.Hand.Add(CardId.ShallowDitch);
            Assert.That(m.PlayCard(CardId.ShallowDitch, new[] { C("d3") }), Is.True);

            Assert.That(m.MoveTargets(marshal), Has.No.Member(C("d3")).And.No.Member(C("e3")));
            Assert.That(m.MoveTargets(cavalry), Has.No.Member(C("d3")).And.Member(C("d1")));
        }

        [Test]
        public void ScoutsAhead_KeepsTheChosenCardOnTop()
        {
            var m = new Match(1, new List<CardId>(), new List<CardId>(), standardSetup: false);
            var p = m.CurrentPlayer;
            p.Morale = Match.MaxMorale;
            p.Deck.AddRange(new[] { CardId.RaiseShields, CardId.ForcedMarch, CardId.CrossbowVolley });
            p.Hand.Add(CardId.ScoutsAhead);

            Assert.That(m.PlayCard(CardId.ScoutsAhead, new Coord[0]), Is.True);
            Assert.That(m.ForesightOptions, Is.EqualTo(new[] { CardId.CrossbowVolley, CardId.ForcedMarch }));
            Assert.That(m.ChooseForesight(1), Is.True);
            Assert.That(p.Deck[p.Deck.Count - 1], Is.EqualTo(CardId.ForcedMarch));
            Assert.That(p.Deck[0], Is.EqualTo(CardId.CrossbowVolley));
            Assert.That(p.Hand, Is.Empty);
        }

        [Test]
        public void OracleEcho_ReturnsANonArcaneDiscardToTheDeck()
        {
            var m = Empty();
            var p = m.CurrentPlayer;
            p.Discard.AddRange(new[] { CardId.RaiseShields, CardId.SeersOmen, CardId.ForcedMarch });
            p.Hand.Add(CardId.OracleEcho);

            Assert.That(m.PlayCard(CardId.OracleEcho, new Coord[0]), Is.True);
            Assert.That(m.ForesightOptions, Is.EquivalentTo(new[] { CardId.ForcedMarch, CardId.RaiseShields }));
            Assert.That(m.ChooseForesight(new List<CardId>(m.ForesightOptions).IndexOf(CardId.RaiseShields)), Is.True);
            Assert.That(p.Deck[p.Deck.Count - 1], Is.EqualTo(CardId.RaiseShields));
            Assert.That(p.Discard, Has.No.Member(CardId.RaiseShields));
        }

        [Test]
        public void SupplyWagon_AddsMoraleAtTheNextPreparation()
        {
            var m = Empty();
            m.CurrentPlayer.Morale = 4;
            m.CurrentPlayer.Hand.Add(CardId.SupplyWagon);
            Assert.That(m.PlayCard(CardId.SupplyWagon, new Coord[0]), Is.True);
            m.EndTurn();
            m.EndTurn();
            Assert.That(m.CurrentPlayer.Morale, Is.EqualTo(2 + Match.MoralePerTurn + 1));
        }

        [Test]
        public void DawnPrayer_ClearsTheWeather_OrDrawsACard()
        {
            var m = Empty();
            m.CurrentPlayer.Hand.Add(CardId.Fog);
            m.PlayCard(CardId.Fog, new Coord[0]);
            m.EndTurn();
            m.CurrentPlayer.Hand.Add(CardId.DawnPrayer);
            Assert.That(m.PlayCard(CardId.DawnPrayer, new Coord[0]), Is.True);
            Assert.That(m.Weather, Is.Null);

            m.EndTurn();
            m.EndTurn();
            m.CurrentPlayer.Deck.Add(CardId.RaiseShields);
            m.CurrentPlayer.Hand.Add(CardId.DawnPrayer);
            Assert.That(m.PlayCard(CardId.DawnPrayer, new Coord[0]), Is.True);
            Assert.That(m.CurrentPlayer.Hand, Does.Contain(CardId.RaiseShields));
        }

        [Test]
        public void Ai_PlaysWholeMatchesWithTheFullArsenal()
        {
            for (int seed = 1; seed <= 2; seed++)
            {
                var m = Match.CreateDefault(seed);
                var white = new Ai(Side.White, seed);
                var black = new Ai(Side.Black, seed + 100);
                for (int i = 0; i < 200 && m.Phase != Phase.GameOver; i++)
                {
                    var ai = white.HasDecision(m) ? white : black;
                    Assert.That(ai.Act(m), Is.True);
                }
            }
        }
    }
}
