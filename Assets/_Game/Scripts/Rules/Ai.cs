using System;
using System.Collections.Generic;

namespace ArcaneGambit.Rules
{
    /// <summary>Only uses what a human player could see: never the opponent's hand or deck contents.</summary>
    public sealed class Ai
    {
        const float Win = 1000f;
        const int Shortlist = 8;
        const float Threshold = 0.05f;
        const float Noise = 0.04f;
        const float ReactChance = 0.3f;

        public enum ChoiceKind { Recruit, Move, Capture, Card, EndTurn, React, Foresight }

        /// <summary>A decision made by <see cref="Plan"/>, shown to the player before <see cref="Execute"/> applies it.</summary>
        public sealed class Choice
        {
            public ChoiceKind Kind;
            public UnitType Type;
            public int UnitId;
            public Coord To;
            public CardId Card;
            public Coord[] Picks;
            public CardId? Reaction;
            public int Index;

            public bool IsAttack => Kind == ChoiceKind.Capture || (Kind == ChoiceKind.Card && Match.DeclaresCapture(Card));
        }

        readonly Random _rng;

        public Side Side { get; }

        public Ai(Side side, int seed)
        {
            Side = side;
            _rng = new Random(seed);
        }

        public bool HasDecision(Match m) => m.Phase switch
        {
            Phase.Maneuvers or Phase.Foresight => m.Current == Side,
            Phase.Reaction => m.Pending.Defender.Side == Side,
            _ => false,
        };

        public bool Act(Match m) => Execute(m, Plan(m));

        public Choice Plan(Match m)
        {
            if (!HasDecision(m)) return null;
            return m.Phase switch
            {
                Phase.Reaction => new Choice { Kind = ChoiceKind.React, Reaction = ChooseReaction(m) },
                Phase.Foresight => new Choice { Kind = ChoiceKind.Foresight, Index = ChooseForesight(m) },
                _ => ChooseCommand(m) ?? new Choice { Kind = ChoiceKind.EndTurn },
            };
        }

        public bool Execute(Match m, Choice c)
        {
            if (c == null || !HasDecision(m)) return false;
            return c.Kind switch
            {
                ChoiceKind.React => m.React(c.Reaction),
                ChoiceKind.Foresight => m.ChooseForesight(c.Index),
                ChoiceKind.EndTurn => m.EndTurn(),
                _ => Apply(m, c) || m.EndTurn(),
            };
        }

        Choice ChooseCommand(Match m)
        {
            float stay = Evaluate(m);
            var options = new List<(Choice choice, float score)>();
            foreach (var c in Choices(m)) options.Add((c, Score(m, c, stay, lookAhead: false)));
            options.Sort((a, b) => b.score.CompareTo(a.score));

            Choice best = null;
            float bestScore = stay + Threshold;
            for (int i = 0; i < options.Count; i++)
            {
                var (choice, score) = options[i];
                if (i < Shortlist) score = Math.Max(score, Score(m, choice, stay, lookAhead: true));
                score += (float)_rng.NextDouble() * Noise;
                if (score > bestScore)
                {
                    best = choice;
                    bestScore = score;
                }
            }
            return best;
        }

        CardId? ChooseReaction(Match m)
        {
            CardId? best = null;
            float bestScore = AfterReaction(m, null);
            foreach (var id in m.ReactionOptions())
            {
                float score = AfterReaction(m, id);
                if (score > bestScore)
                {
                    best = id;
                    bestScore = score;
                }
            }
            return best;
        }

        float AfterReaction(Match m, CardId? card)
        {
            var sim = m.Clone();
            sim.React(card);
            return Evaluate(sim);
        }

        int ChooseForesight(Match m)
        {
            int best = 0;
            float bestScore = float.NegativeInfinity;
            for (int i = 0; i < m.ForesightOptions.Count; i++)
            {
                var sim = m.Clone();
                sim.ChooseForesight(i);
                float score = Evaluate(sim);
                if (score > bestScore)
                {
                    best = i;
                    bestScore = score;
                }
            }
            return best;
        }

        float Score(Match m, Choice c, float stay, bool lookAhead)
        {
            var sim = m.Clone();
            if (!Apply(sim, c)) return float.NegativeInfinity;
            Settle(sim);
            float score = Evaluate(sim);
            if (lookAhead && sim.Phase == Phase.Maneuvers && sim.Current == Side)
            {
                float here = score;
                foreach (var next in Choices(sim)) score = Math.Max(score, Score(sim, next, here, lookAhead: false));
            }

            var foe = m.Get(Side.Other());
            if (c.IsAttack && foe.Morale > 0 && foe.Hand.Count > 0)
                score = (1f - ReactChance) * score + ReactChance * (stay + 0.4f);
            return score;
        }

        void Settle(Match sim)
        {
            if (sim.Phase == Phase.Reaction) sim.React(null);
            if (sim.Phase == Phase.Foresight) sim.ChooseForesight(ChooseForesight(sim));
        }

        List<Choice> Choices(Match m)
        {
            var list = new List<Choice>();
            foreach (var type in Match.Recruitable)
                if (m.CanRecruit(type))
                    foreach (var c in m.RecruitSquares())
                        list.Add(new Choice { Kind = ChoiceKind.Recruit, Type = type, To = c });

            foreach (var u in m.Units)
            {
                if (!m.CanAct(u)) continue;
                foreach (var c in m.MoveTargets(u)) list.Add(new Choice { Kind = ChoiceKind.Move, UnitId = u.Id, To = c });
                foreach (var c in m.CaptureTargets(u)) list.Add(new Choice { Kind = ChoiceKind.Capture, UnitId = u.Id, To = c });
            }

            var seen = new HashSet<CardId>();
            foreach (var id in m.CurrentPlayer.Hand)
                if (seen.Add(id) && m.CanPlay(id)) AddCardChoices(m, id, new List<Coord>(), list);
            return list;
        }

        static void AddCardChoices(Match m, CardId id, List<Coord> picks, List<Choice> list)
        {
            if (Match.PicksComplete(id, picks.Count))
                list.Add(new Choice { Kind = ChoiceKind.Card, Card = id, Picks = picks.ToArray() });
            if (picks.Count == Match.PickCount(id)) return;
            foreach (var c in m.CardTargets(id, picks))
            {
                picks.Add(c);
                AddCardChoices(m, id, picks, list);
                picks.RemoveAt(picks.Count - 1);
            }
        }

        static bool Apply(Match m, Choice c) => c.Kind switch
        {
            ChoiceKind.Recruit => m.Recruit(c.Type, c.To),
            ChoiceKind.Move => m.Move(m.FindUnit(c.UnitId), c.To),
            ChoiceKind.Capture => m.DeclareCapture(m.FindUnit(c.UnitId), c.To),
            ChoiceKind.Card => m.PlayCard(c.Card, c.Picks),
            _ => false,
        };

        float Evaluate(Match m)
        {
            if (m.Winner.HasValue) return m.Winner == Side ? Win : -Win;

            var me = m.Get(Side);
            var foe = m.Get(Side.Other());
            Unit mySovereign = null, foeSovereign = null;
            foreach (var u in m.Units)
                if (u.Type == UnitType.Sovereign)
                {
                    if (u.Side == Side) mySovereign = u;
                    else foeSovereign = u;
                }

            float score = 0f;
            foreach (var u in m.Units)
            {
                float value = Worth(u) + Placement(m, u, u.Side == Side ? foeSovereign : mySovereign);
                score += u.Side == Side ? value : -value;
            }

            bool holdsReaction = false;
            foreach (var id in me.Hand)
            {
                score += CardWorth(id);
                holdsReaction |= CardCatalog.Get(id).IsReaction;
            }
            score -= foe.Hand.Count * 0.45f;
            score += MoraleWorth(me.Morale, holdsReaction) - MoraleWorth(foe.Morale, foe.Hand.Count > 0);

            return score - Danger(m, me, mySovereign) + Pressure(m);
        }

        static float Value(UnitType type) => type switch
        {
            UnitType.Infantry => 1f,
            UnitType.Crossbowmen => 2.2f,
            UnitType.Cavalry => 3f,
            UnitType.SiegeEngineers => 3.2f,
            UnitType.Marshal => 5f,
            _ => 0f,
        };

        static float Worth(Unit u)
        {
            float value = Value(u.Type);
            if (u.Protection > 0) value += u.Type == UnitType.Sovereign ? 2.5f : 0.3f + 0.25f * value;
            if (u.RangedProtection > 0) value += 0.1f + 0.1f * value;
            if (u.HasHammer && u.Type != UnitType.Sovereign) value += 0.5f;
            if (u.HasLance) value += 0.5f;
            if (u.HasHorseshoes) value += 0.3f;
            if (u.HasBanner) value += 0.4f;
            return value;
        }

        static float Placement(Match m, Unit u, Unit enemySovereign)
        {
            if (u.Type == UnitType.Sovereign)
            {
                float escort = 0f;
                foreach (var other in m.Units)
                    if (other.Side == u.Side && other != u && Distance(other.Pos, u.Pos) == 1)
                        escort += other.Type == UnitType.Infantry ? 0.25f : 0.15f;
                return Math.Min(escort, 0.6f);
            }

            float value = 0f;
            if (enemySovereign != null) value += 0.06f * (Match.Size - 1 - Distance(u.Pos, enemySovereign.Pos));
            if (u.Type == UnitType.Infantry)
                value += 0.05f * (u.Side == Side.White ? u.Pos.Y : Match.Size - 1 - u.Pos.Y);
            return value;
        }

        static float CardWorth(CardId id) => id switch
        {
            CardId.SovereignsGuard => 0.9f,
            CardId.RaiseShields => 0.7f,
            CardId.OrganizedRetreat => 0.6f,
            CardId.ReinforcedShield => 0.6f,
            CardId.BracedSpears => 0.5f,
            CardId.SeersOmen => 0.5f,
            CardId.HeroicInterposition => 0.7f,
            CardId.CloseRanks => 0.6f,
            CardId.VigilSeal => 0.6f,
            _ => 0.4f,
        };

        static float MoraleWorth(int morale, bool holdsReaction)
        {
            float first = holdsReaction ? 0.8f : 0.45f;
            return first * Math.Min(morale, 2) + 0.4f * Math.Max(0, Math.Min(morale, 6) - 2) + 0.1f * Math.Max(0, morale - 6);
        }

        static int Distance(Coord a, Coord b) => Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));

        float Danger(Match m, Player me, Unit mySovereign)
        {
            var cheapestAttacker = new Dictionary<Coord, float>();
            var attackerHammer = new HashSet<Coord>();
            var defended = new HashSet<Coord>();
            foreach (var u in m.Units)
            {
                var squares = m.ThreatSquares(u, u.Pos);
                if (u.Side == Side)
                {
                    foreach (var c in squares) defended.Add(c);
                    continue;
                }
                float value = Value(u.Type);
                foreach (var c in squares)
                {
                    if (!cheapestAttacker.TryGetValue(c, out float best) || value < best) cheapestAttacker[c] = value;
                    if (u.HasHammer) attackerHammer.Add(c);
                }
            }

            float first = 0f, second = 0f;
            foreach (var u in m.Units)
            {
                if (u.Side != Side || u.Type == UnitType.Sovereign) continue;
                if (!cheapestAttacker.TryGetValue(u.Pos, out float attacker)) continue;

                float loss = Value(u.Type);
                if (defended.Contains(u.Pos)) loss = Math.Max(0f, loss - attacker) + 0.1f;
                if (u.Protection > 0 && !attackerHammer.Contains(u.Pos)) loss = 0.3f + 0.25f * Value(u.Type);
                if (CanShield(m, me, u)) loss *= 0.45f;

                if (loss > first) { second = first; first = loss; }
                else if (loss > second) second = loss;
            }
            float danger = 0.85f * first + 0.5f * second;

            if (mySovereign != null)
            {
                float exposure = cheapestAttacker.ContainsKey(mySovereign.Pos) ? 80f
                    : OneMoveFromSovereign(m, mySovereign) ? 20f : 0f;
                if (exposure > 0f && mySovereign.Protection > 0) exposure *= 0.1f;
                else if (exposure > 0f && CanGuardSovereign(m, me, mySovereign)) exposure *= 0.2f;
                danger += exposure;
            }
            return danger;
        }

        float Pressure(Match m)
        {
            var threatened = new HashSet<Coord>();
            foreach (var u in m.Units)
                if (u.Side == Side)
                    foreach (var c in m.ThreatSquares(u, u.Pos)) threatened.Add(c);

            float pressure = 0f;
            foreach (var u in m.Units)
                if (u.Side != Side && threatened.Contains(u.Pos))
                    pressure += u.Type == UnitType.Sovereign ? 1.5f : u.Protection > 0 ? 0.03f : 0.12f * Value(u.Type);
            return pressure;
        }

        bool OneMoveFromSovereign(Match m, Unit sovereign)
        {
            foreach (var u in m.Units)
            {
                if (u.Side == Side || Distance(u.Pos, sovereign.Pos) > 4) continue;
                foreach (bool captures in new[] { false, true })
                    foreach (var step in m.Reach(u, captures))
                        if (m.ThreatSquares(u, step).Contains(sovereign.Pos)) return true;
            }
            return false;
        }

        static bool CanShield(Match m, Player me, Unit u)
        {
            foreach (var id in me.Hand)
            {
                if (me.Morale < CardCatalog.Get(id).Cost) continue;
                if (id == CardId.RaiseShields && u.Type == UnitType.Infantry) return true;
                if (id == CardId.OrganizedRetreat && m.RetreatSquare(u).HasValue) return true;
            }
            return false;
        }

        static bool CanGuardSovereign(Match m, Player me, Unit sovereign)
        {
            if (!me.Hand.Contains(CardId.SovereignsGuard) || me.Morale < CardCatalog.Get(CardId.SovereignsGuard).Cost)
                return false;
            foreach (var u in m.Units)
                if (u.Side == sovereign.Side && u.Type == UnitType.Infantry && Distance(u.Pos, sovereign.Pos) == 1)
                    return true;
            return false;
        }
    }
}
