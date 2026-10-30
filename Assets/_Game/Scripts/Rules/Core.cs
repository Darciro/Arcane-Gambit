using System;
using System.Collections.Generic;

namespace ArcaneGambit.Rules
{
    public enum Side { White, Black }

    public enum UnitType { Infantry, Cavalry, Crossbowmen, SiegeEngineers, Marshal, Sovereign }

    public enum Phase { Maneuvers, Reaction, Foresight, GameOver }

    /// <summary>X = file, Y = rank, both zero-based; White plays from Y = 0.</summary>
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

        public override string ToString() => $"{(char)('a' + X)}{Y + 1}";

        public static Coord Parse(string square) => new Coord(square[0] - 'a', square[1] - '1');
    }

    public static class SideExtensions
    {
        public static Side Other(this Side side) => side == Side.White ? Side.Black : Side.White;

        public static int Forward(this Side side) => side == Side.White ? 1 : -1;
    }

    public sealed class Unit
    {
        public int Id { get; internal set; }
        public Side Side { get; internal set; }
        public UnitType Type { get; internal set; }
        public Coord Pos { get; internal set; }

        public int RecruitedTurn { get; internal set; } = -1;
        public bool MovedThisTurn { get; internal set; }

        public int Protection { get; set; }
        /// <summary>Charges that only stop ranged captures (Reinforced Armor, Crossbow Pavise).</summary>
        public int RangedProtection { get; set; }
        public bool HasHammer { get; set; }
        public bool HasHorseshoes { get; set; }
        public bool HasLance { get; set; }
        public bool HasBanner { get; set; }

        /// <summary>A Cavalry that just made a non-capturing jump may take its horseshoe step.</summary>
        public bool Jumped { get; internal set; }
        /// <summary>Movement bonuses are refused up to and including this turn (Close Ranks).</summary>
        public int BonusLockTurn { get; internal set; }
        public int FeintTurn { get; internal set; }

        internal Unit Copy() => (Unit)MemberwiseClone();

        public override string ToString() => $"{Type} {Pos}";
    }

    public sealed class Player
    {
        public Side Side { get; }
        public int Morale { get; set; }
        public int Orders { get; set; }
        public int BonusMoves { get; set; }
        public bool PlayedProactive { get; set; }
        public bool WagonReady { get; set; }

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
        /// <summary>The card that declared the capture; null for an ordinary capture by a piece.</summary>
        public CardId? Card;

        public bool Ranged => Card == CardId.CrossbowVolley || Card == CardId.SiegeOrder;
        public bool Arcane => Card == CardId.Fireball;
        public bool Advances => !Ranged && !Arcane;
    }

    public enum MarkerKind { Palisade, Ditch, Stakes, Smoke, Lightning }

    public sealed class Marker
    {
        public MarkerKind Kind;
        public Coord Pos;
        public Side Owner;
        public int ExpiresTurn;
    }

    public enum EventKind
    {
        TurnStarted, Recruited, Moved, CaptureDeclared, Captured, Blocked,
        Retreated, Intercepted, CardPlayed, Equipped, MarkerPlaced, MarkerRemoved, GameOver,
    }

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
        ProtectionBroken, Blocked, UnitCaptured, Victory, CardPlayed, Fortified, ExtraStep, ForesightDrawn,
        Swapped, WeatherCleared, CardDrawn, LightningMarked, LightningStruck,
    }

    /// <summary>Field meaning per kind is documented at each call site in <see cref="Match"/>.</summary>
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
