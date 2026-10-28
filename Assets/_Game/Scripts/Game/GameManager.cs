using System.Collections;
using System.Collections.Generic;
using ArcaneGambit.Rules;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace ArcaneGambit.Game
{
    /// <summary>
    /// Match against the computer: the player's army is drawn at random and the other one is driven by <see cref="Ai"/>.
    /// Builds the screen in code, turns clicks into <see cref="Match"/> commands and animates the resulting events.
    /// The Play* methods are the swap points for Feel (MMF_Player) later.
    /// </summary>
    public sealed class GameManager : MonoBehaviour
    {
        // 1920×1080 grid of the art direction: 400 | board and hand | 500 columns, 32 apart, 40 side margins.
        const float StageWidth = 1920f;
        const float StageHeight = 1080f;
        const float LeftX = 40f, LeftWidth = 400f;
        const float CenterX = 472f, CenterWidth = 876f;
        const float RightX = 1380f, RightWidth = 500f;
        const float HeaderHeight = 60f, ColumnTop = 76f, ColumnBottom = 1061f, Gap = 16f;
        // The board keeps its 700-unit art coordinates and is scaled up to 760 px on screen.
        const float BoardSize = 700f, BoardScale = 760f / 700f;
        const float BoardX = CenterX + (CenterWidth - BoardSize * BoardScale) * 0.5f;
        const float Inset = 30f, Square = 80f, PieceSize = 70f;
        const float HandScale = 0.605f, HandY = ColumnTop + BoardSize * BoardScale + 14f;
        const float OpponentPanelHeight = 240f, PlayerPanelHeight = 280f;
        const float ActionPanelHeight = 486f, DeckPanelHeight = 174f;
        const float WaitingAlpha = 0.5f;

        [SerializeField, Tooltip("0 = random seed for every match")] int _seed;
        [SerializeField, Min(0.01f)] float _moveDuration = 0.22f;
        [SerializeField, Min(0f), Tooltip("Pause before each AI decision, so its moves can be followed")]
        float _aiDelay = 0.7f;

        enum Mode { Idle, Unit, Recruit, Card }

        enum StepState { Done, Now, Next }

        sealed class Piece
        {
            public int Id;
            public RectTransform Rt;
            public Image Body;
            public CanvasGroup Group;
            public Image Shield;
            public Image Hammer;
            public Coroutine Anim;
        }

        /// <summary>The player's panel has Orders and recruit tiles; the opponent's shows a read-only summary.
        /// Fields that do not apply to the panel's role stay null.</summary>
        sealed class ArmyPanel
        {
            public RectTransform Root;
            public CanvasGroup Group;
            public TextMeshProUGUI Subtitle, Morale, Orders, Summary, ReserveHint;
            public Image[] Pips, OrderIcons, RecruitHighlights;
            public TextMeshProUGUI[] ReserveCounts;
            public Button[] RecruitButtons;
            public CanvasGroup[] RecruitGroups;
        }

        Match _match;
        Mode _mode;
        Unit _selected;
        UnitType _recruitType;
        CardId _card;
        int _cardIndex = -1;
        readonly List<Coord> _picks = new List<Coord>();
        Side _human;
        Ai _ai;
        bool _awaitingStart;
        Coroutine _aiStep;
        bool _waiting;
        Coroutine _waitingFade;

        /// <summary>The player may issue commands (their Maneuvers phase, after the opening dialog).</summary>
        bool HumanTurn => !_awaitingStart && _match.Phase == Phase.Maneuvers && _match.Current == _human;

        RectTransform _stage, _piecesLayer, _palisadeLayer, _hand, _overlay, _overlayContent;
        readonly Image[,] _under = new Image[Match.Size, Match.Size];
        readonly Image[,] _hint = new Image[Match.Size, Match.Size];
        readonly TextMeshProUGUI[] _fileLabels = new TextMeshProUGUI[Match.Size];
        readonly TextMeshProUGUI[] _rankLabels = new TextMeshProUGUI[Match.Size];
        bool _flipped;
        readonly Dictionary<int, Piece> _pieces = new Dictionary<int, Piece>();
        readonly Dictionary<Coord, Image> _palisades = new Dictionary<Coord, Image>();
        readonly ArmyPanel[] _armies = new ArmyPanel[2];
        CanvasScaler _scaler;
        TextMeshProUGUI _phases, _turn, _actionBand, _deck, _discard, _discardLast;
        RectTransform _actionSlot, _actionDetails, _discardPile, _logEntries;
        Button _endTurn, _actionPrimary, _actionCancel;
        CanvasGroup _handGroup, _rightColumn;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            if (FindAnyObjectByType<GameManager>() != null) return;
            var host = GameObject.Find("GameManager");
            if (host == null) host = new GameObject("GameManager");
            host.AddComponent<GameManager>();
        }

        void Start()
        {
            BuildUi();
            NewMatch();
        }

        void Update()
        {
            float aspect = (float)Screen.width / Mathf.Max(1, Screen.height);
            _scaler.matchWidthOrHeight = aspect >= StageWidth / StageHeight ? 1f : 0f;

            var mouse = Mouse.current;
            var keyboard = Keyboard.current;
            bool cancel = (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
                || (mouse != null && mouse.rightButton.wasPressedThisFrame);
            if (cancel && _mode != Mode.Idle)
            {
                ClearMode();
                Refresh();
            }
        }

        void NewMatch()
        {
            foreach (var p in _pieces.Values) Destroy(p.Rt.gameObject);
            _pieces.Clear();
            foreach (var img in _palisades.Values) Destroy(img.gameObject);
            _palisades.Clear();
            if (_aiStep != null) StopCoroutine(_aiStep);
            _aiStep = null;
            if (_waitingFade != null) StopCoroutine(_waitingFade);
            _waitingFade = null;
            _waiting = false;
            _handGroup.alpha = _rightColumn.alpha = 1f;

            int seed = _seed != 0 ? _seed : System.Environment.TickCount;
            _match = Match.CreateDefault(seed);
            _human = new System.Random(seed).Next(2) == 0 ? Side.White : Side.Black;
            _ai = new Ai(_human.Other(), seed);
            _awaitingStart = true;
            LayoutForPlayer();
            ClearMode();
            Sync();
        }

        // ================================================================== commands

        void OnSquareClicked(Coord c)
        {
            if (!HumanTurn) return;
            var unit = _match.At(c);

            switch (_mode)
            {
                case Mode.Recruit:
                    _match.Recruit(_recruitType, c);
                    ClearMode();
                    Sync();
                    return;

                case Mode.Card when _match.CanPlay(_card):
                    if (!_match.CardTargets(_card, _picks).Contains(c)) return;
                    _picks.Add(c);
                    if (_picks.Count == Match.PickCount(_card))
                    {
                        var card = _card;
                        var picks = new List<Coord>(_picks);
                        ClearMode();
                        _match.PlayCard(card, picks);
                    }
                    Sync();
                    return;

                case Mode.Unit:
                    if (_match.CaptureTargets(_selected).Contains(c))
                    {
                        var attacker = _selected;
                        ClearMode();
                        _match.DeclareCapture(attacker, c);
                        Sync();
                        return;
                    }
                    if (_match.MoveTargets(_selected).Contains(c))
                    {
                        _match.Move(_selected, c);
                        Sync();
                        return;
                    }
                    break;
            }

            ClearMode();
            if (unit != null && unit.Side == _match.Current)
            {
                _mode = Mode.Unit;
                _selected = unit;
            }
            Refresh();
        }

        void OnRecruitClicked(Side side, UnitType type)
        {
            if (side != _human || !HumanTurn || !_match.CanRecruit(type)) return;
            ClearMode();
            _mode = Mode.Recruit;
            _recruitType = type;
            Refresh();
        }

        void OnCardClicked(int index)
        {
            if (!HumanTurn) return;
            bool same = _mode == Mode.Card && _cardIndex == index;
            ClearMode();
            if (!same)
            {
                _mode = Mode.Card;
                _cardIndex = index;
                _card = _match.CurrentPlayer.Hand[index];
            }
            Refresh();
        }

        void OnActionPrimary()
        {
            if (_mode != Mode.Card || Match.PickCount(_card) != 0) return;
            var card = _card;
            ClearMode();
            _match.PlayCard(card, new Coord[0]);
            Sync();
        }

        void OnEndTurn()
        {
            if (!HumanTurn) return;
            ClearMode();
            _match.EndTurn();
            Sync();
        }

        void OnReaction(CardId? card)
        {
            _match.React(card);
            Sync();
        }

        void OnForesight(int index)
        {
            _match.ChooseForesight(index);
            Sync();
        }

        void OnStartMatch()
        {
            _awaitingStart = false;
            Sync();
        }

        /// <summary>Lets the AI take its next decision after a short pause; <see cref="Sync"/> re-arms it until
        /// the match waits for the player again.</summary>
        void ScheduleAi()
        {
            if (_aiStep == null && !_awaitingStart && _ai.HasDecision(_match)) _aiStep = StartCoroutine(AiStepRoutine());
        }

        IEnumerator AiStepRoutine()
        {
            yield return new WaitForSeconds(_aiDelay);
            _aiStep = null;
            _ai.Act(_match);
            Sync();
        }

        void ClearMode()
        {
            _mode = Mode.Idle;
            _selected = null;
            _cardIndex = -1;
            _picks.Clear();
        }

        // ================================================================== sync

        /// <summary>Consumes rule events (animations) and redraws the HUD.</summary>
        void Sync()
        {
            foreach (var e in _match.DrainEvents())
            {
                switch (e.Kind)
                {
                    case EventKind.Moved:
                    case EventKind.Retreated:
                    case EventKind.Intercepted:
                        if (_pieces.TryGetValue(e.UnitId, out var moved)) PlayMove(moved, e.To);
                        break;
                    case EventKind.Captured:
                        if (_pieces.TryGetValue(e.UnitId, out var captured))
                        {
                            _pieces.Remove(e.UnitId);
                            PlayCapture(captured);
                        }
                        break;
                    case EventKind.Recruited:
                        PlayRecruit(CreatePiece(_match.FindUnit(e.UnitId)));
                        break;
                    case EventKind.Blocked:
                        if (_pieces.TryGetValue(e.UnitId, out var blocked)) PlayBlocked(blocked, e.From, e.To);
                        break;
                }
            }
            SyncPieces();
            Refresh();
            ScheduleAi();
        }

        void SyncPieces()
        {
            var alive = new HashSet<int>();
            foreach (var unit in _match.Units)
            {
                alive.Add(unit.Id);
                if (!_pieces.TryGetValue(unit.Id, out var piece)) piece = CreatePiece(unit);
                if (piece.Anim == null) piece.Rt.anchoredPosition = SquareCenter(unit.Pos);
                piece.Shield.enabled = unit.Protection > 0;
                piece.Hammer.enabled = unit.HasHammer;
                bool resting = unit.RecruitedTurn == _match.Turn;
                piece.Body.color = resting ? new Color(1f, 1f, 1f, 0.7f) : Color.white;
            }

            var dead = new List<int>();
            foreach (var id in _pieces.Keys) if (!alive.Contains(id)) dead.Add(id);
            foreach (var id in dead)
            {
                Destroy(_pieces[id].Rt.gameObject);
                _pieces.Remove(id);
            }

            var standing = new HashSet<Coord>();
            foreach (var p in _match.Palisades)
            {
                standing.Add(p.Pos);
                if (_palisades.ContainsKey(p.Pos)) continue;
                var img = Ui.Image(_palisadeLayer, "Palisade " + p.Pos, Ui.Art("Palisade"), 0, 0, Square, Square);
                var rt = img.rectTransform;
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = SquareCenter(p.Pos);
                _palisades[p.Pos] = img;
                StartCoroutine(PopRoutine(rt));
            }
            var fallen = new List<Coord>();
            foreach (var pos in _palisades.Keys) if (!standing.Contains(pos)) fallen.Add(pos);
            foreach (var pos in fallen)
            {
                Destroy(_palisades[pos].gameObject);
                _palisades.Remove(pos);
            }
        }

        void Refresh()
        {
            RefreshHeader();
            RefreshArmy(Side.White);
            RefreshArmy(Side.Black);
            RefreshBoard();
            RefreshHand();
            RefreshAction();
            RefreshLog();
            RefreshOverlay();
            RefreshWaiting();
        }

        /// <summary>Fades the player's side of the screen (army panel, hand, right column) while the AI plays.</summary>
        void RefreshWaiting()
        {
            bool waiting = !_awaitingStart && _match.Phase != Phase.GameOver && _match.Current != _human;
            if (waiting == _waiting) return;
            _waiting = waiting;
            PlayWaitingFade(waiting ? WaitingAlpha : 1f);
        }

        void RefreshHeader()
        {
            string Tab(string key, bool on) =>
                on ? $"<color={Ui.ToHex(Ui.Rubric)}><u>{Loc.Get(key)}</u></color>" : Loc.Get(key);
            bool maneuvers = _match.Phase == Phase.Maneuvers || _match.Phase == Phase.Foresight;
            _phases.text = $"{Tab("ui.phase.preparation", _awaitingStart)}  ·  "
                + $"{Tab("ui.phase.maneuvers", maneuvers && !_awaitingStart)}  ·  "
                + $"{Tab("ui.phase.confrontation", _match.Phase == Phase.Reaction)}  ·  "
                + Tab("ui.phase.end", false);
            _turn.text = Loc.Format("ui.turnOf", _match.Turn, Loc.Army(_match.Current));
            _endTurn.interactable = HumanTurn;
        }

        void RefreshArmy(Side side)
        {
            var ui = _armies[(int)side];
            var player = _match.Get(side);
            bool active = side == _match.Current;
            bool human = side == _human;

            ui.Subtitle.text = Loc.Get(human
                ? active ? "ui.army.yourTurn" : "ui.army.youWaiting"
                : active ? "ui.army.aiTurn" : "ui.army.aiWaiting");
            ui.Subtitle.color = active ? Ui.Rubric : Ui.InkSoft;
            ui.Morale.text = Loc.Format("ui.moraleValue", player.Morale, Match.MaxMorale);
            // The Morale the current selection would spend is previewed as faded pips.
            int cost = human && HumanTurn ? PendingCost() : 0;
            for (int i = 0; i < ui.Pips.Length; i++)
            {
                ui.Pips[i].sprite = Ui.Art(i < player.Morale ? "Pip_On" : "Pip_Off");
                ui.Pips[i].color = i >= player.Morale - cost && i < player.Morale ? new Color(1f, 1f, 1f, 0.4f) : Color.white;
            }

            for (int i = 0; i < Match.Recruitable.Length; i++)
                ui.ReserveCounts[i].text = Loc.Format("ui.reserveCount", player.Reserve[Match.Recruitable[i]]);

            if (!human)
            {
                ui.Summary.text = Loc.Format("ui.handSummary", player.Hand.Count, player.Deck.Count, player.Discard.Count);
                return;
            }

            int orders = active ? player.Orders : Match.OrdersPerTurn;
            int bonus = active ? player.BonusMoves : 0;
            ui.Orders.text = active
                ? Loc.Format("ui.orders", orders, Match.OrdersPerTurn) + (bonus > 0 ? Loc.Format("ui.ordersBonus", bonus) : "")
                : Loc.Get("ui.ordersIdle");
            int shown = Match.OrdersPerTurn + bonus;
            for (int i = 0; i < ui.OrderIcons.Length; i++)
            {
                var icon = ui.OrderIcons[i];
                icon.enabled = i < shown;
                icon.sprite = Ui.Art(active && i < orders + bonus ? "Order_On" : "Order_Off");
                icon.rectTransform.anchoredPosition = new Vector2(LeftWidth - 20f - (shown - i) * 44f, -104f);
            }

            bool choosing = active && _mode == Mode.Recruit;
            ui.ReserveHint.text = Loc.Get(choosing ? "ui.reserveChoose" : "ui.reserveHint");
            for (int i = 0; i < Match.Recruitable.Length; i++)
            {
                var type = Match.Recruitable[i];
                bool can = HumanTurn && _match.CanRecruit(type);
                ui.RecruitButtons[i].interactable = can;
                // Out of turn the whole panel already fades (RefreshWaiting).
                ui.RecruitGroups[i].alpha = can || !HumanTurn ? 1f : 0.45f;
                ui.RecruitHighlights[i].enabled = choosing && _recruitType == type;
            }
        }

        /// <summary>Morale the current selection would spend if confirmed.</summary>
        int PendingCost() => _mode switch
        {
            Mode.Recruit => Match.Cost(_recruitType),
            Mode.Card when _match.CanPlay(_card) => CardCatalog.Get(_card).Cost,
            _ => 0,
        };

        void RefreshBoard()
        {
            for (int x = 0; x < Match.Size; x++)
                for (int y = 0; y < Match.Size; y++)
                {
                    _under[x, y].enabled = false;
                    _hint[x, y].enabled = false;
                }

            if (_awaitingStart || _match.Phase == Phase.GameOver) return;

            foreach (var unit in _match.Units)
                if (unit.Side == _human && _match.IsThreatened(unit))
                    Mark(_under, unit.Pos, "Marker_Threat");

            if (_match.Pending != null)
            {
                Mark(_under, _match.Pending.From, "Marker_Selected");
                Mark(_hint, _match.Pending.Target, "Marker_Target");
                return;
            }

            switch (_mode)
            {
                case Mode.Unit:
                    Mark(_under, _selected.Pos, "Marker_Selected");
                    foreach (var c in _match.MoveTargets(_selected)) Mark(_hint, c, "Marker_Move");
                    foreach (var c in _match.CaptureTargets(_selected)) Mark(_hint, c, "Marker_Target");
                    break;
                case Mode.Recruit:
                    foreach (var c in _match.RecruitSquares()) Mark(_hint, c, "Marker_Recruit");
                    break;
                case Mode.Card when _match.CanPlay(_card):
                    foreach (var c in _picks) Mark(_under, c, "Marker_Selected");
                    foreach (var c in _match.CardTargets(_card, _picks))
                    {
                        var occupant = _match.At(c);
                        Mark(_hint, c, occupant != null && occupant.Side != _match.Current ? "Marker_Target" : "Marker_Recruit");
                    }
                    break;
            }
        }

        static void Mark(Image[,] layer, Coord c, string sprite)
        {
            var img = layer[c.X, c.Y];
            img.sprite = Ui.Art(sprite);
            img.enabled = true;
        }

        void RefreshHand()
        {
            for (int i = _hand.childCount - 1; i >= 0; i--) Destroy(_hand.GetChild(i).gameObject);
            if (_awaitingStart || _match.Phase == Phase.GameOver) return;

            var hand = _match.Get(_human).Hand;
            float w = Ui.CardWidth * HandScale;
            float h = Ui.CardHeight * HandScale;
            float area = _hand.rect.width;
            float step = hand.Count > 1 ? Mathf.Min(w + 14f, (area - w) / (hand.Count - 1)) : 0f;
            float start = (area - (w + step * (hand.Count - 1))) * 0.5f;
            float middle = (hand.Count - 1) * 0.5f;
            RectTransform selectedCard = null;

            for (int i = 0; i < hand.Count; i++)
            {
                var info = CardCatalog.Get(hand[i]);
                bool selected = _mode == Mode.Card && _cardIndex == i;
                // Loose fan: outer cards dip and tilt; the chosen one rises.
                float d = i - middle;
                float top = selected ? -14f : d * d * 2f;

                var card = Ui.Card(_hand, info, HandScale);
                card.pivot = new Vector2(0.5f, 0f);
                card.anchoredPosition = new Vector2(start + step * i + w * 0.5f, -(top + h));
                card.localRotation = Quaternion.Euler(0f, 0f, -2f * d);
                if (selected)
                {
                    var outline = Ui.Image(card, "Outline", Ui.Solid, -5f, -5f, w + 10f, h + 10f);
                    outline.color = Ui.Rubric;
                    outline.transform.SetAsFirstSibling();
                }

                var group = card.gameObject.AddComponent<CanvasGroup>();
                group.alpha = !HumanTurn || info.IsReaction || _match.CanPlay(info.Id) ? 1f : 0.55f;
                if (selected) selectedCard = card;

                int index = i;
                var button = card.gameObject.AddComponent<Button>();
                button.transition = Selectable.Transition.None;
                button.onClick.AddListener(() => OnCardClicked(index));
                AddHover(card, selected);
            }
            // Drawn last so no neighbour overlaps it.
            if (selectedCard != null) selectedCard.SetAsLastSibling();
        }

        /// <summary>Lifts the card while hovered, from wherever it currently rests.</summary>
        static void AddHover(RectTransform card, bool selected)
        {
            if (selected) return;
            var rest = card.anchoredPosition;
            var trigger = card.gameObject.AddComponent<EventTrigger>();
            var enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
            enter.callback.AddListener(_ => card.anchoredPosition = rest + new Vector2(0f, 16f));
            var exit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
            exit.callback.AddListener(_ => card.anchoredPosition = rest);
            trigger.triggers.Add(enter);
            trigger.triggers.Add(exit);
        }

        /// <summary>Rebuilds the action panel: the subject (card, or a plate for a unit/army) on the left,
        /// numbered steps and figures on the right.</summary>
        void RefreshAction()
        {
            for (int i = _actionSlot.childCount - 1; i >= 0; i--) Destroy(_actionSlot.GetChild(i).gameObject);
            for (int i = _actionDetails.childCount - 1; i >= 0; i--) Destroy(_actionDetails.GetChild(i).gameObject);

            var p = _match.Get(_human);
            string band;
            var steps = new List<(string title, string text, StepState state)>();
            var rows = new List<(string label, string value)>();
            bool showPrimary = false, primaryEnabled = false, showCancel = _mode != Mode.Idle;

            bool live = !_awaitingStart && _match.Phase != Phase.GameOver;
            bool aiReacting = live && _match.Phase == Phase.Reaction && _match.Pending.Defender.Side == _ai.Side;
            if (aiReacting || (live && _match.Current == _ai.Side))
            {
                var ai = _match.Get(_ai.Side);
                band = Loc.Get(aiReacting ? "ui.reaction.header" : "ui.action.aiTitle");
                ArmyPlate(_ai.Side, "ui.plate.enemyText");
                steps.Add((Loc.Get("ui.step.opponent"),
                    Loc.Format(aiReacting ? "ui.action.aiReacting" : "ui.action.aiHint", Loc.Army(_ai.Side)), StepState.Now));
                rows.Add((Loc.Get("ui.morale"), Loc.Format("ui.moraleValue", ai.Morale, Match.MaxMorale)));
                rows.Add((Loc.Get("ui.action.orders"), ai.Orders.ToString()));
            }
            else switch (_mode)
            {
                case Mode.Unit:
                {
                    var u = _selected;
                    band = Loc.Get("ui.action.unitTitle");
                    UnitPlate(u.Type, u.Side, "ui.plate.unit");
                    steps.Add((Loc.Get("ui.step.unit"), Loc.UnitAt(u), StepState.Done));
                    steps.Add((Loc.Get("ui.step.order"), Loc.Get(u.RecruitedTurn == _match.Turn ? "ui.action.hintResting"
                        : p.Orders <= 0 && p.BonusMoves <= 0 ? "ui.action.hintNoOrders"
                        : "ui.action.hintUnit"), StepState.Now));
                    rows.Add((Loc.Get("ui.action.moves"), _match.MoveTargets(u).Count.ToString()));
                    rows.Add((Loc.Get("ui.action.captures"), _match.CaptureTargets(u).Count.ToString()));
                    var gear = new List<string>();
                    if (u.Protection > 0) gear.Add(Loc.Get("ui.action.protection"));
                    if (u.HasHammer) gear.Add(Loc.Get("ui.action.hammer"));
                    rows.Add((Loc.Get("ui.action.equipment"), gear.Count > 0 ? string.Join(" · ", gear) : Loc.Get("ui.action.none")));
                    break;
                }
                case Mode.Recruit:
                {
                    int cost = Match.Cost(_recruitType);
                    band = Loc.Format("ui.action.recruitTitle", Loc.UnitName(_recruitType));
                    UnitPlate(_recruitType, _match.Current, "ui.plate.reserve");
                    steps.Add((Loc.Get("ui.step.reserve"), Loc.UnitName(_recruitType), StepState.Done));
                    steps.Add((Loc.Get("ui.step.square"),
                        Loc.Format("ui.action.hintRecruit", Loc.Get("ui.ranks." + _match.Current)), StepState.Now));
                    rows.Add((Loc.Get("ui.action.cost"), Loc.Format("ui.action.recruitCost", cost)));
                    rows.Add((Loc.Get("ui.action.inReserve"), Loc.Format("ui.action.reserveValue", p.Reserve[_recruitType])));
                    rows.Add((Loc.Get("ui.action.moraleAfter"), Loc.Format("ui.moraleValue", p.Morale - cost, Match.MaxMorale)));
                    break;
                }
                case Mode.Card:
                {
                    var info = CardCatalog.Get(_card);
                    band = Loc.KindName(info.Kind);
                    Ui.Card(_actionSlot, info, 1f);
                    CardSteps(info, steps);
                    rows.Add((Loc.Get("ui.action.cost"), Loc.Format("ui.action.cardCost", info.Cost)
                        + (_card == CardId.CrossbowVolley ? Loc.Get("ui.action.plusOrder") : "")));
                    if (_match.CanPlay(_card))
                        rows.Add((Loc.Get("ui.action.moraleAfter"),
                            Loc.Format("ui.moraleValue", p.Morale - info.Cost, Match.MaxMorale)));
                    showPrimary = !info.IsReaction && Match.PickCount(_card) == 0;
                    primaryEnabled = _match.CanPlay(_card);
                    break;
                }
                default:
                    band = Loc.Get("ui.action.idleTitle");
                    ArmyPlate(_human, "ui.plate.armyText");
                    steps.Add((Loc.Get("ui.step.next"), Loc.Get("ui.action.idleHint"), StepState.Now));
                    rows.Add((Loc.Get("ui.morale"), Loc.Format("ui.moraleValue", p.Morale, Match.MaxMorale)));
                    rows.Add((Loc.Get("ui.action.orders"), p.BonusMoves > 0
                        ? Loc.Format("ui.action.ordersBonus", p.Orders, p.BonusMoves) : p.Orders.ToString()));
                    rows.Add((Loc.Get("ui.action.proactive"),
                        Loc.Get(p.PlayedProactive ? "ui.action.proactiveUsed" : "ui.action.proactiveAvailable")));
                    break;
            }

            _actionBand.text = band;
            BuildActionDetails(steps, rows);
            _actionPrimary.gameObject.SetActive(showPrimary);
            Ui.SetButton(_actionPrimary, Loc.Get("ui.action.playCard"), primaryEnabled);
            _actionCancel.gameObject.SetActive(showCancel);
        }

        void UnitPlate(UnitType type, Side side, string kindKey) =>
            Ui.Plate(_actionSlot, Loc.UnitName(type), Ui.UnitArt(type, side), Loc.Get(kindKey), Loc.UnitText(type));

        void ArmyPlate(Side side, string textKey) =>
            Ui.Plate(_actionSlot, Loc.Army(side), Ui.UnitArt(UnitType.Sovereign, side), Loc.Get("ui.plate.army"),
                Loc.Get(textKey));

        /// <summary>One step per square the card asks for: picked ones show what was chosen.</summary>
        void CardSteps(CardInfo info, List<(string title, string text, StepState state)> steps)
        {
            string hint = CardHint(info);
            int count = Match.PickCount(info.Id);
            if (count == 0 || !_match.CanPlay(info.Id))
            {
                steps.Add((Loc.Get(_match.CanPlay(info.Id) ? "ui.step.confirm" : "ui.step.next"), hint, StepState.Now));
                return;
            }
            for (int i = 0; i < count; i++)
            {
                string title = Loc.Get($"ui.step.{info.Id}.{i}");
                if (i < _picks.Count)
                {
                    var unit = _match.At(_picks[i]);
                    steps.Add((title, unit != null ? Loc.UnitAt(unit) : _picks[i].ToString(), StepState.Done));
                }
                else steps.Add((title, i == _picks.Count ? hint : "", i == _picks.Count ? StepState.Now : StepState.Next));
            }
        }

        void BuildActionDetails(List<(string title, string text, StepState state)> steps,
            List<(string label, string value)> rows)
        {
            float w = _actionDetails.rect.width, y = 0f;
            for (int i = 0; i < steps.Count; i++)
            {
                var (title, text, state) = steps[i];
                var marker = Ui.Image(_actionDetails, "Step", Ui.Art(state == StepState.Next ? "Pip_Off" : "Seal"),
                    0f, y, 22f, 22f);
                if (state == StepState.Done) marker.color = Ui.Ink;
                Ui.Text(marker.transform, (i + 1).ToString(), Ui.TitleFont, 12f,
                    state == StepState.Next ? Ui.InkText : Ui.Cream, 0f, 0f, 22f, 22f, TextAlignmentOptions.Center);

                var label = Ui.Label(_actionDetails, title, 30f, y + 1f, w - 30f, 16f);
                label.fontSize = 12f;
                float textHeight = 0f;
                if (!string.IsNullOrEmpty(text))
                {
                    var body = Ui.Text(_actionDetails, text, Ui.BodyFont, 15f, Ui.InkText, 30f, y + 18f, w - 30f, 0f);
                    textHeight = body.GetPreferredValues(text, w - 30f, 0f).y;
                    body.rectTransform.sizeDelta = new Vector2(w - 30f, textHeight);
                }
                y += Mathf.Max(22f, 18f + textHeight) + 12f;
            }

            Ui.Line(_actionDetails, 0f, y, w, 1f, 0.35f);
            y += 12f;
            foreach (var (label, value) in rows)
            {
                Ui.Text(_actionDetails, label, Ui.BodyFont, 15f, Ui.InkText, 0f, y, w, 22f).textWrappingMode =
                    TextWrappingModes.NoWrap;
                Ui.Text(_actionDetails, value, Ui.BodyFont, 15f, Ui.InkText, 0f, y, w, 22f, TextAlignmentOptions.TopRight)
                    .textWrappingMode = TextWrappingModes.NoWrap;
                y += 25f;
            }
        }

        string CardHint(CardInfo info)
        {
            var p = _match.CurrentPlayer;
            if (info.IsReaction) return Loc.Get("ui.hint.reaction");
            if (p.PlayedProactive) return Loc.Get("ui.hint.alreadyPlayed");
            if (p.Morale < info.Cost) return Loc.Get("ui.hint.noMorale");
            if (!_match.CanPlay(info.Id)) return Loc.Get("ui.hint.noTarget");
            return info.Id switch
            {
                CardId.ReinforcedShield or CardId.ReinforcedArmor or CardId.WarHammer or CardId.ImprovisedPalisade
                    => Loc.Get("ui.hint." + info.Id),
                CardId.CrossbowVolley or CardId.ExtraRations => Loc.Get($"ui.hint.{info.Id}.{_picks.Count}"),
                _ => Loc.Get("ui.hint.confirm"),
            };
        }

        /// <summary>Ruled history rows, newest first, as many as fit; then the deck and discard counts.</summary>
        void RefreshLog()
        {
            for (int i = _logEntries.childCount - 1; i >= 0; i--) Destroy(_logEntries.GetChild(i).gameObject);
            var log = _match.Log;
            float w = _logEntries.rect.width, textWidth = w - 42f, y = 0f;
            for (int i = log.Count - 1; i >= 0; i--)
            {
                string line = Loc.LogLine(log[i]);
                var entry = Ui.Text(_logEntries, line, Ui.BodyFont, 15f, Ui.InkText, 42f, y + 7f, textWidth, 0f);
                float height = entry.GetPreferredValues(line, textWidth, 0f).y;
                if (y + height + 14f > _logEntries.rect.height)
                {
                    Destroy(entry.gameObject);
                    break;
                }
                entry.rectTransform.sizeDelta = new Vector2(textWidth, height);
                Ui.Line(_logEntries, 0f, y, w, 1f, 0.3f);
                Ui.Text(_logEntries, Ui.Roman(log[i].Turn), Ui.TitleFont, 15f, Ui.Rubric, 0f, y + 7f, 40f, 20f);
                y += height + 14f;
            }

            var p = _match.Get(_human);
            _deck.text = Loc.Format("ui.cardCount", p.Deck.Count);
            _discard.text = Loc.Format("ui.cardCount", p.Discard.Count);
            for (int i = _discardPile.childCount - 1; i >= 0; i--) Destroy(_discardPile.GetChild(i).gameObject);
            if (p.Discard.Count == 0)
            {
                _discardLast.text = Loc.Get("ui.discardEmpty");
                return;
            }
            var last = p.Discard[p.Discard.Count - 1];
            _discardLast.text = Loc.Format("ui.discardLast", Loc.CardName(last));
            // Two loose sheets under the last discarded card.
            Ui.Image(_discardPile, "Sheet", Ui.Art("CardFrame"), 10f, 8f, 89f, 125f).rectTransform.localRotation =
                Quaternion.Euler(0f, 0f, -9f);
            Ui.Image(_discardPile, "Sheet", Ui.Art("CardFrame"), 6f, 6f, 89f, 125f).rectTransform.localRotation =
                Quaternion.Euler(0f, 0f, 7f);
            var top = Ui.Card(_discardPile, CardCatalog.Get(last), 0.36f);
            top.anchoredPosition = new Vector2(10f, -6f);
            top.localRotation = Quaternion.Euler(0f, 0f, -3f);
        }

        // ================================================================== overlays

        void RefreshOverlay()
        {
            for (int i = _overlayContent.childCount - 1; i >= 0; i--) Destroy(_overlayContent.GetChild(i).gameObject);
            _overlay.gameObject.SetActive(true);

            if (_match.Phase == Phase.GameOver) BuildGameOver();
            else if (_awaitingStart) BuildIntro();
            else if (_match.Phase == Phase.Reaction && _match.Pending.Defender.Side == _human) BuildReaction();
            else if (_match.Phase == Phase.Foresight && _match.Current == _human) BuildForesight();
            else _overlay.gameObject.SetActive(false);
        }

        RectTransform OverlayPanel(float w, float h)
        {
            return Ui.Panel(_overlayContent, "Dialog", (StageWidth - w) * 0.5f, (StageHeight - h) * 0.5f, w, h).rectTransform;
        }

        void BuildIntro()
        {
            var panel = OverlayPanel(1040f, 580f);
            var header = Ui.Label(panel, Loc.Get("ui.intro.header"), 0, 56, 1040, 40, TextAlignmentOptions.Top);
            header.fontSize = 28f;
            Ui.Text(panel, Ui.Initial(Loc.Army(_human), 130), Ui.TitleFont, 76f, Ui.InkText, 0, 108, 1040, 100,
                TextAlignmentOptions.Top);
            string order = Loc.Get(_match.Current == _human ? "ui.intro.youFirst" : "ui.intro.aiFirst");
            Ui.Text(panel, Loc.Format("ui.intro.text", Loc.Army(_ai.Side)) + "\n" + order, Ui.ItalicFont, 36f,
                Ui.InkBody, 80, 224, 880, 140, TextAlignmentOptions.Top);
            Ui.Button(panel, Loc.Get("ui.intro.button"), true, 300, 412, 440, 92, OnStartMatch, 34f);
        }

        void BuildReaction()
        {
            var pc = _match.Pending;
            var defender = _match.Get(pc.Defender.Side);
            var options = _match.ReactionOptions();
            float scale = 0.7f;
            float cw = Ui.CardWidth * scale, ch = Ui.CardHeight * scale;
            float width = Mathf.Max(620f, options.Count * (cw + 16f) + 64f);

            var panel = OverlayPanel(width, 470f);
            Ui.Label(panel, Loc.Get("ui.reaction.header"), 0, 24, width, 20, TextAlignmentOptions.Top);
            Ui.Text(panel, Ui.Initial(Loc.Format("ui.reaction.title", Loc.Army(pc.Defender.Side)), 130), Ui.TitleFont,
                32f, Ui.InkText, 0, 48, width, 42, TextAlignmentOptions.Top);
            Ui.Text(panel, Loc.Format(pc.Ranged ? "ui.reaction.volley" : "ui.reaction.capture",
                    Loc.UnitAt(pc.Attacker), Loc.UnitAt(pc.Defender), defender.Morale),
                Ui.ItalicFont, 18f, Ui.InkBody, 24, 94, width - 48, 26, TextAlignmentOptions.Top);

            float x = (width - (options.Count * cw + (options.Count - 1) * 16f)) * 0.5f;
            foreach (var id in options)
            {
                var card = Ui.Card(panel, CardCatalog.Get(id), scale);
                card.anchoredPosition = new Vector2(x, -130f);
                var button = card.gameObject.AddComponent<Button>();
                button.transition = Selectable.Transition.None;
                var chosen = id;
                button.onClick.AddListener(() => OnReaction(chosen));
                AddHover(card, false);
                x += cw + 16f;
            }
            Ui.Button(panel, Loc.Get("ui.reaction.pass"), false, (width - 200f) * 0.5f, 130f + ch + 18f, 200, 44,
                () => OnReaction(null));
        }

        void BuildForesight()
        {
            var options = _match.ForesightOptions;
            float scale = 0.75f;
            float cw = Ui.CardWidth * scale;
            float width = Mathf.Max(560f, options.Count * (cw + 20f) + 64f);
            var panel = OverlayPanel(width, 400f);
            Ui.Label(panel, Loc.CardName(CardId.SeersOmen), 0, 24, width, 20, TextAlignmentOptions.Top);
            Ui.Text(panel, Ui.Initial(Loc.Get("ui.foresight.title"), 130), Ui.TitleFont, 30f, Ui.InkText, 0, 48, width, 40,
                TextAlignmentOptions.Top);

            float x = (width - (options.Count * cw + (options.Count - 1) * 20f)) * 0.5f;
            for (int i = 0; i < options.Count; i++)
            {
                var card = Ui.Card(panel, CardCatalog.Get(options[i]), scale);
                card.anchoredPosition = new Vector2(x, -104f);
                var button = card.gameObject.AddComponent<Button>();
                button.transition = Selectable.Transition.None;
                int index = i;
                button.onClick.AddListener(() => OnForesight(index));
                AddHover(card, false);
                x += cw + 20f;
            }
        }

        void BuildGameOver()
        {
            var winner = _match.Winner ?? Side.White;
            var panel = OverlayPanel(560f, 280f);
            Ui.Label(panel, Loc.Get("ui.gameOver.header"), 0, 28, 560, 20, TextAlignmentOptions.Top);
            Ui.Text(panel, Ui.Initial(Loc.Format("ui.gameOver.title", Loc.Army(winner)), 130), Ui.TitleFont, 36f,
                Ui.InkText, 0, 54, 560, 48, TextAlignmentOptions.Top);
            Ui.Text(panel, Loc.Format("ui.gameOver.text", Loc.Army(winner.Other())) + "\n"
                    + Loc.Get(winner == _human ? "ui.gameOver.won" : "ui.gameOver.lost"), Ui.ItalicFont, 19f,
                Ui.InkBody, 40, 112, 480, 60, TextAlignmentOptions.Top);
            Ui.Button(panel, Loc.Get("ui.gameOver.button"), true, 170, 190, 220, 46, NewMatch);
        }

        // ================================================================== animations (swap for Feel later)

        void PlayMove(Piece piece, Coord to)
        {
            piece.Rt.SetAsLastSibling();
            Run(piece, MoveRoutine(piece, SquareCenter(to)));
        }

        void PlayCapture(Piece piece)
        {
            if (piece.Anim != null) StopCoroutine(piece.Anim);
            StartCoroutine(CaptureRoutine(piece));
        }

        void PlayBlocked(Piece attacker, Coord from, Coord target) =>
            Run(attacker, LungeRoutine(attacker, SquareCenter(from), SquareCenter(target)));

        void PlayRecruit(Piece piece) => StartCoroutine(PopRoutine(piece.Rt));

        void PlayWaitingFade(float alpha)
        {
            if (_waitingFade != null) StopCoroutine(_waitingFade);
            _armies[(int)_human.Other()].Group.alpha = 1f;
            _waitingFade = StartCoroutine(FadeRoutine(alpha, _handGroup, _rightColumn, _armies[(int)_human].Group));
        }

        void Run(Piece piece, IEnumerator routine)
        {
            if (piece.Anim != null) StopCoroutine(piece.Anim);
            piece.Anim = StartCoroutine(routine);
        }

        static float EaseOut(float t) => 1f - (1f - t) * (1f - t) * (1f - t);

        IEnumerator MoveRoutine(Piece piece, Vector2 to)
        {
            var from = piece.Rt.anchoredPosition;
            for (float t = 0f; t < 1f; t += Time.deltaTime / _moveDuration)
            {
                piece.Rt.anchoredPosition = Vector2.LerpUnclamped(from, to, EaseOut(t));
                yield return null;
            }
            piece.Rt.anchoredPosition = to;
            piece.Anim = null;
        }

        IEnumerator LungeRoutine(Piece piece, Vector2 home, Vector2 target)
        {
            var peak = Vector2.Lerp(home, target, 0.35f);
            const float duration = 0.34f;
            for (float t = 0f; t < 1f; t += Time.deltaTime / duration)
            {
                float k = t < 0.4f ? EaseOut(t / 0.4f) : 1f - EaseOut((t - 0.4f) / 0.6f);
                piece.Rt.anchoredPosition = Vector2.LerpUnclamped(home, peak, k);
                yield return null;
            }
            piece.Rt.anchoredPosition = home;
            piece.Anim = null;
        }

        IEnumerator CaptureRoutine(Piece piece)
        {
            const float duration = 0.3f;
            for (float t = 0f; t < 1f; t += Time.deltaTime / duration)
            {
                piece.Group.alpha = 1f - t;
                piece.Rt.localScale = Vector3.one * (1f + 0.25f * EaseOut(t));
                yield return null;
            }
            Destroy(piece.Rt.gameObject);
        }

        IEnumerator FadeRoutine(float to, params CanvasGroup[] groups)
        {
            const float duration = 0.25f;
            bool done = false;
            while (!done)
            {
                done = true;
                foreach (var g in groups)
                {
                    g.alpha = Mathf.MoveTowards(g.alpha, to, Time.deltaTime / duration);
                    done &= Mathf.Approximately(g.alpha, to);
                }
                yield return null;
            }
            _waitingFade = null;
        }

        static IEnumerator PopRoutine(RectTransform rt)
        {
            const float duration = 0.26f;
            for (float t = 0f; t < 1f; t += Time.deltaTime / duration)
            {
                float s = t < 0.7f ? Mathf.Lerp(0f, 1.12f, EaseOut(t / 0.7f)) : Mathf.Lerp(1.12f, 1f, (t - 0.7f) / 0.3f);
                rt.localScale = new Vector3(s, s, 1f);
                yield return null;
            }
            rt.localScale = Vector3.one;
        }

        // ================================================================== screen construction

        /// <summary>Top-left corner of a square on the board, seen from the player's side
        /// (their army always at the bottom; as Black the board is rotated 180°).</summary>
        Vector2 SquareOrigin(Coord c)
        {
            int column = _flipped ? Match.Size - 1 - c.X : c.X;
            int row = _flipped ? c.Y : Match.Size - 1 - c.Y;
            return new Vector2(Inset + Square * column, -(Inset + Square * row));
        }

        Vector2 SquareCenter(Coord c) => SquareOrigin(c) + new Vector2(Square * 0.5f, -Square * 0.5f);

        /// <summary>Orients the board for the player's side and rebuilds the army panels for their roles
        /// (opponent on top, player at the bottom). Called at every new match.</summary>
        void LayoutForPlayer()
        {
            _flipped = _human == Side.Black;
            for (int x = 0; x < Match.Size; x++)
                for (int y = 0; y < Match.Size; y++)
                {
                    var origin = SquareOrigin(new Coord(x, y));
                    _under[x, y].rectTransform.anchoredPosition = origin;
                    ((RectTransform)_hint[x, y].transform.parent).anchoredPosition = origin;
                }
            for (int i = 0; i < Match.Size; i++)
            {
                _fileLabels[i].text = ((char)(_flipped ? 'h' - i : 'a' + i)).ToString();
                _rankLabels[i].text = (_flipped ? i + 1 : Match.Size - i).ToString();
            }
            foreach (var army in _armies)
                if (army != null) Destroy(army.Root.gameObject);
            _armies[(int)_human] = BuildArmy(_human, human: true);
            _armies[(int)_human.Other()] = BuildArmy(_human.Other(), human: false);
        }

        Piece CreatePiece(Unit unit)
        {
            var rt = Ui.Rect(_piecesLayer, $"{unit.Side} {unit.Type} #{unit.Id}", 0, 0, PieceSize, PieceSize);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = SquareCenter(unit.Pos);
            var piece = new Piece
            {
                Id = unit.Id,
                Rt = rt,
                Group = rt.gameObject.AddComponent<CanvasGroup>(),
                Body = Ui.Image(rt, "Token", Ui.UnitArt(unit.Type, unit.Side), 0, 0, PieceSize, PieceSize),
                Shield = Ui.Image(rt, "Shield", Ui.Art("Badge_Shield"), -4, 44, 28, 28),
                Hammer = Ui.Image(rt, "Hammer", Ui.Art("Badge_Hammer"), 46, 44, 28, 28),
            };
            piece.Group.blocksRaycasts = false;
            _pieces[unit.Id] = piece;
            return piece;
        }

        void BuildUi()
        {
            if (FindAnyObjectByType<EventSystem>() == null)
                new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            if (Camera.main != null) Camera.main.backgroundColor = Ui.Table;

            var canvasGo = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);
            canvasGo.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            _scaler = canvasGo.GetComponent<CanvasScaler>();
            _scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            _scaler.referenceResolution = new Vector2(StageWidth, StageHeight);
            _scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            var root = (RectTransform)canvasGo.transform;

            Ui.Stretch(Ui.Image(root, "Background", Ui.Art("Background"), 0, 0, 0, 0).rectTransform);

            _stage = Ui.Rect(root, "Stage", 0, 0, StageWidth, StageHeight);
            _stage.anchorMin = _stage.anchorMax = _stage.pivot = new Vector2(0.5f, 0.5f);
            _stage.anchoredPosition = Vector2.zero;

            BuildHeader();
            BuildConditions();
            BuildBoard();
            _hand = Ui.Rect(_stage, "Hand", CenterX, HandY, CenterWidth, StageHeight - HandY);
            _handGroup = _hand.gameObject.AddComponent<CanvasGroup>();
            BuildRightColumn();

            _overlay = Ui.Stretch(Ui.Rect(root, "Overlay", 0, 0, 0, 0));
            var dim = _overlay.gameObject.AddComponent<Image>();
            dim.color = new Color(0.17f, 0.11f, 0.06f, 0.45f);
            _overlayContent = Ui.Rect(_overlay, "Content", 0, 0, StageWidth, StageHeight);
            _overlayContent.anchorMin = _overlayContent.anchorMax = _overlayContent.pivot = new Vector2(0.5f, 0.5f);
            _overlayContent.anchoredPosition = Vector2.zero;
        }

        void BuildHeader()
        {
            var header = Ui.Rect(_stage, "Header", 0, 0, StageWidth, HeaderHeight);
            Ui.Line(header, 0, HeaderHeight - 1.5f, StageWidth, 1.5f);
            Ui.Line(header, 0, HeaderHeight + 2f, StageWidth, 1f);
            Ui.Text(header, Ui.Initial(Loc.Get("ui.title"), 125), Ui.TitleFont, 26f, Ui.InkText, LeftX, 0, LeftWidth,
                HeaderHeight, TextAlignmentOptions.Left);
            _phases = Ui.Text(header, "", Ui.TitleFont, 17f, Ui.InkSoft, CenterX, 0, CenterWidth, HeaderHeight,
                TextAlignmentOptions.Center);
            _phases.characterSpacing = 4f;
            const float buttonWidth = 180f;
            _turn = Ui.Text(header, "", Ui.ItalicFont, 18f, Ui.InkBody, RightX, 0, RightWidth - buttonWidth - 18f,
                HeaderHeight, TextAlignmentOptions.Right);
            _endTurn = Ui.Button(header, Loc.Get("ui.endTurn"), false, RightX + RightWidth - buttonWidth, 8, buttonWidth,
                44, OnEndTurn, 18f);
        }

        /// <summary>Left column panel: the player's one (bottom) has Orders and recruit tiles; the opponent's (top)
        /// a hand/deck/discard summary and a read-only reserve.</summary>
        ArmyPanel BuildArmy(Side side, bool human)
        {
            float height = human ? PlayerPanelHeight : OpponentPanelHeight;
            float y = human ? ColumnBottom - height : ColumnTop;
            var panel = Ui.Panel(_stage, $"Army {side}", LeftX, y, LeftWidth, height).rectTransform;
            var ui = new ArmyPanel { Root = panel, Group = panel.gameObject.AddComponent<CanvasGroup>() };

            Ui.Image(panel, "Emblem", Ui.UnitArt(UnitType.Sovereign, side), 20, 18, 46, 46);
            Ui.Text(panel, Loc.Army(side), Ui.TitleFont, 22f, Ui.InkText, 78, 19, 300, 28);
            ui.Subtitle = Ui.Label(panel, "", 78, 47, 300);

            Ui.Label(panel, Loc.Get("ui.morale"), 20, 81, 100);
            ui.Pips = new Image[Match.MaxMorale];
            for (int i = 0; i < ui.Pips.Length; i++)
                ui.Pips[i] = Ui.Image(panel, "Pip", Ui.Art("Pip_Off"), 118 + i * 21, 81, 16, 16);
            ui.Morale = Ui.Text(panel, "", Ui.BodyFont, 15f, Ui.InkBody, 300, 78, 80, 22, TextAlignmentOptions.TopRight);

            float tilesY;
            if (human)
            {
                ui.Orders = Ui.Label(panel, "", 20, 115, 220);
                ui.OrderIcons = new Image[Match.OrdersPerTurn + 2];
                for (int i = 0; i < ui.OrderIcons.Length; i++)
                    ui.OrderIcons[i] = Ui.Image(panel, "Order", Ui.Art("Order_Off"), 0, 104, 40, 36);
                Ui.Label(panel, Loc.Format("ui.reserveHeader", Loc.Get("ui.ranks." + side)), 20, 160, 220);
                ui.ReserveHint = Ui.Text(panel, "", Ui.BodyFont, 15f, Ui.InkBody, 200, 157, 180, 22,
                    TextAlignmentOptions.TopRight);
                tilesY = 186f;
            }
            else
            {
                ui.Summary = Ui.Text(panel, "", Ui.BodyFont, 15f, Ui.InkBody, 20, 110, 360, 22);
                tilesY = 146f;
            }
            BuildReserve(panel, ui, side, human, tilesY);
            return ui;
        }

        /// <summary>One tile per recruitable unit: token, name, count and (for the player) the Morale seal.
        /// The player's tiles are the recruit buttons.</summary>
        void BuildReserve(RectTransform panel, ArmyPanel ui, Side side, bool human, float y)
        {
            int n = Match.Recruitable.Length;
            const float gap = 8f, height = 76f;
            float w = (LeftWidth - 40f - gap * (n - 1)) / n;
            ui.ReserveCounts = new TextMeshProUGUI[n];
            if (human)
            {
                ui.RecruitButtons = new Button[n];
                ui.RecruitGroups = new CanvasGroup[n];
                ui.RecruitHighlights = new Image[n];
            }

            for (int i = 0; i < n; i++)
            {
                var type = Match.Recruitable[i];
                var tile = Ui.Image(panel, "Reserve " + type, Ui.Tile, 20 + i * (w + gap), y, w, height, raycast: human);
                tile.type = Image.Type.Sliced;
                Ui.Image(tile.transform, "Icon", Ui.UnitArt(type, side), (w - 34f) * 0.5f, 5, 34, 34);
                Ui.Text(tile.transform, Loc.UnitName(type), Ui.BodyFont, 12.5f, Ui.InkText, 0, 41, w, 16,
                    TextAlignmentOptions.Top).textWrappingMode = TextWrappingModes.NoWrap;

                if (!human)
                {
                    ui.ReserveCounts[i] = Ui.Text(tile.transform, "", Ui.BodyFont, 14f, Ui.InkText, 0, 56, w, 18,
                        TextAlignmentOptions.Top);
                    continue;
                }
                ui.ReserveCounts[i] = Ui.Text(tile.transform, "", Ui.BodyFont, 14f, Ui.InkText, 0, 56, w * 0.5f + 1f, 18,
                    TextAlignmentOptions.TopRight);
                Ui.Seal(tile.transform, Match.Cost(type), w * 0.5f + 4f, 54, 18);
                ui.RecruitGroups[i] = tile.gameObject.AddComponent<CanvasGroup>();
                ui.RecruitButtons[i] = Ui.MakeButton(tile, () => OnRecruitClicked(side, type));
                var highlight = Ui.Image(tile.transform, "Chosen", Ui.Highlight, -2, -2, w + 4, height + 4);
                highlight.type = Image.Type.Sliced;
                highlight.enabled = false;
                ui.RecruitHighlights[i] = highlight;
            }
        }

        /// <summary>Field conditions (weather cards) are not in the rules yet: the panel holds their place in the
        /// layout and shows the empty state.</summary>
        void BuildConditions()
        {
            float y = ColumnTop + OpponentPanelHeight + Gap;
            float height = ColumnBottom - PlayerPanelHeight - Gap - y;
            var panel = Ui.Panel(_stage, "Conditions Panel", LeftX, y, LeftWidth, height).rectTransform;
            Ui.Label(panel, Loc.Get("ui.conditions.header"), 20, 18, 220).color = Ui.Rubric;
            Ui.Label(panel, Loc.Get("ui.conditions.none"), 180, 18, 200, align: TextAlignmentOptions.TopRight);
            Ui.Text(panel, Loc.Get("ui.conditions.empty"), Ui.ItalicFont, 17f, Ui.InkBody, 40, 48, LeftWidth - 80f,
                height - 66f, TextAlignmentOptions.Center);
        }

        void BuildBoard()
        {
            var board = Ui.Image(_stage, "Board", Ui.Art("Board"), BoardX, ColumnTop, BoardSize, BoardSize).rectTransform;
            board.localScale = new Vector3(BoardScale, BoardScale, 1f);

            // Squares and coordinate labels get their final position/text in LayoutForPlayer.
            for (int i = 0; i < Match.Size; i++)
            {
                float offset = Inset + Square * i;
                _fileLabels[i] = Ui.Text(board, "", Ui.TitleFont, 15f, Ui.InkSoft, offset, 674, Square, 24,
                    TextAlignmentOptions.Center);
                _rankLabels[i] = Ui.Text(board, "", Ui.TitleFont, 15f, Ui.InkSoft, 0, offset, 24, Square,
                    TextAlignmentOptions.Center);
            }

            var under = Ui.Rect(board, "Underlay", 0, 0, BoardSize, BoardSize);
            _palisadeLayer = Ui.Rect(board, "Palisades", 0, 0, BoardSize, BoardSize);
            _piecesLayer = Ui.Rect(board, "Pieces", 0, 0, BoardSize, BoardSize);
            var over = Ui.Rect(board, "Squares", 0, 0, BoardSize, BoardSize);

            for (int x = 0; x < Match.Size; x++)
                for (int y = 0; y < Match.Size; y++)
                {
                    var c = new Coord(x, y);
                    _under[x, y] = Ui.Image(under, "Under " + c, null, 0, 0, Square, Square);

                    var hit = Ui.Image(over, c.ToString(), Ui.Solid, 0, 0, Square, Square, raycast: true);
                    hit.color = Color.clear;
                    var button = hit.gameObject.AddComponent<Button>();
                    button.transition = Selectable.Transition.None;
                    button.onClick.AddListener(() => OnSquareClicked(c));
                    _hint[x, y] = Ui.Image(hit.transform, "Hint", null, 0, 0, Square, Square);
                }
        }

        void BuildRightColumn()
        {
            const float w = RightWidth;
            var column = Ui.Rect(_stage, "Right Column", RightX, 0f, w, StageHeight);
            _rightColumn = column.gameObject.AddComponent<CanvasGroup>();

            // Action panel: singled out by a faint rubric halo and a rubric title band.
            var halo = Ui.Image(column, "Halo", Ui.Solid, -4f, ColumnTop - 4f, w + 8f, ActionPanelHeight + 8f);
            halo.color = new Color(Ui.Rubric.r, Ui.Rubric.g, Ui.Rubric.b, 0.22f);
            var action = Ui.Panel(column, "Action Panel", 0f, ColumnTop, w, ActionPanelHeight).rectTransform;
            var band = Ui.Image(action, "Band", Ui.Solid, 2f, 2f, w - 4f, 40f);
            band.color = Ui.Rubric;
            var bandLabel = Ui.Label(band.transform, Loc.Get("ui.action.header"), 18f, 0f, 200f, 40f,
                TextAlignmentOptions.MidlineLeft);
            bandLabel.fontSize = 15f;
            bandLabel.characterSpacing = 18f;
            bandLabel.color = Ui.Cream;
            _actionBand = Ui.Text(band.transform, "", Ui.ItalicFont, 16f, Ui.Cream, 200f, 0f, w - 222f, 40f,
                TextAlignmentOptions.MidlineRight);
            _actionSlot = Ui.Rect(action, "Subject", 20f, 56f, Ui.CardWidth, Ui.CardHeight);
            _actionDetails = Ui.Rect(action, "Details", 288f, 60f, w - 308f, Ui.CardHeight);
            _actionPrimary = Ui.Button(action, Loc.Get("ui.action.playCard"), true, 20f, 420f, 320f, 48f,
                OnActionPrimary, 18f);
            _actionCancel = Ui.Button(action, Loc.Get("ui.action.cancel"), false, 350f, 420f, 130f, 48f,
                () => { ClearMode(); Refresh(); }, 18f);

            // Deck (stacked backs) and discard (last card on loose sheets), side by side.
            float deckY = ColumnTop + ActionPanelHeight + Gap;
            var deck = Ui.Panel(column, "Deck Panel", 0f, deckY, w, DeckPanelHeight).rectTransform;
            for (int i = 2; i >= 0; i--) Ui.Image(deck, "Back", Ui.Art("CardBack"), 20f + i * 6f, 18f + i * 6f, 89f, 125f);
            Ui.Label(deck, Loc.Get("ui.deck"), 137f, 40f, 105f);
            _deck = Ui.Text(deck, "", Ui.TitleFont, 22f, Ui.InkText, 137f, 60f, 105f, 28f);
            Ui.Text(deck, Loc.Get("ui.deckDraw"), Ui.BodyFont, 14f, Ui.InkBody, 137f, 92f, 105f, 40f);
            _discardPile = Ui.Rect(deck, "Discard Pile", 258f, 16f, 112f, 140f);
            Ui.Label(deck, Loc.Get("ui.discard"), 386f, 40f, 94f);
            _discard = Ui.Text(deck, "", Ui.TitleFont, 22f, Ui.InkText, 386f, 60f, 94f, 28f);
            _discardLast = Ui.Text(deck, "", Ui.BodyFont, 14f, Ui.InkBody, 386f, 92f, 94f, 60f);

            float historyY = deckY + DeckPanelHeight + Gap;
            var history = Ui.Panel(column, "History Panel", 0f, historyY, w, ColumnBottom - historyY).rectTransform;
            Ui.Label(history, Loc.Get("ui.history"), 20, 18, 200);
            Ui.Label(history, Loc.Get("ui.historyOrder"), 200, 18, 280, align: TextAlignmentOptions.TopRight);
            _logEntries = Ui.Rect(history, "Entries", 20f, 42f, w - 40f, ColumnBottom - historyY - 58f);
        }
    }
}
