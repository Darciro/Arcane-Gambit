using System;
using System.Collections.Generic;

namespace ArcaneGambit.Rules
{
    /// <summary>Plain C#, deterministic per seed. Illegal commands return false and have no effect.</summary>
    public sealed class Match
    {
        public const int Size = 8;
        public const int MaxMorale = 8;
        public const int MoralePerTurn = 2;
        public const int OrdersPerTurn = 2;
        public const int StartingHand = 4;
        public const int ShotRange = 3;
        public const int SightRange = 2;
        public const int FireballRange = 2;
        const int FortificationTurns = 4;

        public static readonly UnitType[] Recruitable =
            { UnitType.Infantry, UnitType.Cavalry, UnitType.Crossbowmen, UnitType.SiegeEngineers, UnitType.Marshal };

        static readonly Coord[] s_orthogonal = { new Coord(1, 0), new Coord(-1, 0), new Coord(0, 1), new Coord(0, -1) };
        static readonly Coord[] s_diagonal = { new Coord(1, 1), new Coord(1, -1), new Coord(-1, 1), new Coord(-1, -1) };
        static readonly Coord[] s_all =
        {
            new Coord(1, 0), new Coord(-1, 0), new Coord(0, 1), new Coord(0, -1),
            new Coord(1, 1), new Coord(1, -1), new Coord(-1, 1), new Coord(-1, -1),
        };
        static readonly Coord[] s_knight =
        {
            new Coord(1, 2), new Coord(2, 1), new Coord(2, -1), new Coord(1, -2),
            new Coord(-1, -2), new Coord(-2, -1), new Coord(-2, 1), new Coord(-1, 2),
        };

        readonly Random _rng;
        readonly Player[] _players;
        readonly List<Unit> _units = new List<Unit>();
        readonly List<Marker> _markers = new List<Marker>();
        readonly List<LogEntry> _log = new List<LogEntry>();
        readonly List<MatchEvent> _events = new List<MatchEvent>();
        readonly List<CardId> _foresight = new List<CardId>();
        int _nextUnitId = 1;

        public int Turn { get; private set; }
        /// <summary>A round is a White turn followed by a Black turn.</summary>
        public int Round => (Turn + 1) / 2;
        public Side Current { get; private set; }
        public Phase Phase { get; private set; }
        public Side? Winner { get; private set; }
        public PendingCapture Pending { get; private set; }
        public CardId? Weather { get; private set; }
        public CardId ForesightCard { get; private set; }

        public IReadOnlyList<Unit> Units => _units;
        public IReadOnlyList<Marker> Markers => _markers;
        public IReadOnlyList<LogEntry> Log => _log;
        public IReadOnlyList<CardId> ForesightOptions => _foresight;

        public Player Get(Side side) => _players[(int)side];
        public Player CurrentPlayer => Get(Current);

        public static int Cost(UnitType type) => type switch
        {
            UnitType.Infantry => 1,
            UnitType.Cavalry => 3,
            UnitType.Crossbowmen => 2,
            UnitType.SiegeEngineers => 4,
            UnitType.Marshal => 5,
            _ => 0,
        };

        public static int Distance(Coord a, Coord b) => Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));

        public static Match CreateDefault(int seed) =>
            new Match(seed, CardCatalog.Vanguard(), CardCatalog.Bastion());

        public Match(int seed, IEnumerable<CardId> whiteDeck, IEnumerable<CardId> blackDeck, bool standardSetup = true)
        {
            _rng = new Random(seed);
            _players = new[] { new Player(Side.White), new Player(Side.Black) };
            SetupPlayer(Get(Side.White), whiteDeck);
            SetupPlayer(Get(Side.Black), blackDeck);

            if (standardSetup)
            {
                Place(Side.White, UnitType.Sovereign, Coord.Parse("e1"));
                Place(Side.White, UnitType.Infantry, Coord.Parse("d2"));
                Place(Side.White, UnitType.Infantry, Coord.Parse("f2"));
                Place(Side.Black, UnitType.Sovereign, Coord.Parse("e8"));
                Place(Side.Black, UnitType.Infantry, Coord.Parse("d7"));
                Place(Side.Black, UnitType.Infantry, Coord.Parse("f7"));
            }

            StartTurn(Side.White);
        }

        /// <summary>The copy gets its own RNG; only the initial shuffle draws from it, so play continues identically.</summary>
        public Match Clone() => new Match(this);

        Match(Match source)
        {
            _rng = new Random(source.Turn);
            _players = new[] { CopyPlayer(source._players[0]), CopyPlayer(source._players[1]) };
            foreach (var u in source._units) _units.Add(u.Copy());
            foreach (var m in source._markers)
                _markers.Add(new Marker { Kind = m.Kind, Pos = m.Pos, Owner = m.Owner, ExpiresTurn = m.ExpiresTurn });
            _foresight.AddRange(source._foresight);
            _nextUnitId = source._nextUnitId;
            Turn = source.Turn;
            Current = source.Current;
            Phase = source.Phase;
            Winner = source.Winner;
            Weather = source.Weather;
            ForesightCard = source.ForesightCard;

            var pc = source.Pending;
            if (pc != null)
                Pending = new PendingCapture
                {
                    Attacker = FindUnit(pc.Attacker.Id), Defender = FindUnit(pc.Defender.Id),
                    From = pc.From, Target = pc.Target, Card = pc.Card,
                };
        }

        static Player CopyPlayer(Player source)
        {
            var p = new Player(source.Side)
            {
                Morale = source.Morale, Orders = source.Orders, BonusMoves = source.BonusMoves,
                PlayedProactive = source.PlayedProactive, WagonReady = source.WagonReady,
            };
            foreach (var pair in source.Reserve) p.Reserve[pair.Key] = pair.Value;
            p.Deck.AddRange(source.Deck);
            p.Hand.AddRange(source.Hand);
            p.Discard.AddRange(source.Discard);
            return p;
        }

        void SetupPlayer(Player player, IEnumerable<CardId> deck)
        {
            player.Reserve[UnitType.Infantry] = 6;
            player.Reserve[UnitType.Cavalry] = 2;
            player.Reserve[UnitType.Crossbowmen] = 2;
            player.Reserve[UnitType.SiegeEngineers] = 2;
            player.Reserve[UnitType.Marshal] = 1;

            player.Deck.AddRange(deck);
            for (int i = player.Deck.Count - 1; i > 0; i--)
            {
                int j = _rng.Next(i + 1);
                (player.Deck[i], player.Deck[j]) = (player.Deck[j], player.Deck[i]);
            }
            for (int i = 0; i < StartingHand; i++) Draw(player);
        }

        public Unit At(Coord c)
        {
            foreach (var u in _units) if (u.Pos == c) return u;
            return null;
        }

        public Unit FindUnit(int id)
        {
            foreach (var u in _units) if (u.Id == id) return u;
            return null;
        }

        public bool HasMarker(Coord c, MarkerKind kind)
        {
            foreach (var m in _markers) if (m.Pos == c && m.Kind == kind) return true;
            return false;
        }

        bool Impassable(Coord c) => HasMarker(c, MarkerKind.Palisade) || HasMarker(c, MarkerKind.Ditch);

        bool BlocksShot(Coord c) => HasMarker(c, MarkerKind.Palisade) || HasMarker(c, MarkerKind.Smoke);

        bool IsFree(Coord c) => c.InBoard && At(c) == null && !Impassable(c);

        bool CanBuild(Coord c) => IsFree(c) && !HasMarker(c, MarkerKind.Stakes);

        bool CanEnd(Unit u, Coord c)
        {
            if (u.Type != UnitType.Cavalry) return true;
            foreach (var m in _markers)
                if (m.Pos == c && m.Kind == MarkerKind.Stakes && m.Owner != u.Side) return false;
            return true;
        }

        bool BonusAllowed(Unit u) => Weather != CardId.Blizzard && Turn > u.BonusLockTurn;

        bool ExtraStepAllowed(Unit u) => BonusAllowed(u) && Weather != CardId.Mud;

        bool CanStep(Unit u) => u.HasHorseshoes && u.Jumped && ExtraStepAllowed(u);

        public bool CanAct(Unit u) =>
            Phase == Phase.Maneuvers && u != null && u.Side == Current && u.RecruitedTurn != Turn;

        public List<Coord> MoveTargets(Unit u)
        {
            if (!CanAct(u)) return new List<Coord>();
            var p = CurrentPlayer;
            var result = p.Orders > 0 || (p.BonusMoves > 0 && BonusAllowed(u)) ? Reach(u, captures: false) : new List<Coord>();
            if (CanStep(u)) result.AddRange(StepSquares(u));
            return result;
        }

        public List<Coord> CaptureTargets(Unit u)
        {
            if (!CanAct(u) || CurrentPlayer.Orders <= 0) return new List<Coord>();
            var result = Reach(u, captures: true);
            if (u.HasLance)
                foreach (var via in StepSquares(u))
                    foreach (var c in Reach(u, via, ReachMode.Capture))
                        if (!result.Contains(c)) result.Add(c);
            return result;
        }

        enum ReachMode { Move, Capture, Threat }

        public List<Coord> Reach(Unit u, bool captures) => Reach(u, u.Pos, captures ? ReachMode.Capture : ReachMode.Move);

        public List<Coord> ThreatSquares(Unit u, Coord from) => Reach(u, from, ReachMode.Threat);

        List<Coord> Reach(Unit u, Coord from, ReachMode mode)
        {
            var result = new List<Coord>();
            int f = u.Side.Forward();
            switch (u.Type)
            {
                case UnitType.Infantry:
                    if (mode == ReachMode.Move) Consider(u, from + new Coord(0, f), mode, result);
                    else
                    {
                        Consider(u, from + new Coord(-1, f), mode, result);
                        Consider(u, from + new Coord(1, f), mode, result);
                    }
                    break;
                case UnitType.Cavalry:
                    foreach (var d in s_knight) Consider(u, from + d, mode, result);
                    break;
                case UnitType.Crossbowmen: Slide(u, from, s_diagonal, 2, mode, result); break;
                case UnitType.SiegeEngineers: Slide(u, from, s_orthogonal, 1, mode, result); break;
                case UnitType.Marshal: Slide(u, from, s_all, 2, mode, result); break;
                case UnitType.Sovereign: Slide(u, from, s_all, 1, mode, result); break;
            }
            return result;
        }

        Unit OtherAt(Unit u, Coord c)
        {
            var occupant = At(c);
            return occupant == u ? null : occupant;
        }

        void Consider(Unit u, Coord c, ReachMode mode, List<Coord> result)
        {
            if (!c.InBoard || Impassable(c) || !CanEnd(u, c)) return;
            var occupant = OtherAt(u, c);
            if (mode == ReachMode.Threat) result.Add(c);
            else if (occupant == null) { if (mode == ReachMode.Move) result.Add(c); }
            else if (mode == ReachMode.Capture && occupant.Side != u.Side) result.Add(c);
        }

        void Slide(Unit u, Coord from, Coord[] dirs, int max, ReachMode mode, List<Coord> result)
        {
            foreach (var d in dirs)
            {
                for (int step = 1; step <= max; step++)
                {
                    var c = from + d * step;
                    if (!c.InBoard || Impassable(c)) break;
                    var occupant = OtherAt(u, c);
                    if (occupant == null)
                    {
                        if (mode != ReachMode.Capture) result.Add(c);
                        continue;
                    }
                    if (mode == ReachMode.Threat || (mode == ReachMode.Capture && occupant.Side != u.Side)) result.Add(c);
                    break;
                }
            }
        }

        List<Coord> StepSquares(Unit u)
        {
            var result = new List<Coord>();
            foreach (var d in s_all)
                if (IsFree(u.Pos + d) && CanEnd(u, u.Pos + d)) result.Add(u.Pos + d);
            return result;
        }

        public bool IsThreatened(Unit target)
        {
            foreach (var enemy in _units)
            {
                if (enemy.Side == target.Side) continue;
                if (Reach(enemy, captures: true).Contains(target.Pos)) return true;
            }
            return false;
        }

        public List<Coord> RecruitSquares()
        {
            var result = new List<Coord>();
            int[] rows = Current == Side.White ? new[] { 0, 1 } : new[] { 7, 6 };
            foreach (int y in rows)
                for (int x = 0; x < Size; x++)
                {
                    var c = new Coord(x, y);
                    if (IsFree(c)) result.Add(c);
                }
            return result;
        }

        public bool CanRecruit(UnitType type)
        {
            var p = CurrentPlayer;
            return Phase == Phase.Maneuvers && type != UnitType.Sovereign
                && p.Reserve.TryGetValue(type, out int left) && left > 0
                && p.Morale >= Cost(type) && p.Orders > 0 && RecruitSquares().Count > 0;
        }

        public Unit Place(Side side, UnitType type, Coord c)
        {
            var u = new Unit { Id = _nextUnitId++, Side = side, Type = type, Pos = c };
            _units.Add(u);
            return u;
        }

        public bool Recruit(UnitType type, Coord c)
        {
            if (!CanRecruit(type) || !RecruitSquares().Contains(c)) return false;
            var p = CurrentPlayer;
            p.Morale -= Cost(type);
            p.Reserve[type]--;
            p.Orders--;
            var u = Place(Current, type, c);
            u.RecruitedTurn = Turn;
            AddLog(LogKind.Recruited, Current, unit: type, to: c);
            Emit(EventKind.Recruited, u.Id, c, c);
            return true;
        }

        public bool Move(Unit u, Coord to)
        {
            if (!MoveTargets(u).Contains(to)) return false;
            var from = u.Pos;
            // A cavalry jump never lands on an adjacent square, so an adjacent target is the horseshoe step.
            if (CanStep(u) && Distance(from, to) == 1)
            {
                u.HasHorseshoes = false;
                u.Jumped = false;
                Shift(u, to, LogKind.ExtraStep);
                return true;
            }
            var p = CurrentPlayer;
            if (p.BonusMoves > 0 && BonusAllowed(u)) p.BonusMoves--; else p.Orders--;
            u.MovedThisTurn = true;
            u.Jumped = u.Type == UnitType.Cavalry;
            Shift(u, to, LogKind.Moved);
            return true;
        }

        void Shift(Unit u, Coord to, LogKind kind)
        {
            var from = u.Pos;
            u.Pos = to;
            AddLog(kind, u.Side, unit: u.Type, from: from, to: to);
            Emit(EventKind.Moved, u.Id, from, to);
        }

        public bool DeclareCapture(Unit attacker, Coord target)
        {
            if (!CaptureTargets(attacker).Contains(target)) return false;
            CurrentPlayer.Orders--;
            attacker.Jumped = false;
            CardId? card = null;
            if (attacker.HasLance)
            {
                attacker.HasLance = false;
                if (!Reach(attacker, captures: true).Contains(target))
                    foreach (var via in StepSquares(attacker))
                    {
                        if (!Reach(attacker, via, ReachMode.Capture).Contains(target)) continue;
                        Shift(attacker, via, LogKind.Moved);
                        card = CardId.JoustingLance;
                        break;
                    }
            }
            StartCapture(attacker, At(target), card);
            return true;
        }

        void StartCapture(Unit attacker, Unit defender, CardId? card)
        {
            Pending = new PendingCapture
            {
                Attacker = attacker, Defender = defender, From = attacker.Pos, Target = defender.Pos, Card = card,
            };
            // Side/Unit/From = attacker; OtherUnit/To = defender; Card = declaring card; Flag = the attacker stays put.
            AddLog(LogKind.CaptureDeclared, attacker.Side, unit: attacker.Type, from: attacker.Pos,
                otherUnit: defender.Type, to: defender.Pos, card: card ?? default, flag: !Pending.Advances);
            Emit(EventKind.CaptureDeclared, attacker.Id, attacker.Pos, defender.Pos);

            if (ReactionOptions().Count > 0) Phase = Phase.Reaction;
            else Resolve(null);
        }

        public List<CardId> ReactionOptions()
        {
            var result = new List<CardId>();
            if (Pending == null) return result;
            var defender = Get(Pending.Defender.Side);
            foreach (var id in defender.Hand)
            {
                if (!CardCatalog.Get(id).IsReaction || result.Contains(id)) continue;
                if (ReactionApplies(id, Pending) && defender.Morale >= ReactionCost(id)) result.Add(id);
            }
            return result;
        }

        /// <summary>Whether the pending capture allows this reaction, ignoring Morale and the hand.</summary>
        public bool ReactionApplies(CardId id) => Pending != null && ReactionApplies(id, Pending);

        bool ReactionApplies(CardId id, PendingCapture pc)
        {
            var d = pc.Defender;
            return id switch
            {
                CardId.RaiseShields => d.Type == UnitType.Infantry,
                CardId.BracedSpears => d.Type == UnitType.Infantry && pc.Attacker.Type == UnitType.Cavalry && pc.Advances,
                CardId.OrganizedRetreat => RetreatSquare(d).HasValue,
                CardId.SovereignsGuard => d.Type == UnitType.Sovereign && GuardFor(d) != null,
                CardId.CloseRanks => d.Type == UnitType.Infantry && GuardFor(d) != null,
                CardId.Countercharge => d.Type == UnitType.Cavalry && pc.Advances && Distance(pc.From, d.Pos) == 1
                    && CounterchargeSquare(d).HasValue,
                CardId.SmokeScreen => pc.Ranged,
                CardId.HeroicInterposition => !pc.Arcane && GuardFor(d) != null,
                CardId.VigilSeal => d.Type == UnitType.Sovereign && pc.Card.HasValue,
                _ => false,
            };
        }

        /// <summary>What the defender would pay for this reaction against the pending capture.</summary>
        public int ReactionCost(CardId id)
        {
            int cost = CardCatalog.Get(id).Cost;
            if (Pending == null) return cost;
            if (id == CardId.BracedSpears && GuardFor(Pending.Defender) != null) cost = 0;
            if (Pending.Attacker.FeintTurn == Turn) cost++;
            return cost;
        }

        public Coord? RetreatSquare(Unit u)
        {
            var c = u.Pos + new Coord(0, -u.Side.Forward());
            return IsFree(c) ? c : (Coord?)null;
        }

        public Coord? CounterchargeSquare(Unit cavalry)
        {
            var squares = Reach(cavalry, captures: false);
            return squares.Count > 0 ? squares[0] : (Coord?)null;
        }

        /// <summary>An allied Infantry adjacent to the unit, if any.</summary>
        public Unit GuardFor(Unit unit)
        {
            foreach (var d in s_all)
            {
                var u = At(unit.Pos + d);
                if (u != null && u.Side == unit.Side && u.Type == UnitType.Infantry) return u;
            }
            return null;
        }

        public bool React(CardId? card)
        {
            if (Phase != Phase.Reaction || Pending == null) return false;
            var defender = Get(Pending.Defender.Side);
            if (card.HasValue)
            {
                if (!ReactionOptions().Contains(card.Value)) return false;
                int cost = ReactionCost(card.Value);
                defender.Morale -= cost;
                defender.Hand.Remove(card.Value);
                defender.Discard.Add(card.Value);
                Pending.Attacker.FeintTurn = 0;
                // Amount = Morale paid.
                AddLog(LogKind.Reacted, defender.Side, card: card.Value, amount: cost);
                Emit(EventKind.CardPlayed, Pending.Defender.Id);
            }
            Resolve(card);
            return true;
        }

        void Resolve(CardId? reaction)
        {
            var pc = Pending;
            Pending = null;
            Phase = Phase.Maneuvers;
            var attacker = pc.Attacker;
            var defender = pc.Defender;

            switch (reaction)
            {
                case CardId.RaiseShields:
                case CardId.BracedSpears:
                case CardId.VigilSeal:
                    Block(pc, reaction.Value, byEquipment: false);
                    return;

                case CardId.CloseRanks:
                    defender.BonusLockTurn = GuardFor(defender).BonusLockTurn = Turn + 1;
                    Block(pc, reaction.Value, byEquipment: false);
                    return;

                case CardId.SmokeScreen:
                    AddMarker(MarkerKind.Smoke, defender.Pos, defender.Side, RoundEnd(0));
                    Block(pc, reaction.Value, byEquipment: false);
                    return;

                case CardId.OrganizedRetreat:
                case CardId.Countercharge:
                {
                    var back = reaction == CardId.OrganizedRetreat
                        ? RetreatSquare(defender).Value : CounterchargeSquare(defender).Value;
                    bool attackerStays = !pc.Advances || reaction == CardId.Countercharge;
                    var defenderFrom = defender.Pos;
                    defender.Pos = back;
                    Emit(EventKind.Retreated, defender.Id, defenderFrom, back);
                    if (attackerStays) Emit(EventKind.Blocked, attacker.Id, pc.From, pc.Target);
                    else
                    {
                        attacker.Pos = pc.Target;
                        Emit(EventKind.Moved, attacker.Id, pc.From, pc.Target);
                    }
                    // Side/Unit/To = defender and its new square; OtherUnit/From = attacker and the vacated square;
                    // Card = reaction; Flag = the attacker stays put.
                    AddLog(LogKind.Retreated, defender.Side, unit: defender.Type, to: back,
                        otherUnit: attacker.Type, from: pc.Target, card: reaction.Value, flag: attackerStays);
                    return;
                }

                case CardId.SovereignsGuard:
                case CardId.HeroicInterposition:
                {
                    var guard = GuardFor(defender);
                    var targetPos = defender.Pos;
                    defender.Pos = guard.Pos;
                    guard.Pos = targetPos;
                    Emit(EventKind.Intercepted, guard.Id, defender.Pos, targetPos);
                    Emit(EventKind.Moved, defender.Id, targetPos, defender.Pos);
                    // Unit/To = the protected unit and where it stepped aside to.
                    AddLog(LogKind.Intercepted, defender.Side, unit: defender.Type, to: defender.Pos);
                    defender = guard;
                    break;
                }
            }

            if (pc.Ranged && defender.RangedProtection > 0)
            {
                defender.RangedProtection--;
                Block(pc, default, byEquipment: true);
                return;
            }
            if (defender.Protection > 0)
            {
                defender.Protection--;
                if (attacker.HasHammer && pc.Advances)
                {
                    attacker.HasHammer = false;
                    AddLog(LogKind.ProtectionBroken, attacker.Side);
                }
                else
                {
                    Block(pc, default, byEquipment: true);
                    return;
                }
            }

            _units.Remove(defender);
            Emit(EventKind.Captured, defender.Id, defender.Pos, defender.Pos);
            if (pc.Advances)
            {
                var from = attacker.Pos;
                attacker.Pos = defender.Pos;
                Emit(EventKind.Moved, attacker.Id, from, attacker.Pos);
            }
            AddLog(LogKind.UnitCaptured, defender.Side, unit: defender.Type, to: defender.Pos);

            if (defender.Type == UnitType.Sovereign)
            {
                Winner = attacker.Side;
                Phase = Phase.GameOver;
                AddLog(LogKind.Victory, attacker.Side);
                Emit(EventKind.GameOver, attacker.Id);
            }
        }

        void Block(PendingCapture pc, CardId card, bool byEquipment)
        {
            // Card = blocking reaction; Flag = blocked by equipped protection instead.
            AddLog(LogKind.Blocked, pc.Defender.Side, card: card, flag: byEquipment);
            Emit(EventKind.Blocked, pc.Attacker.Id, pc.From, pc.Target);
        }

        public bool EndTurn()
        {
            if (Phase != Phase.Maneuvers) return false;
            StartTurn(Current.Other());
            return true;
        }

        void StartTurn(Side side)
        {
            Turn++;
            Current = side;
            Phase = Phase.Maneuvers;
            bool roundStart = Turn % 2 == 1;
            if (roundStart) StrikeLightning();

            for (int i = _markers.Count - 1; i >= 0; i--)
            {
                if (Turn < _markers[i].ExpiresTurn) continue;
                Emit(EventKind.MarkerRemoved, -1, _markers[i].Pos, _markers[i].Pos);
                _markers.RemoveAt(i);
            }
            if (roundStart && Weather == CardId.LightningStorm) MarkLightning();

            var p = Get(side);
            int before = p.Morale;
            p.Morale = Math.Min(MaxMorale, p.Morale + MoralePerTurn + (p.WagonReady ? 1 : 0));
            p.WagonReady = false;
            p.Orders = OrdersPerTurn;
            p.BonusMoves = 0;
            p.PlayedProactive = false;
            foreach (var u in _units)
            {
                u.MovedThisTurn = false;
                u.Jumped = false;
            }

            bool drew = Draw(p);
            // Amount = Morale gained; Flag = a card was drawn.
            AddLog(LogKind.Preparation, side, amount: p.Morale - before, flag: drew);
            Emit(EventKind.TurnStarted);
        }

        /// <summary>The first turn after the end of the round <paramref name="roundsLater"/> rounds from now.</summary>
        int RoundEnd(int roundsLater) => 2 * (Round + roundsLater) + 1;

        void AddMarker(MarkerKind kind, Coord c, Side owner, int expiresTurn)
        {
            _markers.Add(new Marker { Kind = kind, Pos = c, Owner = owner, ExpiresTurn = expiresTurn });
            Emit(EventKind.MarkerPlaced, -1, c, c);
        }

        void ClearLightning()
        {
            for (int i = _markers.Count - 1; i >= 0; i--)
            {
                if (_markers[i].Kind != MarkerKind.Lightning) continue;
                Emit(EventKind.MarkerRemoved, -1, _markers[i].Pos, _markers[i].Pos);
                _markers.RemoveAt(i);
            }
        }

        void MarkLightning()
        {
            var squares = new List<Coord>();
            for (int x = 0; x < Size; x++)
                for (int y = 0; y < Size; y++)
                {
                    var c = new Coord(x, y);
                    if (At(c)?.Type != UnitType.Sovereign) squares.Add(c);
                }
            var mark = squares[_rng.Next(squares.Count)];
            AddMarker(MarkerKind.Lightning, mark, Current, int.MaxValue);
            AddLog(LogKind.LightningMarked, Current, to: mark);
        }

        void StrikeLightning()
        {
            Coord? mark = null;
            foreach (var m in _markers) if (m.Kind == MarkerKind.Lightning) mark = m.Pos;
            if (!mark.HasValue) return;
            ClearLightning();
            var victim = At(mark.Value);
            bool hit = victim != null && victim.Type != UnitType.Sovereign;
            if (hit)
            {
                _units.Remove(victim);
                Emit(EventKind.Captured, victim.Id, victim.Pos, victim.Pos);
            }
            // Flag = a unit was struck (Side/Unit = that unit).
            AddLog(LogKind.LightningStruck, hit ? victim.Side : Current, unit: hit ? victim.Type : default,
                to: mark.Value, flag: hit);
        }

        void SetWeather(CardId? weather)
        {
            Weather = weather;
            ClearLightning();
        }

        bool Draw(Player p)
        {
            if (p.Deck.Count == 0) return false;
            p.Hand.Add(p.Deck[p.Deck.Count - 1]);
            p.Deck.RemoveAt(p.Deck.Count - 1);
            return true;
        }

        public static int PickCount(CardId card) => card switch
        {
            CardId.ReinforcedShield or CardId.ReinforcedArmor or CardId.WarHammer or CardId.CampaignHorseshoes
                or CardId.CrossbowPavise or CardId.MarshalsBanner or CardId.JoustingLance or CardId.CavalryFeint
                or CardId.ImprovisedPalisade or CardId.ShallowDitch => 1,
            CardId.CrossbowVolley or CardId.SiegeOrder or CardId.Fireball or CardId.ExtraRations or CardId.LineSwap
                or CardId.AntiCavalryStakes => 2,
            CardId.CoordinatedAdvance => 4,
            _ => 0,
        };

        /// <summary>Anti-Cavalry Stakes may stop after its first square ("up to 2").</summary>
        public static bool PicksComplete(CardId card, int picks) =>
            picks == PickCount(card) || (card == CardId.AntiCavalryStakes && picks == 1);

        public static bool UsesOrder(CardId card) =>
            card is CardId.CrossbowVolley or CardId.SiegeOrder or CardId.CoordinatedAdvance or CardId.LineSwap;

        public static bool DeclaresCapture(CardId card) =>
            card is CardId.CrossbowVolley or CardId.SiegeOrder or CardId.Fireball;

        /// <summary>A Tactic aimed first at an Infantry next to a Marshal's Banner costs 1 less.</summary>
        public int CardCost(CardId card, IReadOnlyList<Coord> picks)
        {
            int cost = CardCatalog.Get(card).Cost;
            var first = picks.Count > 0 ? At(picks[0]) : null;
            return first != null && Banners(card, first) ? Math.Max(0, cost - 1) : cost;
        }

        bool Banners(CardId card, Unit target)
        {
            if (CardCatalog.Get(card).Kind != CardKind.Tactic || target.Type != UnitType.Infantry) return false;
            foreach (var u in _units)
                if (u.HasBanner && u.Side == target.Side && Distance(u.Pos, target.Pos) == 1) return true;
            return false;
        }

        bool Affordable(CardId card, Unit target) => CurrentPlayer.Morale >= CardCost(card, new[] { target.Pos });

        public bool CanPlay(CardId card)
        {
            var p = CurrentPlayer;
            var info = CardCatalog.Get(card);
            if (Phase != Phase.Maneuvers || info.IsReaction || p.PlayedProactive || !p.Hand.Contains(card)) return false;
            bool bannered = card is CardId.LineSwap or CardId.CoordinatedAdvance
                && _units.Exists(u => u.HasBanner && u.Side == Current);
            if (p.Morale < info.Cost - (bannered ? 1 : 0)) return false;
            if (UsesOrder(card) && p.Orders <= 0) return false;
            if (info.IsWeather) return Weather != card;
            switch (card)
            {
                case CardId.SeersOmen:
                case CardId.ScoutsAhead:
                    return p.Deck.Count > 0;
                case CardId.OracleEcho: return EchoOptions(p).Count > 0;
                case CardId.ForcedMarch: return Weather != CardId.Blizzard;
                case CardId.SupplyWagon: return !p.WagonReady;
                case CardId.DawnPrayer: return Weather.HasValue || p.Deck.Count > 0;
            }
            return PickCount(card) == 0 || CardTargets(card, Array.Empty<Coord>()).Count > 0;
        }

        public List<Coord> CardTargets(CardId card, IReadOnlyList<Coord> picks)
        {
            var result = new List<Coord>();
            var first = picks.Count > 0 ? At(picks[0]) : null;

            void Own(Predicate<Unit> match)
            {
                foreach (var u in _units) if (u.Side == Current && match(u)) result.Add(u.Pos);
            }

            void Squares(Predicate<Coord> match)
            {
                for (int x = 0; x < Size; x++)
                    for (int y = 0; y < Size; y++)
                        if (match(new Coord(x, y))) result.Add(new Coord(x, y));
            }

            switch (card)
            {
                case CardId.ReinforcedShield: Own(u => u.Protection == 0); break;
                case CardId.ReinforcedArmor: Own(u => u.Type == UnitType.SiegeEngineers && u.RangedProtection == 0); break;
                case CardId.CrossbowPavise: Own(u => u.Type == UnitType.Crossbowmen && u.RangedProtection == 0); break;
                case CardId.WarHammer:
                    Own(u => (u.Type == UnitType.Cavalry || u.Type == UnitType.Marshal) && !u.HasHammer);
                    break;
                case CardId.CampaignHorseshoes: Own(u => u.Type == UnitType.Cavalry && !u.HasHorseshoes); break;
                case CardId.JoustingLance: Own(u => u.Type == UnitType.Cavalry && !u.HasLance); break;
                case CardId.MarshalsBanner: Own(u => u.Type == UnitType.Marshal && !u.HasBanner); break;
                case CardId.CavalryFeint: Own(u => u.Type == UnitType.Cavalry && u.FeintTurn != Turn); break;

                case CardId.ImprovisedPalisade:
                    Squares(c => CanBuild(c) && NextToOwn(c, UnitType.Infantry, UnitType.SiegeEngineers));
                    break;
                case CardId.ShallowDitch: Squares(c => CanBuild(c) && NextToOwn(c, UnitType.SiegeEngineers)); break;
                case CardId.AntiCavalryStakes:
                    if (picks.Count == 0) Squares(c => CanBuild(c) && NextToOwn(c, UnitType.Infantry));
                    else Squares(c => c != picks[0] && CanBuild(c) && SharedInfantry(c, picks[0]));
                    break;

                case CardId.CrossbowVolley:
                case CardId.SiegeOrder:
                {
                    var type = card == CardId.CrossbowVolley ? UnitType.Crossbowmen : UnitType.SiegeEngineers;
                    var dirs = card == CardId.CrossbowVolley ? s_diagonal : s_orthogonal;
                    if (picks.Count == 0) Own(u => u.Type == type && CanAct(u) && ShotTargets(u, dirs).Count > 0);
                    else result.AddRange(ShotTargets(first, dirs));
                    break;
                }
                case CardId.Fireball:
                    if (picks.Count == 0) Own(u => FireballTargets(u).Count > 0);
                    else result.AddRange(FireballTargets(first));
                    break;

                case CardId.ExtraRations:
                    if (picks.Count == 0) Own(u => u.MovedThisTurn && ExtraStepAllowed(u) && StepSquares(u).Count > 0);
                    else result.AddRange(StepSquares(first));
                    break;

                case CardId.LineSwap:
                    if (picks.Count == 0) Own(u => Swappable(u) && Affordable(card, u) && SwapPartners(u).Count > 0);
                    else result.AddRange(SwapPartners(first));
                    break;

                case CardId.CoordinatedAdvance:
                    switch (picks.Count)
                    {
                        case 0:
                            Own(u => u.Type == UnitType.Infantry && CanAct(u) && Affordable(card, u)
                                && Reach(u, captures: false).Count > 0);
                            break;
                        case 1: result.AddRange(Reach(first, captures: false)); break;
                        case 2:
                            Own(u => u.Type == UnitType.Infantry && u != first && CanAct(u)
                                && AdvanceAfter(first, picks[1], u).Count > 0);
                            break;
                        default: result.AddRange(AdvanceAfter(first, picks[1], At(picks[2]))); break;
                    }
                    break;
            }
            return result;
        }

        bool NextToOwn(Coord c, params UnitType[] types)
        {
            foreach (var u in _units)
                if (u.Side == Current && Array.IndexOf(types, u.Type) >= 0 && Distance(u.Pos, c) == 1) return true;
            return false;
        }

        bool SharedInfantry(Coord a, Coord b)
        {
            foreach (var u in _units)
                if (u.Side == Current && u.Type == UnitType.Infantry && Distance(u.Pos, a) == 1 && Distance(u.Pos, b) == 1)
                    return true;
            return false;
        }

        List<Coord> ShotTargets(Unit shooter, Coord[] dirs)
        {
            var result = new List<Coord>();
            if (shooter == null) return result;
            int range = Weather == CardId.Gale ? Math.Max(1, ShotRange - 1) : ShotRange;
            if (Weather == CardId.Fog) range = Math.Min(range, SightRange);
            foreach (var d in dirs)
                for (int step = 1; step <= range; step++)
                {
                    var c = shooter.Pos + d * step;
                    if (!c.InBoard || BlocksShot(c)) break;
                    var occupant = At(c);
                    if (occupant == null) continue;
                    bool seen = step <= SightRange || Weather != CardId.MoonlessNight || Observed(shooter.Side, c);
                    if (occupant.Side != shooter.Side && occupant.Type != UnitType.Sovereign && seen) result.Add(c);
                    break;
                }
            return result;
        }

        bool Observed(Side side, Coord c)
        {
            foreach (var u in _units) if (u.Side == side && Distance(u.Pos, c) == 1) return true;
            return false;
        }

        List<Coord> FireballTargets(Unit caster)
        {
            var result = new List<Coord>();
            if (caster == null) return result;
            foreach (var u in _units)
                if (u.Side != caster.Side && (u.Type == UnitType.Infantry || u.Type == UnitType.Crossbowmen)
                    && Distance(u.Pos, caster.Pos) <= FireballRange)
                    result.Add(u.Pos);
            return result;
        }

        bool Swappable(Unit u) => u.Type == UnitType.Infantry && CanAct(u);

        List<Coord> SwapPartners(Unit u)
        {
            var result = new List<Coord>();
            if (u == null) return result;
            foreach (var d in s_all)
            {
                var other = At(u.Pos + d);
                if (other != null && other.Side == u.Side && Swappable(other)) result.Add(other.Pos);
            }
            return result;
        }

        List<Coord> AdvanceAfter(Unit first, Coord firstTo, Unit second)
        {
            if (second == null) return new List<Coord>();
            var from = first.Pos;
            first.Pos = firstTo;
            var result = Reach(second, captures: false);
            first.Pos = from;
            return result;
        }

        List<CardId> EchoOptions(Player p)
        {
            var result = new List<CardId>();
            for (int i = p.Discard.Count - 1; i >= 0; i--)
            {
                var id = p.Discard[i];
                if (CardCatalog.Get(id).Kind != CardKind.Arcane && !result.Contains(id)) result.Add(id);
            }
            return result;
        }

        public bool PlayCard(CardId card, IReadOnlyList<Coord> picks)
        {
            if (!CanPlay(card) || !PicksComplete(card, picks.Count)) return false;
            var chosen = new List<Coord>();
            foreach (var pick in picks)
            {
                if (!CardTargets(card, chosen).Contains(pick)) return false;
                chosen.Add(pick);
            }

            var p = CurrentPlayer;
            int cost = CardCost(card, picks);
            if (p.Morale < cost) return false;
            p.Morale -= cost;
            p.Hand.Remove(card);
            p.Discard.Add(card);
            p.PlayedProactive = true;
            if (UsesOrder(card)) p.Orders--;
            var target = picks.Count > 0 ? At(picks[0]) : null;
            // Flag = the card targets a unit (Unit/To = that unit).
            AddLog(LogKind.CardPlayed, Current, card: card, flag: target != null,
                unit: target?.Type ?? default, to: target?.Pos ?? default);
            Emit(EventKind.CardPlayed, target?.Id ?? -1);

            switch (card)
            {
                case CardId.ReinforcedShield: Equip(target, u => u.Protection = 1); break;
                case CardId.ReinforcedArmor: Equip(target, u => u.RangedProtection = 2); break;
                case CardId.CrossbowPavise: Equip(target, u => u.RangedProtection = 1); break;
                case CardId.WarHammer: Equip(target, u => u.HasHammer = true); break;
                case CardId.CampaignHorseshoes: Equip(target, u => u.HasHorseshoes = true); break;
                case CardId.JoustingLance: Equip(target, u => u.HasLance = true); break;
                case CardId.MarshalsBanner: Equip(target, u => u.HasBanner = true); break;
                case CardId.CavalryFeint: target.FeintTurn = Turn; break;

                case CardId.ImprovisedPalisade: Fortify(card, MarkerKind.Palisade, picks, Turn + FortificationTurns); break;
                case CardId.ShallowDitch: Fortify(card, MarkerKind.Ditch, picks, Turn + FortificationTurns); break;
                case CardId.AntiCavalryStakes: Fortify(card, MarkerKind.Stakes, picks, RoundEnd(1)); break;

                case CardId.ForcedMarch:
                    p.BonusMoves++;
                    break;
                case CardId.ExtraRations:
                    Shift(target, picks[1], LogKind.ExtraStep);
                    break;
                case CardId.CrossbowVolley:
                case CardId.SiegeOrder:
                case CardId.Fireball:
                    StartCapture(target, At(picks[1]), card);
                    break;
                case CardId.LineSwap:
                {
                    var other = At(picks[1]);
                    target.Pos = picks[1];
                    other.Pos = picks[0];
                    // Unit/From and OtherUnit/To = the two infantry before the swap.
                    AddLog(LogKind.Swapped, Current, unit: target.Type, from: picks[0], otherUnit: other.Type, to: picks[1]);
                    Emit(EventKind.Moved, target.Id, picks[0], picks[1]);
                    Emit(EventKind.Moved, other.Id, picks[1], picks[0]);
                    break;
                }
                case CardId.CoordinatedAdvance:
                {
                    var second = At(picks[2]);
                    target.MovedThisTurn = second.MovedThisTurn = true;
                    Shift(target, picks[1], LogKind.Moved);
                    Shift(second, picks[3], LogKind.Moved);
                    break;
                }

                case CardId.SeersOmen:
                case CardId.ScoutsAhead:
                    _foresight.Clear();
                    for (int i = 0; i < (card == CardId.SeersOmen ? 3 : 2) && p.Deck.Count > 0; i++)
                    {
                        _foresight.Add(p.Deck[p.Deck.Count - 1]);
                        p.Deck.RemoveAt(p.Deck.Count - 1);
                    }
                    OpenForesight(card);
                    break;
                case CardId.OracleEcho:
                    _foresight.Clear();
                    _foresight.AddRange(EchoOptions(p));
                    OpenForesight(card);
                    break;
                case CardId.SupplyWagon:
                    p.WagonReady = true;
                    break;
                case CardId.DawnPrayer:
                    if (Weather is CardId cleared)
                    {
                        SetWeather(null);
                        AddLog(LogKind.WeatherCleared, Current, card: cleared);
                    }
                    else if (Draw(p)) AddLog(LogKind.CardDrawn, Current);
                    break;

                default:
                    if (CardCatalog.Get(card).IsWeather) SetWeather(card);
                    break;
            }
            return true;
        }

        void Equip(Unit target, Action<Unit> gear)
        {
            gear(target);
            Emit(EventKind.Equipped, target.Id, target.Pos, target.Pos);
        }

        void Fortify(CardId card, MarkerKind kind, IReadOnlyList<Coord> squares, int expiresTurn)
        {
            foreach (var c in squares)
            {
                AddMarker(kind, c, Current, expiresTurn);
                AddLog(LogKind.Fortified, Current, card: card, to: c);
            }
        }

        void OpenForesight(CardId card)
        {
            ForesightCard = card;
            Phase = Phase.Foresight;
        }

        public bool ChooseForesight(int index)
        {
            if (Phase != Phase.Foresight || index < 0 || index >= _foresight.Count) return false;
            var p = CurrentPlayer;
            var chosen = _foresight[index];
            if (ForesightCard == CardId.OracleEcho)
            {
                p.Discard.Remove(chosen);
                p.Deck.Add(chosen);
            }
            else
            {
                for (int i = 0; i < _foresight.Count; i++)
                    if (i != index) p.Deck.Insert(0, _foresight[i]);
                if (ForesightCard == CardId.ScoutsAhead) p.Deck.Add(chosen);
                else p.Hand.Add(chosen);
            }
            _foresight.Clear();
            Phase = Phase.Maneuvers;
            // Card = the card that opened the choice.
            AddLog(LogKind.ForesightDrawn, Current, card: ForesightCard);
            return true;
        }

        void AddLog(LogKind kind, Side side, UnitType unit = default, Coord from = default, Coord to = default,
            UnitType otherUnit = default, CardId card = default, int amount = 0, bool flag = false) =>
            _log.Add(new LogEntry(Turn, kind, side, unit, from, to, otherUnit, card, amount, flag));

        void Emit(EventKind kind, int unitId = -1, Coord from = default, Coord to = default) =>
            _events.Add(new MatchEvent(kind, unitId, from, to));

        public List<MatchEvent> DrainEvents()
        {
            var copy = new List<MatchEvent>(_events);
            _events.Clear();
            return copy;
        }
    }
}
