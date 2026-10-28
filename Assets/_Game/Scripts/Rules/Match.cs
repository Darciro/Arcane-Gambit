using System;
using System.Collections.Generic;

namespace ArcaneGambit.Rules
{
    /// <summary>
    /// State and rules of a match (GDD v0.2). Plain C#: no UnityEngine, deterministic for a given seed.
    /// Every public command returns false (and has no effect) when it is illegal.
    /// </summary>
    public sealed class Match
    {
        public const int Size = 8;
        public const int MaxMorale = 8;
        public const int MoralePerTurn = 2;
        public const int OrdersPerTurn = 2;
        public const int StartingHand = 4;

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
        readonly List<Palisade> _palisades = new List<Palisade>();
        readonly List<LogEntry> _log = new List<LogEntry>();
        readonly List<MatchEvent> _events = new List<MatchEvent>();
        readonly List<CardId> _foresight = new List<CardId>();
        int _nextUnitId = 1;

        public int Turn { get; private set; }
        public Side Current { get; private set; }
        public Phase Phase { get; private set; }
        public Side? Winner { get; private set; }
        public PendingCapture Pending { get; private set; }

        public IReadOnlyList<Unit> Units => _units;
        public IReadOnlyList<Palisade> Palisades => _palisades;
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

        public static Match CreateDefault(int seed) =>
            new Match(seed, CardCatalog.Vanguard(), CardCatalog.Bastion());

        /// <param name="standardSetup">false = empty board (tests place units with <see cref="Place"/>).</param>
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

        /// <summary>Deep copy for look-ahead (AI). The copy has an empty history/event queue and its own RNG
        /// (only the initial shuffle draws from it, so play continues identically).</summary>
        public Match Clone() => new Match(this);

        Match(Match source)
        {
            _rng = new Random(source.Turn);
            _players = new[] { CopyPlayer(source._players[0]), CopyPlayer(source._players[1]) };
            foreach (var u in source._units)
                _units.Add(new Unit
                {
                    Id = u.Id, Side = u.Side, Type = u.Type, Pos = u.Pos, RecruitedTurn = u.RecruitedTurn,
                    MovedThisTurn = u.MovedThisTurn, Protection = u.Protection, HasHammer = u.HasHammer,
                });
            foreach (var p in source._palisades)
                _palisades.Add(new Palisade { Pos = p.Pos, Owner = p.Owner, ExpiresTurn = p.ExpiresTurn });
            _foresight.AddRange(source._foresight);
            _nextUnitId = source._nextUnitId;
            Turn = source.Turn;
            Current = source.Current;
            Phase = source.Phase;
            Winner = source.Winner;

            var pc = source.Pending;
            if (pc != null)
                Pending = new PendingCapture
                {
                    Attacker = FindUnit(pc.Attacker.Id), Defender = FindUnit(pc.Defender.Id),
                    From = pc.From, Target = pc.Target, Ranged = pc.Ranged,
                };
        }

        static Player CopyPlayer(Player source)
        {
            var p = new Player(source.Side)
            {
                Morale = source.Morale, Orders = source.Orders, BonusMoves = source.BonusMoves,
                PlayedProactive = source.PlayedProactive,
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

        // ------------------------------------------------------------------ queries

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

        public bool HasPalisade(Coord c)
        {
            foreach (var p in _palisades) if (p.Pos == c) return true;
            return false;
        }

        bool IsFree(Coord c) => c.InBoard && At(c) == null && !HasPalisade(c);

        /// <summary>The unit belongs to the active player and was not recruited this turn.</summary>
        public bool CanAct(Unit u) =>
            Phase == Phase.Maneuvers && u != null && u.Side == Current && u.RecruitedTurn != Turn;

        public List<Coord> MoveTargets(Unit u)
        {
            var p = CurrentPlayer;
            if (!CanAct(u) || (p.Orders <= 0 && p.BonusMoves <= 0)) return new List<Coord>();
            return Reach(u, captures: false);
        }

        public List<Coord> CaptureTargets(Unit u)
        {
            if (!CanAct(u) || CurrentPlayer.Orders <= 0) return new List<Coord>();
            return Reach(u, captures: true);
        }

        /// <summary>Move = empty squares; Capture = enemy-occupied squares; Threat = every square where an enemy
        /// would be capturable (empty, enemy or friendly), used to read threats ahead.</summary>
        enum ReachMode { Move, Capture, Threat }

        /// <summary>Pure movement/capture geometry, ignoring Orders (also used for threats).</summary>
        public List<Coord> Reach(Unit u, bool captures) => Reach(u, u.Pos, captures ? ReachMode.Capture : ReachMode.Move);

        /// <summary>Squares <paramref name="u"/> would threaten if it stood on <paramref name="from"/> (AI look-ahead).</summary>
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

        /// <summary>Occupant of <paramref name="c"/>, treating <paramref name="u"/>'s own square as empty
        /// (it matters only when reaching from a hypothetical square).</summary>
        Unit OtherAt(Unit u, Coord c)
        {
            var occupant = At(c);
            return occupant == u ? null : occupant;
        }

        void Consider(Unit u, Coord c, ReachMode mode, List<Coord> result)
        {
            if (!c.InBoard || HasPalisade(c)) return;
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
                    if (!c.InBoard || HasPalisade(c)) break;
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

        /// <summary>Whether any enemy unit could capture this one with a conventional move.</summary>
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

        // ------------------------------------------------------------------ commands

        /// <summary>Places a unit directly (initial formation and tests).</summary>
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
            var p = CurrentPlayer;
            if (p.BonusMoves > 0) p.BonusMoves--; else p.Orders--;
            var from = u.Pos;
            u.Pos = to;
            u.MovedThisTurn = true;
            AddLog(LogKind.Moved, Current, unit: u.Type, from: from, to: to);
            Emit(EventKind.Moved, u.Id, from, to);
            return true;
        }

        public bool DeclareCapture(Unit attacker, Coord target)
        {
            if (!CaptureTargets(attacker).Contains(target)) return false;
            CurrentPlayer.Orders--;
            StartCapture(attacker, At(target), ranged: false);
            return true;
        }

        void StartCapture(Unit attacker, Unit defender, bool ranged)
        {
            Pending = new PendingCapture
            {
                Attacker = attacker, Defender = defender, From = attacker.Pos, Target = defender.Pos, Ranged = ranged,
            };
            // Side/Unit/From = attacker; OtherUnit/To = defender; Flag = ranged.
            AddLog(LogKind.CaptureDeclared, attacker.Side, unit: attacker.Type, from: attacker.Pos,
                otherUnit: defender.Type, to: defender.Pos, flag: ranged);
            Emit(EventKind.CaptureDeclared, attacker.Id, attacker.Pos, defender.Pos);

            if (ReactionOptions().Count > 0) Phase = Phase.Reaction;
            else Resolve(null);
        }

        /// <summary>Distinct Reaction cards the defender can afford and that apply to the pending capture.</summary>
        public List<CardId> ReactionOptions()
        {
            var result = new List<CardId>();
            if (Pending == null) return result;
            var defender = Get(Pending.Defender.Side);
            foreach (var id in defender.Hand)
            {
                var info = CardCatalog.Get(id);
                if (!info.IsReaction || result.Contains(id) || defender.Morale < info.Cost) continue;
                if (ReactionApplies(id, Pending)) result.Add(id);
            }
            return result;
        }

        bool ReactionApplies(CardId id, PendingCapture pc) => id switch
        {
            CardId.RaiseShields => pc.Defender.Type == UnitType.Infantry,
            CardId.BracedSpears => pc.Defender.Type == UnitType.Infantry
                && pc.Attacker.Type == UnitType.Cavalry && !pc.Ranged,
            CardId.OrganizedRetreat => RetreatSquare(pc.Defender).HasValue,
            CardId.SovereignsGuard => pc.Defender.Type == UnitType.Sovereign && GuardFor(pc.Defender) != null,
            _ => false,
        };

        Coord? RetreatSquare(Unit u)
        {
            var c = u.Pos + new Coord(0, -u.Side.Forward());
            return IsFree(c) ? c : (Coord?)null;
        }

        Unit GuardFor(Unit sovereign)
        {
            foreach (var d in s_all)
            {
                var u = At(sovereign.Pos + d);
                if (u != null && u.Side == sovereign.Side && u.Type == UnitType.Infantry) return u;
            }
            return null;
        }

        /// <summary>Defender answers the confrontation window. null = no reaction.</summary>
        public bool React(CardId? card)
        {
            if (Phase != Phase.Reaction || Pending == null) return false;
            var defender = Get(Pending.Defender.Side);
            if (card.HasValue)
            {
                if (!ReactionOptions().Contains(card.Value)) return false;
                defender.Morale -= CardCatalog.Get(card.Value).Cost;
                defender.Hand.Remove(card.Value);
                defender.Discard.Add(card.Value);
                AddLog(LogKind.Reacted, defender.Side, card: card.Value);
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
                    Block(pc, reaction.Value, byEquipment: false);
                    return;

                case CardId.OrganizedRetreat:
                {
                    var back = RetreatSquare(defender).Value;
                    var defenderFrom = defender.Pos;
                    defender.Pos = back;
                    Emit(EventKind.Retreated, defender.Id, defenderFrom, back);
                    if (!pc.Ranged)
                    {
                        attacker.Pos = pc.Target;
                        Emit(EventKind.Moved, attacker.Id, pc.From, pc.Target);
                    }
                    // Side/Unit/To = defender and its retreat square; OtherUnit/From = attacker and the vacated square.
                    AddLog(LogKind.Retreated, defender.Side, unit: defender.Type, to: back,
                        otherUnit: attacker.Type, from: pc.Target, flag: pc.Ranged);
                    return;
                }

                case CardId.SovereignsGuard:
                {
                    var guard = GuardFor(defender);
                    var sovereignPos = defender.Pos;
                    defender.Pos = guard.Pos;
                    guard.Pos = sovereignPos;
                    Emit(EventKind.Intercepted, guard.Id, defender.Pos, sovereignPos);
                    Emit(EventKind.Moved, defender.Id, sovereignPos, defender.Pos);
                    AddLog(LogKind.Intercepted, defender.Side, to: defender.Pos);
                    defender = guard;
                    break;
                }
            }

            if (defender.Protection > 0)
            {
                defender.Protection--;
                if (attacker.HasHammer)
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
            if (!pc.Ranged)
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
            var p = Get(side);
            int before = p.Morale;
            p.Morale = Math.Min(MaxMorale, p.Morale + MoralePerTurn);
            p.Orders = OrdersPerTurn;
            p.BonusMoves = 0;
            p.PlayedProactive = false;
            foreach (var u in _units) u.MovedThisTurn = false;

            for (int i = _palisades.Count - 1; i >= 0; i--)
            {
                if (Turn < _palisades[i].ExpiresTurn) continue;
                Emit(EventKind.PalisadeFell, -1, _palisades[i].Pos, _palisades[i].Pos);
                _palisades.RemoveAt(i);
            }

            bool drew = Draw(p);
            // Amount = Morale gained; Flag = a card was drawn.
            AddLog(LogKind.Preparation, side, amount: p.Morale - before, flag: drew);
            Emit(EventKind.TurnStarted);
        }

        bool Draw(Player p)
        {
            if (p.Deck.Count == 0) return false;
            p.Hand.Add(p.Deck[p.Deck.Count - 1]);
            p.Deck.RemoveAt(p.Deck.Count - 1);
            return true;
        }

        // ------------------------------------------------------------------ proactive cards

        /// <summary>How many squares the player must pick to play the card.</summary>
        public static int PickCount(CardId card) => card switch
        {
            CardId.ReinforcedShield or CardId.ReinforcedArmor or CardId.WarHammer or CardId.ImprovisedPalisade => 1,
            CardId.CrossbowVolley or CardId.ExtraRations => 2,
            _ => 0,
        };

        public bool CanPlay(CardId card)
        {
            var p = CurrentPlayer;
            var info = CardCatalog.Get(card);
            if (Phase != Phase.Maneuvers || info.IsReaction || p.PlayedProactive) return false;
            if (!p.Hand.Contains(card) || p.Morale < info.Cost) return false;
            if (card == CardId.SeersOmen) return p.Deck.Count > 0;
            return PickCount(card) == 0 || CardTargets(card, Array.Empty<Coord>()).Count > 0;
        }

        /// <summary>Valid squares for the next pick, given the picks already made.</summary>
        public List<Coord> CardTargets(CardId card, IReadOnlyList<Coord> picks)
        {
            var result = new List<Coord>();
            var p = CurrentPlayer;
            switch (card)
            {
                case CardId.ReinforcedShield:
                    foreach (var u in _units) if (u.Side == Current && u.Protection == 0) result.Add(u.Pos);
                    break;
                case CardId.ReinforcedArmor:
                    foreach (var u in _units)
                        if (u.Side == Current && u.Type == UnitType.SiegeEngineers && u.Protection == 0) result.Add(u.Pos);
                    break;
                case CardId.WarHammer:
                    foreach (var u in _units) if (u.Side == Current && !u.HasHammer) result.Add(u.Pos);
                    break;
                case CardId.ImprovisedPalisade:
                    for (int x = 0; x < Size; x++)
                        for (int y = 0; y < Size; y++)
                        {
                            var c = new Coord(x, y);
                            bool ownHalf = Current == Side.White ? y <= 3 : y >= 4;
                            if (ownHalf && IsFree(c)) result.Add(c);
                        }
                    break;
                case CardId.CrossbowVolley:
                    if (p.Orders <= 0) break;
                    if (picks.Count == 0)
                    {
                        foreach (var u in _units)
                            if (u.Type == UnitType.Crossbowmen && CanAct(u) && VolleyTargets(u).Count > 0) result.Add(u.Pos);
                    }
                    else result.AddRange(VolleyTargets(At(picks[0])));
                    break;
                case CardId.ExtraRations:
                    if (picks.Count == 0)
                    {
                        foreach (var u in _units)
                            if (u.Side == Current && u.MovedThisTurn && AdjacentFree(u.Pos).Count > 0) result.Add(u.Pos);
                    }
                    else result.AddRange(AdjacentFree(picks[0]));
                    break;
            }
            return result;
        }

        List<Coord> VolleyTargets(Unit archer)
        {
            var result = new List<Coord>();
            if (archer == null) return result;
            foreach (var d in s_diagonal)
                for (int step = 1; step <= 3; step++)
                {
                    var c = archer.Pos + d * step;
                    if (!c.InBoard || HasPalisade(c)) break;
                    var occupant = At(c);
                    if (occupant == null) continue;
                    if (occupant.Side != archer.Side && occupant.Type != UnitType.Sovereign) result.Add(c);
                    break;
                }
            return result;
        }

        List<Coord> AdjacentFree(Coord c)
        {
            var result = new List<Coord>();
            foreach (var d in s_all) if (IsFree(c + d)) result.Add(c + d);
            return result;
        }

        public bool PlayCard(CardId card, IReadOnlyList<Coord> picks)
        {
            if (!CanPlay(card) || picks.Count != PickCount(card)) return false;
            var chosen = new List<Coord>();
            foreach (var pick in picks)
            {
                if (!CardTargets(card, chosen).Contains(pick)) return false;
                chosen.Add(pick);
            }

            var p = CurrentPlayer;
            p.Morale -= CardCatalog.Get(card).Cost;
            p.Hand.Remove(card);
            p.Discard.Add(card);
            p.PlayedProactive = true;
            var target = picks.Count > 0 ? At(picks[0]) : null;
            // Flag = the card targets a unit (Unit/To = that unit).
            AddLog(LogKind.CardPlayed, Current, card: card, flag: target != null,
                unit: target?.Type ?? default, to: target?.Pos ?? default);
            Emit(EventKind.CardPlayed, target?.Id ?? -1);

            switch (card)
            {
                case CardId.ReinforcedShield:
                case CardId.ReinforcedArmor:
                    target.Protection = 1;
                    Emit(EventKind.Equipped, target.Id, target.Pos, target.Pos);
                    break;
                case CardId.WarHammer:
                    target.HasHammer = true;
                    Emit(EventKind.Equipped, target.Id, target.Pos, target.Pos);
                    break;
                case CardId.ImprovisedPalisade:
                    _palisades.Add(new Palisade { Pos = picks[0], Owner = Current, ExpiresTurn = Turn + 2 });
                    AddLog(LogKind.PalisadeRaised, Current, to: picks[0]);
                    Emit(EventKind.PalisadeRaised, -1, picks[0], picks[0]);
                    break;
                case CardId.ForcedMarch:
                    p.BonusMoves++;
                    break;
                case CardId.ExtraRations:
                {
                    var from = target.Pos;
                    target.Pos = picks[1];
                    AddLog(LogKind.ExtraStep, Current, unit: target.Type, from: from, to: picks[1]);
                    Emit(EventKind.Moved, target.Id, from, picks[1]);
                    break;
                }
                case CardId.CrossbowVolley:
                    p.Orders--;
                    StartCapture(target, At(picks[1]), ranged: true);
                    break;
                case CardId.SeersOmen:
                    _foresight.Clear();
                    for (int i = 0; i < 3 && p.Deck.Count > 0; i++)
                    {
                        _foresight.Add(p.Deck[p.Deck.Count - 1]);
                        p.Deck.RemoveAt(p.Deck.Count - 1);
                    }
                    Phase = Phase.Foresight;
                    break;
            }
            return true;
        }

        /// <summary>Seer's Omen: draws the chosen card; the others go to the bottom of the deck.</summary>
        public bool ChooseForesight(int index)
        {
            if (Phase != Phase.Foresight || index < 0 || index >= _foresight.Count) return false;
            var p = CurrentPlayer;
            p.Hand.Add(_foresight[index]);
            for (int i = 0; i < _foresight.Count; i++)
                if (i != index) p.Deck.Insert(0, _foresight[i]);
            _foresight.Clear();
            Phase = Phase.Maneuvers;
            AddLog(LogKind.ForesightDrawn, Current);
            return true;
        }

        // ------------------------------------------------------------------ history & events

        void AddLog(LogKind kind, Side side, UnitType unit = default, Coord from = default, Coord to = default,
            UnitType otherUnit = default, CardId card = default, int amount = 0, bool flag = false) =>
            _log.Add(new LogEntry(Turn, kind, side, unit, from, to, otherUnit, card, amount, flag));

        void Emit(EventKind kind, int unitId = -1, Coord from = default, Coord to = default) =>
            _events.Add(new MatchEvent(kind, unitId, from, to));

        /// <summary>Returns and clears the events accumulated since the last call.</summary>
        public List<MatchEvent> DrainEvents()
        {
            var copy = new List<MatchEvent>(_events);
            _events.Clear();
            return copy;
        }
    }
}
