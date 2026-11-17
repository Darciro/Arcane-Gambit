using System.Collections.Generic;

namespace ArcaneGambit.Rules
{
    public enum CommandKind : byte { Recruit, Move, Capture, PlayCard, EndTurn, React, Foresight }

    /// <summary>
    /// One player decision. Matches built from the same seed stay identical as long as they apply the same
    /// commands in the same order, which is what lets two machines play one match by exchanging only these.
    /// </summary>
    public readonly struct Command
    {
        public readonly CommandKind Kind;
        public readonly UnitType Unit;
        /// <summary>The acting unit for Move and Capture; the chosen option for Foresight.</summary>
        public readonly int Index;
        public readonly Coord To;
        /// <summary>Null on React means letting the capture through.</summary>
        public readonly CardId? Card;
        public readonly Coord[] Picks;

        public Command(CommandKind kind, UnitType unit = default, int index = -1, Coord to = default,
            CardId? card = null, Coord[] picks = null)
        {
            Kind = kind; Unit = unit; Index = index; To = to; Card = card;
            Picks = picks ?? System.Array.Empty<Coord>();
        }

        public static Command Recruit(UnitType type, Coord to) => new Command(CommandKind.Recruit, unit: type, to: to);
        public static Command Move(Unit unit, Coord to) => new Command(CommandKind.Move, index: unit.Id, to: to);
        public static Command Capture(Unit attacker, Coord target) => new Command(CommandKind.Capture, index: attacker.Id, to: target);
        public static Command EndTurn() => new Command(CommandKind.EndTurn);
        public static Command React(CardId? card) => new Command(CommandKind.React, card: card);
        public static Command Foresight(int option) => new Command(CommandKind.Foresight, index: option);

        public static Command Play(CardId card, IReadOnlyList<Coord> picks)
        {
            var copy = new Coord[picks.Count];
            for (int i = 0; i < copy.Length; i++) copy[i] = picks[i];
            return new Command(CommandKind.PlayCard, card: card, picks: copy);
        }

        /// <summary>Applies the command if <paramref name="by"/> is the side the match is waiting on.</summary>
        public bool Apply(Match m, Side by)
        {
            if (Lockstep.Actor(m) != by) return false;
            switch (Kind)
            {
                case CommandKind.Recruit: return m.Recruit(Unit, To);
                case CommandKind.Move: return m.CanAct(m.FindUnit(Index)) && m.Move(m.FindUnit(Index), To);
                case CommandKind.Capture: return m.CanAct(m.FindUnit(Index)) && m.DeclareCapture(m.FindUnit(Index), To);
                case CommandKind.PlayCard: return Card is CardId card && m.PlayCard(card, Picks);
                case CommandKind.EndTurn: return m.EndTurn();
                case CommandKind.React: return m.React(Card);
                case CommandKind.Foresight: return m.ChooseForesight(Index);
                default: return false;
            }
        }

        public override string ToString() => $"{Kind} unit={Unit} index={Index} to={To} card={Card} picks={Picks.Length}";
    }

    public static class Lockstep
    {
        /// <summary>The side whose decision the match is waiting on; null once it is over.</summary>
        public static Side? Actor(Match m) => m.Phase switch
        {
            Phase.Maneuvers or Phase.Foresight => m.Current,
            Phase.Reaction => m.Pending.Defender.Side,
            _ => null,
        };

        /// <summary>A cheap digest of the visible state, compared across machines to catch a desync early.</summary>
        public static int Fingerprint(Match m)
        {
            unchecked
            {
                int h = (int)2166136261;
                void Mix(int v) => h = (h ^ v) * 16777619;

                Mix(m.Turn);
                Mix((int)m.Phase);
                Mix((int)m.Current);
                foreach (var u in m.Units)
                {
                    Mix(u.Id);
                    Mix((int)u.Type | (int)u.Side << 4);
                    Mix(u.Pos.X | u.Pos.Y << 4);
                    Mix(u.Protection | u.RangedProtection << 4);
                }
                foreach (var marker in m.Markers) Mix((int)marker.Kind | marker.Pos.X << 4 | marker.Pos.Y << 8);
                for (int s = 0; s < 2; s++)
                {
                    var p = m.Get((Side)s);
                    Mix(p.Morale);
                    Mix(p.Orders);
                    Mix(p.Deck.Count);
                    foreach (var card in p.Hand) Mix((int)card);
                }
                return h;
            }
        }
    }
}
