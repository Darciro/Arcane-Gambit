using System.Collections.Generic;

namespace ArcaneGambit.Rules
{
    /// <summary>The enum name is also the illustration name (Resources/Art/Card_{Id}) and the localization key.</summary>
    public enum CardId
    {
        RaiseShields, OrganizedRetreat, BracedSpears, SovereignsGuard,
        ReinforcedShield, ReinforcedArmor, WarHammer,
        ExtraRations, ForcedMarch, CrossbowVolley, ImprovisedPalisade,
        SeersOmen,
        CloseRanks, Countercharge, SmokeScreen, HeroicInterposition,
        CampaignHorseshoes, CrossbowPavise, MarshalsBanner, JoustingLance,
        CavalryFeint, CoordinatedAdvance, SiegeOrder, LineSwap,
        ScoutsAhead, SupplyWagon,
        AntiCavalryStakes, ShallowDitch,
        Fireball, DawnPrayer, VigilSeal, OracleEcho,
        Blizzard, Gale, LightningStorm, Fog, Mud, MoonlessNight,
    }

    public enum CardKind { Reaction, Equipment, Tactic, Supply, Fortification, Arcane, Condition }

    public enum PieceLink { None, Or, And, Versus }

    /// <summary>The pieces printed beside a card's type line. With Versus, the second piece is the enemy's.</summary>
    public readonly struct PieceRequirement
    {
        public readonly UnitType First, Second;
        public readonly PieceLink Link;
        public readonly bool Enemy;

        public PieceRequirement(UnitType first, PieceLink link, UnitType second, bool enemy)
        {
            First = first; Link = link; Second = second; Enemy = enemy;
        }

        public bool IsEnemy(int index) => Enemy || (index == 1 && Link == PieceLink.Versus);
    }

    public sealed class CardInfo
    {
        readonly bool _reaction;

        public CardId Id { get; }
        public CardKind Kind { get; }
        public int Cost { get; }
        public int MinCost { get; }
        public bool CostUndefined { get; }
        public bool IsRitual { get; }
        public PieceRequirement? Pieces { get; private set; }

        public bool IsReaction => Kind == CardKind.Reaction || _reaction;
        public bool IsWeather => Kind == CardKind.Condition;

        public CardInfo(CardId id, CardKind kind, int cost, bool reaction = false, int? minCost = null,
            bool costUndefined = false, bool ritual = false)
        {
            Id = id; Kind = kind; Cost = cost; _reaction = reaction;
            MinCost = minCost ?? cost; CostUndefined = costUndefined; IsRitual = ritual;
        }

        internal CardInfo Needs(UnitType first, PieceLink link = PieceLink.None, UnitType second = default)
        {
            Pieces = new PieceRequirement(first, link, second, false);
            return this;
        }

        internal CardInfo Targets(UnitType first, PieceLink link = PieceLink.None, UnitType second = default)
        {
            Pieces = new PieceRequirement(first, link, second, true);
            return this;
        }
    }

    public static class CardCatalog
    {
        static readonly Dictionary<CardId, CardInfo> s_cards = new Dictionary<CardId, CardInfo>();

        static CardCatalog()
        {
            Add(CardId.RaiseShields, CardKind.Reaction, 1).Needs(UnitType.Infantry);
            Add(CardId.OrganizedRetreat, CardKind.Reaction, 2);
            Add(CardId.BracedSpears, CardKind.Reaction, 1, minCost: 0)
                .Needs(UnitType.Infantry, PieceLink.Versus, UnitType.Cavalry);
            Add(CardId.SovereignsGuard, CardKind.Reaction, 2).Needs(UnitType.Infantry, PieceLink.And, UnitType.Sovereign);
            Add(CardId.CloseRanks, CardKind.Reaction, 1).Needs(UnitType.Infantry);
            Add(CardId.Countercharge, CardKind.Reaction, 2).Needs(UnitType.Cavalry);
            Add(CardId.SmokeScreen, CardKind.Reaction, 2);
            Add(CardId.HeroicInterposition, CardKind.Reaction, 2).Needs(UnitType.Infantry);
            Add(CardId.ReinforcedShield, CardKind.Equipment, 2);
            Add(CardId.ReinforcedArmor, CardKind.Equipment, 2).Needs(UnitType.SiegeEngineers);
            Add(CardId.WarHammer, CardKind.Equipment, 2).Needs(UnitType.Cavalry, PieceLink.Or, UnitType.Marshal);
            Add(CardId.CampaignHorseshoes, CardKind.Equipment, 1).Needs(UnitType.Cavalry);
            Add(CardId.CrossbowPavise, CardKind.Equipment, 2).Needs(UnitType.Crossbowmen);
            Add(CardId.MarshalsBanner, CardKind.Equipment, 3).Needs(UnitType.Marshal, PieceLink.And, UnitType.Infantry);
            Add(CardId.JoustingLance, CardKind.Equipment, 2).Needs(UnitType.Cavalry);
            Add(CardId.ForcedMarch, CardKind.Tactic, 2);
            Add(CardId.CrossbowVolley, CardKind.Tactic, 2).Needs(UnitType.Crossbowmen);
            Add(CardId.CavalryFeint, CardKind.Tactic, 2).Needs(UnitType.Cavalry);
            Add(CardId.CoordinatedAdvance, CardKind.Tactic, 2).Needs(UnitType.Infantry);
            Add(CardId.SiegeOrder, CardKind.Tactic, 2).Needs(UnitType.SiegeEngineers);
            Add(CardId.LineSwap, CardKind.Tactic, 1).Needs(UnitType.Infantry);
            Add(CardId.ExtraRations, CardKind.Supply, 1);
            Add(CardId.ScoutsAhead, CardKind.Supply, 1);
            Add(CardId.SupplyWagon, CardKind.Supply, 2);
            Add(CardId.ImprovisedPalisade, CardKind.Fortification, 2)
                .Needs(UnitType.Infantry, PieceLink.Or, UnitType.SiegeEngineers);
            Add(CardId.AntiCavalryStakes, CardKind.Fortification, 2)
                .Needs(UnitType.Infantry, PieceLink.Versus, UnitType.Cavalry);
            Add(CardId.ShallowDitch, CardKind.Fortification, 2).Needs(UnitType.SiegeEngineers);
            Add(CardId.SeersOmen, CardKind.Arcane, 3);
            Add(CardId.Fireball, CardKind.Arcane, 3, ritual: true)
                .Targets(UnitType.Infantry, PieceLink.Or, UnitType.Crossbowmen);
            Add(CardId.DawnPrayer, CardKind.Arcane, 2);
            Add(CardId.VigilSeal, CardKind.Arcane, 3, reaction: true).Needs(UnitType.Sovereign);
            Add(CardId.OracleEcho, CardKind.Arcane, 2, ritual: true);
            Add(CardId.Blizzard, CardKind.Condition, 3);
            Add(CardId.Gale, CardKind.Condition, 2);
            Add(CardId.LightningStorm, CardKind.Condition, 3);
            Add(CardId.Fog, CardKind.Condition, 2);
            // The card sheet leaves these two without a cost ("—"); they play for free until it defines one.
            Add(CardId.Mud, CardKind.Condition, 0, costUndefined: true);
            Add(CardId.MoonlessNight, CardKind.Condition, 0, costUndefined: true);
        }

        static CardInfo Add(CardId id, CardKind kind, int cost, bool reaction = false, int? minCost = null,
            bool costUndefined = false, bool ritual = false) =>
            s_cards[id] = new CardInfo(id, kind, cost, reaction, minCost, costUndefined, ritual);

        public static CardInfo Get(CardId id) => s_cards[id];

        public static List<CardId> Vanguard() => Build(
            (CardId.RaiseShields, 2), (CardId.BracedSpears, 1),
            (CardId.SovereignsGuard, 1), (CardId.HeroicInterposition, 1), (CardId.Countercharge, 1),
            (CardId.ReinforcedShield, 1), (CardId.WarHammer, 1), (CardId.CampaignHorseshoes, 1),
            (CardId.JoustingLance, 1), (CardId.MarshalsBanner, 1),
            (CardId.ForcedMarch, 1), (CardId.CrossbowVolley, 2), (CardId.CavalryFeint, 1), (CardId.CoordinatedAdvance, 1),
            (CardId.ExtraRations, 1), (CardId.ScoutsAhead, 1), (CardId.SupplyWagon, 1),
            (CardId.SeersOmen, 1), (CardId.Fireball, 1),
            (CardId.Gale, 1), (CardId.LightningStorm, 1), (CardId.MoonlessNight, 1));

        public static List<CardId> Bastion() => Build(
            (CardId.RaiseShields, 2), (CardId.OrganizedRetreat, 1), (CardId.BracedSpears, 1),
            (CardId.SovereignsGuard, 1), (CardId.CloseRanks, 1), (CardId.SmokeScreen, 1),
            (CardId.ReinforcedShield, 1), (CardId.ReinforcedArmor, 1), (CardId.CrossbowPavise, 1),
            (CardId.SiegeOrder, 1), (CardId.LineSwap, 1), (CardId.CrossbowVolley, 1),
            (CardId.SupplyWagon, 1),
            (CardId.ImprovisedPalisade, 2), (CardId.AntiCavalryStakes, 1), (CardId.ShallowDitch, 1),
            (CardId.VigilSeal, 1), (CardId.OracleEcho, 1), (CardId.DawnPrayer, 1),
            (CardId.Blizzard, 1), (CardId.Fog, 1), (CardId.Mud, 1));

        static List<CardId> Build(params (CardId id, int copies)[] entries)
        {
            var list = new List<CardId>();
            foreach (var (id, copies) in entries)
                for (int i = 0; i < copies; i++) list.Add(id);
            return list;
        }
    }
}
