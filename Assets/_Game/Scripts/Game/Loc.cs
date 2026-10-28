using System.Collections.Generic;
using ArcaneGambit.Rules;
using UnityEngine;

namespace ArcaneGambit.Game
{
    /// <summary>
    /// Localization: every player-visible string lives here, keyed by id. To add a language, add a table to
    /// <see cref="s_tables"/> with the same keys and set <see cref="Language"/>. Missing keys render as "#key".
    /// </summary>
    public static class Loc
    {
        public const string DefaultLanguage = "pt-BR";

        public static string Language { get; set; } = DefaultLanguage;

        static readonly Dictionary<string, Dictionary<string, string>> s_tables =
            new Dictionary<string, Dictionary<string, string>>
            {
                [DefaultLanguage] = PortugueseBrazil(),
            };

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Language = DefaultLanguage;

        public static string Get(string key)
        {
            if (s_tables.TryGetValue(Language, out var table) && table.TryGetValue(key, out var text)) return text;
            if (s_tables[DefaultLanguage].TryGetValue(key, out var fallback)) return fallback;
            return "#" + key;
        }

        public static string Format(string key, params object[] args) => string.Format(Get(key), args);

        public static string UnitName(UnitType type) => Get("unit." + type);
        public static string Army(Side side) => Get("army." + side);
        public static string SideName(Side side) => Get("side." + side);
        public static string CardName(CardId id) => Get($"card.{id}.name");
        public static string CardText(CardId id) => Get($"card.{id}.text");
        public static string KindName(CardKind kind) => Get("kind." + kind);
        public static string UnitAt(UnitType type, Coord c) => Format("fmt.unitAt", UnitName(type), c);
        public static string UnitAt(Unit unit) => UnitAt(unit.Type, unit.Pos);

        /// <summary>History line for a structured <see cref="LogEntry"/>.</summary>
        public static string LogLine(LogEntry e)
        {
            string side = SideName(e.Side);
            switch (e.Kind)
            {
                case LogKind.Preparation:
                    return Format("log.preparation", side, e.Amount, Get(e.Flag ? "log.cardDrawn" : "log.deckEmpty"));
                case LogKind.Recruited:
                    return Format("log.recruited", side, UnitName(e.Unit), e.To);
                case LogKind.Moved:
                    return Format("log.moved", side, UnitName(e.Unit), e.From, e.To);
                case LogKind.CaptureDeclared:
                    return Format(e.Flag ? "log.volleyDeclared" : "log.captureDeclared", side,
                        UnitAt(e.Unit, e.From), UnitAt(e.OtherUnit, e.To));
                case LogKind.Reacted:
                    return Format("log.reacted", side, CardName(e.Card));
                case LogKind.Retreated:
                    return e.Flag
                        ? Format("log.retreatedRanged", UnitName(e.Unit), e.To)
                        : Format("log.retreated", UnitName(e.Unit), e.To, UnitName(e.OtherUnit), e.From);
                case LogKind.Intercepted:
                    return Format("log.intercepted", e.To);
                case LogKind.ProtectionBroken:
                    return Get("log.protectionBroken");
                case LogKind.Blocked:
                    return e.Flag ? Get("log.blockedByEquipment") : Format("log.blockedByCard", CardName(e.Card));
                case LogKind.UnitCaptured:
                    return Format("log.unitCaptured", side, UnitName(e.Unit), e.To);
                case LogKind.Victory:
                    return Format("log.victory", Army(e.Side));
                case LogKind.CardPlayed:
                    return e.Flag
                        ? Format("log.cardPlayedOn", side, CardName(e.Card), UnitAt(e.Unit, e.To))
                        : Format("log.cardPlayed", side, CardName(e.Card));
                case LogKind.PalisadeRaised:
                    return Format("log.palisadeRaised", e.To);
                case LogKind.ExtraStep:
                    return Format("log.extraStep", UnitName(e.Unit), e.From, e.To);
                case LogKind.ForesightDrawn:
                    return Format("log.foresightDrawn", side);
                default:
                    return e.Kind.ToString();
            }
        }

        static Dictionary<string, string> PortugueseBrazil() => new Dictionary<string, string>
        {
            // armies, units, formats
            ["army.White"] = "Exército Branco",
            ["army.Black"] = "Exército Negro",
            ["side.White"] = "Branco",
            ["side.Black"] = "Negro",
            ["unit.Infantry"] = "Infantaria",
            ["unit.Cavalry"] = "Cavalaria",
            ["unit.Crossbowmen"] = "Besteiros",
            ["unit.SiegeEngineers"] = "Engenharia",
            ["unit.Marshal"] = "Marechal",
            ["unit.Sovereign"] = "Soberano",
            ["fmt.unitAt"] = "{0} {1}",
            ["fmt.cardKind"] = "— {0} —",

            // card kinds
            ["kind.Reaction"] = "Reação",
            ["kind.Equipment"] = "Equipamento",
            ["kind.Tactic"] = "Tática",
            ["kind.Supply"] = "Suprimento",
            ["kind.Fortification"] = "Fortificação",
            ["kind.Arcane"] = "Arcano",

            // cards
            ["card.RaiseShields.name"] = "Erguer Escudos",
            ["card.RaiseShields.text"] = "Cancela uma captura contra Infantaria.",
            ["card.OrganizedRetreat.name"] = "Retirada Organizada",
            ["card.OrganizedRetreat.text"] = "A unidade atacada recua uma casa para a retaguarda; o atacante ocupa a casa abandonada.",
            ["card.BracedSpears.name"] = "Lanças Preparadas",
            ["card.BracedSpears.text"] = "Cancela uma captura de Cavalaria contra Infantaria.",
            ["card.SovereignsGuard.name"] = "Guarda do Soberano",
            ["card.SovereignsGuard.text"] = "Uma Infantaria adjacente intercepta a captura dirigida ao Soberano.",
            ["card.ReinforcedShield.name"] = "Escudo Reforçado",
            ["card.ReinforcedShield.text"] = "Cancela a próxima captura contra a unidade equipada; então é descartado.",
            ["card.ReinforcedArmor.name"] = "Blindagem Reforçada",
            ["card.ReinforcedArmor.text"] = "Concede uma carga de proteção a uma Engenharia de Guerra.",
            ["card.WarHammer.name"] = "Martelo de Guerra",
            ["card.WarHammer.text"] = "Durante uma captura, ignora uma carga de proteção defensiva.",
            ["card.ExtraRations.name"] = "Rações Extras de Viagem",
            ["card.ExtraRations.text"] = "Após um movimento, dê 1 passo adicional para uma casa adjacente livre.",
            ["card.ForcedMarch.name"] = "Marcha Forçada",
            ["card.ForcedMarch.text"] = "Concede um movimento adicional neste turno, sem gastar Ordem.",
            ["card.CrossbowVolley.name"] = "Salva de Virotes",
            ["card.CrossbowVolley.text"] = "Besteiros tentam capturar a até 3 casas em diagonal, sem avançar. Não afeta o Soberano.",
            ["card.ImprovisedPalisade.name"] = "Paliçada Improvisada",
            ["card.ImprovisedPalisade.text"] = "Ergue um obstáculo numa casa livre do seu lado até o fim do próximo turno inimigo.",
            ["card.SeersOmen.name"] = "Presságio do Vidente",
            ["card.SeersOmen.text"] = "Olhe as 3 próximas cartas do baralho e compre 1 delas.",

            // history
            ["log.preparation"] = "{0} — Preparação: +{1} Moral, {2}.",
            ["log.cardDrawn"] = "1 carta comprada",
            ["log.deckEmpty"] = "baralho vazio",
            ["log.recruited"] = "{0} — recruta {1} em {2}.",
            ["log.moved"] = "{0} — {1} {2} → {3}.",
            ["log.captureDeclared"] = "{0} — {1} × {2}.",
            ["log.volleyDeclared"] = "{0} — {1} dispara contra {2}.",
            ["log.reacted"] = "{0} — reage com {1}.",
            ["log.retreated"] = "{0} recua para {1}; {2} ocupa {3}.",
            ["log.retreatedRanged"] = "{0} recua para {1}.",
            ["log.intercepted"] = "Infantaria intercepta: o Soberano recua para {0}.",
            ["log.protectionBroken"] = "Martelo de Guerra rompe a proteção.",
            ["log.blockedByCard"] = "Captura bloqueada por {0}.",
            ["log.blockedByEquipment"] = "Captura bloqueada pela proteção equipada.",
            ["log.unitCaptured"] = "{0} perde {1} em {2}.",
            ["log.victory"] = "O Soberano caiu. Vitória do {0}!",
            ["log.cardPlayed"] = "{0} — joga {1}.",
            ["log.cardPlayedOn"] = "{0} — joga {1} em {2}.",
            ["log.palisadeRaised"] = "Paliçada erguida em {0}.",
            ["log.extraStep"] = "{0} {1} → {2} (passo extra).",
            ["log.foresightDrawn"] = "{0} — compra 1 carta pelo Presságio.",

            // header
            ["ui.title"] = "Arcane Gambit",
            ["ui.phase.preparation"] = "Preparação",
            ["ui.phase.maneuvers"] = "Manobras",
            ["ui.phase.confrontation"] = "Confronto",
            ["ui.phase.end"] = "Encerramento",
            ["ui.turnOf"] = "Turno {0} — vez do {1}",
            ["ui.endTurn"] = "Encerrar turno",

            // army panels
            ["ui.army.yourTurn"] = "Seu turno",
            ["ui.army.youWaiting"] = "Você · aguardando",
            ["ui.army.aiTurn"] = "IA · em ação",
            ["ui.army.aiWaiting"] = "IA · aguardando",
            ["ui.morale"] = "Moral",
            ["ui.moraleValue"] = "{0} / {1}",
            ["ui.orders"] = "Ordens · {0} de {1}",
            ["ui.ordersBonus"] = " · +{0} mov.",
            ["ui.ordersIdle"] = "Ordens · —",
            ["ui.handSummary"] = "Mão · {0} cartas   ·   Baralho {1}",
            ["ui.reserveHeader"] = "Reserva · recrutar nas fileiras {0}",
            ["ui.ranks.White"] = "1–2",
            ["ui.ranks.Black"] = "7–8",
            ["ui.reserveCount"] = "{0} ×{1}",
            ["ui.recruit"] = "Recrutar",
            ["ui.choose"] = "Escolha…",

            // action panel
            ["ui.action.header"] = "Ação em curso",
            ["ui.action.idleTitle"] = "Ordens do turno",
            ["ui.action.idleHint"] = "Selecione uma unidade, recrute da reserva ou escolha uma carta da mão.",
            ["ui.action.orders"] = "Ordens",
            ["ui.action.ordersBonus"] = "{0} (+{1} mov.)",
            ["ui.action.proactive"] = "Carta proativa",
            ["ui.action.proactiveUsed"] = "já usada",
            ["ui.action.proactiveAvailable"] = "1 disponível",
            ["ui.action.position"] = "Posição",
            ["ui.action.moves"] = "Movimentos",
            ["ui.action.captures"] = "Capturas",
            ["ui.action.equipment"] = "Equipamento",
            ["ui.action.protection"] = "Proteção",
            ["ui.action.hammer"] = "Martelo",
            ["ui.action.none"] = "—",
            ["ui.action.hintResting"] = "Recém-recrutada: só age no próximo turno.",
            ["ui.action.hintNoOrders"] = "Sem Ordens restantes neste turno.",
            ["ui.action.hintUnit"] = "Ponto = mover. Cantos em rubrica = declarar captura (1 Ordem).",
            ["ui.action.recruitTitle"] = "Recrutar {0}",
            ["ui.action.cost"] = "Custo",
            ["ui.action.recruitCost"] = "{0} Moral · 1 Ordem",
            ["ui.action.inReserve"] = "Na reserva",
            ["ui.action.reserveValue"] = "×{0}",
            ["ui.action.hintRecruit"] = "Escolha uma casa livre nas fileiras {0}. Ela só age no próximo turno.",
            ["ui.action.type"] = "Tipo",
            ["ui.action.cardCost"] = "{0} Moral",
            ["ui.action.plusOrder"] = " · 1 Ordem",
            ["ui.action.playCard"] = "Jogar carta",
            ["ui.action.cancel"] = "Cancelar",
            ["ui.action.aiTitle"] = "Turno do adversário",
            ["ui.action.aiHint"] = "O {0} planeja suas ordens…",
            ["ui.action.aiReacting"] = "O {0} avalia uma reação…",

            // card hints
            ["ui.hint.reaction"] = "Reação: só pode ser jogada quando o inimigo declarar uma captura contra você.",
            ["ui.hint.alreadyPlayed"] = "Você já jogou uma carta proativa neste turno.",
            ["ui.hint.noMorale"] = "Moral insuficiente.",
            ["ui.hint.noTarget"] = "Não há alvo válido agora.",
            ["ui.hint.confirm"] = "Confirme para jogar a carta.",
            ["ui.hint.ReinforcedShield"] = "Escolha uma unidade sua sem proteção.",
            ["ui.hint.ReinforcedArmor"] = "Escolha uma Engenharia de Guerra sua.",
            ["ui.hint.WarHammer"] = "Escolha a unidade que empunhará o martelo.",
            ["ui.hint.ImprovisedPalisade"] = "Escolha uma casa livre do seu lado do campo.",
            ["ui.hint.CrossbowVolley.0"] = "Escolha os Besteiros que vão disparar.",
            ["ui.hint.CrossbowVolley.1"] = "Escolha o alvo (até 3 casas na diagonal).",
            ["ui.hint.ExtraRations.0"] = "Escolha uma unidade que já se moveu neste turno.",
            ["ui.hint.ExtraRations.1"] = "Escolha a casa adjacente livre.",

            // side panels
            ["ui.history"] = "Histórico",
            ["ui.deck"] = "Baralho",
            ["ui.discard"] = "Descarte",
            ["ui.cardCount"] = "{0} cartas",

            // overlays
            ["ui.intro.header"] = "Você comanda o",
            ["ui.intro.text"] = "O {0} será conduzido pela IA.",
            ["ui.intro.youFirst"] = "Você abre a partida com o primeiro turno.",
            ["ui.intro.aiFirst"] = "O adversário faz o primeiro turno.",
            ["ui.intro.button"] = "Iniciar partida",
            ["ui.reaction.header"] = "Janela de confronto",
            ["ui.reaction.title"] = "{0}, reagir?",
            ["ui.reaction.capture"] = "{0} tenta capturar {1}. Moral disponível: {2}.",
            ["ui.reaction.volley"] = "{0} dispara contra {1}. Moral disponível: {2}.",
            ["ui.reaction.pass"] = "Não reagir",
            ["ui.foresight.title"] = "Escolha uma carta",
            ["ui.gameOver.header"] = "Fim da campanha",
            ["ui.gameOver.title"] = "Vitória do {0}",
            ["ui.gameOver.text"] = "O Soberano do {0} foi capturado.",
            ["ui.gameOver.won"] = "Você venceu a campanha!",
            ["ui.gameOver.lost"] = "Você foi derrotado.",
            ["ui.gameOver.button"] = "Nova partida",
        };
    }
}
