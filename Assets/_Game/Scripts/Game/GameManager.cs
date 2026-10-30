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
    public sealed class GameManager : MonoBehaviour
    {
        const float StageWidth = 1920f;
        const float StageHeight = 1080f;
        static readonly Vector2 Stage = new Vector2(StageWidth, StageHeight);
        const float LeftX = 40f, LeftWidth = 400f;
        const float CenterX = 472f, CenterWidth = 936f;
        const float RightX = 1440f, RightWidth = 440f;
        const float HeaderHeight = 64f, ColumnTop = 76f, ColumnBottom = 1052f, Gap = 14f;
        const float BoardSize = 700f, BoardMargin = 21f, BoardClearance = 150f;
        const float Inset = 30f, Square = 80f, PieceSize = 70f;
        const float HandScale = 0.7f, HandY = 960f, HandOverlap = 12f, HandTilt = 3f, SelectedLift = 16f;
        const float HandLift = 130f, HandZoom = 1.6f, HandEase = 0.16f, HandShadowAlpha = 0.6f;
        const float ActionCardScale = 1.1f, CellsHeight = 88f, ModalCardScale = 0.8f;
        const float OpponentPanelHeight = 290f, PlayerPanelHeight = 340f;
        const float ActionPanelHeight = 664f, DeckPanelHeight = 120f;
        const float WaitingAlpha = 0.5f;
        const float TrailWidth = 2.4f, TrailDash = 7f, TrailGap = 6f, TrailHead = 9f, PreviewAlpha = 0.6f;
        const float TrailEmptyInset = 12f, TrailClearance = 2f;
        const float ToastX = 520f, ToastY = 92f, ToastWidth = 840f, ToastGap = 24f;
        const float ToastLife = 3f, ToastFade = 0.25f;
        const int MaxToasts = 3, DiscardColumns = 5;
        static readonly Color OffTint = new Color(0.9f, 0.92f, 0.96f);
        static readonly Color ScrimColor = new Color(0.125f, 0.082f, 0.039f, 0.62f);

        [SerializeField, Tooltip("0 = random seed for every match")] int _seed;
        [SerializeField, Min(0.01f)] float _moveDuration = 0.22f;
        [SerializeField, Min(0f), Tooltip("Pause before each AI decision, so its moves can be followed")]
        float _aiDelay = 0.7f;
        [SerializeField, Min(0f), Tooltip("How long the AI's next action is shown on the board before it happens")]
        float _aiIntentTime = 1.2f;

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
            public TextMeshProUGUI Subtitle, Morale, Orders, Summary, ReserveHint;
            public Image[] Pips, OrderIcons, RecruitHighlights;
            public TextMeshProUGUI[] ReserveCounts;
            public Button[] RecruitButtons;
            public CanvasGroup[] RecruitGroups;
        }

        sealed class HandCard
        {
            public RectTransform Slot, Card;
            public Image Shadow;
            public float Tilt;
            public bool Selected, Off;
            public float Lift;
            public Graphic[] Graphics;
            public Color[] Colors;
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
        Ai.Choice _aiIntent;
        bool _awaitingStart;
        Coroutine _aiStep;
        bool _waiting;
        Coroutine _waitingFade;
        int _logSeen, _logShown = -1;
        CardId? _reaction;
        int _foresightPick;
        float _foresightScroll;
        bool _discardOpen, _paused, _confirmAbandon, _reviewing;
        Side _discardSide;

        bool HumanTurn => !_awaitingStart && _turnBanner == null && _match.Phase == Phase.Maneuvers && _match.Current == _human;

        RectTransform _stage, _board, _piecesLayer, _markerLayer, _trailLayer, _previewLayer, _hand, _overlay, _overlayContent, _toasts;
        GameObject _backdrop;
        readonly List<RectTransform> _toastList = new List<RectTransform>();
        RectTransform _turnBanner;
        readonly Image[,] _under = new Image[Match.Size, Match.Size];
        readonly Image[,] _hint = new Image[Match.Size, Match.Size];
        readonly TextMeshProUGUI[] _fileLabels = new TextMeshProUGUI[Match.Size];
        readonly TextMeshProUGUI[] _rankLabels = new TextMeshProUGUI[Match.Size];
        bool _flipped;
        readonly Dictionary<int, Piece> _pieces = new Dictionary<int, Piece>();
        readonly Dictionary<(Coord, MarkerKind), RectTransform> _markers = new Dictionary<(Coord, MarkerKind), RectTransform>();
        readonly HashSet<(Coord, Coord)> _trails = new HashSet<(Coord, Coord)>();
        int _trailTurn;
        Coord? _hoverSquare;
        readonly ArmyPanel[] _armies = new ArmyPanel[2];
        CanvasScaler _scaler;
        Vector2 _canvasSize;
        TextMeshProUGUI _phases, _turn, _actionStep, _actionHint, _deck, _deckCaption, _discard, _discardLast;
        RectTransform _actionSlot, _actionCells, _discardPile, _logEntries, _conditions;
        TextMeshProUGUI _conditionsState;
        Button _endTurn, _actionPrimary, _actionCancel;
        CanvasGroup _handGroup, _rightColumn;
        readonly List<HandCard> _handCards = new List<HandCard>();
        int _hoveredCard = -1;

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
            var canvasSize = ((RectTransform)_scaler.transform).rect.size;
            if (canvasSize != _canvasSize)
            {
                _canvasSize = canvasSize;
                FitBoard(canvasSize);
                if (_match != null) RefreshHistory(force: true);
            }
            AnimateHand();

            var mouse = Mouse.current;
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame) OnEscape();
            else if (mouse != null && mouse.rightButton.wasPressedThisFrame && _mode != Mode.Idle)
            {
                ClearMode();
                Refresh();
            }
        }

        void OnDestroy() => Time.timeScale = 1f;

        void OnEscape()
        {
            if (_confirmAbandon) _confirmAbandon = false;
            else if (_paused) SetPaused(false);
            else if (_discardOpen) _discardOpen = false;
            else if (_mode != Mode.Idle) ClearMode();
            else if (!_awaitingStart && _match.Phase != Phase.GameOver) SetPaused(true);
            else return;
            Refresh();
        }

        // Freezing time also holds the AI's pending decision and the toast timers.
        void SetPaused(bool paused)
        {
            _paused = paused;
            _confirmAbandon = false;
            Time.timeScale = paused ? 0f : 1f;
        }

        void NewMatch()
        {
            foreach (var p in _pieces.Values) Destroy(p.Rt.gameObject);
            _pieces.Clear();
            foreach (var rt in _markers.Values) Destroy(rt.gameObject);
            _markers.Clear();
            if (_aiStep != null) StopCoroutine(_aiStep);
            _aiStep = null;
            _aiIntent = null;
            ClearTrails();
            if (_waitingFade != null) StopCoroutine(_waitingFade);
            _waitingFade = null;
            _waiting = false;
            _handGroup.alpha = _rightColumn.alpha = 1f;
            SetPaused(false);
            _discardOpen = _reviewing = false;
            _reaction = null;
            _foresightPick = 0;
            _foresightScroll = 0f;
            _logSeen = 0;
            _logShown = -1;
            foreach (var toast in _toastList) Destroy(toast.gameObject);
            _toastList.Clear();
            _turnBanner = null;

            int seed = _seed != 0 ? _seed : System.Environment.TickCount;
            _match = Match.CreateDefault(seed);
            _human = new System.Random(seed).Next(2) == 0 ? Side.White : Side.Black;
            _ai = new Ai(_human.Other(), seed);
            _awaitingStart = true;
            LayoutForPlayer();
            ClearMode();
            Sync();
        }

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
                    if (_picks.Count == Match.PickCount(_card) || !_match.CardTargets(_card, _picks).Contains(c)) return;
                    _picks.Add(c);
                    Refresh();
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
            if (_mode != Mode.Card || !Match.PicksComplete(_card, _picks.Count)) return;
            var card = _card;
            var picks = new List<Coord>(_picks);
            ClearMode();
            _match.PlayCard(card, picks);
            Sync();
        }

        void OnEndTurn()
        {
            if (_match.Phase == Phase.GameOver)
            {
                _reviewing = false;
                Refresh();
                return;
            }
            if (!HumanTurn) return;
            ClearMode();
            _match.EndTurn();
            Sync();
        }

        void OnReaction(CardId? card)
        {
            _reaction = null;
            _match.React(card);
            Sync();
        }

        void OnForesight()
        {
            _match.ChooseForesight(_foresightPick);
            _foresightPick = 0;
            _foresightScroll = 0f;
            Sync();
        }

        void OnDiscardClicked()
        {
            if (_awaitingStart) return;
            _discardOpen = true;
            _discardSide = _human;
            RefreshOverlay();
        }

        void CloseDiscard()
        {
            _discardOpen = false;
            RefreshOverlay();
        }

        void OnStartMatch()
        {
            _awaitingStart = false;
            Sync();
        }

        void ScheduleAi()
        {
            if (_aiStep == null && !_awaitingStart && _turnBanner == null && _ai.HasDecision(_match))
                _aiStep = StartCoroutine(AiStepRoutine());
        }

        // The AI first shows what it is about to do, then does it, so every enemy action can be followed.
        IEnumerator AiStepRoutine()
        {
            yield return new WaitForSeconds(_aiDelay);
            var plan = _ai.Plan(_match);
            if (plan != null && plan.Kind != Ai.ChoiceKind.Foresight)
            {
                _aiIntent = plan;
                Refresh();
                yield return new WaitForSeconds(plan.Kind == Ai.ChoiceKind.EndTurn ? _aiIntentTime * 0.5f : _aiIntentTime);
                _aiIntent = null;
            }
            _aiStep = null;
            _ai.Execute(_match, plan);
            Sync();
        }

        void ClearMode()
        {
            _mode = Mode.Idle;
            _selected = null;
            _cardIndex = -1;
            _picks.Clear();
        }

        void Sync()
        {
            foreach (var e in _match.DrainEvents())
            {
                switch (e.Kind)
                {
                    case EventKind.Moved:
                    case EventKind.Retreated:
                    case EventKind.Intercepted:
                        AddTrail(e.From, e.To, capture: false);
                        if (_pieces.TryGetValue(e.UnitId, out var moved)) PlayMove(moved, e.To);
                        break;
                    case EventKind.CaptureDeclared:
                        AddTrail(e.From, e.To, capture: true);
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
            ProcessLog();
            Refresh();
            ScheduleAi();
        }

        void ProcessLog()
        {
            if (_awaitingStart) return;
            var log = _match.Log;
            for (; _logSeen < log.Count; _logSeen++)
            {
                var e = log[_logSeen];
                switch (e.Kind)
                {
                    case LogKind.Preparation:
                        ShowTurnBanner(e);
                        break;
                    case LogKind.Blocked:
                    case LogKind.Retreated:
                    case LogKind.UnitCaptured when e.Unit != UnitType.Sovereign:
                        ShowResult(_logSeen);
                        break;
                }
            }
        }

        void SyncPieces()
        {
            var alive = new HashSet<int>();
            foreach (var unit in _match.Units)
            {
                alive.Add(unit.Id);
                if (!_pieces.TryGetValue(unit.Id, out var piece)) piece = CreatePiece(unit);
                if (piece.Anim == null) piece.Rt.anchoredPosition = SquareCenter(unit.Pos);
                piece.Shield.enabled = unit.Protection > 0 || unit.RangedProtection > 0;
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

            var standing = new HashSet<(Coord, MarkerKind)>();
            foreach (var m in _match.Markers)
            {
                var key = (m.Pos, m.Kind);
                standing.Add(key);
                if (_markers.ContainsKey(key)) continue;
                var rt = CreateMarker(m);
                _markers[key] = rt;
                StartCoroutine(PopRoutine(rt));
            }
            var fallen = new List<(Coord, MarkerKind)>();
            foreach (var key in _markers.Keys) if (!standing.Contains(key)) fallen.Add(key);
            foreach (var key in fallen)
            {
                Destroy(_markers[key].gameObject);
                _markers.Remove(key);
            }
        }

        // Pieces cover the middle of a square, so the marker's icon sits in a corner.
        RectTransform CreateMarker(Marker m)
        {
            var rt = Ui.Rect(_markerLayer, $"{m.Kind} {m.Pos}", 0, 0, Square, Square);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = SquareCenter(m.Pos);
            if (m.Kind == MarkerKind.Palisade)
            {
                Ui.Image(rt, "Palisade", Ui.Art("Palisade"), 0, 0, Square, Square);
                return rt;
            }
            var (fill, kind) = m.Kind switch
            {
                MarkerKind.Ditch => (new Color(Ui.Ink.r, Ui.Ink.g, Ui.Ink.b, 0.3f), CardKind.Fortification),
                MarkerKind.Stakes => (new Color(Ui.Rubric.r, Ui.Rubric.g, Ui.Rubric.b, 0.14f), CardKind.Fortification),
                MarkerKind.Smoke => (new Color(0.55f, 0.55f, 0.55f, 0.4f), CardKind.Reaction),
                _ => (new Color(Ui.Rubric.r, Ui.Rubric.g, Ui.Rubric.b, 0.28f), CardKind.Condition),
            };
            Ui.Image(rt, "Fill", Ui.Solid, 3, 3, Square - 6, Square - 6).color = fill;
            var badge = Ui.Image(rt, "Badge", Ui.Art("Kind_Badge"), 4, 4, 26, 26);
            Ui.Image(badge.transform, "Icon", Ui.KindIcon(kind), 5, 5, 16, 16).color =
                m.Kind == MarkerKind.Stakes || m.Kind == MarkerKind.Lightning ? Ui.Rubric : Ui.Ink;
            return rt;
        }

        void Refresh()
        {
            RefreshHeader();
            RefreshArmy(Side.White);
            RefreshArmy(Side.Black);
            RefreshBoard();
            RefreshPreview();
            RefreshHand();
            RefreshAction();
            RefreshHistory(force: false);
            RefreshDeck();
            RefreshConditions();
            RefreshOverlay();
            RefreshWaiting();
        }

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
            _turn.text = Loc.Format("ui.turnOf", _match.Turn, Loc.SideName(_match.Current));
            bool review = _match.Phase == Phase.GameOver && _reviewing;
            _endTurn.GetComponentInChildren<TextMeshProUGUI>().text = Loc.Get(review ? "ui.gameOver.show" : "ui.endTurn");
            _endTurn.interactable = HumanTurn || review;
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
                icon.rectTransform.anchoredPosition = new Vector2(LeftWidth - 16f - (shown - i) * 48f, -120f);
            }

            bool choosing = active && _mode == Mode.Recruit;
            ui.ReserveHint.text = Loc.Get(choosing ? "ui.reserveChoose" : "ui.reserveHint");
            for (int i = 0; i < Match.Recruitable.Length; i++)
            {
                var type = Match.Recruitable[i];
                bool can = HumanTurn && _match.CanRecruit(type);
                ui.RecruitButtons[i].interactable = can;
                ui.RecruitGroups[i].alpha = can || !HumanTurn ? 1f : 0.45f;
                ui.RecruitHighlights[i].enabled = choosing && _recruitType == type;
            }
        }

        int PendingCost() => _mode switch
        {
            Mode.Recruit => Match.Cost(_recruitType),
            Mode.Card when _match.CanPlay(_card) => _match.CardCost(_card, _picks),
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

        // The trails of a turn stay up until the next player acts, so the opponent's moves can still be read.
        void AddTrail(Coord from, Coord to, bool capture)
        {
            if (from == to) return;
            if (_trailTurn != _match.Turn)
            {
                ClearTrails();
                _trailTurn = _match.Turn;
            }
            if (_trails.Add((from, to))) DrawTrail(_trailLayer, from, to, capture, 1f);
        }

        void ClearTrails()
        {
            _trails.Clear();
            if (_trailLayer == null) return;
            for (int i = _trailLayer.childCount - 1; i >= 0; i--) Destroy(_trailLayer.GetChild(i).gameObject);
        }

        void RefreshPreview()
        {
            for (int i = _previewLayer.childCount - 1; i >= 0; i--) Destroy(_previewLayer.GetChild(i).gameObject);
            if (_awaitingStart || _match.Phase == Phase.GameOver) return;
            if (_aiIntent != null)
            {
                DrawIntent(_aiIntent);
                return;
            }
            if (!HumanTurn || !(_hoverSquare is Coord hover)) return;

            if (_mode == Mode.Unit)
            {
                bool capture = _match.CaptureTargets(_selected).Contains(hover);
                if (capture || _match.MoveTargets(_selected).Contains(hover))
                    DrawTrail(_previewLayer, _selected.Pos, hover, capture, PreviewAlpha);
            }
            else if (_mode == Mode.Card && _picks.Count > 0 && PairsWithPrevious(_card, _picks.Count)
                && _match.CardTargets(_card, _picks).Contains(hover))
                DrawTrail(_previewLayer, _picks[_picks.Count - 1], hover, Match.DeclaresCapture(_card), PreviewAlpha);
        }

        static bool PairsWithPrevious(CardId card, int pick) =>
            card == CardId.CoordinatedAdvance ? pick % 2 == 1
            : pick == 1 && (Match.DeclaresCapture(card) || card == CardId.ExtraRations || card == CardId.LineSwap);

        void DrawIntent(Ai.Choice c)
        {
            var unit = _match.FindUnit(c.UnitId);
            switch (c.Kind)
            {
                case Ai.ChoiceKind.Move when unit != null:
                    Stamp(unit.Pos, "Marker_Selected");
                    Stamp(c.To, "Marker_Move");
                    DrawTrail(_previewLayer, unit.Pos, c.To, false, 1f);
                    break;
                case Ai.ChoiceKind.Capture when unit != null:
                    Stamp(unit.Pos, "Marker_Selected");
                    DrawTrail(_previewLayer, unit.Pos, c.To, true, 1f);
                    break;
                case Ai.ChoiceKind.Recruit:
                    Stamp(c.To, "Marker_Recruit");
                    break;
                case Ai.ChoiceKind.Card:
                    for (int i = 0; i < c.Picks.Length; i++)
                    {
                        if (PairsWithPrevious(c.Card, i))
                            DrawTrail(_previewLayer, c.Picks[i - 1], c.Picks[i], Match.DeclaresCapture(c.Card), 1f);
                        else Stamp(c.Picks[i], "Marker_Selected");
                    }
                    break;
            }
        }

        void Stamp(Coord c, string sprite, float alpha = 1f) =>
            Ui.Image(_previewLayer, "Mark " + c, Ui.Art(sprite), SquareOrigin(c).x, -SquareOrigin(c).y, Square, Square)
                .color = new Color(1f, 1f, 1f, alpha);

        // As in the visual direction: a rubric dashed line running under the pieces, with the vacated square
        // outlined for a move and the target bracketed for a capture. Each end stops at the rim of a piece standing
        // there, otherwise a step from one square to the next would vanish beneath the piece.
        void DrawTrail(RectTransform parent, Coord from, Coord to, bool capture, float alpha)
        {
            Vector2 a = SquareCenter(from), b = SquareCenter(to), dir = (b - a).normalized;
            Vector2 start = a + dir * TrailInsetAt(from), tip = b - dir * TrailInsetAt(to);
            var group = Ui.Rect(parent, $"Trail {from}-{to}", 0f, 0f, BoardSize, BoardSize);
            if (capture)
                Ui.Image(group, "Target", Ui.Art("Marker_Target"), SquareOrigin(to).x, -SquareOrigin(to).y, Square, Square)
                    .color = new Color(1f, 1f, 1f, alpha);
            else
            {
                var ink = new Color(Ui.Ink.r, Ui.Ink.g, Ui.Ink.b, 0.7f * alpha);
                var tl = SquareOrigin(from) + new Vector2(4f, -4f);
                var tr = tl + new Vector2(Square - 8f, 0f);
                var br = tr + new Vector2(0f, 8f - Square);
                var bl = tl + new Vector2(0f, 8f - Square);
                Dashed(group, tl, tr, ink, 1.2f, 4f, 4f);
                Dashed(group, tr, br, ink, 1.2f, 4f, 4f);
                Dashed(group, br, bl, ink, 1.2f, 4f, 4f);
                Dashed(group, bl, tl, ink, 1.2f, 4f, 4f);
            }
            var rubric = new Color(Ui.Rubric.r, Ui.Rubric.g, Ui.Rubric.b, alpha);
            if (Vector2.Dot(tip - start, dir) > 1f) Dashed(group, tip, start, rubric, TrailWidth, TrailDash, TrailGap);
            ArrowHead(group, tip, dir, rubric);
        }

        float TrailInsetAt(Coord c) => _match.At(c) != null ? PieceSize * 0.5f + TrailClearance : TrailEmptyInset;

        static void ArrowHead(RectTransform parent, Vector2 tip, Vector2 dir, Color color)
        {
            float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
            foreach (float spread in new[] { 35f, -35f })
            {
                var head = Ui.Image(parent, "Head", Ui.Solid, 0f, 0f, TrailHead, TrailWidth);
                head.color = color;
                var rt = head.rectTransform;
                rt.pivot = new Vector2(1f, 0.5f);
                rt.anchoredPosition = tip;
                rt.localRotation = Quaternion.Euler(0f, 0f, angle + spread);
            }
        }

        static void Dashed(RectTransform parent, Vector2 start, Vector2 end, Color color, float width, float dash, float gap)
        {
            var delta = end - start;
            float length = delta.magnitude;
            var line = Ui.Rect(parent, "Dashes", 0f, 0f, length, width);
            line.pivot = new Vector2(0f, 0.5f);
            line.anchoredPosition = start;
            line.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
            for (float d = 0f; d < length; d += dash + gap)
                Ui.Image(line, "Dash", Ui.Solid, d, 0f, Mathf.Min(dash, length - d), width).color = color;
        }

        void HoverSquare(Coord c, bool on)
        {
            if (on) _hoverSquare = c;
            else if (_hoverSquare == c) _hoverSquare = null;
            RefreshPreview();
        }

        void RefreshHand()
        {
            for (int i = _hand.childCount - 1; i >= 0; i--) Destroy(_hand.GetChild(i).gameObject);
            _handCards.Clear();
            var hand = _awaitingStart || _match.Phase == Phase.GameOver ? null : _match.Get(_human).Hand;
            // A rebuild (after a click on the hovered card, say) keeps that card lifted instead of replaying the rise.
            if (hand == null || _hoveredCard >= hand.Count) _hoveredCard = -1;
            if (hand == null) return;

            float w = Ui.CardWidth * HandScale;
            float h = Ui.CardHeight * HandScale;
            float area = _hand.rect.width;
            float step = hand.Count > 1 ? Mathf.Min(w - HandOverlap, (area - w) / (hand.Count - 1)) : 0f;
            float start = (area - (w + step * (hand.Count - 1))) * 0.5f;
            float middle = (hand.Count - 1) * 0.5f;

            for (int i = 0; i < hand.Count; i++)
            {
                var info = CardCatalog.Get(hand[i]);
                var slot = Ui.Image(_hand, "Slot", Ui.Solid, start + step * i, 0f, w, h, raycast: true);
                slot.color = Color.clear;

                var card = Ui.Card(slot.transform, info, HandScale);
                card.pivot = new Vector2(0.5f, 0f);
                var graphics = card.GetComponentsInChildren<Graphic>();
                bool selected = _mode == Mode.Card && _cardIndex == i;
                var c = new HandCard
                {
                    Slot = slot.rectTransform,
                    Card = card,
                    Tilt = -HandTilt * (i - middle),
                    Selected = selected,
                    Off = HumanTurn && !_match.CanPlay(info.Id),
                    Lift = i == _hoveredCard ? 1f : 0f,
                    Graphics = graphics,
                    Colors = System.Array.ConvertAll(graphics, g => g.color),
                };
                c.Shadow = Ui.Image(card, "Shadow", Ui.Art("Card_Shadow"), -17.5f, -2.5f, w + 35f, h + 35f);
                c.Shadow.type = Image.Type.Sliced;
                c.Shadow.pixelsPerUnitMultiplier = 60f / 25f;
                c.Shadow.transform.SetAsFirstSibling();
                _handCards.Add(c);
                PoseHandCard(c);

                int index = i;
                var button = slot.gameObject.AddComponent<Button>();
                button.transition = Selectable.Transition.None;
                button.onClick.AddListener(() => OnCardClicked(index));
                OnHover(slot.gameObject, () => HoverHandCard(index, true), () => HoverHandCard(index, false));
            }
            OrderHand();
        }

        void HoverHandCard(int index, bool on)
        {
            if (on) _hoveredCard = index;
            else if (_hoveredCard == index) _hoveredCard = -1;
            OrderHand();
        }

        void OrderHand()
        {
            foreach (var c in _handCards) c.Slot.SetAsLastSibling();
            foreach (var c in _handCards)
                if (c.Selected) c.Slot.SetAsLastSibling();
            if (_hoveredCard >= 0) _handCards[_hoveredCard].Slot.SetAsLastSibling();
        }

        void AnimateHand()
        {
            for (int i = 0; i < _handCards.Count; i++)
            {
                var c = _handCards[i];
                float target = i == _hoveredCard ? 1f : 0f;
                if (Mathf.Approximately(c.Lift, target)) continue;
                c.Lift = Mathf.MoveTowards(c.Lift, target, Time.deltaTime / HandEase);
                PoseHandCard(c);
            }
        }

        static void PoseHandCard(HandCard c)
        {
            float t = Mathf.SmoothStep(0f, 1f, c.Lift);
            float bottom = -Ui.CardHeight * HandScale;
            float rest = bottom + (c.Selected ? SelectedLift : 0f);
            c.Card.anchoredPosition =
                new Vector2(Ui.CardWidth * HandScale * 0.5f, Mathf.Lerp(rest, bottom + HandLift, t));
            c.Card.localRotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(c.Tilt, 0f, t));
            float scale = Mathf.Lerp(1f, HandZoom, t);
            c.Card.localScale = new Vector3(scale, scale, 1f);
            c.Shadow.color = new Color(1f, 1f, 1f, HandShadowAlpha * t);
            if (!c.Off) return;
            for (int i = 0; i < c.Graphics.Length; i++)
                c.Graphics[i].color = Color.Lerp(c.Colors[i] * OffTint, c.Colors[i], t);
        }

        static void OnHover(GameObject target, System.Action enter, System.Action exit)
        {
            var trigger = target.AddComponent<EventTrigger>();
            var enterEntry = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
            enterEntry.callback.AddListener(_ => enter());
            var exitEntry = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
            exitEntry.callback.AddListener(_ => exit());
            trigger.triggers.Add(enterEntry);
            trigger.triggers.Add(exitEntry);
        }

        void RefreshAction()
        {
            for (int i = _actionSlot.childCount - 1; i >= 0; i--) Destroy(_actionSlot.GetChild(i).gameObject);
            for (int i = _actionCells.childCount - 1; i >= 0; i--) Destroy(_actionCells.GetChild(i).gameObject);

            var p = _match.Get(_human);
            string step, hint, primaryLabel = Loc.Get("ui.action.playCard");
            var cells = new List<(string label, string value, string sub, bool marked)>();
            bool showPrimary = false, showCancel = _mode != Mode.Idle;

            bool live = !_awaitingStart && _match.Phase != Phase.GameOver;
            bool aiReacting = live && _match.Phase == Phase.Reaction && _match.Pending.Defender.Side == _ai.Side;
            if (aiReacting || (live && _match.Current == _ai.Side))
            {
                var ai = _match.Get(_ai.Side);
                var intentCard = IntentCard(_aiIntent);
                if (intentCard is CardId shown) Ui.Card(_actionSlot, CardCatalog.Get(shown), ActionCardScale);
                else ArmyPlate(_ai.Side, "ui.plate.enemyText");
                if (_aiIntent != null)
                {
                    step = Loc.Get("ui.ai.intent");
                    hint = IntentText(_aiIntent);
                    var (value, sub) = IntentCell(_aiIntent);
                    cells.Add((Loc.Get("ui.cell.action"), value, sub, true));
                }
                else if (aiReacting)
                {
                    var pc = _match.Pending;
                    step = Loc.Get("ui.reaction.header");
                    hint = Loc.Format("ui.action.aiReacting", Loc.Army(_ai.Side));
                    cells.Add((Loc.Get("ui.cell.target"), Loc.UnitDot(pc.Defender),
                        Loc.Format("ui.cell.from", Loc.UnitDot(pc.Attacker)), true));
                }
                else
                {
                    step = Loc.Get("ui.action.aiTitle");
                    hint = Loc.Format("ui.action.aiHint", Loc.Army(_ai.Side));
                    cells.Add((Loc.Get("ui.cell.orders"), Loc.Format("ui.cell.ordersValue", ai.Orders, Match.OrdersPerTurn),
                        Loc.Get("ui.cell.ordersSub"), false));
                }
                cells.Add((Loc.Get("ui.morale"), Loc.Format("ui.moraleValue", ai.Morale, Match.MaxMorale),
                    Loc.Get("ui.cell.opponent"), false));
                cells.Add((Loc.Get("ui.cell.hand"), Loc.Format("ui.cell.handValue", ai.Hand.Count),
                    Loc.Format("ui.cell.deckSub", ai.Deck.Count), false));
            }
            else switch (_mode)
            {
                case Mode.Unit:
                {
                    var u = _selected;
                    step = Loc.Get("ui.action.unitTitle");
                    UnitPlate(u.Type, u.Side, "ui.plate.unit");
                    hint = Loc.Get(u.RecruitedTurn == _match.Turn ? "ui.action.hintResting"
                        : p.Orders <= 0 && p.BonusMoves <= 0 ? "ui.action.hintNoOrders"
                        : "ui.action.hintUnit");
                    var gear = new List<string>();
                    if (u.Protection > 0) gear.Add(Loc.Get("ui.action.protection"));
                    if (u.RangedProtection > 0) gear.Add(Loc.Format("ui.action.rangedProtection", u.RangedProtection));
                    if (u.HasHammer) gear.Add(Loc.Get("ui.action.hammer"));
                    if (u.HasHorseshoes) gear.Add(Loc.Get("ui.action.horseshoes"));
                    if (u.HasLance) gear.Add(Loc.Get("ui.action.lance"));
                    if (u.HasBanner) gear.Add(Loc.Get("ui.action.banner"));
                    cells.Add((Loc.Get("ui.cell.moves"), _match.MoveTargets(u).Count.ToString(),
                        Loc.Get("ui.cell.movesSub"), false));
                    cells.Add((Loc.Get("ui.cell.captures"), _match.CaptureTargets(u).Count.ToString(),
                        Loc.Get("ui.cell.capturesSub"), false));
                    cells.Add((Loc.Get("ui.cell.gear"),
                        gear.Count > 0 ? string.Join(" · ", gear) : Loc.Get("ui.action.none"), "", false));
                    break;
                }
                case Mode.Recruit:
                {
                    int cost = Match.Cost(_recruitType);
                    string ranks = Loc.Get("ui.ranks." + _match.Current);
                    step = Loc.Format("ui.action.recruitTitle", Loc.UnitName(_recruitType));
                    UnitPlate(_recruitType, _match.Current, "ui.plate.reserve");
                    hint = Loc.Format("ui.action.hintRecruit", ranks);
                    cells.Add((Loc.Get("ui.cell.square"), Loc.Format("ui.cell.ranks", ranks),
                        Loc.Get("ui.cell.freeSquare"), false));
                    cells.Add((Loc.Get("ui.cell.reserve"), Loc.Format("ui.cell.reserveValue", p.Reserve[_recruitType]),
                        Loc.Get("ui.cell.available"), false));
                    cells.Add((Loc.Get("ui.cell.cost"), Loc.Format("ui.cell.moraleCost", cost),
                        Loc.Format("ui.cell.left", p.Morale - cost, Match.MaxMorale) + Loc.Get("ui.cell.plusOrder"), false));
                    break;
                }
                case Mode.Card:
                {
                    var info = CardCatalog.Get(_card);
                    int count = Match.PickCount(_card);
                    bool can = _match.CanPlay(_card);
                    bool ready = can && Match.PicksComplete(_card, _picks.Count);
                    bool declares = Match.DeclaresCapture(_card);
                    int cost = can ? _match.CardCost(_card, _picks) : info.Cost;
                    Ui.Card(_actionSlot, info, ActionCardScale);
                    string title = ready ? Loc.Get(declares ? "ui.step.declare" : "ui.step.confirm")
                        : can ? Loc.Get($"ui.step.{_card}.{_picks.Count}") : "";
                    step = can ? Loc.Format("ui.action.step", _picks.Count + 1, count + 1, title) : Loc.KindName(info.Kind);
                    hint = ready && _picks.Count == count ? Loc.Get(declares ? "ui.hint.declare" : "ui.hint.confirm")
                        : CardHint(info);
                    cells.Add(TargetCell(count, can));
                    string range = info.IsWeather ? "weather" : _card.ToString();
                    cells.Add((Loc.Get("ui.cell.range"), Loc.Get($"ui.range.{range}"), Loc.Get($"ui.range.{range}.sub"), false));
                    string left = info.IsReaction ? Loc.Get("ui.cell.onReaction")
                        : can ? Loc.Format("ui.cell.left", p.Morale - cost, Match.MaxMorale)
                            + (Match.UsesOrder(_card) ? Loc.Get("ui.cell.plusOrder") : "")
                        : Loc.Get("ui.cell.unavailable");
                    cells.Add((Loc.Get("ui.cell.cost"), Loc.Format("ui.cell.moraleCost", cost), left, false));
                    showPrimary = ready;
                    if (declares) primaryLabel = Loc.Get("ui.action.declare");
                    break;
                }
                default:
                    step = Loc.Get("ui.action.idleTitle");
                    ArmyPlate(_human, "ui.plate.armyText");
                    hint = Loc.Get("ui.action.idleHint");
                    cells.Add((Loc.Get("ui.morale"), Loc.Format("ui.moraleValue", p.Morale, Match.MaxMorale),
                        Loc.Format("ui.cell.perTurn", Match.MoralePerTurn), false));
                    cells.Add((Loc.Get("ui.cell.orders"), Loc.Format("ui.cell.ordersValue", p.Orders, Match.OrdersPerTurn),
                        p.BonusMoves > 0 ? Loc.Format("ui.cell.bonus", p.BonusMoves) : Loc.Get("ui.cell.ordersSub"), false));
                    cells.Add((Loc.Get("ui.cell.card"),
                        Loc.Get(p.PlayedProactive ? "ui.cell.cardUsed" : "ui.cell.cardAvailable"), Loc.Get("ui.cell.cardSub"), false));
                    break;
            }

            _actionStep.text = step;
            _actionHint.text = hint;
            BuildCells(cells);
            _actionPrimary.gameObject.SetActive(showPrimary);
            Ui.SetButton(_actionPrimary, primaryLabel, true);
            _actionCancel.gameObject.SetActive(showCancel);
        }

        static CardId? IntentCard(Ai.Choice c) => c?.Kind switch
        {
            Ai.ChoiceKind.Card => c.Card,
            Ai.ChoiceKind.React => c.Reaction,
            _ => null,
        };

        string IntentText(Ai.Choice c)
        {
            string army = Loc.Army(_ai.Side);
            var unit = _match.FindUnit(c.UnitId);
            switch (c.Kind)
            {
                case Ai.ChoiceKind.Move when unit != null:
                    return Loc.Format("ui.ai.move", army, Loc.UnitName(unit.Type), unit.Pos, c.To);
                case Ai.ChoiceKind.Capture when unit != null:
                    return Loc.Format("ui.ai.capture", army, Loc.UnitAt(unit), Describe(c.To));
                case Ai.ChoiceKind.Recruit:
                    return Loc.Format("ui.ai.recruit", army, Loc.UnitName(c.Type), c.To);
                case Ai.ChoiceKind.Card:
                    return c.Picks.Length > 0
                        ? Loc.Format("ui.ai.cardOn", army, Loc.CardName(c.Card), Describe(c.Picks[0]))
                        : Loc.Format("ui.ai.card", army, Loc.CardName(c.Card));
                case Ai.ChoiceKind.React:
                    return c.Reaction is CardId r ? Loc.Format("ui.ai.react", army, Loc.CardName(r)) : Loc.Format("ui.ai.pass", army);
                default:
                    return Loc.Format("ui.ai.endTurn", army);
            }
        }

        (string value, string sub) IntentCell(Ai.Choice c)
        {
            var unit = _match.FindUnit(c.UnitId);
            return c.Kind switch
            {
                Ai.ChoiceKind.Move when unit != null => (Loc.Get("ui.ai.cell.move"), $"{unit.Pos} → {c.To}"),
                Ai.ChoiceKind.Capture when unit != null => (Loc.Get("ui.ai.cell.capture"), $"{unit.Pos} × {c.To}"),
                Ai.ChoiceKind.Recruit => (Loc.Get("ui.ai.cell.recruit"), Loc.UnitDot(c.Type, c.To)),
                Ai.ChoiceKind.Card => (Loc.Get("ui.ai.cell.card"), Loc.Format("ui.cell.moraleCost", _match.CardCost(c.Card, c.Picks))),
                Ai.ChoiceKind.React when c.Reaction is CardId r =>
                    (Loc.Get("ui.ai.cell.react"), Loc.Format("ui.cell.moraleCost", _match.ReactionCost(r))),
                Ai.ChoiceKind.React => (Loc.Get("ui.ai.cell.pass"), ""),
                _ => (Loc.Get("ui.ai.cell.endTurn"), ""),
            };
        }

        void UnitPlate(UnitType type, Side side, string kindKey) =>
            Ui.Plate(_actionSlot, Loc.UnitName(type), Ui.UnitArt(type, side), Loc.Get(kindKey), Loc.UnitText(type),
                ActionCardScale);

        void ArmyPlate(Side side, string textKey) =>
            Ui.Plate(_actionSlot, Loc.Army(side), Ui.UnitArt(UnitType.Sovereign, side), Loc.Get("ui.plate.army"),
                Loc.Get(textKey), ActionCardScale);

        (string label, string value, string sub, bool marked) TargetCell(int count, bool can)
        {
            string label = Loc.Get("ui.cell.target");
            if (count == 0) return (label, Loc.Get("ui.cell.self"), "", false);
            if (!can) return (label, Loc.Get("ui.action.none"), "", false);
            string from = _picks.Count > 0 && count > 1 ? Loc.Format("ui.cell.from", Describe(_picks[0])) : "";
            if (!Match.PicksComplete(_card, _picks.Count))
                return (label, Loc.Get("ui.cell.choose"), _picks.Count > 0 ? from : Loc.Get($"ui.step.{_card}.0"), false);
            return (label, Describe(_picks[_picks.Count - 1]), from, true);
        }

        string Describe(Coord c)
        {
            var unit = _match.At(c);
            return unit != null ? Loc.UnitDot(unit) : c.ToString();
        }

        void BuildCells(List<(string label, string value, string sub, bool marked)> cells)
        {
            float w = _actionCells.rect.width, cw = w / cells.Count;
            Ui.Line(_actionCells, 0f, 0f, w, 1f, 0.35f);
            Ui.Line(_actionCells, 0f, CellsHeight - 1f, w, 1f, 0.35f);
            for (int i = 0; i < cells.Count; i++)
            {
                var (label, value, sub, marked) = cells[i];
                float x = cw * i;
                if (i > 0) Ui.Line(_actionCells, x, 0f, 1f, CellsHeight, 0.35f);
                var title = Ui.Label(_actionCells, label, x + 10f, 8f, cw - 20f);
                title.fontSize = 14f;
                title.characterSpacing = 8f;
                title.textWrappingMode = TextWrappingModes.NoWrap;
                Ui.Text(_actionCells, value, Ui.BodyFont, 18f, marked ? Ui.Rubric : Ui.InkText, x + 10f, 29f, cw - 20f, 22f);
                Ui.Text(_actionCells, sub, Ui.BodyFont, 14.5f, Ui.InkBody, x + 10f, 52f, cw - 20f, 34f);
            }
        }

        string CardHint(CardInfo info)
        {
            var p = _match.CurrentPlayer;
            if (info.IsReaction) return Loc.Get("ui.hint.reaction");
            if (p.PlayedProactive) return Loc.Get("ui.hint.alreadyPlayed");
            if (!_match.CanPlay(info.Id)) return Loc.Get(p.Morale < info.Cost ? "ui.hint.noMorale" : "ui.hint.noTarget");
            return Match.PickCount(info.Id) switch
            {
                0 => Loc.Get("ui.hint.confirm"),
                1 => Loc.Get("ui.hint." + info.Id),
                _ => Loc.Get($"ui.hint.{info.Id}.{_picks.Count}"),
            };
        }

        void RefreshHistory(bool force)
        {
            var log = _match.Log;
            if (!force && log.Count == _logShown) return;
            for (int i = _logEntries.childCount - 1; i >= 0; i--) Destroy(_logEntries.GetChild(i).gameObject);
            float w = _logEntries.rect.width, textWidth = w - 46f, y = 0f;
            for (int i = log.Count - 1; i >= 0; i--)
            {
                string line = Loc.LogLine(log[i]);
                var entry = Ui.Text(_logEntries, line, Ui.BodyFont, 17f, Ui.InkText, 46f, y + 8f, textWidth, 0f);
                float height = entry.GetPreferredValues(line, textWidth, 0f).y;
                entry.rectTransform.sizeDelta = new Vector2(textWidth, height);
                Ui.Line(_logEntries, 0f, y, w, 1f, 0.3f);
                Ui.Text(_logEntries, Ui.Roman(log[i].Turn), Ui.TitleFont, 17f, Ui.Rubric, 0f, y + 8f, 44f, 22f);
                y += height + 16f;
            }
            _logEntries.sizeDelta = new Vector2(_logEntries.sizeDelta.x, y);
            if (log.Count != _logShown) _logEntries.anchoredPosition = Vector2.zero;
            _logShown = log.Count;
        }

        void RefreshDeck()
        {
            var p = _match.Get(_human);
            _deck.text = p.Deck.Count.ToString();
            _deckCaption.text = Loc.Get(p.Deck.Count == 1 ? "ui.card" : "ui.cards");
            _discard.text = p.Discard.Count.ToString();
            for (int i = _discardPile.childCount - 1; i >= 0; i--) Destroy(_discardPile.GetChild(i).gameObject);
            if (p.Discard.Count == 0)
            {
                _discardLast.text = Loc.Get("ui.discardEmpty");
                return;
            }
            var last = p.Discard[p.Discard.Count - 1];
            _discardLast.text = Loc.Format("ui.discardLast", Loc.CardName(last));
            const float sheetW = 58f, sheetH = 81f;
            Ui.Centered(Ui.Image(_discardPile, "Sheet", Ui.Art("CardFrame"), 8f, 6f, sheetW, sheetH).rectTransform)
                .localRotation = Quaternion.Euler(0f, 0f, -9f);
            Ui.Centered(Ui.Image(_discardPile, "Sheet", Ui.Art("CardFrame"), 5f, 5f, sheetW, sheetH).rectTransform)
                .localRotation = Quaternion.Euler(0f, 0f, 7f);
            var top = Ui.Card(_discardPile, CardCatalog.Get(last), sheetW / Ui.CardWidth);
            top.anchoredPosition = new Vector2(8f, -5f);
            Ui.Centered(top).localRotation = Quaternion.Euler(0f, 0f, -3f);
        }

        void RefreshOverlay()
        {
            for (int i = _overlayContent.childCount - 1; i >= 0; i--) Destroy(_overlayContent.GetChild(i).gameObject);
            // TMP measures text only once awake, so the builders below need an active hierarchy.
            _overlay.gameObject.SetActive(true);
            bool modal = true, discard = false;

            if (_match.Phase == Phase.GameOver && !_reviewing) BuildGameOver();
            else if (_awaitingStart) BuildIntro();
            else if (_match.Phase == Phase.Reaction && _match.Pending.Defender.Side == _human) BuildReaction();
            else if (_match.Phase == Phase.Foresight && _match.Current == _human) BuildForesight();
            else if (_discardOpen) discard = true;
            else modal = false;
            if (discard) BuildDiscard();

            if (_paused)
            {
                if (modal) Scrim(0.45f);
                BuildPause();
                if (_confirmAbandon)
                {
                    Scrim(0.45f);
                    BuildConfirm();
                }
            }
            _overlay.gameObject.SetActive(modal || _paused);
            _backdrop.SetActive(discard && !_paused);
        }

        RectTransform OverlayPanel(float w, float h) =>
            Ui.Modal(_overlayContent, (StageWidth - w) * 0.5f, (StageHeight - h) * 0.5f, w, h);

        // The overlay content is stage-sized; the extra margin keeps the scrim edge-to-edge on wider screens.
        void Scrim(float alpha)
        {
            var scrim = Ui.Stretch(Ui.Image(_overlayContent, "Scrim", Ui.Solid, 0, 0, 0, 0, raycast: true).rectTransform);
            scrim.offsetMin = new Vector2(-StageWidth, -StageHeight);
            scrim.offsetMax = new Vector2(StageWidth, StageHeight);
            scrim.GetComponent<Image>().color = new Color(ScrimColor.r, ScrimColor.g, ScrimColor.b, alpha);
        }

        static TextMeshProUGUI Eyebrow(RectTransform panel, string text, float y, float spacing = 20f)
        {
            var label = Ui.Label(panel, text, 0f, y, panel.sizeDelta.x, 26f, TextAlignmentOptions.Top);
            label.fontSize = 18f;
            label.characterSpacing = spacing;
            return label;
        }

        static TextMeshProUGUI Title(RectTransform panel, string text, float y, float size) =>
            Ui.Text(panel, Ui.Initial(text, 122), Ui.TitleFont, size, Ui.InkText, 0f, y, panel.sizeDelta.x, size * 1.4f,
                TextAlignmentOptions.Top);

        static TextMeshProUGUI Fit(TextMeshProUGUI text)
        {
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.rectTransform.sizeDelta = new Vector2(text.GetPreferredValues(text.text).x, text.rectTransform.sizeDelta.y);
            return text;
        }

        static Button FitButton(RectTransform parent, string label, bool primary, float height, float fontSize,
            float padding, UnityEngine.Events.UnityAction onClick)
        {
            var button = Ui.Button(parent, label, primary, 0f, 0f, 100f, height, onClick, fontSize);
            var text = button.GetComponentInChildren<TextMeshProUGUI>();
            var size = new Vector2(text.GetPreferredValues(label).x + padding * 2f, height);
            ((RectTransform)button.transform).sizeDelta = size;
            text.rectTransform.sizeDelta = size;
            return button;
        }

        static Button Disabled(Button button)
        {
            Ui.SetButton(button, button.GetComponentInChildren<TextMeshProUGUI>().text, false);
            return button;
        }

        static void Row(RectTransform parent, float y, float gap, params RectTransform[] items)
        {
            float total = -gap, tallest = 0f;
            foreach (var item in items)
            {
                total += item.sizeDelta.x + gap;
                tallest = Mathf.Max(tallest, item.sizeDelta.y);
            }
            float x = (parent.sizeDelta.x - total) * 0.5f;
            foreach (var item in items)
            {
                item.anchoredPosition = new Vector2(x, -(y + (tallest - item.sizeDelta.y) * 0.5f));
                x += item.sizeDelta.x + gap;
            }
        }

        static void Row(RectTransform parent, float y, float gap, params Button[] buttons) =>
            Row(parent, y, gap, System.Array.ConvertAll(buttons, b => (RectTransform)b.transform));

        static void Cross(Transform parent, float x, float y, float size, Color color, float thickness = 4f)
        {
            foreach (float angle in new[] { 45f, -45f })
            {
                var line = Ui.Image(parent, "Cross", Ui.Solid, x + (size - size * 1.2f) * 0.5f, y + (size - thickness) * 0.5f,
                    size * 1.2f, thickness);
                line.color = color;
                Ui.Centered(line.rectTransform).localRotation = Quaternion.Euler(0f, 0f, angle);
            }
        }

        static void Divider(RectTransform parent, float y)
        {
            float center = parent.sizeDelta.x * 0.5f;
            Ui.Line(parent, center - 130f, y, 112f, 1.5f, 0.8f);
            Ui.Line(parent, center + 18f, y, 112f, 1.5f, 0.8f);
            var diamond = Ui.Image(parent, "Diamond", Ui.Solid, center - 5f, y - 4.25f, 10f, 10f);
            diamond.color = Ui.Rubric;
            Ui.Centered(diamond.rectTransform).localRotation = Quaternion.Euler(0f, 0f, 45f);
        }

        static void Arrow(RectTransform parent, float x, float y, float length)
        {
            for (float d = 0f; d < length - 14f; d += 14f) Ui.Line(parent, x + d, y, 8f, 2f).color = Ui.Rubric;
            foreach (float angle in new[] { 35f, -35f })
            {
                var head = Ui.Line(parent, 0f, 0f, 14f, 2f);
                head.color = Ui.Rubric;
                var rt = head.rectTransform;
                rt.pivot = new Vector2(1f, 0.5f);
                rt.anchoredPosition = new Vector2(x + length, -(y + 1f));
                rt.localRotation = Quaternion.Euler(0f, 0f, angle);
            }
        }

        static void Pips(RectTransform parent, float x, float y, int on, float size, float gap)
        {
            for (int i = 0; i < Match.MaxMorale; i++)
                Ui.Image(parent, "Pip", Ui.Art(i < on ? "Pip_On" : "Pip_Off"), x + i * (size + gap), y, size, size);
        }

        static Image Chip(RectTransform parent, string text, float x, float y)
        {
            var chip = Ui.Sliced(parent, "Chip",
                Ui.Pill(new Color(1f, 0.98f, 0.93f, 0.35f), new Color(Ui.Ink.r, Ui.Ink.g, Ui.Ink.b, 0.5f), 16), x, y, 0f, 32f);
            var label = Fit(Ui.Text(chip.transform, text, Ui.BodyFont, 17f, Ui.InkText, 12f, 0f, 0f, 32f,
                TextAlignmentOptions.MidlineLeft));
            chip.rectTransform.sizeDelta = new Vector2(label.rectTransform.sizeDelta.x + 24f, 32f);
            return chip;
        }

        void BuildIntro()
        {
            const float w = 960f, h = 794f, tipsY = 465f, tipsHeight = 189f;
            var panel = OverlayPanel(w, h);
            Eyebrow(panel, Loc.Get("ui.intro.eyebrow"), 44f);
            Ui.Image(panel, "Emblem", Ui.UnitArt(UnitType.Sovereign, _human), (w - 104f) * 0.5f, 92f, 104f, 104f);
            Eyebrow(panel, Loc.Get("ui.intro.header"), 218f, 32f);
            Title(panel, Loc.Army(_human), 246f, 60f);
            Ui.Text(panel, Loc.Format(_match.Current == _human ? "ui.intro.youFirst" : "ui.intro.aiFirst", Loc.Army(_ai.Side)),
                Ui.ItalicFont, 26f, Ui.Ink, 0f, 336f, w, 76f, TextAlignmentOptions.Top);
            Divider(panel, 438f);

            const float tipsX = 56f, tipWidth = (w - tipsX * 2f) / 3f;
            Ui.Line(panel, tipsX, tipsY, w - tipsX * 2f, 1f, 0.4f);
            Ui.Line(panel, tipsX, tipsY + tipsHeight, w - tipsX * 2f, 1f, 0.4f);
            var tips = new[]
            {
                (Loc.Get("ui.intro.goalTitle"), Loc.Get("ui.intro.goalText")),
                (Loc.Get("ui.intro.moraleTitle"), Loc.Format("ui.intro.moraleText", Match.MoralePerTurn, Match.MaxMorale)),
                (Loc.Format("ui.intro.ordersTitle", Match.OrdersPerTurn), Loc.Get("ui.intro.ordersText")),
            };
            for (int i = 0; i < tips.Length; i++)
            {
                float x = tipsX + tipWidth * i, center = x + tipWidth * 0.5f;
                if (i > 0) Ui.Line(panel, x, tipsY, 1f, tipsHeight, 0.4f);
                if (i == 0) Ui.Image(panel, "Icon", Ui.UnitArt(UnitType.Sovereign, _ai.Side), center - 26f, tipsY + 22f, 52f, 52f);
                else if (i == 1)
                    for (int k = 0; k < 3; k++) Ui.Image(panel, "Pip", Ui.Art("Pip_On"), center - 36f + k * 26f, tipsY + 38f, 20f, 20f);
                else Ui.Image(panel, "Icon", Ui.Art("Order_On"), center - 25f, tipsY + 24f, 50f, 46f);
                var (title, text) = tips[i];
                Ui.Text(panel, title, Ui.TitleFont, 22f, Ui.InkText, x, tipsY + 86f, tipWidth, 28f, TextAlignmentOptions.Top);
                Ui.Text(panel, text, Ui.BodyFont, 18f, Ui.Ink, x + 18f, tipsY + 120f, tipWidth - 36f, 50f,
                    TextAlignmentOptions.Top);
            }

            Row(panel, 682f, 18f,
                FitButton(panel, Loc.Get("ui.intro.button"), true, 70f, 28f, 64f, OnStartMatch),
                Disabled(FitButton(panel, Loc.Get("ui.intro.rules"), false, 56f, 22f, 28f, () => { })));
        }

        void BuildReaction()
        {
            var pc = _match.Pending;
            var defender = _match.Get(pc.Defender.Side);
            var applicable = _match.ReactionOptions();
            var cards = new List<CardId>();
            foreach (var id in defender.Hand)
                if (CardCatalog.Get(id).IsReaction && !cards.Contains(id)) cards.Add(id);
            if (_reaction == null || !applicable.Contains(_reaction.Value))
                _reaction = applicable.Count > 0 ? applicable[0] : (CardId?)null;

            const float w = 1080f, h = 862f, optionWidth = 226f, optionHeight = 364f, optionGap = 24f;
            var panel = OverlayPanel(w, h);
            Eyebrow(panel, Loc.Get("ui.reaction.eyebrow"), 40f);
            Title(panel, Loc.Format("ui.reaction.title", Loc.Army(pc.Defender.Side)), 72f, 52f);
            Combatant(panel, pc.Attacker, w * 0.5f - 199f, Loc.Format("ui.reaction.attacker", Loc.Army(pc.Attacker.Side)), false);
            Combatant(panel, pc.Defender, w * 0.5f + 199f, Loc.Get("ui.reaction.yourUnit"), true);
            Arrow(panel, w * 0.5f - 90f, 202f, 180f);
            Ui.Text(panel, Loc.Get(pc.Advances ? "ui.reaction.tries" : "ui.reaction.shoots"), Ui.ItalicFont, 18f, Ui.InkBody,
                w * 0.5f - 100f, 218f, 200f, 24f, TextAlignmentOptions.Top);

            var morale = Ui.Rect(panel, "Morale", 0f, 338f, 0f, 26f);
            var label = Ui.Label(morale, Loc.Get("ui.morale"), 0f, 2f, 0f, 24f);
            label.fontSize = 18f;
            Fit(label);
            float x = label.rectTransform.sizeDelta.x + 10f;
            Pips(morale, x, 3f, defender.Morale, 18f, 4f);
            x += Match.MaxMorale * 22f + 6f;
            var value = Fit(Ui.Text(morale, Loc.Format("ui.moraleValue", defender.Morale, Match.MaxMorale) + "   ·   "
                + Loc.Get("ui.reaction.prompt"), Ui.BodyFont, 18f, Ui.InkText, x, 0f, 0f, 26f));
            morale.sizeDelta = new Vector2(x + value.rectTransform.sizeDelta.x, 26f);
            Row(panel, 338f, 0f, morale);

            float optionX = (w - (cards.Count * optionWidth + (cards.Count - 1) * optionGap)) * 0.5f;
            foreach (var id in cards)
            {
                var info = CardCatalog.Get(id);
                bool ok = applicable.Contains(id), chosen = ok && _reaction == id;
                if (chosen) Ui.Sliced(panel, "Glow", Ui.Glow, optionX - 4f, 376f, optionWidth + 8f, optionHeight + 8f);
                var option = Ui.Sliced(panel, "Option", chosen ? Ui.Selection : Ui.Solid, optionX, 380f, optionWidth,
                    optionHeight, raycast: ok);
                if (!chosen) option.color = Color.clear;
                var card = Ui.Card(option.transform, info, ModalCardScale);
                card.anchoredPosition = new Vector2(12f, -12f);
                if (!ok) card.gameObject.AddComponent<CanvasGroup>().alpha = 0.45f;
                var status = Ui.Text(option.transform,
                    Loc.Get(chosen ? "ui.reaction.selected" : ok ? "ui.reaction.applicable" : "ui.reaction.notApplicable"),
                    Ui.TitleFont, 17f, ok ? Ui.Rubric : Ui.InkSoft, 0f, 304f, optionWidth, 22f, TextAlignmentOptions.Top);
                status.characterSpacing = 6f;
                Ui.Text(option.transform, ReactionNote(id, ok, pc), Ui.BodyFont, 18f, Ui.InkText, 12f, 326f,
                    optionWidth - 24f, 40f, TextAlignmentOptions.Top);
                if (ok)
                {
                    var button = option.gameObject.AddComponent<Button>();
                    button.transition = Selectable.Transition.None;
                    var pick = id;
                    button.onClick.AddListener(() =>
                    {
                        _reaction = pick;
                        RefreshOverlay();
                    });
                }
                optionX += optionWidth + optionGap;
            }

            var pass = FitButton(panel, Loc.Get("ui.reaction.pass"), false, 58f, 22f, 30f, () => OnReaction(null));
            if (_reaction is CardId selected)
            {
                var play = FitButton(panel, Loc.Format("ui.reaction.play", Loc.CardName(selected),
                    _match.ReactionCost(selected)), true, 58f, 22f, 30f, () => OnReaction(selected));
                Row(panel, 765f, 16f, play, pass);
            }
            else Row(panel, 765f, 16f, pass);
        }

        static void Combatant(RectTransform panel, Unit unit, float center, string caption, bool target)
        {
            const float y = 164f, size = 88f;
            if (target) Ui.Image(panel, "Target", Ui.Art("Marker_Target"), center - 52f, y - 8f, 104f, 104f);
            Ui.Image(panel, "Token", Ui.UnitArt(unit.Type, unit.Side), center - size * 0.5f, y, size, size);
            Ui.Text(panel, Loc.UnitDot(unit), Ui.TitleFont, 22f, target ? Ui.Rubric : Ui.InkText, center - 160f, y + 100f,
                320f, 28f, TextAlignmentOptions.Top);
            Ui.Text(panel, caption, Ui.BodyFont, 17f, Ui.InkBody, center - 160f, y + 130f, 320f, 24f,
                TextAlignmentOptions.Top);
        }

        string ReactionNote(CardId id, bool ok, PendingCapture pc)
        {
            int cost = _match.ReactionCost(id);
            if (!ok)
            {
                if (_match.ReactionApplies(id)) return Loc.Get("ui.reaction.noMorale");
                if (id == CardId.SovereignsGuard && pc.Defender.Type == UnitType.Sovereign) return Loc.Get("ui.reaction.noGuard");
                return Loc.Get("ui.reaction.no." + id);
            }
            return id switch
            {
                CardId.OrganizedRetreat => Loc.Format("ui.reaction.retreat", _match.RetreatSquare(pc.Defender), cost),
                CardId.Countercharge => Loc.Format("ui.reaction.countercharge", _match.CounterchargeSquare(pc.Defender), cost),
                CardId.SovereignsGuard or CardId.HeroicInterposition
                    => Loc.Format("ui.reaction.guard", Loc.UnitDot(_match.GuardFor(pc.Defender)), cost),
                _ => Loc.Format("ui.reaction.block", cost),
            };
        }

        void BuildForesight()
        {
            var options = _match.ForesightOptions;
            var source = _match.ForesightCard;
            int n = options.Count;
            _foresightPick = Mathf.Clamp(_foresightPick, 0, n - 1);
            const float w = 1080f, h = 804f, boxGap = 26f, boxY = 242f, areaX = 40f, areaWidth = w - areaX * 2f;
            const float scrollTop = 28f, viewHeight = 438f;
            int columns = Mathf.Min(n, 4), rows = (n + columns - 1) / columns;
            float available = rows > 1 ? areaWidth - 30f : areaWidth;
            float boxWidth = Mathf.Min(272f, (available - (columns - 1) * boxGap) / columns);
            float scale = (boxWidth - 24f) / Ui.CardWidth;
            float boxHeight = Ui.CardHeight * scale + 62f;

            var panel = OverlayPanel(w, h);
            Eyebrow(panel, Loc.Format("ui.foresight.eyebrow", Loc.CardName(source), Loc.KindName(CardCatalog.Get(source).Kind)), 40f);
            Title(panel, Loc.Get("ui.foresight.title"), 72f, 52f);
            string lead = source == CardId.OracleEcho ? Loc.Get("ui.foresight.leadEcho")
                : n == 1 ? Loc.Get("ui.foresight.leadOne") : Loc.Format("ui.foresight.lead", n);
            Ui.Text(panel, lead, Ui.ItalicFont, 23f, Ui.Ink, 0f, 158f, w, 32f, TextAlignmentOptions.Top);

            RectTransform area = panel;
            float top = boxY, areaW = w;
            if (rows > 1)
            {
                var scroll = Ui.Scroll(panel, areaX, boxY - scrollTop, areaWidth, viewHeight);
                area = scroll.content;
                area.sizeDelta = new Vector2(0f, scrollTop + rows * (boxHeight + boxGap));
                area.anchoredPosition = new Vector2(0f, _foresightScroll);
                scroll.onValueChanged.AddListener(_ => _foresightScroll = area.anchoredPosition.y);
                top = scrollTop;
                areaW = areaWidth - 18f;
            }

            float left = (areaW - (columns * boxWidth + (columns - 1) * boxGap)) * 0.5f;
            for (int i = 0; i < n; i++)
            {
                var info = CardCatalog.Get(options[i]);
                bool chosen = i == _foresightPick;
                float x = left + i % columns * (boxWidth + boxGap);
                float y = top + i / columns * (boxHeight + boxGap) - (chosen && rows == 1 ? 12f : 0f);
                if (chosen) Ui.Sliced(area, "Glow", Ui.Glow, x - 4f, y - 4f, boxWidth + 8f, boxHeight + 8f);
                var box = Ui.Sliced(area, "Pick", chosen ? Ui.Selection : Ui.Solid, x, y, boxWidth, boxHeight, raycast: true);
                if (!chosen) box.color = Color.clear;
                Ui.Card(box.transform, info, scale).anchoredPosition = new Vector2(12f, -12f);
                Ui.Text(box.transform, Ui.SmallCaps(Loc.KindName(info.Kind)) + " · " + Loc.Format("ui.cell.moraleCost", info.Cost),
                    Ui.BodyFont, 18f, Ui.InkText, 0f, boxHeight - 38f, boxWidth, 24f, TextAlignmentOptions.Top);
                if (chosen)
                {
                    var badge = Ui.Sliced(box.transform, "Badge", Ui.Pill(Ui.Rubric, Ui.Rubric, 14, 0f), 0f, -16f, 0f, 28f);
                    var text = Ui.Text(badge.transform, Loc.Get("ui.foresight.chosen"), Ui.TitleFont, 16f, Ui.Cream,
                        14f, 0f, 0f, 28f, TextAlignmentOptions.MidlineLeft);
                    text.characterSpacing = 10f;
                    Fit(text);
                    float bw = text.rectTransform.sizeDelta.x + 28f;
                    badge.rectTransform.sizeDelta = new Vector2(bw, 28f);
                    badge.rectTransform.anchoredPosition = new Vector2((boxWidth - bw) * 0.5f, 16f);
                }
                var button = box.gameObject.AddComponent<Button>();
                button.transition = Selectable.Transition.None;
                int index = i;
                button.onClick.AddListener(() =>
                {
                    _foresightPick = index;
                    RefreshOverlay();
                });
            }

            if (n > 1 && source != CardId.OracleEcho)
                Ui.Text(panel, n == 2 ? Loc.Get("ui.foresight.restOne") : Loc.Format("ui.foresight.rest", n - 1),
                    Ui.BodyFont, 19f, Ui.Ink, 0f, 664f, w, 26f, TextAlignmentOptions.Top);
            string action = source switch
            {
                CardId.ScoutsAhead => "ui.foresight.keep",
                CardId.OracleEcho => "ui.foresight.recover",
                _ => "ui.foresight.buy",
            };
            Row(panel, 707f, 0f, FitButton(panel, Loc.Format(action, Loc.CardName(options[_foresightPick])), true,
                58f, 22f, 30f, OnForesight));
        }

        void BuildDiscard()
        {
            const float w = 1300f, h = 646f, padding = 56f, gridY = 151f, viewHeight = 365f;
            const float itemWidth = 198f, itemHeight = 360f, columnGap = 28f, rowGap = 28f;
            var panel = OverlayPanel(w, h);

            var close = Ui.Sliced(panel, "Close", Ui.Pill(Color.clear, Ui.Ink, 26, 1.5f), w - 74f, 22f, 52f, 52f, raycast: true);
            Ui.MakeButton(close, CloseDiscard);
            Cross(close.transform, 16f, 16f, 20f, Ui.Ink, 2f);

            var eyebrow = Ui.Label(panel, Loc.Get("ui.discard.eyebrow"), padding, 44f, 500f, 26f);
            eyebrow.fontSize = 18f;
            eyebrow.characterSpacing = 20f;
            Ui.Text(panel, Ui.Initial(Loc.Get("ui.discard.title"), 122), Ui.TitleFont, 52f, Ui.InkText, padding, 64f, 500f, 70f);

            var tabs = new[] { _human, _ai.Side };
            var tabButtons = new RectTransform[tabs.Length];
            for (int i = 0; i < tabs.Length; i++)
            {
                var side = tabs[i];
                bool selected = side == _discardSide;
                string label = Loc.Format(side == _human ? "ui.discard.yours" : "ui.discard.opponent",
                    _match.Get(side).Discard.Count);
                var tab = Ui.Image(panel, "Tab", Ui.Solid, 0f, 73f, 0f, 50f, raycast: true);
                tab.color = selected ? Ui.Ink : Color.clear;
                var text = Ui.Text(tab.transform, label, Ui.TitleFont, 20f, selected ? Ui.Parchment : Ui.InkText, 22f, 0f,
                    0f, 50f, TextAlignmentOptions.MidlineLeft);
                text.characterSpacing = 4f;
                Fit(text);
                tab.rectTransform.sizeDelta = new Vector2(text.rectTransform.sizeDelta.x + 44f, 50f);
                var button = tab.gameObject.AddComponent<Button>();
                button.transition = Selectable.Transition.None;
                button.onClick.AddListener(() =>
                {
                    _discardSide = side;
                    RefreshOverlay();
                });
                tabButtons[i] = tab.rectTransform;
            }
            float tabsRight = w - padding - 72f, tabX = tabsRight;
            foreach (var tab in tabButtons) tabX -= tab.sizeDelta.x;
            Ui.Sliced(panel, "Tabs", Ui.Outline, tabX, 73f, tabsRight - tabX, 50f);
            for (int i = 0; i < tabButtons.Length; i++)
            {
                tabButtons[i].anchoredPosition = new Vector2(tabX, -73f);
                if (i > 0) Ui.Line(panel, tabX, 73f, 1.5f, 50f);
                tabX += tabButtons[i].sizeDelta.x;
            }

            var history = DiscardHistory(_discardSide);
            if (history.Count == 0)
                Ui.Text(panel, Loc.Get("ui.discard.empty"), Ui.ItalicFont, 22f, Ui.InkBody, 0f, gridY + 150f, w, 40f,
                    TextAlignmentOptions.Top);
            else
            {
                var scroll = Ui.Scroll(panel, padding, gridY, w - padding * 2f, viewHeight);
                var content = scroll.content;
                int rows = (history.Count + DiscardColumns - 1) / DiscardColumns;
                content.sizeDelta = new Vector2(0f, rows * itemHeight + (rows - 1) * rowGap);
                float gridWidth = DiscardColumns * itemWidth + (DiscardColumns - 1) * columnGap;
                float left = (w - padding * 2f - 18f - gridWidth) * 0.5f;
                for (int i = 0; i < history.Count; i++)
                {
                    var (card, turn, note) = history[i];
                    float x = left + i % DiscardColumns * (itemWidth + columnGap), y = i / DiscardColumns * (itemHeight + rowGap);
                    Ui.Card(content, CardCatalog.Get(card), ModalCardScale).anchoredPosition = new Vector2(x, -y);
                    var when = Ui.Text(content, Loc.Format("ui.discard.turn", Ui.Roman(turn)), Ui.TitleFont, 16f, Ui.Rubric, x,
                        y + 290f, itemWidth, 22f, TextAlignmentOptions.Top);
                    when.characterSpacing = 10f;
                    Ui.Text(content, note, Ui.BodyFont, 18f, Ui.InkText, x - 8f, y + 312f, itemWidth + 16f, 46f,
                        TextAlignmentOptions.Top);
                }
            }

            Ui.Line(panel, padding, 532f, w - padding * 2f, 1f, 0.4f);
            var owner = _match.Get(_discardSide);
            Ui.Text(panel, Loc.Format("ui.discard.footer", owner.Deck.Count, owner.Discard.Count), Ui.BodyFont, 19f, Ui.Ink,
                padding, 566f, 600f, 28f);
            var back = FitButton(panel, Loc.Get("ui.discard.back"), false, 56f, 22f, 28f, CloseDiscard);
            var backRt = (RectTransform)back.transform;
            backRt.anchoredPosition = new Vector2(w - padding - backRt.sizeDelta.x, -553f);
            var hint = Fit(Ui.Text(panel, Loc.Get("ui.discard.close"), Ui.ItalicFont, 17f, Ui.InkBody, 0f, 568f, 0f, 26f));
            hint.rectTransform.anchoredPosition =
                new Vector2(backRt.anchoredPosition.x - 16f - hint.rectTransform.sizeDelta.x, -568f);
        }

        List<(CardId card, int turn, string note)> DiscardHistory(Side side)
        {
            var result = new List<(CardId, int, string)>();
            var log = _match.Log;
            for (int i = 0; i < log.Count; i++)
            {
                var e = log[i];
                if (e.Side == side && (e.Kind == LogKind.CardPlayed || e.Kind == LogKind.Reacted))
                    result.Add((e.Card, e.Turn, DiscardNote(i)));
            }
            result.Reverse();
            return result;
        }

        string DiscardNote(int index)
        {
            var log = _match.Log;
            var e = log[index];
            var next = index + 1 < log.Count ? log[index + 1] : null;
            if (e.Kind == LogKind.Reacted)
            {
                var declared = FindDeclared(index);
                string target = Loc.UnitDot(declared.OtherUnit, declared.To);
                for (int k = index + 1; k < log.Count; k++)
                    switch (log[k].Kind)
                    {
                        case LogKind.Blocked: return Loc.Format("ui.note.blocked", target);
                        case LogKind.Retreated: return Loc.Format("ui.note.retreat", Loc.UnitName(declared.OtherUnit), log[k].To);
                        case LogKind.Intercepted: return Loc.Format("ui.note.guard", declared.To);
                        case LogKind.UnitCaptured: return Loc.Format("ui.note.failed", log[k].To);
                    }
                return "";
            }
            var kind = CardCatalog.Get(e.Card).Kind;
            return e.Card switch
            {
                _ when kind == CardKind.Equipment => Loc.Format("ui.note.equip", Loc.UnitDot(e.Unit, e.To)),
                _ when kind == CardKind.Condition => Loc.Get("ui.note.weather"),
                _ when kind == CardKind.Fortification && next != null => Loc.Format("ui.note.fortified", next.To),
                _ when Match.DeclaresCapture(e.Card) && next != null
                    => Loc.Format("ui.note.volley", Loc.UnitDot(next.OtherUnit, next.To)),
                CardId.ExtraRations => Loc.Format("ui.note.rations", Loc.UnitName(e.Unit)),
                CardId.ForcedMarch => Loc.Get("ui.note.march"),
                CardId.SeersOmen => Loc.Get("ui.note.omen"),
                CardId.CavalryFeint => Loc.Format("ui.note.feint", Loc.UnitDot(e.Unit, e.To)),
                CardId.LineSwap => Loc.Format("ui.note.swap", e.To),
                CardId.CoordinatedAdvance => Loc.Get("ui.note.advance"),
                CardId.ScoutsAhead => Loc.Get("ui.note.scouts"),
                CardId.SupplyWagon => Loc.Get("ui.note.wagon"),
                CardId.OracleEcho => Loc.Get("ui.note.echo"),
                CardId.DawnPrayer when next?.Kind == LogKind.WeatherCleared => Loc.Format("ui.note.cleared", Loc.CardName(next.Card)),
                CardId.DawnPrayer => Loc.Get("ui.note.drew"),
                _ => "",
            };
        }

        LogEntry FindDeclared(int index)
        {
            var log = _match.Log;
            for (int k = index; k >= 0; k--)
                if (log[k].Kind == LogKind.CaptureDeclared) return log[k];
            return log[index];
        }

        void BuildGameOver()
        {
            var winner = _match.Winner ?? Side.White;
            var loser = winner.Other();
            bool won = winner == _human;
            LogEntry fall = null;
            int captures = 0, saves = 0, played = 0;
            foreach (var e in _match.Log)
                switch (e.Kind)
                {
                    case LogKind.UnitCaptured:
                        if (e.Unit == UnitType.Sovereign) fall = e;
                        else if (e.Side != _human) captures++;
                        break;
                    case LogKind.Blocked when e.Side == _human && !e.Flag:
                    case LogKind.Retreated when e.Side == _human:
                        saves++;
                        break;
                    case LogKind.CardPlayed when e.Side == _human:
                    case LogKind.Reacted when e.Side == _human:
                        played++;
                        break;
                }

            const float w = 980f, h = 657f, statsY = 421f, statsHeight = 104f, statsX = 56f;
            var panel = OverlayPanel(w, h);
            Eyebrow(panel, Loc.Get("ui.gameOver.header"), 48f);
            const float token = 110f;
            Ui.Image(panel, "Sovereign", Ui.UnitArt(UnitType.Sovereign, loser), (w - token) * 0.5f, 90f, token, token);
            Cross(panel, (w - token) * 0.5f, 90f, token, Ui.Rubric, 5f);
            Ui.Text(panel, Loc.Get(won ? "ui.gameOver.victory" : "ui.gameOver.defeat"), Ui.FrakturFont, 120f,
                won ? Ui.Rubric : Ui.Ink, 0f, 208f, w, 150f, TextAlignmentOptions.Top);
            if (fall != null)
                Ui.Text(panel, won
                        ? Loc.Format("ui.gameOver.won", Loc.SideName(loser), fall.To, Ui.Roman(fall.Turn))
                        : Loc.Format("ui.gameOver.lost", fall.To, Ui.Roman(fall.Turn)),
                    Ui.ItalicFont, 26f, Ui.Ink, 0f, 368f, w, 36f, TextAlignmentOptions.Top);

            var stats = new[]
            {
                (Ui.Roman(_match.Turn), Loc.Get("ui.gameOver.turns")),
                (captures.ToString(), Loc.Get("ui.gameOver.captures")),
                (saves.ToString(), Loc.Get("ui.gameOver.saves")),
                (played.ToString(), Loc.Get("ui.gameOver.cards")),
            };
            float statWidth = (w - statsX * 2f) / stats.Length;
            Ui.Line(panel, statsX, statsY, w - statsX * 2f, 1f, 0.4f);
            Ui.Line(panel, statsX, statsY + statsHeight, w - statsX * 2f, 1f, 0.4f);
            for (int i = 0; i < stats.Length; i++)
            {
                float x = statsX + statWidth * i;
                if (i > 0) Ui.Line(panel, x, statsY, 1f, statsHeight, 0.4f);
                var (value, caption) = stats[i];
                Ui.Text(panel, value, Ui.TitleFont, 40f, Ui.InkText, x, statsY + 16f, statWidth, 46f, TextAlignmentOptions.Top);
                Ui.Text(panel, caption, Ui.BodyFont, 18f, Ui.Ink, x + 10f, statsY + 66f, statWidth - 20f, 26f,
                    TextAlignmentOptions.Top);
            }

            Row(panel, 551f, 14f,
                FitButton(panel, Loc.Get("ui.gameOver.button"), true, 58f, 22f, 30f, NewMatch),
                FitButton(panel, Loc.Get("ui.gameOver.review"), false, 58f, 22f, 30f, () =>
                {
                    _reviewing = true;
                    Refresh();
                }),
                Disabled(FitButton(panel, Loc.Get("ui.gameOver.menu"), false, 58f, 22f, 30f, () => { })));
        }

        void BuildPause()
        {
            const float w = 580f, h = 564f, buttonX = 52f, buttonY = 214f, step = 68f;
            var panel = OverlayPanel(w, h);
            Eyebrow(panel, Loc.Format("ui.pause.eyebrow", Ui.Roman(_match.Turn)), 44f);
            Title(panel, Loc.Get("ui.pause.title"), 84f, 52f);
            float bw = w - buttonX * 2f;
            Ui.Button(panel, Loc.Get("ui.pause.resume"), true, buttonX, buttonY, bw, 56f, () =>
            {
                SetPaused(false);
                Refresh();
            }, 22f);
            Disabled(Ui.Button(panel, Loc.Get("ui.pause.rules"), false, buttonX, buttonY + step, bw, 56f, () => { }, 22f));
            Disabled(Ui.Button(panel, Loc.Get("ui.pause.settings"), false, buttonX, buttonY + step * 2f, bw, 56f, () => { }, 22f));
            Ui.Danger(Ui.Button(panel, Loc.Get("ui.pause.abandon"), false, buttonX, buttonY + step * 3f, bw, 56f, () =>
            {
                _confirmAbandon = true;
                RefreshOverlay();
            }, 22f));
            Ui.Text(panel, Loc.Get("ui.pause.hint"), Ui.ItalicFont, 18f, Ui.InkBody, 0f, 496f, w, 26f, TextAlignmentOptions.Top);
        }

        void BuildConfirm()
        {
            const float w = 760f, h = 318f;
            var panel = OverlayPanel(w, h);
            Eyebrow(panel, Loc.Get("ui.confirm.eyebrow"), 44f).color = Ui.Rubric;
            Title(panel, Loc.Get("ui.confirm.title"), 84f, 44f);
            Ui.Text(panel, Loc.Get("ui.confirm.text"), Ui.BodyFont, 22f, Ui.InkText, 0f, 160f, w, 32f, TextAlignmentOptions.Top);
            Row(panel, 217f, 14f,
                FitButton(panel, Loc.Get("ui.confirm.stay"), false, 58f, 22f, 30f, () =>
                {
                    _confirmAbandon = false;
                    RefreshOverlay();
                }),
                FitButton(panel, Loc.Get("ui.confirm.abandon"), true, 58f, 22f, 30f, NewMatch));
        }

        void ShowTurnBanner(LogEntry e)
        {
            const float height = 128f, token = 60f;
            var previous = _turnBanner;
            _turnBanner = null;
            if (previous != null) DismissToast(previous);
            var toast = _turnBanner = Toast(height, dark: true);
            bool human = e.Side == _human;
            var eyebrow = Ui.Label(toast, Loc.Format("ui.toast.turn", Ui.Roman(e.Turn)), 0f, 18f, ToastWidth, 22f,
                TextAlignmentOptions.Top);
            eyebrow.fontSize = 16f;
            eyebrow.characterSpacing = 20f;
            eyebrow.color = Ui.Table;
            var title = Ui.Text(toast, Loc.Format("ui.toast.turnOf", Loc.Army(e.Side)), Ui.TitleFont, 38f, Ui.Parchment, 0f, 42f,
                ToastWidth, 44f, TextAlignmentOptions.Top);
            Ui.Text(toast, human
                    ? Loc.Format("ui.toast.you", e.Amount, Loc.Get(e.Flag ? "log.cardDrawn" : "log.deckEmpty"))
                    : Loc.Get("ui.toast.ai"),
                Ui.ItalicFont, 19f, new Color(0.89f, 0.83f, 0.69f), 0f, 88f, ToastWidth, 26f, TextAlignmentOptions.Top);
            float half = title.GetPreferredValues(title.text).x * 0.5f + 26f;
            foreach (float side in new[] { -1f, 1f })
                Ui.Image(toast, "Sovereign", Ui.UnitArt(UnitType.Sovereign, e.Side),
                    ToastWidth * 0.5f + side * (half + token * 0.5f) - token * 0.5f, (height - token) * 0.5f, token, token);
        }

        void ShowResult(int index)
        {
            var log = _match.Log;
            var e = log[index];
            var declared = FindDeclared(index);
            CardId? reaction = null;
            int paid = 0;
            for (int k = index - 1; k >= 0 && log[k] != declared; k--)
                if (log[k].Kind == LogKind.Reacted)
                {
                    reaction = log[k].Card;
                    paid = log[k].Amount;
                }
            Side attackerSide = declared.Side, defenderSide = attackerSide.Other();
            string attacker = Loc.UnitName(declared.Unit), defender = Loc.UnitName(declared.OtherUnit);
            string title, text;
            var chips = new List<string>();
            var toast = Toast(188f, dark: false);
            const float visualX = 28f, visual = 100f;
            float visualY = (188f - visual) * 0.5f;

            switch (e.Kind)
            {
                case LogKind.Blocked:
                    title = Loc.Get("ui.toast.blocked");
                    text = e.Flag
                        ? Loc.Format("ui.toast.blockedByGear", defender, declared.To)
                        : Loc.Format("ui.toast.blockedByCard", Loc.CardName(e.Card), defender, declared.To);
                    chips.Add(Loc.Format("ui.chip.attackerStays", declared.From));
                    chips.Add(Loc.Format("ui.chip.order", Loc.SideName(attackerSide)));
                    chips.Add(e.Flag ? Loc.Get("ui.chip.gearSpent")
                        : Loc.Format("ui.chip.morale", Loc.SideName(defenderSide), paid));
                    if (e.Flag) Ui.Image(toast, "Shield", Ui.Art("Badge_Shield"), visualX + 14f, visualY + 14f, 72f, 72f);
                    else
                        Ui.Card(toast, CardCatalog.Get(e.Card), 89f / Ui.CardWidth).anchoredPosition =
                            new Vector2(visualX + 5f, -(188f - 125f) * 0.5f);
                    break;
                case LogKind.Retreated:
                    title = Loc.CardName(e.Card);
                    text = e.Card == CardId.Countercharge ? Loc.Format("ui.toast.countercharge", defender, declared.To, e.To)
                        : e.Flag ? Loc.Format("ui.toast.retreatRanged", defender, declared.To, e.To)
                        : Loc.Format("ui.toast.retreat", defender, declared.To, e.To, attacker);
                    chips.Add(Loc.Get("ui.chip.noLoss"));
                    if (!e.Flag) chips.Add(Loc.Format("ui.chip.ground", Loc.SideName(defenderSide)));
                    chips.Add(Loc.Format("ui.chip.morale", Loc.SideName(defenderSide), paid));
                    Ui.Image(toast, "Token", Ui.UnitArt(declared.OtherUnit, defenderSide), visualX + 12f, visualY + 18f, 76f, 76f);
                    Arrow(toast, visualX + 22f, visualY + 6f, 56f);
                    break;
                default:
                    title = Loc.Get("ui.toast.captured");
                    text = declared.Flag
                        ? Loc.Format("ui.toast.capturedVolley", attacker, Loc.UnitName(e.Unit), e.To, Loc.CardName(declared.Card))
                        : Loc.Format("ui.toast.capturedText", attacker, Loc.UnitName(e.Unit), e.To);
                    chips.Add(Loc.Format("ui.chip.lost", Loc.SideName(e.Side), Loc.UnitName(e.Unit)));
                    chips.Add(declared.Flag ? Loc.Format("ui.chip.attackerStays", declared.From)
                        : Loc.Format("ui.chip.attackerMoves", e.To));
                    chips.Add(reaction is CardId played ? Loc.Format("ui.chip.reaction", Loc.CardName(played))
                        : Loc.Get("ui.chip.noReaction"));
                    Ui.Image(toast, "Token", Ui.UnitArt(e.Unit, e.Side), visualX + 12f, visualY + 12f, 76f, 76f);
                    Cross(toast, visualX + 12f, visualY + 12f, 76f, Ui.Rubric);
                    break;
            }

            const float textX = 152f, textWidth = ToastWidth - textX - 28f;
            Ui.Text(toast, Loc.Get("ui.toast.dismiss"), Ui.ItalicFont, 15f, Ui.InkBody, ToastWidth - 322f, 16f, 300f, 20f,
                TextAlignmentOptions.TopRight);
            var eyebrow = Ui.Label(toast, Loc.Get("ui.toast.result"), textX, 20f, 320f, 22f);
            eyebrow.fontSize = 16f;
            eyebrow.characterSpacing = 20f;
            Ui.Text(toast, Ui.Initial(title, 120), Ui.TitleFont, 38f, Ui.InkText, textX, 44f, textWidth, 48f);
            Ui.Text(toast, text, Ui.BodyFont, 21f, Ui.InkText, textX, 96f, textWidth, 28f);
            float chipX = textX;
            foreach (var chip in chips) chipX += Chip(toast, chip, chipX, 136f).rectTransform.sizeDelta.x + 8f;
        }

        RectTransform Toast(float height, bool dark)
        {
            var toast = Ui.Rect(_toasts, "Toast", 0f, 0f, ToastWidth, height);
            var group = toast.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            Ui.Modal(toast, 0f, 0f, ToastWidth, height, dark);
            var button = toast.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(() => DismissToast(toast));
            _toastList.Add(toast);
            while (_toastList.Count > MaxToasts) DismissToast(_toastList[0]);
            LayoutToasts();
            StartCoroutine(ToastRoutine(toast, group));
            return toast;
        }

        void DismissToast(RectTransform toast)
        {
            if (!_toastList.Remove(toast)) return;
            Destroy(toast.gameObject);
            LayoutToasts();
            // Neither side acts while the turn banner is up; its exit hands the turn over.
            if (toast != _turnBanner) return;
            _turnBanner = null;
            Refresh();
            ScheduleAi();
        }

        void LayoutToasts()
        {
            float y = 0f;
            foreach (var toast in _toastList)
            {
                toast.anchoredPosition = new Vector2(0f, -y);
                y += toast.sizeDelta.y + ToastGap;
            }
        }

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

        IEnumerator ToastRoutine(RectTransform toast, CanvasGroup group)
        {
            for (float t = 0f; t < 1f; t += Time.deltaTime / ToastFade)
            {
                if (toast == null) yield break;
                group.alpha = t;
                yield return null;
            }
            if (toast == null) yield break;
            group.alpha = 1f;
            yield return new WaitForSeconds(ToastLife);
            for (float t = 1f; t > 0f; t -= Time.deltaTime / ToastFade)
            {
                if (toast == null) yield break;
                group.alpha = t;
                yield return null;
            }
            if (toast != null) DismissToast(toast);
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

        Vector2 SquareOrigin(Coord c)
        {
            int column = _flipped ? Match.Size - 1 - c.X : c.X;
            int row = _flipped ? c.Y : Match.Size - 1 - c.Y;
            return new Vector2(Inset + Square * column, -(Inset + Square * row));
        }

        Vector2 SquareCenter(Coord c) => SquareOrigin(c) + new Vector2(Square * 0.5f, -Square * 0.5f);

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
            // A lifted card is drawn over everything on the stage, the new panels included.
            _hand.SetAsLastSibling();
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

            _stage = Ui.Stretch(Ui.Rect(root, "Stage", 0, 0, 0, 0));

            BuildHeader();
            BuildConditions();
            BuildBoard();
            _hand = Ui.Rect(_stage, "Hand", CenterX, HandY, CenterWidth, StageHeight - HandY);
            Ui.Anchor(_hand, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), Stage);
            _handGroup = _hand.gameObject.AddComponent<CanvasGroup>();
            BuildRightColumn();

            _toasts = Ui.Rect(root, "Toasts", ToastX, ToastY, ToastWidth, 0f);
            Ui.Anchor(_toasts, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), Stage);

            _overlay = Ui.Stretch(Ui.Rect(root, "Overlay", 0, 0, 0, 0));
            _overlay.gameObject.AddComponent<Image>().color = ScrimColor;
            var backdrop = Ui.Stretch(Ui.Image(_overlay, "Backdrop", Ui.Solid, 0, 0, 0, 0, raycast: true).rectTransform);
            backdrop.GetComponent<Image>().color = Color.clear;
            backdrop.gameObject.AddComponent<Button>().onClick.AddListener(CloseDiscard);
            _backdrop = backdrop.gameObject;
            _overlayContent = Ui.Rect(_overlay, "Content", 0, 0, StageWidth, StageHeight);
            _overlayContent.anchorMin = _overlayContent.anchorMax = _overlayContent.pivot = new Vector2(0.5f, 0.5f);
            _overlayContent.anchoredPosition = Vector2.zero;
        }

        void BuildHeader()
        {
            var size = new Vector2(StageWidth, HeaderHeight);
            Vector2 left = new Vector2(0f, 1f), center = new Vector2(0.5f, 1f), right = new Vector2(1f, 1f);
            var header = Ui.Anchor(Ui.Rect(_stage, "Header", 0, 0, StageWidth, HeaderHeight), left, right, Stage);
            Ui.Anchor(Ui.Line(header, 0, HeaderHeight - 1.5f, StageWidth, 1.5f).rectTransform, left, right, size);
            Ui.Anchor(Ui.Line(header, 0, HeaderHeight + 2f, StageWidth, 1f).rectTransform, left, right, size);
            Ui.Text(header, Ui.Initial(Loc.Get("ui.title"), 120), Ui.TitleFont, 30f, Ui.InkText, LeftX, 0, LeftWidth,
                HeaderHeight, TextAlignmentOptions.Left);
            _phases = Ui.Text(header, "", Ui.TitleFont, 20f, Ui.InkSoft, CenterX, 0, CenterWidth, HeaderHeight,
                TextAlignmentOptions.Center);
            _phases.characterSpacing = 5f;
            Ui.Anchor(_phases.rectTransform, center, center, size);
            const float buttonWidth = 196f, buttonHeight = 46f;
            _turn = Ui.Text(header, "", Ui.ItalicFont, 20f, Ui.InkBody, RightX, 0, RightWidth - buttonWidth - 16f,
                HeaderHeight, TextAlignmentOptions.Right);
            Ui.Anchor(_turn.rectTransform, right, right, size);
            _endTurn = Ui.Button(header, Loc.Get("ui.endTurn"), false, RightX + RightWidth - buttonWidth,
                (HeaderHeight - buttonHeight) * 0.5f, buttonWidth, buttonHeight, OnEndTurn, 20f);
            Ui.Anchor((RectTransform)_endTurn.transform, right, right, size);
        }

        ArmyPanel BuildArmy(Side side, bool human)
        {
            float height = human ? PlayerPanelHeight : OpponentPanelHeight;
            float y = human ? ColumnBottom - height : ColumnTop;
            var panel = Ui.Panel(_stage, $"Army {side}", LeftX, y, LeftWidth, height).rectTransform;
            if (human) Ui.Anchor(panel, Vector2.zero, Vector2.zero, Stage);
            var ui = new ArmyPanel { Root = panel, Group = panel.gameObject.AddComponent<CanvasGroup>() };

            Ui.Image(panel, "Emblem", Ui.UnitArt(UnitType.Sovereign, side), 20, 18, 50, 50);
            Ui.Text(panel, Loc.Army(side), Ui.TitleFont, 26f, Ui.InkText, 82, 16, 300, 32);
            ui.Subtitle = Ui.Label(panel, "", 82, 48, 300);

            Ui.Label(panel, Loc.Get("ui.morale"), 20, 84, 100);
            ui.Pips = new Image[Match.MaxMorale];
            for (int i = 0; i < ui.Pips.Length; i++)
                ui.Pips[i] = Ui.Image(panel, "Pip", Ui.Art("Pip_Off"), 122 + i * 23, 85, 18, 18);
            ui.Morale = Ui.Text(panel, "", Ui.BodyFont, 17f, Ui.InkBody, 300, 81, 80, 24, TextAlignmentOptions.TopRight);

            float tilesY;
            if (human)
            {
                ui.Orders = Ui.Label(panel, "", 20, 130, 220);
                ui.OrderIcons = new Image[Match.OrdersPerTurn + 2];
                for (int i = 0; i < ui.OrderIcons.Length; i++)
                    ui.OrderIcons[i] = Ui.Image(panel, "Order", Ui.Art("Order_Off"), 0, 120, 44, 40);
                Ui.Label(panel, Loc.Format("ui.reserveHeader", Loc.Get("ui.ranks." + side)), 20, 176, 220);
                ui.ReserveHint = Ui.Text(panel, "", Ui.BodyFont, 15f, Ui.InkBody, 200, 175, 180, 22,
                    TextAlignmentOptions.TopRight);
                tilesY = 209f;
            }
            else
            {
                ui.Summary = Ui.Text(panel, "", Ui.BodyFont, 17f, Ui.InkBody, 20, 120, 360, 24);
                tilesY = 158f;
            }
            BuildReserve(panel, ui, side, human, tilesY);
            return ui;
        }

        void BuildReserve(RectTransform panel, ArmyPanel ui, Side side, bool human, float y)
        {
            int n = Match.Recruitable.Length;
            const int columns = 3;
            const float gap = 8f, height = 52f, textX = 49f;
            float w = (LeftWidth - 40f - gap * (columns - 1)) / columns;
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
                float x = 20f + i % columns * (w + gap), top = y + i / columns * (height + gap);
                var tile = Ui.Image(panel, "Reserve " + type, Ui.Tile, x, top, w, height, raycast: human);
                tile.type = Image.Type.Sliced;
                Ui.Image(tile.transform, "Icon", Ui.UnitArt(type, side), 6, 8, 36, 36);
                Ui.Text(tile.transform, Loc.UnitName(type), Ui.BodyFont, 16f, Ui.InkText, textX, 6, w - textX - 3f, 22)
                    .textWrappingMode = TextWrappingModes.NoWrap;

                ui.ReserveCounts[i] = Ui.Text(tile.transform, "", Ui.BodyFont, 15f, Ui.InkText, textX, 27, 22, 19);
                if (!human) continue;
                Ui.Seal(tile.transform, Match.Cost(type), textX + 22f, 26, 19);
                ui.RecruitGroups[i] = tile.gameObject.AddComponent<CanvasGroup>();
                ui.RecruitButtons[i] = Ui.MakeButton(tile, () => OnRecruitClicked(side, type));
                var highlight = Ui.Image(tile.transform, "Chosen", Ui.Highlight, -2, -2, w + 4, height + 4);
                highlight.type = Image.Type.Sliced;
                highlight.enabled = false;
                ui.RecruitHighlights[i] = highlight;
            }
        }

        void BuildConditions()
        {
            float y = ColumnTop + OpponentPanelHeight + Gap;
            float height = ColumnBottom - PlayerPanelHeight - Gap - y;
            var panel = Ui.Panel(_stage, "Conditions Panel", LeftX, y, LeftWidth, height).rectTransform;
            Ui.Anchor(panel, Vector2.zero, Vector2.up, Stage);
            Ui.Label(panel, Loc.Get("ui.conditions.header"), 20, 18, 220).color = Ui.Rubric;
            _conditionsState = Ui.Label(panel, "", 180, 18, 200, align: TextAlignmentOptions.TopRight);
            _conditions = Ui.Rect(panel, "Conditions", 0, 48, LeftWidth, height - 66f);
            Ui.Anchor(_conditions, Vector2.zero, Vector2.up, new Vector2(LeftWidth, height));
        }

        void RefreshConditions()
        {
            for (int i = _conditions.childCount - 1; i >= 0; i--) Destroy(_conditions.GetChild(i).gameObject);
            var weather = _match.Weather;
            _conditionsState.text = Loc.Get(weather.HasValue ? "ui.conditions.active" : "ui.conditions.none");
            if (!(weather is CardId active))
            {
                var empty = Ui.Text(_conditions, Loc.Get("ui.conditions.empty"), Ui.ItalicFont, 19f, Ui.InkBody, 40, 0,
                    LeftWidth - 80f, 0f, TextAlignmentOptions.Center);
                Ui.Stretch(empty.rectTransform).offsetMin = new Vector2(40f, 0f);
                empty.rectTransform.offsetMax = new Vector2(-40f, 0f);
                return;
            }

            const float scale = 0.6f, x = 20f, textX = x + Ui.CardWidth * scale + 16f;
            Ui.Card(_conditions, CardCatalog.Get(active), scale).anchoredPosition = new Vector2(x, 0f);
            float textWidth = LeftWidth - textX - 20f;
            Ui.Text(_conditions, Ui.Initial(Loc.CardName(active), 120), Ui.TitleFont, 22f, Ui.InkText, textX, 0f, textWidth, 56f);
            string detail = Loc.Get("ui.conditions.lasting");
            foreach (var m in _match.Markers)
                if (m.Kind == MarkerKind.Lightning) detail += "\n\n" + Loc.Format("ui.conditions.lightning", m.Pos);
            Ui.Text(_conditions, detail, Ui.BodyFont, 16f, Ui.InkBody, textX, 62f, textWidth, 150f);
        }

        void FitBoard(Vector2 canvas)
        {
            float width = canvas.x - (StageWidth - CenterWidth);
            float height = canvas.y - ColumnTop - BoardClearance;
            float scale = Mathf.Min(width / BoardSize, height / (BoardSize - BoardMargin));
            float centerOffset = CenterX + CenterWidth * 0.5f - StageWidth * 0.5f;
            _board.localScale = new Vector3(scale, scale, 1f);
            _board.anchoredPosition =
                new Vector2(centerOffset - BoardSize * scale * 0.5f, -(ColumnTop - BoardMargin * scale));
        }

        void BuildBoard()
        {
            var board = _board = Ui.Image(_stage, "Board", Ui.Art("Board"), 0, 0, BoardSize, BoardSize).rectTransform;
            board.anchorMin = board.anchorMax = new Vector2(0.5f, 1f);
            FitBoard(Stage);

            for (int i = 0; i < Match.Size; i++)
            {
                float offset = Inset + Square * i;
                _fileLabels[i] = Ui.Text(board, "", Ui.TitleFont, 15f, Ui.InkSoft, offset, 674, Square, 24,
                    TextAlignmentOptions.Center);
                _rankLabels[i] = Ui.Text(board, "", Ui.TitleFont, 15f, Ui.InkSoft, 0, offset, 24, Square,
                    TextAlignmentOptions.Center);
            }

            var under = Ui.Rect(board, "Underlay", 0, 0, BoardSize, BoardSize);
            _markerLayer = Ui.Rect(board, "Markers", 0, 0, BoardSize, BoardSize);
            _trailLayer = Ui.Rect(board, "Trails", 0, 0, BoardSize, BoardSize);
            _previewLayer = Ui.Rect(board, "Preview", 0, 0, BoardSize, BoardSize);
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
                    OnHover(hit.gameObject, () => HoverSquare(c, true), () => HoverSquare(c, false));
                    _hint[x, y] = Ui.Image(hit.transform, "Hint", null, 0, 0, Square, Square);
                }
        }

        void BuildRightColumn()
        {
            const float w = RightWidth;
            var column = Ui.Rect(_stage, "Right Column", RightX, 0f, w, StageHeight);
            Ui.Anchor(column, Vector2.right, Vector2.one, Stage);
            _rightColumn = column.gameObject.AddComponent<CanvasGroup>();

            var halo = Ui.Image(column, "Halo", Ui.Solid, -4f, ColumnTop - 4f, w + 8f, ActionPanelHeight + 8f);
            halo.color = new Color(Ui.Rubric.r, Ui.Rubric.g, Ui.Rubric.b, 0.22f);
            var action = Ui.Panel(column, "Action Panel", 0f, ColumnTop, w, ActionPanelHeight).rectTransform;
            const float bandHeight = 44f;
            var band = Ui.Image(action, "Band", Ui.Solid, 2f, 2f, w - 4f, bandHeight);
            band.color = Ui.Rubric;
            var bandLabel = Ui.Label(band.transform, Loc.Get("ui.action.header"), 18f, 0f, 200f, bandHeight,
                TextAlignmentOptions.MidlineLeft);
            bandLabel.fontSize = 17f;
            bandLabel.characterSpacing = 16f;
            bandLabel.color = Ui.Cream;
            _actionStep = Ui.Text(band.transform, "", Ui.ItalicFont, 18f, Ui.Cream, 190f, 0f, w - 212f, bandHeight,
                TextAlignmentOptions.MidlineRight);
            float cardWidth = Ui.CardWidth * ActionCardScale, cardHeight = Ui.CardHeight * ActionCardScale;
            float cardY = 2f + bandHeight + 14f;
            _actionSlot = Ui.Rect(action, "Subject", (w - cardWidth) * 0.5f, cardY, cardWidth, cardHeight);
            float cellsY = cardY + cardHeight + 14f;
            _actionCells = Ui.Rect(action, "Cells", 20f, cellsY, w - 40f, CellsHeight);
            float hintY = cellsY + CellsHeight + 8f;
            _actionHint = Ui.Text(action, "", Ui.ItalicFont, 17f, Ui.InkBody, 20f, hintY, w - 40f, 44f,
                TextAlignmentOptions.Top);
            float buttonsY = hintY + 50f;
            const float cancelWidth = 130f;
            _actionPrimary = Ui.Button(action, Loc.Get("ui.action.playCard"), true, 20f, buttonsY,
                w - 50f - cancelWidth, 52f, OnActionPrimary, 20f);
            _actionCancel = Ui.Button(action, Loc.Get("ui.action.cancel"), false, w - 20f - cancelWidth, buttonsY,
                cancelWidth, 52f, () => { ClearMode(); Refresh(); }, 20f);

            float deckY = ColumnTop + ActionPanelHeight + Gap;
            var deck = Ui.Panel(column, "Deck Panel", 0f, deckY, w, DeckPanelHeight).rectTransform;
            const float backWidth = 58f, backHeight = 81f;
            for (int i = 2; i >= 0; i--)
                Ui.Image(deck, "Back", Ui.Art("CardBack"), 18f + i * 4f, 15f + i * 4f, backWidth, backHeight);
            const float deckTextX = 96f, discardX = 227f, discardTextX = 313f;
            Ui.Label(deck, Loc.Get("ui.deck"), deckTextX, 18f, 110f).textWrappingMode = TextWrappingModes.NoWrap;
            _deck = Ui.Text(deck, "", Ui.TitleFont, 24f, Ui.InkText, deckTextX, 40f, 110f, 30f);
            _deckCaption = Ui.Text(deck, "", Ui.BodyFont, 15f, Ui.InkBody, deckTextX, 72f, 110f, 22f);
            _discardPile = Ui.Rect(deck, "Discard Pile", discardX, 13f, 74f, 93f);
            float discardTextWidth = w - 18f - discardTextX;
            Ui.Label(deck, Loc.Get("ui.discard"), discardTextX, 18f, discardTextWidth).textWrappingMode =
                TextWrappingModes.NoWrap;
            _discard = Ui.Text(deck, "", Ui.TitleFont, 24f, Ui.InkText, discardTextX, 40f, discardTextWidth, 30f);
            _discardLast = Ui.Text(deck, "", Ui.BodyFont, 15f, Ui.InkBody, discardTextX, 72f, discardTextWidth, 40f);
            var discardHit = Ui.Image(deck, "Open Discard", Ui.Solid, discardX - 6f, 6f, w - discardX, DeckPanelHeight - 12f,
                raycast: true);
            discardHit.color = Color.clear;
            var discardButton = discardHit.gameObject.AddComponent<Button>();
            discardButton.transition = Selectable.Transition.None;
            discardButton.onClick.AddListener(OnDiscardClicked);
            OnHover(discardHit.gameObject, () => _discardPile.anchoredPosition = new Vector2(discardX, -9f),
                () => _discardPile.anchoredPosition = new Vector2(discardX, -13f));

            float historyY = deckY + DeckPanelHeight + Gap;
            float historyHeight = ColumnBottom - historyY;
            var history = Ui.Panel(column, "History Panel", 0f, historyY, w, historyHeight).rectTransform;
            Ui.Anchor(history, Vector2.zero, Vector2.up, new Vector2(w, StageHeight));
            Ui.Label(history, Loc.Get("ui.history"), 20, 16, 140);
            Ui.Label(history, Loc.Get("ui.historyOrder"), 150, 16, w - 170f, align: TextAlignmentOptions.TopRight);
            var scroll = Ui.Scroll(history, 20f, 40f, w - 40f, historyHeight - 54f);
            Ui.Anchor((RectTransform)scroll.transform, Vector2.zero, Vector2.up, new Vector2(w, historyHeight));
            _logEntries = scroll.content;
        }
    }
}
