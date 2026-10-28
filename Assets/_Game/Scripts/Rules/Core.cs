using System;
using System.Collections.Generic;

namespace ArcaneGambit.Rules
{
    public enum Side { White, Black }

    public enum UnitType { Infantry, Cavalry, Crossbowmen, SiegeEngineers, Marshal, Sovereign }

    /// <summary>Maneuvers = active player acts; Reaction = defender's confrontation window;
    /// Foresight = choosing a card from Seer's Omen; GameOver = a Sovereign was captured.</summary>
    public enum Phase { Maneuvers, Reaction, Foresight, GameOver }

    /// <summary>Board square. X = file (a..h), Y = rank (1..8), both zero-based. White plays from the bottom (Y = 0).</summary>
    public readonly struct Coord : IEquatable<Coord>
    {
        public readonly int X;
        public readonly int Y;

        public Coord(int x, int y) { X = x; Y = y; }

        public bool InBoard => X >= 0 && X < Match.Size && Y >= 0 && Y < Match.Size;

        public static Coord operator +(Coord a, Coord b) => new Coord(a.X + b.X, a.Y + b.Y);
        public static Coord operator *(Coord a, int k) => new Coord(a.X * k, a.Y * k);
        public static bool operator ==(Coord a, Coord b) => a.Equals(b);
        public static bool operator !=(Coord a, Coord b) => !a.Equals(b);

        public bool Equals(Coord other) => X == other.X && Y == other.Y;
        public override bool Equals(object obj) => obj is Coord c && Equals(c);
        public override int GetHashCode() => X * 31 + Y;

        /// <summary>Algebraic notation ("e4"); language-neutral.</summary>
        public override string ToString() => $"{(char)('a' + X)}{Y + 1}";

        /// <summary>"e4" -> Coord(4, 3).</summary>
        public static Coord Parse(string square) => new Coord(square[0] - 'a', square[1] - '1');
    }

    public static class SideExtensions
    {
        public static Side Other(this Side side) => side == Side.White ? Side.Black : Side.White;

        /// <summary>Forward direction (towards the enemy).</summary>
        public static int Forward(this Side side) => side == Side.White ? 1 : -1;
    }

    public sealed class Unit
    {
        public int Id { get; internal set; }
        public Side Side { get; internal set; }
        public UnitType Type { get; internal set; }
        public Coord Pos { get; internal set; }

        /// <summary>Turn in which it was recruited; it cannot act during that turn.</summary>
        public int RecruitedTurn { get; internal set; } = -1;
        public bool MovedThisTurn { get; internal set; }

        /// <summary>Protection charges (Reinforced Shield / Reinforced Armor). At most 1 per category.</summary>
        public int Protection { get; set; }
        public bool HasHammer { get; set; }

        public override string ToString() => $"{Type} {Pos}";
    }

    public sealed class Player
    {
        public Side Side { get; }
        public int Morale { get; set; }
        public int Orders { get; set; }
        /// <summary>Extra moves granted by cards (Forced March); spent before Orders.</summary>
        public int BonusMoves { get; set; }
        public bool PlayedProactive { get; set; }

        public Dictionary<UnitType, int> Reserve { get; } = new Dictionary<UnitType, int>();
        /// <summary>Top of the deck = last element.</summary>
        public List<CardId> Deck { get; } = new List<CardId>();
        public List<CardId> Hand { get; } = new List<CardId>();
        public List<CardId> Discard { get; } = new List<CardId>();

        public Player(Side side) { Side = side; }
    }

    public sealed class PendingCapture
    {
        public Unit Attacker;
        public Unit Defender;
        public Coord From;
        public Coord Target;
        /// <summary>Ranged attack (Crossbow Volley): the attacker does not advance.</summary>
        public bool Ranged;
    }

    public sealed class Palisade
    {
        public Coord Pos;
        public Side Owner;
        public int ExpiresTurn;
    }

    public enum EventKind
    {
        TurnStarted, Recruited, Moved, CaptureDeclared, Captured, Blocked,
        Retreated, Intercepted, CardPlayed, Equipped, PalisadeRaised, PalisadeFell, GameOver,
    }

    /// <summary>Something that happened in the rules; the presentation drains the queue to animate it (later: Feel).</summary>
    public readonly struct MatchEvent
    {
        public readonly EventKind Kind;
        public readonly int UnitId;
        public readonly Coord From;
        public readonly Coord To;

        public MatchEvent(EventKind kind, int unitId = -1, Coord from = default, Coord to = default)
        {
            Kind = kind; UnitId = unitId; From = from; To = to;
        }
    }

    public enum LogKind
    {
        Preparation, Recruited, Moved, CaptureDeclared, Reacted, Retreated, Intercepted,
        ProtectionBroken, Blocked, UnitCaptured, Victory, CardPlayed, PalisadeRaised, ExtraStep, ForesightDrawn,
    }

    /// <summary>
    /// Structured history entry. The rules never produce display text: the presentation formats each
    /// <see cref="LogKind"/> in the active language. Field meaning per kind is documented at each call site in <see cref="Match"/>.
    /// </summary>
    public sealed class LogEntry
    {
        public readonly int Turn;
        public readonly LogKind Kind;
        public readonly Side Side;
        public readonly UnitType Unit;
        public readonly Coord From;
        public readonly Coord To;
        public readonly UnitType OtherUnit;
        public readonly CardId Card;
        public readonly int Amount;
        public readonly bool Flag;

        public LogEntry(int turn, LogKind kind, Side side, UnitType unit = default, Coord from = default,
            Coord to = default, UnitType otherUnit = default, CardId card = default, int amount = 0, bool flag = false)
        {
            Turn = turn; Kind = kind; Side = side; Unit = unit; From = from; To = to;
            OtherUnit = otherUnit; Card = card; Amount = amount; Flag = flag;
        }
    }
}
