using System.Collections.Generic;
using ArcaneGambit.Rules;
using UnityEngine;

namespace ArcaneGambit.Game
{
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
        public static string UnitText(UnitType type) => Get($"unit.{type}.text");
        public static string Army(Side side) => Get("army." + side);
        public static string SideName(Side side) => Get("side." + side);
        public static string CardName(CardId id) => Get($"card.{id}.name");
        public static string CardText(CardId id) => Get($"card.{id}.text");
        public static string KindName(CardKind kind) => Get("kind." + kind);

        public static string CardType(CardInfo info)
        {
            string kind = KindName(info.Kind);
            if (info.Kind != CardKind.Reaction && info.IsReaction) return Format("fmt.cardType", kind, KindName(CardKind.Reaction));
            return info.IsRitual ? Format("fmt.cardType", kind, Get("kind.ritual")) : kind;
        }
        public static string UnitAt(UnitType type, Coord c) => Format("fmt.unitAt", UnitName(type), c);
        public static string UnitAt(Unit unit) => UnitAt(unit.Type, unit.Pos);
        public static string UnitDot(UnitType type, Coord c) => Format("fmt.unitDot", UnitName(type), c);
        public static string UnitDot(Unit unit) => UnitDot(unit.Type, unit.Pos);

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
                    return e.Flag
                        ? Format("log.cardCapture", side, UnitAt(e.Unit, e.From), UnitAt(e.OtherUnit, e.To), CardName(e.Card))
                        : Format("log.captureDeclared", side, UnitAt(e.Unit, e.From), UnitAt(e.OtherUnit, e.To));
                case LogKind.Reacted:
                    return Format("log.reacted", side, CardName(e.Card));
                case LogKind.Retreated:
                    if (e.Card == CardId.Countercharge) return Format("log.countercharge", UnitName(e.Unit), e.To);
                    return e.Flag
                        ? Format("log.retreatedRanged", UnitName(e.Unit), e.To)
                        : Format("log.retreated", UnitName(e.Unit), e.To, UnitName(e.OtherUnit), e.From);
                case LogKind.Intercepted:
                    return Format("log.intercepted", UnitName(e.Unit), e.To);
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
                case LogKind.Fortified:
                    return Format("log.fortified", CardName(e.Card), e.To);
                case LogKind.ExtraStep:
                    return Format("log.extraStep", UnitName(e.Unit), e.From, e.To);
                case LogKind.ForesightDrawn:
                    return Format("log.foresight." + e.Card, side);
                case LogKind.Swapped:
                    return Format("log.swapped", side, UnitName(e.Unit), e.From, e.To);
                case LogKind.WeatherCleared:
                    return Format("log.weatherCleared", side, CardName(e.Card));
                case LogKind.CardDrawn:
                    return Format("log.cardDrawnBy", side);
                case LogKind.LightningMarked:
                    return Format("log.lightningMarked", e.To);
                case LogKind.LightningStruck:
                    return e.Flag
                        ? Format("log.lightningStruck", e.To, side, UnitName(e.Unit))
                        : Format("log.lightningMissed", e.To);
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
            ["unit.Infantry.text"] = "Avança 1 casa. Captura 1 casa na diagonal à frente.",
            ["unit.Cavalry.text"] = "Salta em L, por cima de outras peças. Captura na casa de destino.",
            ["unit.Crossbowmen.text"] = "Até 2 casas na diagonal, sem atravessar peças. Captura na casa de destino.",
            ["unit.SiegeEngineers.text"] = "1 casa em linha reta. Captura na casa de destino.",
            ["unit.Marshal.text"] = "Até 2 casas em qualquer direção, sem atravessar peças. Captura na casa de destino.",
            ["unit.Sovereign.text"] = "1 casa em qualquer direção. Se for capturado, a campanha está perdida.",
            ["fmt.unitAt"] = "{0} {1}",
            ["fmt.unitDot"] = "{0} · {1}",
            ["fmt.cardType"] = "{0} · {1}",

            // card kinds
            ["kind.Reaction"] = "Reação",
            ["kind.Equipment"] = "Equipamento",
            ["kind.Tactic"] = "Tática",
            ["kind.Supply"] = "Suprimento",
            ["kind.Fortification"] = "Fortificação",
            ["kind.Arcane"] = "Arcanismo",
            ["kind.Condition"] = "Condição climática",
            ["kind.ritual"] = "Ritual",
            ["card.pieces.Or"] = "ou",
            ["card.pieces.And"] = "+",
            ["card.pieces.Versus"] = "vs",
            ["card.weatherBand"] = "Ambos os exércitos",

            // cards
            ["card.RaiseShields.name"] = "Erguer Escudos",
            ["card.RaiseShields.text"] = "Cancele uma captura contra uma Infantaria aliada.",
            ["card.OrganizedRetreat.name"] = "Retirada Organizada",
            ["card.OrganizedRetreat.text"] = "A unidade alvo recua 1 casa vazia para a retaguarda; o atacante ocupa a casa somente se a captura original exigia avanço.",
            ["card.BracedSpears.name"] = "Lanças Preparadas",
            ["card.BracedSpears.text"] = "Quando uma Cavalaria tenta capturar sua Infantaria, cancele a captura. Custa 0 Moral se houver outra Infantaria aliada adjacente à defensora; caso contrário, custa 1.",
            ["card.SovereignsGuard.name"] = "Guarda do Soberano",
            ["card.SovereignsGuard.text"] = "Uma Infantaria aliada adjacente troca de lugar com o Soberano, tornando-se alvo da captura; se a captura ainda for válida, a Infantaria é sacrificada.",
            ["card.ReinforcedShield.name"] = "Escudo Reforçado",
            ["card.ReinforcedShield.text"] = "Equipe uma unidade terrestre aliada. Cancele a próxima captura contra ela e descarte o equipamento.",
            ["card.ReinforcedArmor.name"] = "Blindagem Reforçada",
            ["card.ReinforcedArmor.text"] = "Equipe uma Engenharia de Guerra. Recebe 2 cargas de proteção contra capturas à distância; não protege contra capturas corpo a corpo.",
            ["card.WarHammer.name"] = "Martelo de Guerra",
            ["card.WarHammer.text"] = "Equipe Cavalaria ou Marechal. Em uma captura corpo a corpo, ignora 1 carga de proteção de equipamento; não anula Reações.",
            ["card.ExtraRations.name"] = "Rações Extras de Viagem",
            ["card.ExtraRations.text"] = "Após movimentar uma unidade, acrescente 1 passo para casa adjacente vazia, sujeito a restrições de terreno e clima.",
            ["card.ForcedMarch.name"] = "Marcha Forçada",
            ["card.ForcedMarch.text"] = "Uma unidade realiza um movimento legal adicional sem gastar Ordem; não pode capturar durante esse movimento.",
            ["card.CrossbowVolley.name"] = "Salva de Virotes",
            ["card.CrossbowVolley.text"] = "Besteiros declaram captura diagonal em até 3 casas, com linha desobstruída, sem avançar. Não atinge o Soberano. Consome uma Ordem.",
            ["card.ImprovisedPalisade.name"] = "Paliçada Improvisada",
            ["card.ImprovisedPalisade.text"] = "Ocupa uma casa vazia adjacente a Infantaria ou Engenharia aliada por 2 rodadas; bloqueia movimentos e linhas de captura, mas não pode bloquear a casa de um Soberano.",
            ["card.SeersOmen.name"] = "Presságio do Vidente",
            ["card.SeersOmen.text"] = "Olhe as 3 cartas do topo do baralho. Compre 1 e coloque as outras 2 no fundo, na ordem escolhida.",
            ["card.CloseRanks.name"] = "Fechar Fileiras",
            ["card.CloseRanks.text"] = "Quando uma Infantaria adjacente a outra Infantaria aliada seria capturada, cancele a captura. Ambas não podem receber bônus de movimento até o fim do próximo turno próprio.",
            ["card.Countercharge.name"] = "Contracarga",
            ["card.Countercharge.text"] = "Quando sua Cavalaria seria capturada por uma peça adjacente, mova-a para uma casa vazia alcançável por seu salto em L. A captura falha e o atacante não avança.",
            ["card.SmokeScreen.name"] = "Cortina de Fumaça",
            ["card.SmokeScreen.text"] = "Quando uma unidade sua seria capturada à distância, cancele essa captura. Até o fim da rodada, nenhuma unidade pode realizar capturas à distância através da casa dessa unidade.",
            ["card.HeroicInterposition.name"] = "Interposição Heroica",
            ["card.HeroicInterposition.text"] = "Quando uma unidade sua seria capturada, uma Infantaria aliada adjacente a ela troca de lugar com o alvo e recebe a captura, se válida. Não funciona contra capturas arcanas.",
            ["card.CampaignHorseshoes.name"] = "Ferraduras de Campanha",
            ["card.CampaignHorseshoes.text"] = "Equipe Cavalaria. Uma vez, após um salto em L que não seja captura, mova-a 1 casa adjacente vazia. Descarte o equipamento.",
            ["card.CrossbowPavise.name"] = "Pavês do Besteiro",
            ["card.CrossbowPavise.text"] = "Equipe Besteiros. Cancele a próxima captura à distância recebida por essa unidade; depois descarte.",
            ["card.MarshalsBanner.name"] = "Estandarte do Marechal",
            ["card.MarshalsBanner.text"] = "Equipe Marechal. Uma vez por turno próprio, a primeira Tática jogada sobre uma Infantaria aliada adjacente custa 1 Moral a menos (mínimo 0).",
            ["card.JoustingLance.name"] = "Lança de Justa",
            ["card.JoustingLance.text"] = "Equipe Cavalaria. Na próxima captura que ela declarar, pode primeiro avançar 1 casa adjacente vazia; a captura em L deve ser legal a partir da nova casa. Toda a manobra consome uma Ordem. Depois descarte.",
            ["card.CavalryFeint.name"] = "Finta de Cavalaria",
            ["card.CavalryFeint.text"] = "Escolha uma Cavalaria aliada. Até o fim do turno, a primeira Reação inimiga contra uma captura declarada por ela custa +1 Moral (máximo +1).",
            ["card.CoordinatedAdvance.name"] = "Avanço Coordenado",
            ["card.CoordinatedAdvance.text"] = "Mova duas Infantarias aliadas, cada uma 1 casa legal, sem capturar. Consome uma Ordem no total.",
            ["card.SiegeOrder.name"] = "Ordem de Cerco",
            ["card.SiegeOrder.text"] = "Uma Engenharia de Guerra aliada declara captura em linha reta a até 3 casas, sem avançar, se houver linha livre; não pode atingir o Soberano. Consome uma Ordem.",
            ["card.LineSwap.name"] = "Troca de Linha",
            ["card.LineSwap.text"] = "Troque de lugar duas Infantarias aliadas adjacentes, desde que nenhuma esteja sob efeito que impeça movimento. Consome uma Ordem.",
            ["card.ScoutsAhead.name"] = "Batedores Adiante",
            ["card.ScoutsAhead.text"] = "Revele 2 cartas do topo do seu baralho. Coloque uma no topo e outra no fundo, sem comprar.",
            ["card.SupplyWagon.name"] = "Carroça de Mantimentos",
            ["card.SupplyWagon.text"] = "Na sua próxima Preparação, receba 1 Moral adicional (respeitando o máximo de 8). Esta carta não acumula com outra cópia de si mesma.",
            ["card.AntiCavalryStakes.name"] = "Estacas Anticavalaria",
            ["card.AntiCavalryStakes.text"] = "Coloque marcadores em até 2 casas vazias adjacentes a uma Infantaria aliada. Até o fim da próxima rodada, Cavalaria inimiga não pode terminar movimento nessas casas.",
            ["card.ShallowDitch.name"] = "Fosso Raso",
            ["card.ShallowDitch.text"] = "Escolha uma casa vazia adjacente a sua Engenharia. Por 2 rodadas, unidades que deslizam não podem atravessá-la; Cavalaria pode saltar sobre ela, mas não terminar nela.",
            ["card.Fireball.name"] = "Bola de Fogo",
            ["card.Fireball.text"] = "Ritual ofensivo: declare captura contra Infantaria ou Besteiros inimigos a até 2 casas de uma unidade aliada. Não atinge Soberano nem Engenharia; permite Reação.",
            ["card.DawnPrayer.name"] = "Prece ao Amanhecer",
            ["card.DawnPrayer.text"] = "Remova a condição climática ativa. Se não houver condição ativa, compre 1 carta.",
            ["card.VigilSeal.name"] = "Selo da Vigília",
            ["card.VigilSeal.text"] = "Reação arcana: quando seu Soberano seria capturado por uma carta, cancele o efeito da carta e descarte o Selo. Não funciona contra captura normal por movimento de peça.",
            ["card.OracleEcho.name"] = "Eco do Oráculo",
            ["card.OracleEcho.text"] = "Ritual: escolha uma carta do seu descarte que não seja Arcana e coloque-a no topo do baralho; ela não é comprada imediatamente.",
            ["card.Blizzard.name"] = "Nevasca",
            ["card.Blizzard.text"] = "Besteiros, Engenharia e Marechal movem no máximo 2 casas; bônus de movimento são suspensos. Cavalaria mantém o salto em L.",
            ["card.Gale.name"] = "Ventania",
            ["card.Gale.text"] = "Ataques à distância perdem 1 casa de alcance (mínimo 1).",
            ["card.LightningStorm.name"] = "Tempestade de Relâmpagos",
            ["card.LightningStorm.text"] = "No início de cada rodada, uma casa é marcada. No fim da rodada, um raio atinge essa casa; o Soberano não pode ser alvo.",
            ["card.Fog.name"] = "Neblina",
            ["card.Fog.text"] = "Capturas à distância exigem alvos a até 2 casas. Moral e equipamentos continuam visíveis.",
            ["card.Mud.name"] = "Lamaçal",
            ["card.Mud.text"] = "Nenhum efeito pode conceder movimento adicional após o movimento normal da peça.",
            ["card.MoonlessNight.name"] = "Noite Sem Lua",
            ["card.MoonlessNight.text"] = "Ataques à distância além de 2 casas exigem que o alvo esteja adjacente a pelo menos uma unidade aliada do atacante (observador).",

            // history
            ["log.preparation"] = "{0} — +{1} Moral, {2}.",
            ["log.cardDrawn"] = "1 carta comprada",
            ["log.deckEmpty"] = "baralho vazio",
            ["log.recruited"] = "{0} — recruta {1} em {2}.",
            ["log.moved"] = "{0} — {1} {2} → {3}.",
            ["log.captureDeclared"] = "{0} — {1} × {2}.",
            ["log.cardCapture"] = "{0} — {1} ataca {2} com {3}.",
            ["log.reacted"] = "{0} — reage com {1}.",
            ["log.retreated"] = "{0} recua para {1}; {2} ocupa {3}.",
            ["log.retreatedRanged"] = "{0} recua para {1}.",
            ["log.countercharge"] = "{0} salta para {1}; a captura falha.",
            ["log.intercepted"] = "Infantaria intercepta: {0} recua para {1}.",
            ["log.protectionBroken"] = "Martelo de Guerra rompe a proteção.",
            ["log.blockedByCard"] = "Captura bloqueada por {0}.",
            ["log.blockedByEquipment"] = "Captura bloqueada pela proteção equipada.",
            ["log.unitCaptured"] = "{0} perde {1} em {2}.",
            ["log.victory"] = "O Soberano caiu. Vitória do {0}!",
            ["log.cardPlayed"] = "{0} — joga {1}.",
            ["log.cardPlayedOn"] = "{0} — joga {1} em {2}.",
            ["log.fortified"] = "{0} em {1}.",
            ["log.extraStep"] = "{0} {1} → {2} (passo extra).",
            ["log.foresight.SeersOmen"] = "{0} — compra 1 carta pelo Presságio.",
            ["log.foresight.ScoutsAhead"] = "{0} — reordena o topo do baralho.",
            ["log.foresight.OracleEcho"] = "{0} — devolve 1 carta do descarte ao topo do baralho.",
            ["log.swapped"] = "{0} — {1} {2} ⇄ {3}.",
            ["log.weatherCleared"] = "{0} — dissipa {1}.",
            ["log.cardDrawnBy"] = "{0} — compra 1 carta.",
            ["log.lightningMarked"] = "Nuvens carregadas sobre {0}.",
            ["log.lightningStruck"] = "Um raio atinge {0}: {1} perde {2}.",
            ["log.lightningMissed"] = "Um raio atinge {0}, sem vítimas.",

            // header
            ["ui.title"] = "Arcane Gambit",
            ["ui.phase.preparation"] = "Preparação",
            ["ui.phase.maneuvers"] = "Manobras",
            ["ui.phase.confrontation"] = "Confronto",
            ["ui.phase.end"] = "Encerramento",
            ["ui.turnOf"] = "Turno {0} — {1}",
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
            ["ui.handSummary"] = "Mão {0} · Baralho {1} · Descarte {2}",
            ["ui.reserveHeader"] = "Reserva · fileiras {0}",
            ["ui.ranks.White"] = "1–2",
            ["ui.ranks.Black"] = "7–8",
            ["ui.reserveCount"] = "×{0}",
            ["ui.reserveHint"] = "clique para recrutar",
            ["ui.reserveChoose"] = "escolha a casa",
            ["ui.conditions.header"] = "Condições em campo",
            ["ui.conditions.none"] = "nenhuma ativa",
            ["ui.conditions.active"] = "1 ativa",
            ["ui.conditions.empty"] = "Nenhuma condição afeta o campo de batalha.",
            ["ui.conditions.lasting"] = "Permanece até ser substituída por outra condição ou dissipada.",
            ["ui.conditions.lightning"] = "Raio marcado em {0}.",

            // action panel
            ["ui.action.header"] = "Ação em curso",
            ["ui.action.step"] = "Passo {0} de {1} · {2}",
            ["ui.action.idleTitle"] = "Ordens do turno",
            ["ui.action.unitTitle"] = "Unidade selecionada",
            ["ui.plate.unit"] = "Unidade",
            ["ui.plate.reserve"] = "Reserva",
            ["ui.plate.army"] = "Exército",
            ["ui.plate.armyText"] = "Proteja o seu Soberano: se ele for capturado, a campanha está perdida.",
            ["ui.plate.enemyText"] = "Capture o Soberano inimigo para vencer a campanha.",
            ["ui.step.confirm"] = "confirmar",
            ["ui.step.declare"] = "declarar",
            ["ui.step.ReinforcedShield.0"] = "unidade",
            ["ui.step.ReinforcedArmor.0"] = "engenharia",
            ["ui.step.WarHammer.0"] = "portador",
            ["ui.step.CampaignHorseshoes.0"] = "cavalaria",
            ["ui.step.CrossbowPavise.0"] = "besteiros",
            ["ui.step.MarshalsBanner.0"] = "marechal",
            ["ui.step.JoustingLance.0"] = "cavalaria",
            ["ui.step.CavalryFeint.0"] = "cavalaria",
            ["ui.step.ImprovisedPalisade.0"] = "casa",
            ["ui.step.ShallowDitch.0"] = "casa",
            ["ui.step.AntiCavalryStakes.0"] = "casa",
            ["ui.step.AntiCavalryStakes.1"] = "segunda casa",
            ["ui.step.CrossbowVolley.0"] = "atirador",
            ["ui.step.CrossbowVolley.1"] = "alvo",
            ["ui.step.SiegeOrder.0"] = "engenharia",
            ["ui.step.SiegeOrder.1"] = "alvo",
            ["ui.step.Fireball.0"] = "conjurador",
            ["ui.step.Fireball.1"] = "alvo",
            ["ui.step.ExtraRations.0"] = "unidade",
            ["ui.step.ExtraRations.1"] = "destino",
            ["ui.step.LineSwap.0"] = "infantaria",
            ["ui.step.LineSwap.1"] = "parceira",
            ["ui.step.CoordinatedAdvance.0"] = "infantaria",
            ["ui.step.CoordinatedAdvance.1"] = "destino",
            ["ui.step.CoordinatedAdvance.2"] = "infantaria",
            ["ui.step.CoordinatedAdvance.3"] = "destino",
            ["ui.action.idleHint"] = "Selecione uma unidade, recrute da reserva ou escolha uma carta da mão.",
            ["ui.action.protection"] = "Proteção",
            ["ui.action.hammer"] = "Martelo",
            ["ui.action.rangedProtection"] = "Anteparo ×{0}",
            ["ui.action.horseshoes"] = "Ferraduras",
            ["ui.action.lance"] = "Lança",
            ["ui.action.banner"] = "Estandarte",
            ["ui.action.none"] = "—",
            ["ui.action.hintResting"] = "Recém-recrutada: só age no próximo turno.",
            ["ui.action.hintNoOrders"] = "Sem Ordens restantes neste turno.",
            ["ui.action.hintUnit"] = "Ponto = mover. Cantos em rubrica = declarar captura (1 Ordem).",
            ["ui.action.recruitTitle"] = "Recrutar {0}",
            ["ui.action.hintRecruit"] = "Escolha uma casa livre nas fileiras {0}. Ela só age no próximo turno.",
            ["ui.action.playCard"] = "Jogar carta",
            ["ui.action.declare"] = "Declarar captura",
            ["ui.action.cancel"] = "Cancelar",
            ["ui.action.aiTitle"] = "Turno do adversário",
            ["ui.action.aiHint"] = "O {0} planeja suas ordens…",
            ["ui.action.aiReacting"] = "O {0} avalia uma reação…",
            ["ui.ai.intent"] = "Jogada do adversário",
            ["ui.ai.move"] = "O {0} move {1} de {2} para {3}.",
            ["ui.ai.capture"] = "O {0} declara captura: {1} contra {2}.",
            ["ui.ai.recruit"] = "O {0} recruta {1} em {2}.",
            ["ui.ai.card"] = "O {0} joga {1}.",
            ["ui.ai.cardOn"] = "O {0} joga {1} em {2}.",
            ["ui.ai.react"] = "O {0} reage com {1}.",
            ["ui.ai.pass"] = "O {0} deixa a captura seguir.",
            ["ui.ai.endTurn"] = "O {0} encerra o turno.",
            ["ui.ai.cell.move"] = "Mover",
            ["ui.ai.cell.capture"] = "Capturar",
            ["ui.ai.cell.recruit"] = "Recrutar",
            ["ui.ai.cell.card"] = "Carta",
            ["ui.ai.cell.react"] = "Reação",
            ["ui.ai.cell.pass"] = "Sem reação",
            ["ui.ai.cell.endTurn"] = "Encerrar",

            // action panel cells
            ["ui.cell.target"] = "Alvo",
            ["ui.cell.action"] = "Ação",
            ["ui.cell.range"] = "Alcance",
            ["ui.cell.cost"] = "Custo",
            ["ui.cell.orders"] = "Ordens",
            ["ui.cell.card"] = "Carta",
            ["ui.cell.hand"] = "Mão",
            ["ui.cell.moves"] = "Movimentos",
            ["ui.cell.captures"] = "Capturas",
            ["ui.cell.gear"] = "Equipamento",
            ["ui.cell.square"] = "Casa",
            ["ui.cell.reserve"] = "Reserva",
            ["ui.cell.perTurn"] = "+{0} por turno",
            ["ui.cell.ordersValue"] = "{0} de {1}",
            ["ui.cell.ordersSub"] = "por turno",
            ["ui.cell.bonus"] = "+{0} mov. extra",
            ["ui.cell.cardAvailable"] = "1 disponível",
            ["ui.cell.cardUsed"] = "já usada",
            ["ui.cell.cardSub"] = "proativa",
            ["ui.cell.handValue"] = "{0} cartas",
            ["ui.cell.deckSub"] = "baralho {0}",
            ["ui.cell.opponent"] = "do adversário",
            ["ui.cell.movesSub"] = "casas livres",
            ["ui.cell.capturesSub"] = "1 Ordem cada",
            ["ui.cell.ranks"] = "fileiras {0}",
            ["ui.cell.freeSquare"] = "casa livre",
            ["ui.cell.reserveValue"] = "×{0}",
            ["ui.cell.available"] = "disponíveis",
            ["ui.cell.moraleCost"] = "{0} Moral",
            ["ui.cell.left"] = "fica {0} / {1}",
            ["ui.cell.plusOrder"] = " · 1 Ordem",
            ["ui.cell.unavailable"] = "indisponível",
            ["ui.cell.onReaction"] = "na reação",
            ["ui.cell.choose"] = "a escolher",
            ["ui.cell.from"] = "de {0}",
            ["ui.cell.self"] = "seu exército",
            ["ui.range.RaiseShields"] = "1 captura",
            ["ui.range.RaiseShields.sub"] = "contra Infantaria",
            ["ui.range.OrganizedRetreat"] = "1 casa",
            ["ui.range.OrganizedRetreat.sub"] = "para a retaguarda",
            ["ui.range.BracedSpears"] = "1 captura",
            ["ui.range.BracedSpears.sub"] = "de Cavalaria",
            ["ui.range.SovereignsGuard"] = "adjacente",
            ["ui.range.SovereignsGuard.sub"] = "ao Soberano",
            ["ui.range.ReinforcedShield"] = "1 unidade",
            ["ui.range.ReinforcedShield.sub"] = "sua, sem proteção",
            ["ui.range.ReinforcedArmor"] = "1 Engenharia",
            ["ui.range.ReinforcedArmor.sub"] = "sua, sem proteção",
            ["ui.range.WarHammer"] = "1 unidade",
            ["ui.range.WarHammer.sub"] = "Cavalaria ou Marechal",
            ["ui.range.ExtraRations"] = "1 casa",
            ["ui.range.ExtraRations.sub"] = "adjacente e livre",
            ["ui.range.ForcedMarch"] = "+1 movimento",
            ["ui.range.ForcedMarch.sub"] = "neste turno",
            ["ui.range.CrossbowVolley"] = "até 3 casas",
            ["ui.range.CrossbowVolley.sub"] = "em diagonal",
            ["ui.range.ImprovisedPalisade"] = "1 casa livre",
            ["ui.range.ImprovisedPalisade.sub"] = "por 2 rodadas",
            ["ui.range.SeersOmen"] = "3 cartas",
            ["ui.range.SeersOmen.sub"] = "do topo do baralho",
            ["ui.range.CloseRanks"] = "1 captura",
            ["ui.range.CloseRanks.sub"] = "Infantaria em fileira",
            ["ui.range.Countercharge"] = "salto em L",
            ["ui.range.Countercharge.sub"] = "contra peça adjacente",
            ["ui.range.SmokeScreen"] = "1 captura",
            ["ui.range.SmokeScreen.sub"] = "à distância",
            ["ui.range.HeroicInterposition"] = "adjacente",
            ["ui.range.HeroicInterposition.sub"] = "à unidade atacada",
            ["ui.range.CampaignHorseshoes"] = "1 Cavalaria",
            ["ui.range.CampaignHorseshoes.sub"] = "sua, sem ferraduras",
            ["ui.range.CrossbowPavise"] = "1 Besteiros",
            ["ui.range.CrossbowPavise.sub"] = "seus, sem anteparo",
            ["ui.range.MarshalsBanner"] = "1 Marechal",
            ["ui.range.MarshalsBanner.sub"] = "seu, sem estandarte",
            ["ui.range.JoustingLance"] = "1 Cavalaria",
            ["ui.range.JoustingLance.sub"] = "sua, sem lança",
            ["ui.range.CavalryFeint"] = "1 Cavalaria",
            ["ui.range.CavalryFeint.sub"] = "neste turno",
            ["ui.range.CoordinatedAdvance"] = "2 Infantarias",
            ["ui.range.CoordinatedAdvance.sub"] = "1 casa cada",
            ["ui.range.SiegeOrder"] = "até 3 casas",
            ["ui.range.SiegeOrder.sub"] = "em linha reta",
            ["ui.range.LineSwap"] = "2 Infantarias",
            ["ui.range.LineSwap.sub"] = "adjacentes",
            ["ui.range.ScoutsAhead"] = "2 cartas",
            ["ui.range.ScoutsAhead.sub"] = "do topo do baralho",
            ["ui.range.SupplyWagon"] = "+1 Moral",
            ["ui.range.SupplyWagon.sub"] = "na próxima Preparação",
            ["ui.range.AntiCavalryStakes"] = "até 2 casas",
            ["ui.range.AntiCavalryStakes.sub"] = "junto à Infantaria",
            ["ui.range.ShallowDitch"] = "1 casa livre",
            ["ui.range.ShallowDitch.sub"] = "junto à Engenharia",
            ["ui.range.Fireball"] = "até 2 casas",
            ["ui.range.Fireball.sub"] = "de uma unidade sua",
            ["ui.range.DawnPrayer"] = "clima ativo",
            ["ui.range.DawnPrayer.sub"] = "ou 1 carta",
            ["ui.range.VigilSeal"] = "1 captura",
            ["ui.range.VigilSeal.sub"] = "do Soberano, por carta",
            ["ui.range.OracleEcho"] = "1 carta",
            ["ui.range.OracleEcho.sub"] = "do seu descarte",
            ["ui.range.weather"] = "campo inteiro",
            ["ui.range.weather.sub"] = "até ser dissipada",

            // card hints
            ["ui.hint.reaction"] = "Reação: só pode ser jogada quando o inimigo declarar uma captura contra você.",
            ["ui.hint.alreadyPlayed"] = "Você já jogou uma carta proativa neste turno.",
            ["ui.hint.noMorale"] = "Moral insuficiente.",
            ["ui.hint.noTarget"] = "Não há alvo válido agora.",
            ["ui.hint.confirm"] = "Confirme para jogar a carta.",
            ["ui.hint.declare"] = "O oponente poderá responder com 1 Reação.",
            ["ui.hint.ReinforcedShield"] = "Escolha uma unidade sua sem proteção.",
            ["ui.hint.ReinforcedArmor"] = "Escolha uma Engenharia de Guerra sua.",
            ["ui.hint.WarHammer"] = "Escolha a Cavalaria ou o Marechal que empunhará o martelo.",
            ["ui.hint.CampaignHorseshoes"] = "Escolha uma Cavalaria sua.",
            ["ui.hint.CrossbowPavise"] = "Escolha Besteiros seus sem anteparo.",
            ["ui.hint.MarshalsBanner"] = "Escolha o seu Marechal.",
            ["ui.hint.JoustingLance"] = "Escolha uma Cavalaria sua.",
            ["ui.hint.CavalryFeint"] = "Escolha a Cavalaria que fará a finta.",
            ["ui.hint.ImprovisedPalisade"] = "Escolha uma casa livre junto a Infantaria ou Engenharia sua.",
            ["ui.hint.ShallowDitch"] = "Escolha uma casa livre junto à sua Engenharia.",
            ["ui.hint.AntiCavalryStakes.0"] = "Escolha uma casa livre junto a uma Infantaria sua.",
            ["ui.hint.AntiCavalryStakes.1"] = "Escolha uma segunda casa junto à mesma Infantaria, ou confirme.",
            ["ui.hint.CrossbowVolley.0"] = "Escolha os Besteiros que vão disparar.",
            ["ui.hint.CrossbowVolley.1"] = "Escolha o alvo (até 3 casas na diagonal).",
            ["ui.hint.SiegeOrder.0"] = "Escolha a Engenharia que vai atacar.",
            ["ui.hint.SiegeOrder.1"] = "Escolha o alvo (até 3 casas em linha reta).",
            ["ui.hint.Fireball.0"] = "Escolha a unidade sua que conjura o ritual.",
            ["ui.hint.Fireball.1"] = "Escolha Infantaria ou Besteiros inimigos a até 2 casas.",
            ["ui.hint.ExtraRations.0"] = "Escolha uma unidade que já se moveu neste turno.",
            ["ui.hint.ExtraRations.1"] = "Escolha a casa adjacente livre.",
            ["ui.hint.LineSwap.0"] = "Escolha a primeira Infantaria.",
            ["ui.hint.LineSwap.1"] = "Escolha a Infantaria adjacente que troca de lugar.",
            ["ui.hint.CoordinatedAdvance.0"] = "Escolha a primeira Infantaria.",
            ["ui.hint.CoordinatedAdvance.1"] = "Escolha a casa para onde ela avança.",
            ["ui.hint.CoordinatedAdvance.2"] = "Escolha a segunda Infantaria.",
            ["ui.hint.CoordinatedAdvance.3"] = "Escolha a casa para onde ela avança.",

            // side panels
            ["ui.history"] = "Histórico",
            ["ui.historyOrder"] = "recente no topo · role ↕",
            ["ui.deck"] = "Baralho",
            ["ui.discard"] = "Descarte",
            ["ui.discardLast"] = "última: {0}",
            ["ui.discardEmpty"] = "nenhuma carta",
            ["ui.card"] = "carta",
            ["ui.cards"] = "cartas",

            // overlays
            ["ui.intro.eyebrow"] = "Partida rápida · contra a IA",
            ["ui.intro.header"] = "Você comanda o",
            ["ui.intro.youFirst"] = "O {0} será conduzido pela IA.\nVocê faz o primeiro turno.",
            ["ui.intro.aiFirst"] = "O {0} será conduzido pela IA\ne faz o primeiro turno.",
            ["ui.intro.goalTitle"] = "Capture o Soberano",
            ["ui.intro.goalText"] = "Uma captura bem-sucedida do Soberano inimigo vence a partida.",
            ["ui.intro.moraleTitle"] = "Moral",
            ["ui.intro.moraleText"] = "+{0} a cada Preparação, até {1}. Paga recrutas e cartas.",
            ["ui.intro.ordersTitle"] = "{0} Ordens por turno",
            ["ui.intro.ordersText"] = "Recrutar, mover ou declarar uma captura.",
            ["ui.intro.button"] = "Iniciar partida",
            ["ui.intro.rules"] = "Regras completas",

            ["ui.reaction.header"] = "Janela de confronto",
            ["ui.reaction.eyebrow"] = "Janela de confronto · sua reação",
            ["ui.reaction.title"] = "{0}, reagir?",
            ["ui.reaction.tries"] = "tenta capturar",
            ["ui.reaction.shoots"] = "dispara contra",
            ["ui.reaction.attacker"] = "atacante · {0}",
            ["ui.reaction.yourUnit"] = "sua unidade",
            ["ui.reaction.prompt"] = "Escolha <b>até 1 Reação</b> ou deixe a captura seguir.",
            ["ui.reaction.selected"] = "Selecionada",
            ["ui.reaction.applicable"] = "Aplicável",
            ["ui.reaction.notApplicable"] = "Não se aplica",
            ["ui.reaction.block"] = "Bloqueia a captura · {0} Moral",
            ["ui.reaction.retreat"] = "Recua para {0} · {1} Moral",
            ["ui.reaction.guard"] = "{0} intercepta · {1} Moral",
            ["ui.reaction.countercharge"] = "Salta para {0} · {1} Moral",
            ["ui.reaction.noMorale"] = "Moral insuficiente",
            ["ui.reaction.noGuard"] = "Sem Infantaria adjacente",
            ["ui.reaction.no.RaiseShields"] = "Só protege Infantaria",
            ["ui.reaction.no.BracedSpears"] = "Só contra carga de Cavalaria",
            ["ui.reaction.no.OrganizedRetreat"] = "Sem casa livre atrás",
            ["ui.reaction.no.SovereignsGuard"] = "Só protege o Soberano",
            ["ui.reaction.no.CloseRanks"] = "Só Infantaria em fileira",
            ["ui.reaction.no.Countercharge"] = "Só Cavalaria contra peça adjacente",
            ["ui.reaction.no.SmokeScreen"] = "Só contra disparos",
            ["ui.reaction.no.HeroicInterposition"] = "Sem Infantaria adjacente",
            ["ui.reaction.no.VigilSeal"] = "Só contra carta que ataca o Soberano",
            ["ui.reaction.play"] = "Jogar {0} · {1} Moral",
            ["ui.reaction.pass"] = "Não reagir",

            ["ui.toast.dismiss"] = "some em 3 s · clique para seguir",
            ["ui.toast.turn"] = "Turno {0}",
            ["ui.toast.turnOf"] = "Vez do {0}",
            ["ui.toast.ai"] = "A IA planeja suas ordens…",
            ["ui.toast.you"] = "Preparação: +{0} Moral, {1}.",
            ["ui.toast.result"] = "Resultado do confronto",
            ["ui.toast.blocked"] = "Captura bloqueada",
            ["ui.toast.blockedByCard"] = "{0} protegeu {1} em {2}.",
            ["ui.toast.blockedByGear"] = "A proteção equipada salvou {0} em {1}.",
            ["ui.toast.captured"] = "Captura bem-sucedida",
            ["ui.toast.capturedText"] = "Vitória de {0} sobre {1} em {2}.",
            ["ui.toast.capturedVolley"] = "Vitória de {0} sobre {1} em {2}, com {3}.",
            ["ui.toast.retreat"] = "{0} recua de {1} para {2}; {3} ocupa {1}.",
            ["ui.toast.retreatRanged"] = "{0} recua de {1} para {2}.",
            ["ui.toast.countercharge"] = "{0} salta de {1} para {2}; a captura falha.",
            ["ui.chip.attackerStays"] = "Atacante segue em {0}",
            ["ui.chip.attackerMoves"] = "Atacante ocupa {0}",
            ["ui.chip.order"] = "{0} gastou 1 Ordem",
            ["ui.chip.morale"] = "{0} gasta {1} Moral",
            ["ui.chip.gearSpent"] = "Proteção consumida",
            ["ui.chip.lost"] = "{0} perde {1}",
            ["ui.chip.noReaction"] = "Sem reação do oponente",
            ["ui.chip.reaction"] = "Reação: {0}",
            ["ui.chip.noLoss"] = "Nenhuma unidade perdida",
            ["ui.chip.ground"] = "{0} cede território",

            ["ui.discard.eyebrow"] = "Pilha de descarte",
            ["ui.discard.title"] = "Descarte",
            ["ui.discard.yours"] = "Seu descarte · {0}",
            ["ui.discard.opponent"] = "Oponente · {0}",
            ["ui.discard.turn"] = "Turno {0}",
            ["ui.discard.empty"] = "Nenhuma carta descartada ainda.",
            ["ui.discard.footer"] = "Mais recentes primeiro · Baralho {0} · Descarte {1}",
            ["ui.discard.close"] = "Esc ou clique fora para fechar",
            ["ui.discard.back"] = "Voltar à partida",
            ["ui.note.blocked"] = "Protegeu {0}",
            ["ui.note.retreat"] = "Recuo de {0} para {1}",
            ["ui.note.guard"] = "Interceptou a captura em {0}",
            ["ui.note.failed"] = "Não evitou a perda em {0}",
            ["ui.note.equip"] = "Equipou {0}",
            ["ui.note.fortified"] = "Erguida em {0}",
            ["ui.note.rations"] = "Passo extra de {0}",
            ["ui.note.volley"] = "Ataque contra {0}",
            ["ui.note.march"] = "Movimento adicional",
            ["ui.note.omen"] = "Viu 3 cartas e comprou 1",
            ["ui.note.feint"] = "Finta com {0}",
            ["ui.note.swap"] = "Troca a partir de {0}",
            ["ui.note.advance"] = "Duas Infantarias avançam",
            ["ui.note.scouts"] = "Reordenou o topo do baralho",
            ["ui.note.wagon"] = "+1 Moral na Preparação",
            ["ui.note.cleared"] = "Dissipou {0}",
            ["ui.note.drew"] = "Comprou 1 carta",
            ["ui.note.echo"] = "Devolveu 1 carta ao baralho",
            ["ui.note.weather"] = "Mudou o clima",

            ["ui.foresight.eyebrow"] = "{0} · {1}",
            ["ui.foresight.title"] = "Escolha 1 carta",
            ["ui.foresight.lead"] = "Estas são as {0} próximas cartas do seu baralho.",
            ["ui.foresight.leadOne"] = "Esta é a próxima carta do seu baralho.",
            ["ui.foresight.rest"] = "As outras {0} cartas voltam para o fundo do baralho.",
            ["ui.foresight.restOne"] = "A outra carta volta para o fundo do baralho.",
            ["ui.foresight.chosen"] = "Escolhida",
            ["ui.foresight.buy"] = "Comprar {0}",
            ["ui.foresight.keep"] = "Manter {0} no topo",
            ["ui.foresight.recover"] = "Devolver {0} ao topo",
            ["ui.foresight.leadEcho"] = "Escolha uma carta não arcana do seu descarte.",

            ["ui.gameOver.header"] = "Fim de partida",
            ["ui.gameOver.victory"] = "Vitória",
            ["ui.gameOver.defeat"] = "Derrota",
            ["ui.gameOver.won"] = "O Soberano {0} caiu em {1} no turno {2}.",
            ["ui.gameOver.lost"] = "Seu Soberano caiu em {0} no turno {1}.",
            ["ui.gameOver.turns"] = "turnos",
            ["ui.gameOver.captures"] = "capturas",
            ["ui.gameOver.saves"] = "reações que salvaram peças",
            ["ui.gameOver.cards"] = "cartas jogadas",
            ["ui.gameOver.button"] = "Nova partida",
            ["ui.gameOver.review"] = "Rever histórico",
            ["ui.gameOver.menu"] = "Menu principal",
            ["ui.gameOver.show"] = "Ver resultado",

            ["ui.pause.eyebrow"] = "Partida pausada · turno {0}",
            ["ui.pause.title"] = "Pausa",
            ["ui.pause.resume"] = "Retomar",
            ["ui.pause.rules"] = "Regras do jogo",
            ["ui.pause.settings"] = "Configurações",
            ["ui.pause.abandon"] = "Abandonar partida",
            ["ui.pause.hint"] = "Esc para retomar",
            ["ui.confirm.eyebrow"] = "Confirmar",
            ["ui.confirm.title"] = "Abandonar a partida?",
            ["ui.confirm.text"] = "A partida será encerrada e não poderá ser retomada.",
            ["ui.confirm.stay"] = "Continuar jogando",
            ["ui.confirm.abandon"] = "Abandonar",
        };
    }
}
