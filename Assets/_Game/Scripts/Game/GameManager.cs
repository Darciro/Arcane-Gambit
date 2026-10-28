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
        const float StageWidth = 1440f;
        const float StageHeight = 1080f;
        const float BoardX = 370f, BoardY = 84f, BoardSize = 700f;
        const float Inset = 30f, Square = 80f, PieceSize = 70f;
        const float HandScale = 0.6f;
        const float ArmyX = 42f, TopArmyY = 84f, BottomArmyY = 544f;
        const float WaitingAlpha = 0.5f;

        [SerializeField, Tooltip("0 = random seed for every match")] int _seed;
        [SerializeField, Min(0.01f)] float _moveDuration = 0.22f;
        [SerializeField, Min(0f), Tooltip("Pause before each AI decision, so its moves can be followed")]
        float _aiDelay = 0.7f;

        enum Mode { Idle, Unit, Recruit, Card }

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

        sealed class ArmyPanel
        {
            public RectTransform Root;
            public CanvasGroup Group;
            public TextMeshProUGUI Subtitle, Morale, Orders, Hand;
            public Image[] Pips, OrderIcons;
            public TextMeshProUGUI[] ReserveLabels;
            public Button[] RecruitButtons;
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
        TextMeshProUGUI _phases, _turn, _actionTitle, _actionHint, _log, _deck, _discard;
        readonly TextMeshProUGUI[] _rowLabels = new TextMeshProUGUI[4];
        readonly TextMeshProUGUI[] _rowValues = new TextMeshProUGUI[4];
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
            for (int i = 0; i < ui.Pips.Length; i++)
                ui.Pips[i].sprite = Ui.Art(i < player.Morale ? "Pip_On" : "Pip_Off");

            int orders = active ? player.Orders : Match.OrdersPerTurn;
            int bonus = active ? player.BonusMoves : 0;
            ui.Orders.text = active
                ? Loc.Format("ui.orders", orders, Match.OrdersPerTurn) + (bonus > 0 ? Loc.Format("ui.ordersBonus", bonus) : "")
                : Loc.Get("ui.ordersIdle");
            for (int i = 0; i < ui.OrderIcons.Length; i++)
            {
                ui.OrderIcons[i].enabled = i < Match.OrdersPerTurn + bonus;
                ui.OrderIcons[i].sprite = Ui.Art(active && i < orders + bonus ? "Order_On" : "Order_Off");
            }
            ui.Hand.text = Loc.Format("ui.handSummary", player.Hand.Count, player.Deck.Count);

            for (int i = 0; i < Match.Recruitable.Length; i++)
            {
                var type = Match.Recruitable[i];
                ui.ReserveLabels[i].text = Loc.Format("ui.reserveCount", Loc.UnitName(type), player.Reserve[type]);
                bool can = human && HumanTurn && _match.CanRecruit(type);
                bool chosen = active && _mode == Mode.Recruit && _recruitType == type;
                Ui.SetButton(ui.RecruitButtons[i], Loc.Get(chosen ? "ui.choose" : "ui.recruit"), can);
            }
        }

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
            float step = hand.Count > 1 ? Mathf.Min(w + 12f, (area - w) / (hand.Count - 1)) : 0f;
            float start = (area - (w + step * (hand.Count - 1))) * 0.5f;
            RectTransform selectedCard = null;

            for (int i = 0; i < hand.Count; i++)
            {
                var info = CardCatalog.Get(hand[i]);
                bool selected = _mode == Mode.Card && _cardIndex == i;
                float baseY = selected ? 4f : 22f;

                var card = Ui.Card(_hand, info, HandScale);
                card.anchoredPosition = new Vector2(start + step * i, -baseY);
                if (selected)
                {
                    var outline = Ui.Image(card, "Outline", Ui.Solid, -5f, -5f, w + 10f, h + 10f);
                    outline.color = Ui.Rubric;
                    outline.transform.SetAsFirstSibling();
                }

                var group = card.gameObject.AddComponent<CanvasGroup>();
                group.alpha = !HumanTurn || info.IsReaction || _match.CanPlay(info.Id) ? 1f : 0.72f;
                if (selected) selectedCard = card;

                int index = i;
                var button = card.gameObject.AddComponent<Button>();
                button.transition = Selectable.Transition.None;
                button.onClick.AddListener(() => OnCardClicked(index));
                AddHover(card, baseY, selected);
            }
            // Drawn last so no neighbour overlaps it.
            if (selectedCard != null) selectedCard.SetAsLastSibling();
        }

        static void AddHover(RectTransform card, float baseY, bool selected)
        {
            if (selected) return;
            var trigger = card.gameObject.AddComponent<EventTrigger>();
            var enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
            enter.callback.AddListener(_ => card.anchoredPosition = new Vector2(card.anchoredPosition.x, -(baseY - 16f)));
            var exit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
            exit.callback.AddListener(_ => card.anchoredPosition = new Vector2(card.anchoredPosition.x, -baseY));
            trigger.triggers.Add(enter);
            trigger.triggers.Add(exit);
        }

        void RefreshAction()
        {
            var p = _match.Get(_human);
            string title, hint;
            var rows = new List<(string label, string value)>();
            bool showPrimary = false, primaryEnabled = false, showCancel = _mode != Mode.Idle;

            bool live = !_awaitingStart && _match.Phase != Phase.GameOver;
            bool aiReacting = live && _match.Phase == Phase.Reaction && _match.Pending.Defender.Side == _ai.Side;
            if (aiReacting || (live && _match.Current == _ai.Side))
            {
                var ai = _match.Get(_ai.Side);
                title = Loc.Get(aiReacting ? "ui.reaction.header" : "ui.action.aiTitle");
                rows.Add((Loc.Get("ui.morale"), Loc.Format("ui.moraleValue", ai.Morale, Match.MaxMorale)));
                rows.Add((Loc.Get("ui.action.orders"), ai.Orders.ToString()));
                hint = Loc.Format(aiReacting ? "ui.action.aiReacting" : "ui.action.aiHint", Loc.Army(_ai.Side));
            }
            else switch (_mode)
            {
                case Mode.Unit:
                {
                    var u = _selected;
                    title = Loc.UnitName(u.Type);
                    rows.Add((Loc.Get("ui.action.position"), u.Pos.ToString()));
                    rows.Add((Loc.Get("ui.action.moves"), _match.MoveTargets(u).Count.ToString()));
                    rows.Add((Loc.Get("ui.action.captures"), _match.CaptureTargets(u).Count.ToString()));
                    var gear = new List<string>();
                    if (u.Protection > 0) gear.Add(Loc.Get("ui.action.protection"));
                    if (u.HasHammer) gear.Add(Loc.Get("ui.action.hammer"));
                    rows.Add((Loc.Get("ui.action.equipment"), gear.Count > 0 ? string.Join(" · ", gear) : Loc.Get("ui.action.none")));
                    hint = Loc.Get(u.RecruitedTurn == _match.Turn ? "ui.action.hintResting"
                        : p.Orders <= 0 && p.BonusMoves <= 0 ? "ui.action.hintNoOrders"
                        : "ui.action.hintUnit");
                    break;
                }
                case Mode.Recruit:
                    title = Loc.Format("ui.action.recruitTitle", Loc.UnitName(_recruitType));
                    rows.Add((Loc.Get("ui.action.cost"), Loc.Format("ui.action.recruitCost", Match.Cost(_recruitType))));
                    rows.Add((Loc.Get("ui.action.inReserve"), Loc.Format("ui.action.reserveValue", p.Reserve[_recruitType])));
                    hint = Loc.Format("ui.action.hintRecruit", Loc.Get("ui.ranks." + _match.Current));
                    break;
                case Mode.Card:
                {
                    var info = CardCatalog.Get(_card);
                    title = Loc.CardName(_card);
                    rows.Add((Loc.Get("ui.action.type"), Loc.KindName(info.Kind)));
                    rows.Add((Loc.Get("ui.action.cost"), Loc.Format("ui.action.cardCost", info.Cost)
                        + (_card == CardId.CrossbowVolley ? Loc.Get("ui.action.plusOrder") : "")));
                    hint = CardHint(info);
                    showPrimary = !info.IsReaction && Match.PickCount(_card) == 0;
                    primaryEnabled = _match.CanPlay(_card);
                    break;
                }
                default:
                    title = Loc.Get("ui.action.idleTitle");
                    rows.Add((Loc.Get("ui.morale"), Loc.Format("ui.moraleValue", p.Morale, Match.MaxMorale)));
                    rows.Add((Loc.Get("ui.action.orders"), p.BonusMoves > 0
                        ? Loc.Format("ui.action.ordersBonus", p.Orders, p.BonusMoves) : p.Orders.ToString()));
                    rows.Add((Loc.Get("ui.action.proactive"),
                        Loc.Get(p.PlayedProactive ? "ui.action.proactiveUsed" : "ui.action.proactiveAvailable")));
                    hint = Loc.Get("ui.action.idleHint");
                    break;
            }

            _actionTitle.text = Ui.Initial(title);
            for (int i = 0; i < _rowLabels.Length; i++)
            {
                bool has = i < rows.Count;
                _rowLabels[i].text = has ? rows[i].label : "";
                _rowValues[i].text = has ? rows[i].value : "";
            }
            _actionHint.text = hint;
            _actionPrimary.gameObject.SetActive(showPrimary);
            Ui.SetButton(_actionPrimary, Loc.Get("ui.action.playCard"), primaryEnabled);
            _actionCancel.gameObject.SetActive(showCancel);
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

        void RefreshLog()
        {
            var sb = new System.Text.StringBuilder();
            var log = _match.Log;
            string rubric = Ui.ToHex(Ui.Rubric);
            for (int i = log.Count - 1, n = 0; i >= 0 && n < 14; i--, n++)
                sb.Append($"<color={rubric}>{Ui.Caps(Ui.Roman(log[i].Turn))}</color><indent=13%>{Loc.LogLine(log[i])}</indent>\n");
            _log.text = sb.ToString();

            var p = _match.Get(_human);
            _deck.text = Loc.Format("ui.cardCount", p.Deck.Count);
            _discard.text = Loc.Format("ui.cardCount", p.Discard.Count);
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

        RectTransform OverlayPanel(float w, float h, float y)
        {
            return Ui.Panel(_overlayContent, "Dialog", (StageWidth - w) * 0.5f, y, w, h).rectTransform;
        }

        void BuildIntro()
        {
            var panel = OverlayPanel(1040f, 580f, 250f);
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

            var panel = OverlayPanel(width, 470f, 220f);
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
                AddHover(card, 130f, false);
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
            var panel = OverlayPanel(width, 400f, 250f);
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
                AddHover(card, 104f, false);
                x += cw + 20f;
            }
        }

        void BuildGameOver()
        {
            var winner = _match.Winner ?? Side.White;
            var panel = OverlayPanel(560f, 280f, 330f);
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

        /// <summary>Orients board and army panels for the player's side (called at every new match).</summary>
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
            _armies[(int)_human].Root.anchoredPosition = new Vector2(ArmyX, -BottomArmyY);
            _armies[(int)_human.Other()].Root.anchoredPosition = new Vector2(ArmyX, -TopArmyY);
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
            _armies[(int)Side.Black] = BuildArmy(Side.Black);
            _armies[(int)Side.White] = BuildArmy(Side.White);
            BuildBoard();
            _hand = Ui.Rect(_stage, "Hand", 342f, 790f, 756f, 290f);
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
            var header = Ui.Rect(_stage, "Header", 0, 0, StageWidth, 64f);
            Ui.Line(header, 0, 62, StageWidth, 1.5f);
            Ui.Line(header, 0, 66, StageWidth, 1f);
            Ui.Text(header, Ui.Initial(Loc.Get("ui.title"), 125), Ui.TitleFont, 26f, Ui.InkText, 42, 0, 260, 64,
                TextAlignmentOptions.Left);
            _phases = Ui.Text(header, "", Ui.TitleFont, 17f, Ui.InkSoft, 300, 0, 520, 64, TextAlignmentOptions.Center);
            _phases.characterSpacing = 4f;
            _turn = Ui.Text(header, "", Ui.ItalicFont, 18f, Ui.InkBody, 820, 0, 395, 64, TextAlignmentOptions.Right);
            _endTurn = Ui.Button(header, Loc.Get("ui.endTurn"), false, 1228, 10, 170, 44, OnEndTurn);
        }

        /// <summary>Positioned later by <see cref="LayoutForPlayer"/>.</summary>
        ArmyPanel BuildArmy(Side side)
        {
            var panel = Ui.Panel(_stage, $"Army {side}", ArmyX, TopArmyY, 300f, 444f).rectTransform;
            var ui = new ArmyPanel { Root = panel, Group = panel.gameObject.AddComponent<CanvasGroup>() };

            Ui.Image(panel, "Emblem", Ui.UnitArt(UnitType.Sovereign, side), 16, 16, 50, 50);
            Ui.Text(panel, Loc.Army(side), Ui.TitleFont, 22f, Ui.InkText, 74, 18, 210, 28);
            ui.Subtitle = Ui.Label(panel, "", 74, 46, 210);

            Ui.Label(panel, Loc.Get("ui.morale"), 18, 80, 120);
            ui.Morale = Ui.Text(panel, "", Ui.BodyFont, 16f, Ui.InkText, 160, 78, 122, 22, TextAlignmentOptions.TopRight);
            ui.Pips = new Image[Match.MaxMorale];
            for (int i = 0; i < ui.Pips.Length; i++)
                ui.Pips[i] = Ui.Image(panel, "Pip", Ui.Art("Pip_Off"), 18 + i * 25, 104, 20, 20);

            ui.Orders = Ui.Label(panel, "", 18, 144, 170);
            ui.OrderIcons = new Image[Match.OrdersPerTurn + 2];
            for (int i = 0; i < ui.OrderIcons.Length; i++)
                ui.OrderIcons[i] = Ui.Image(panel, "Order", Ui.Art("Order_Off"), 158 + i * 31, 134, 33, 30);

            ui.Hand = Ui.Label(panel, "", 18, 180, 264);
            Ui.Label(panel, Loc.Format("ui.reserveHeader", Loc.Get("ui.ranks." + side)), 18, 210, 264);

            ui.ReserveLabels = new TextMeshProUGUI[Match.Recruitable.Length];
            ui.RecruitButtons = new Button[Match.Recruitable.Length];
            for (int i = 0; i < Match.Recruitable.Length; i++)
            {
                var type = Match.Recruitable[i];
                float rowY = 234 + i * 41;
                Ui.Line(panel, 18, rowY, 264, 1f, 0.35f);
                Ui.Image(panel, "Icon", Ui.UnitArt(type, side), 16, rowY + 5, 32, 32);
                ui.ReserveLabels[i] = Ui.Text(panel, "", Ui.BodyFont, 16f, Ui.InkText, 54, rowY + 10, 120, 24);
                Ui.Seal(panel, Match.Cost(type), 168, rowY + 8, 26);
                ui.RecruitButtons[i] = Ui.Button(panel, Loc.Get("ui.recruit"), false, 202, rowY + 5, 80, 32,
                    () => OnRecruitClicked(side, type), 14f);
            }
            return ui;
        }

        void BuildBoard()
        {
            var board = Ui.Image(_stage, "Board", Ui.Art("Board"), BoardX, BoardY, BoardSize, BoardSize).rectTransform;

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
            const float w = 300f;
            var column = Ui.Rect(_stage, "Right Column", 1098f, 0f, w, StageHeight);
            _rightColumn = column.gameObject.AddComponent<CanvasGroup>();

            var action = Ui.Panel(column, "Action Panel", 0f, 84f, w, 312f).rectTransform;
            Ui.Label(action, Loc.Get("ui.action.header"), 20, 18, 260);
            _actionTitle = Ui.Text(action, "", Ui.TitleFont, 26f, Ui.InkText, 20, 40, 260, 36);
            for (int i = 0; i < _rowLabels.Length; i++)
            {
                _rowLabels[i] = Ui.Text(action, "", Ui.BodyFont, 16f, Ui.InkText, 20, 84 + i * 24, 130, 24);
                _rowValues[i] = Ui.Text(action, "", Ui.TitleFont, 16f, Ui.InkText, 130, 84 + i * 24, 150, 24,
                    TextAlignmentOptions.TopRight);
            }
            _actionHint = Ui.Text(action, "", Ui.ItalicFont, 16f, Ui.InkBody, 20, 186, 260, 66);
            _actionPrimary = Ui.Button(action, Loc.Get("ui.action.playCard"), true, 20, 254, 150, 42, OnActionPrimary);
            _actionCancel = Ui.Button(action, Loc.Get("ui.action.cancel"), false, 180, 254, 100, 42,
                () => { ClearMode(); Refresh(); });

            var history = Ui.Panel(column, "History Panel", 0f, 412f, w, 456f).rectTransform;
            Ui.Label(history, Loc.Get("ui.history"), 20, 18, 260);
            _log = Ui.Text(history, "", Ui.BodyFont, 15f, Ui.InkText, 20, 44, 260, 396);
            _log.overflowMode = TextOverflowModes.Truncate;
            _log.paragraphSpacing = 6f;

            var deck = Ui.Panel(column, "Deck Panel", 0f, 884f, w, 92f).rectTransform;
            Ui.Image(deck, "Back", Ui.Art("CardBack"), 20, 18, 38, 56);
            Ui.Label(deck, Loc.Get("ui.deck"), 68, 24, 80);
            _deck = Ui.Text(deck, "", Ui.BodyFont, 18f, Ui.InkText, 68, 44, 80, 26);
            var discardBack = Ui.Image(deck, "Back", Ui.Art("CardBack"), 160, 18, 38, 56);
            discardBack.color = new Color(1f, 1f, 1f, 0.55f);
            Ui.Label(deck, Loc.Get("ui.discard"), 208, 24, 80);
            _discard = Ui.Text(deck, "", Ui.BodyFont, 18f, Ui.InkText, 208, 44, 80, 26);
        }
    }
}
