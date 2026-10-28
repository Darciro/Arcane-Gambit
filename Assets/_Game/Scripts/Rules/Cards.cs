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
    }

    public enum CardKind { Reaction, Equipment, Tactic, Supply, Fortification, Arcane }

    /// <summary>Gameplay data of a card. Names and rules text live in the presentation's localization tables.</summary>
    public sealed class CardInfo
    {
        public CardId Id { get; }
        public CardKind Kind { get; }
        public int Cost { get; }

        public bool IsReaction => Kind == CardKind.Reaction;

        public CardInfo(CardId id, CardKind kind, int cost)
        {
            Id = id; Kind = kind; Cost = cost;
        }
    }

    /// <summary>Card catalog (values from the GDD "Arsenal" sheet). Effects are resolved in <see cref="Match"/>.</summary>
    public static class CardCatalog
    {
        static readonly Dictionary<CardId, CardInfo> s_cards = new Dictionary<CardId, CardInfo>
        {
            [CardId.RaiseShields] = new CardInfo(CardId.RaiseShields, CardKind.Reaction, 1),
            [CardId.OrganizedRetreat] = new CardInfo(CardId.OrganizedRetreat, CardKind.Reaction, 2),
            [CardId.BracedSpears] = new CardInfo(CardId.BracedSpears, CardKind.Reaction, 1),
            [CardId.SovereignsGuard] = new CardInfo(CardId.SovereignsGuard, CardKind.Reaction, 2),
            [CardId.ReinforcedShield] = new CardInfo(CardId.ReinforcedShield, CardKind.Equipment, 2),
            [CardId.ReinforcedArmor] = new CardInfo(CardId.ReinforcedArmor, CardKind.Equipment, 2),
            [CardId.WarHammer] = new CardInfo(CardId.WarHammer, CardKind.Equipment, 2),
            [CardId.ExtraRations] = new CardInfo(CardId.ExtraRations, CardKind.Supply, 1),
            [CardId.ForcedMarch] = new CardInfo(CardId.ForcedMarch, CardKind.Tactic, 2),
            [CardId.CrossbowVolley] = new CardInfo(CardId.CrossbowVolley, CardKind.Tactic, 2),
            [CardId.ImprovisedPalisade] = new CardInfo(CardId.ImprovisedPalisade, CardKind.Fortification, 2),
            [CardId.SeersOmen] = new CardInfo(CardId.SeersOmen, CardKind.Arcane, 3),
        };

        public static CardInfo Get(CardId id) => s_cards[id];

        /// <summary>Offensive deck for the White army (16 cards).</summary>
        public static List<CardId> Vanguard() => Build(
            (CardId.RaiseShields, 2), (CardId.OrganizedRetreat, 2), (CardId.BracedSpears, 1),
            (CardId.SovereignsGuard, 2), (CardId.ReinforcedShield, 1), (CardId.WarHammer, 1),
            (CardId.ExtraRations, 2), (CardId.ForcedMarch, 2), (CardId.CrossbowVolley, 2),
            (CardId.SeersOmen, 1));

        /// <summary>Defensive deck for the Black army (16 cards).</summary>
        public static List<CardId> Bastion() => Build(
            (CardId.RaiseShields, 2), (CardId.OrganizedRetreat, 1), (CardId.BracedSpears, 2),
            (CardId.SovereignsGuard, 2), (CardId.ReinforcedShield, 2), (CardId.ReinforcedArmor, 2),
            (CardId.ImprovisedPalisade, 2), (CardId.ForcedMarch, 1), (CardId.CrossbowVolley, 1),
            (CardId.SeersOmen, 1));

        static List<CardId> Build(params (CardId id, int copies)[] entries)
        {
            var list = new List<CardId>();
            foreach (var (id, copies) in entries)
                for (int i = 0; i < copies; i++) list.Add(id);
            return list;
        }
    }
}
