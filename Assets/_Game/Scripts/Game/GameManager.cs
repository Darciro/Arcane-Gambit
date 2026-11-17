using System.Collections;
using System.Collections.Generic;
using ArcaneGambit.FX;
using ArcaneGambit.Rules;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace ArcaneGambit.Game
{
    public sealed partial class GameManager : MonoBehaviour
    {
        public const string SceneName = "Main";
        const string SandboxSceneName = "FX_Sandbox";

        const float BoardSize = 700f, BoardMargin = 21f;
        const float Inset = 30f, Square = 80f, PieceSize = 70f;
        const float HandVisible = 120f, HandTilt = 3f, SelectedLift = 16f;
        const float HandFootGap = 6f, HandEase = 0.16f, HandShadowAlpha = 0.6f;
        const float ModalCardScale = 0.8f;
        const float WaitingAlpha = 0.5f;
        const float CanvasDistance = 10f;
        const float TrailWidth = 2.4f, TrailDash = 7f, TrailGap = 6f, TrailHead = 9f, PreviewAlpha = 0.6f;
        const float TrailEmptyInset = 12f, TrailClearance = 2f;
        const float ToastWidth = 840f, ToastGap = 24f;
        const float ToastLife = 3f, ToastFade = 0.25f;
        const int MaxToasts = 3, DiscardColumns = 5;
        const float DockWidth = 460f, DockPadding = 24f;
        const float PeekTime = 0.25f, PeekWidth = 300f, PeekHeight = 52f, ConfrontScrimAlpha = 0.58f, SpotAlpha = 0.34f;
        const float OptionHeight = 141f, OptionGap = 14f, OptionCard = 89f, ReactionButtonHeight = 54f;
        static readonly Color OffTint = new Color(0.9f, 0.92f, 0.96f);
        static readonly Color ScrimColor = new Color(0.125f, 0.082f, 0.039f, 0.62f);

        // The layout of the current screen profile, set by ApplyProfile before the HUD is built.
        static UiSize s_size;
        static float StageWidth, StageHeight;
        static Vector2 Stage;
        static float LeftX, LeftWidth, CenterX, CenterWidth, RightX, RightWidth;
        static float HeaderHeight, ColumnTop, ColumnBottom, Gap, PanelPad, BoardClearance;
        static float HandScale, HandY, HandOverlap, HandZoom, ActionCardScale, PlayCardScale, CellsHeight;
        static float OpponentPanelHeight, PlayerPanelHeight, ActionPanelHeight, DeckPanelHeight;

        // The small profile trades detail for room: icon-only reserves, one history line, smaller art.
        static bool Compact => s_size == UiSize.Small;
        // The reaction dock is drawn for the Medium right column, which it covers with a small overhang.
        static float DockScale => (RightWidth + 20f) / DockWidth;

        static void ApplyProfile()
        {
            s_size = UiProfile.Size;
            Stage = UiProfile.Reference(s_size);
            StageWidth = Stage.x;
            StageHeight = Stage.y;
            if (UiProfile.Mobile)
            {
                ApplyMobileProfile();
                return;
            }
            bool small = Compact, large = s_size == UiSize.Large;

            float columnGap = small ? 20f : large ? 36f : 32f;
            LeftX = small ? 24f : large ? 48f : 40f;
            LeftWidth = small ? 280f : large ? 420f : 400f;
            RightWidth = small ? 380f : large ? 470f : 440f;
            RightX = StageWidth - LeftX - RightWidth;
            CenterX = LeftX + LeftWidth + columnGap;
            CenterWidth = RightX - columnGap - CenterX;

            HeaderHeight = small ? 50f : 64f;
            ColumnTop = HeaderHeight + (small ? 8f : 12f);
            ColumnBottom = StageHeight - (small ? 12f : 28f);
            Gap = small ? 10f : 14f;
            PanelPad = small ? 14f : 20f;
            // What the board leaves free below it for the hand: it ends up 650, 880 and 960 wide.
            BoardClearance = small ? 121f : large ? 145f : 150f;

            HandScale = small ? 0.72f : 0.85f;
            HandY = StageHeight - HandVisible;
            HandOverlap = small ? 20f : 12f;
            HandZoom = small ? 1.6f : 1.55f;
            ActionCardScale = PlayCardScale = small ? 0.96f : large ? 1.15f : 1.1f;
            CellsHeight = small ? 64f : 88f;
            OpponentPanelHeight = small ? 177f : 290f;
            PlayerPanelHeight = small ? 210f : 340f;
            ActionPanelHeight = small ? 560f : large ? 682f : 664f;
            DeckPanelHeight = small ? 72f : 120f;
        }

        [SerializeField, Tooltip("0 = random seed for every match")] int _seed;
        [SerializeField, Min(0.01f)] float _moveDuration = 0.22f;
        [SerializeField, Min(0f), Tooltip("Pause before each AI decision, so its moves can be followed")]
        float _aiDelay = 0.7f;
        [SerializeField, Min(0f), Tooltip("How long the AI's next action is shown on the board before it happens")]
        float _aiIntentTime = 1.2f;

        enum Mode { Idle, Unit, Recruit, Card, Inspect }

        sealed class Piece
        {
            public int Id;
            public RectTransform Rt;
            public RectTransform Holder;
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
            public float OrderY, OrderStep, OrderRight;
        }

        sealed class HandCard
        {
            public RectTransform Slot, Holder, Card;
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
        NetSession _net;
        bool _awaitingHost, _netLost;
        bool _awaitingStart;
        Coroutine _aiStep;
        bool _waiting;
        Coroutine _waitingFade;
        int _logSeen, _logShown = -1;
        CardId? _reaction;
        int _foresightPick;
        float _foresightScroll;
        bool _discardOpen, _paused, _confirmAbandon, _reviewing, _historyOpen;
        Side _discardSide;
        bool _peek;
        float _peekShown, _dockX;

        // The banner only delays input; what is unaffordable must already show as disabled while it is up.
        bool HumanOwnsTurn => !_awaitingStart && _match.Phase == Phase.Maneuvers && _match.Current == _human;
        bool HumanTurn => HumanOwnsTurn && _turnBanner == null && _weatherNotices.Count == 0 && !FxLocked
            && !_awaitingHost && !_netLost;
        Side Opponent => _human.Other();
        // A boom (FX_SPEC 5.1) holds the input and the AI until it has played.
        bool FxLocked => _matchFx != null && _matchFx.Locked;

        RectTransform _stage, _piecesLayer, _markerLayer, _trailLayer, _previewLayer, _hand, _overlay, _overlayContent, _toasts;
        RectTransform _focusLayer, _confront, _dock, _worldStage, _boardRig, _overlayNative;
        FxRoot _fx;
        MatchFx _matchFx;
        GraphicRaycaster _worldRaycaster, _hudRaycaster;
        CanvasGroup _spotGroup, _confrontShade;
        Image _overlayShade;
        readonly Image[,] _spot = new Image[Match.Size, Match.Size];
        GameObject _backdrop;
        readonly List<RectTransform> _toastList = new List<RectTransform>();
        RectTransform _turnBanner;
        readonly List<int> _weatherNotices = new List<int>();
        CardId? _conditionView;
        readonly Image[,] _under = new Image[Match.Size, Match.Size];
        readonly Image[,] _hint = new Image[Match.Size, Match.Size];
        readonly TextMeshProUGUI[] _fileLabels = new TextMeshProUGUI[Match.Size];
        readonly TextMeshProUGUI[] _rankLabels = new TextMeshProUGUI[Match.Size];
        bool _flipped;
        readonly Dictionary<int, Piece> _pieces = new Dictionary<int, Piece>();
        readonly Dictionary<(Coord, MarkerKind), RectTransform> _markers = new Dictionary<(Coord, MarkerKind), RectTransform>();
        readonly HashSet<(Coord, Coord)> _trails = new HashSet<(Coord, Coord)>();
        Coord? _hoverSquare;
        readonly ArmyPanel[] _armies = new ArmyPanel[2];
        CanvasScaler _scaler, _worldScaler;
        Vector2 _canvasSize;
        TextMeshProUGUI _phases, _turn, _actionStep, _actionHint, _deck, _deckCaption, _discard, _discardLast;
        RectTransform _actionSlot, _actionCells, _discardPile, _logEntries, _logLatest, _conditions;
        GameObject _historyOpenPanel;
        float _overlayFit = 1f;
        TextMeshProUGUI _conditionsState;
        Button _endTurn, _actionPrimary, _actionCancel;
        OptionsModal _options;
        readonly List<(TextMeshProUGUI text, System.Func<string> get)> _localized =
            new List<(TextMeshProUGUI, System.Func<string>)>();
        CanvasGroup _handGroup, _rightColumn;
        readonly List<HandCard> _handCards = new List<HandCard>();
        int _hoveredCard = -1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            string scene = SceneManager.GetActiveScene().name;
            if ((scene != SceneName && scene != SandboxSceneName) || FindAnyObjectByType<GameManager>() != null) return;
            var host = GameObject.Find("GameManager");
            if (host == null) host = new GameObject("GameManager");
            host.AddComponent<GameManager>();
        }

        void Start()
        {
            Ui.SetCursor(CursorKind.Default);
            BuildUi();
            NewMatch();
            if (Debug.isDebugBuild && SceneManager.GetActiveScene().name == SandboxSceneName)
                gameObject.AddComponent<FxDebugPanel>().Init(this);
        }

        internal Match CurrentMatch => _match;
        internal MatchFx Fx => _matchFx;
        internal FxDirector FxDirector => _fx != null ? _fx.Director : null;
        internal float BoardScale => _boardRig.localScale.x;

        internal Vector3 SquareWorld(Coord c) => _piecesLayer.TransformPoint(SquareCenter(c));

        internal GameObject PieceObject(int unitId) => _pieces.TryGetValue(unitId, out var piece) ? piece.Holder.gameObject : null;

        // Locking can happen halfway through Sync, so it only blocks input; the refresh waits for the unlock,
        // which comes from the director's own Update. Modals keep their raycaster, so a pause still works meanwhile.
        void OnFxLock(bool locked)
        {
            _worldRaycaster.enabled = _hudRaycaster.enabled = !locked;
            Ui.SetCursor(locked ? CursorKind.Wait : CursorKind.Default);
            if (locked) return;
            Refresh();
            ScheduleAi();
        }

        void Update()
        {
            if (UiProfile.Size != s_size || MobileInsetChanged) ChangeProfile();
            var canvasSize = ((RectTransform)_scaler.transform).rect.size;
            if (canvasSize != _canvasSize)
            {
                _canvasSize = canvasSize;
                FitBoard(canvasSize);
                if (_match != null)
                {
                    RefreshHistory(force: true);
                    RefreshOverlay();
                }
            }
            if (_net != null) PumpNet();
            AnimateHand();
            float peek = _peek ? 1f : 0f;
            if (!Mathf.Approximately(_peekShown, peek))
            {
                _peekShown = Mathf.MoveTowards(_peekShown, peek, Time.deltaTime / PeekTime);
                PosePeek();
            }

            // A clicked button keeps UI focus, and the UI module would click it again on Enter or Space.
            var events = EventSystem.current;
            if (events != null && events.currentSelectedGameObject != null) events.SetSelectedGameObject(null);

            var keyboard = Keyboard.current;
            if (_options != null)
            {
                _options.Update(SceneTransition.Busy ? null : keyboard);
                return;
            }
            if (SceneTransition.Busy) return;
            var mouse = Mouse.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame) OnEscape();
            else if (mouse != null && mouse.rightButton.wasPressedThisFrame && _mode != Mode.Idle)
            {
                ClearMode();
                Refresh();
            }
            else if (keyboard != null && !_paused) OnKeys(keyboard);
        }

        void OnKeys(Keyboard keys)
        {
            if (FxLocked) return;
            int step = keys.rightArrowKey.wasPressedThisFrame || keys.downArrowKey.wasPressedThisFrame ? 1
                : keys.leftArrowKey.wasPressedThisFrame || keys.upArrowKey.wasPressedThisFrame ? -1 : 0;
            bool enter = keys.enterKey.wasPressedThisFrame || keys.numpadEnterKey.wasPressedThisFrame;
            bool space = keys.spaceKey.wasPressedThisFrame;
            if (step == 0 && !enter && !space) return;

            if (_match.Phase == Phase.GameOver && !_reviewing)
            {
                if (enter && _net != null) LoadMenu();
                else if (enter) NewMatch();
            }
            else if (_awaitingStart)
            {
                if (enter) OnStartMatch();
            }
            else if (_weatherNotices.Count > 0)
            {
                if (enter || space) DismissWeather();
            }
            else if (_conditionView != null)
            {
                if (enter || space) CloseCondition();
            }
            else if (_match.Phase == Phase.Reaction && _match.Pending.Defender.Side == _human)
            {
                if (step != 0) CycleReaction(step);
                else if (enter && _reaction is CardId card) OnReaction(card);
                else if (space) OnReaction(null);
            }
            else if (_match.Phase == Phase.Foresight && _match.Current == _human)
            {
                int columns = Mathf.Min(_match.ForesightOptions.Count, 4);
                bool vertical = keys.upArrowKey.wasPressedThisFrame || keys.downArrowKey.wasPressedThisFrame;
                if (step != 0)
                {
                    _foresightPick = Mathf.Clamp(_foresightPick + step * (vertical ? columns : 1), 0,
                        _match.ForesightOptions.Count - 1);
                    RefreshOverlay();
                }
                else if (enter) OnForesight();
            }
            else if (_discardOpen) { }
            else if (space) OnEndTurn();
            else if (!HumanTurn) { }
            else if (enter) OnActionPrimary();
            else CycleCard(step);
        }

        void CycleCard(int step)
        {
            int count = _match.Get(_human).Hand.Count;
            if (count == 0) return;
            int from = _mode == Mode.Card ? _cardIndex : step > 0 ? -1 : count;
            int next = (from + step + count) % count;
            if (_mode != Mode.Card || next != _cardIndex) OnCardClicked(next);
        }

        void CycleReaction(int step)
        {
            var applicable = _match.ReactionOptions();
            var order = new List<CardId>();
            foreach (var id in _match.Get(_human).Hand)
                if (applicable.Contains(id) && !order.Contains(id)) order.Add(id);
            if (order.Count == 0) return;
            int from = _reaction is CardId current ? order.IndexOf(current) : -1;
            _reaction = order[(Mathf.Max(from, 0) + step + order.Count) % order.Count];
            RefreshOverlay();
        }

        void OnDestroy()
        {
            Time.timeScale = 1f;
            if (FxDirector != null) FxDirector.LockChanged -= OnFxLock;
        }

        void OnEscape()
        {
            if (_confirmAbandon) _confirmAbandon = false;
            else if (_paused) SetPaused(false);
            else if (_peek) _peek = false;
            else if (_conditionView != null) _conditionView = null;
            else if (_discardOpen) _discardOpen = false;
            else if (_historyOpen) ToggleHistory();
            else if (CloseMobileSheets()) { }
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
            FxRoot.SetPaused(paused);
        }

        void NewMatch()
        {
            GameAudio.PlayMusic(Playlist.Match);
            if (FxDirector != null) FxDirector.StopAll();
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
            _discardOpen = _reviewing = _peek = _historyOpen = false;
            _zoom = -1;
            _infoTab = InfoTab.None;
            ResetCardFx();
            ResetBoardFx();
            _peekShown = 0f;
            _reaction = null;
            _foresightPick = 0;
            _foresightScroll = 0f;
            _logSeen = 0;
            _logShown = -1;
            foreach (var toast in _toastList) Destroy(toast.gameObject);
            _toastList.Clear();
            _turnBanner = null;
            _weatherNotices.Clear();
            _conditionView = null;

            _net = NetSession.Online;
            _awaitingHost = _netLost = false;
            int seed = _net != null ? _net.Seed : _seed != 0 ? _seed : System.Environment.TickCount;
            _match = Match.CreateDefault(seed);
            _human = _net != null ? _net.LocalSide : NetSession.HostSide(seed);
            _ai = _net != null ? null : new Ai(Opponent, seed);
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
                    Submit(Command.Recruit(_recruitType, c));
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
                        Submit(Command.Capture(attacker, c));
                        Sync();
                        return;
                    }
                    if (_match.MoveTargets(_selected).Contains(c))
                    {
                        Submit(Command.Move(_selected, c));
                        Sync();
                        return;
                    }
                    break;
            }

            bool again = _mode == Mode.Inspect && _selected == unit;
            ClearMode();
            if (unit != null && (unit.Side == _match.Current || !again))
            {
                _mode = unit.Side == _match.Current ? Mode.Unit : Mode.Inspect;
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
            if (index < _handCards.Count && _handCards[index].Off) PlayInvalidFx(index);
        }

        void OnActionPrimary()
        {
            if (_mode != Mode.Card || !Match.PicksComplete(_card, _picks.Count)) return;
            var card = _card;
            var picks = new List<Coord>(_picks);
            ClearMode();
            Submit(Command.Play(card, picks));
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
            Submit(Command.EndTurn());
            Sync();
        }

        void OnReaction(CardId? card)
        {
            if (_awaitingHost || _netLost) return;
            _reaction = null;
            _peek = false;
            _peekShown = 0f;
            Submit(Command.React(card));
            Sync();
        }

        void OnForesight()
        {
            if (_awaitingHost || _netLost) return;
            Submit(Command.Foresight(_foresightPick));
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
            PlayMatchStartFx();
        }

        void ScheduleAi()
        {
            if (_aiStep == null && !_awaitingStart && _turnBanner == null && _weatherNotices.Count == 0 && !FxLocked
                && _ai != null && _ai.HasDecision(_match))
                _aiStep = StartCoroutine(AiStepRoutine());
        }

        // Offline the match is applied directly. Online the host applies and relays it, while the guest only
        // proposes it: the guest's board moves when the host's copy comes back.
        void Submit(Command command)
        {
            if (_net == null) command.Apply(_match, _human);
            else if (!_net.IsHost)
            {
                _net.Send(command);
                _awaitingHost = true;
            }
            else if (command.Apply(_match, _human)) _net.Relay(command, _human, Lockstep.Fingerprint(_match));
        }

        // Remote commands wait for the board to settle, as the AI's do, and replay in the order the host applied them.
        void PumpNet()
        {
            if (_net.State == NetState.Lost && !_netLost)
            {
                _netLost = true;
                ClearMode();
                Refresh();
            }
            if (_awaitingStart || _paused || _turnBanner != null || _weatherNotices.Count > 0 || FxLocked) return;
            if (!_net.TryReceive(out var message)) return;
            if (message.Rejected)
            {
                _awaitingHost = false;
                Refresh();
                return;
            }
            if (_net.IsHost)
            {
                if (!message.Command.Apply(_match, Opponent))
                {
                    _net.Reject();
                    return;
                }
                _net.Relay(message.Command, Opponent, Lockstep.Fingerprint(_match));
            }
            else
            {
                if (!message.Command.Apply(_match, message.By) || Lockstep.Fingerprint(_match) != message.Fingerprint)
                    Debug.LogError($"Online match out of sync after {message.Command}");
                if (message.By == _human) _awaitingHost = false;
            }
            Sync();
        }

        string ModeKey(string key) => _net != null ? key + ".online" : key;

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
            ClaimBoardCaptures();
            foreach (var e in _match.DrainEvents())
            {
                switch (e.Kind)
                {
                    // Trails belong to the turn that made them: they vanish once the turn passes.
                    case EventKind.TurnStarted:
                        ClearTrails();
                        break;
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
                            if (!DeferCapture(e.To, captured)) PlayCapture(captured);
                        }
                        break;
                    case EventKind.Recruited:
                        PlayRecruit(CreatePiece(_match.FindUnit(e.UnitId)));
                        break;
                    case EventKind.Blocked:
                        if (_pieces.TryGetValue(e.UnitId, out var blocked)) PlayBlocked(blocked, e.From, e.To);
                        break;
                }
                _matchFx.OnEvent(e, _match);
            }
            SyncPieces();
            ProcessLog();
            ReleaseUnpresented();
            Refresh();
            FlushCardFx();
            ScheduleAi();
        }

        void ProcessLog()
        {
            if (_awaitingStart) return;
            var log = _match.Log;
            for (; _logSeen < log.Count; _logSeen++)
            {
                var e = log[_logSeen];
                _matchFx.OnLog(e, _match);
                QueueCardFx(e);
                QueueBoardFx(e);
                switch (e.Kind)
                {
                    case LogKind.Preparation:
                        ShowTurnBanner(e);
                        break;
                    case LogKind.CardPlayed when CardCatalog.Get(e.Card).IsWeather:
                        _weatherNotices.Add(_logSeen);
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
                PlayMarkerFx(m, rt);
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
            // Feel animates the holder; the root keeps the board's own pop.
            var holder = Ui.Centered(Ui.Rect(rt, "Holder", 0, 0, Square, Square));
            if (m.Kind == MarkerKind.Palisade)
            {
                Ui.Image(holder, "Palisade", Ui.Art("Palisade"), 0, 0, Square, Square);
                return rt;
            }
            var (fill, kind) = m.Kind switch
            {
                MarkerKind.Ditch => (new Color(Ui.Ink.r, Ui.Ink.g, Ui.Ink.b, 0.3f), CardKind.Fortification),
                MarkerKind.Stakes => (new Color(Ui.Rubric.r, Ui.Rubric.g, Ui.Rubric.b, 0.14f), CardKind.Fortification),
                MarkerKind.Smoke => (new Color(0.55f, 0.55f, 0.55f, 0.4f), CardKind.Reaction),
                _ => (new Color(Ui.Rubric.r, Ui.Rubric.g, Ui.Rubric.b, 0.28f), CardKind.Condition),
            };
            Ui.Image(holder, "Fill", Ui.Solid, 3, 3, Square - 6, Square - 6).color = fill;
            var badge = Ui.Image(holder, "Badge", Ui.Art("Kind_Badge"), 4, 4, 26, 26);
            Ui.Image(badge.transform, "Icon", Ui.KindIcon(kind), 5, 5, 16, 16).color =
                m.Kind == MarkerKind.Stakes || m.Kind == MarkerKind.Lightning ? Ui.Rubric : Ui.Ink;
            if (m.Kind == MarkerKind.Lightning && _markRing != null)
            {
                var ring = Ui.Image(rt, "Ring", _markRing, 0, 0, Square, Square);
                Ui.Centered(ring.rectTransform);
                ring.color = Ui.Rubric;
            }
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
            if (UiProfile.Mobile) RefreshPhaseMobile();
            else
            {
                _phases.text = $"{Tab("ui.phase.preparation", _awaitingStart)}  ·  "
                    + $"{Tab("ui.phase.maneuvers", maneuvers && !_awaitingStart)}  ·  "
                    + $"{Tab("ui.phase.confrontation", _match.Phase == Phase.Reaction)}  ·  "
                    + Tab("ui.phase.end", false);
                _turn.text = _match.Phase == Phase.Reaction
                    ? Loc.Format("ui.turnAttacks", _match.Turn, Loc.SideName(_match.Pending.Attacker.Side))
                    : Loc.Format("ui.turnOf", _match.Turn, Loc.SideName(_match.Current));
            }
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

            // The phone's opponent bar has no room for a status line: it shows the hand and deck instead.
            if (ui.Subtitle != null)
            {
                ui.Subtitle.text = Loc.Get(human
                    ? active ? "ui.army.yourTurn" : "ui.army.youWaiting"
                    : ModeKey(active ? "ui.army.aiTurn" : "ui.army.aiWaiting"));
                ui.Subtitle.color = active ? Ui.Rubric : Ui.InkSoft;
            }
            ui.Morale.text = Loc.Format("ui.moraleValue", player.Morale, Match.MaxMorale);
            int cost = human && HumanTurn ? PendingCost() : 0;
            for (int i = 0; i < ui.Pips.Length; i++)
            {
                ui.Pips[i].sprite = Ui.Art(i < player.Morale ? "Pip_On" : "Pip_Off");
                ui.Pips[i].color = i >= player.Morale - cost && i < player.Morale ? new Color(1f, 1f, 1f, 0.4f) : Color.white;
            }

            for (int i = 0; i < ui.ReserveCounts.Length; i++)
                ui.ReserveCounts[i].text = Loc.Format("ui.reserveCount", player.Reserve[Match.Recruitable[i]]);

            if (!human)
            {
                ui.Summary.text = Loc.Format(UiProfile.Mobile ? "ui.m.handSummary" : "ui.handSummary", player.Hand.Count,
                    player.Deck.Count, player.Discard.Count);
                return;
            }

            int orders = active ? player.Orders : Match.OrdersPerTurn;
            int bonus = active ? player.BonusMoves : 0;
            if (ui.Orders != null)
                ui.Orders.text = active
                    ? Loc.Format("ui.orders", orders, Match.OrdersPerTurn) + (bonus > 0 ? Loc.Format("ui.ordersBonus", bonus) : "")
                    : Loc.Get("ui.ordersIdle");
            int shown = Match.OrdersPerTurn + bonus;
            for (int i = 0; i < ui.OrderIcons.Length; i++)
            {
                var icon = ui.OrderIcons[i];
                icon.enabled = i < shown;
                icon.sprite = Ui.Art(active && i < orders + bonus ? "Order_On" : "Order_Off");
                icon.rectTransform.anchoredPosition = new Vector2(ui.OrderRight - (shown - i) * ui.OrderStep, -ui.OrderY);
            }

            bool choosing = active && _mode == Mode.Recruit;
            ui.ReserveHint.text = Loc.Get((UiProfile.Mobile ? "ui.m." : "ui.") + (choosing ? "reserveChoose" : "reserveHint"));
            for (int i = 0; i < Match.Recruitable.Length; i++)
            {
                var type = Match.Recruitable[i];
                bool allowed = HumanOwnsTurn && _match.CanRecruit(type);
                ui.RecruitButtons[i].interactable = allowed && HumanTurn;
                ui.RecruitGroups[i].alpha = allowed || !HumanOwnsTurn ? 1f : 0.45f;
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
                case Mode.Inspect:
                    Mark(_under, _selected.Pos, "Marker_Selected");
                    foreach (var c in _match.Reach(_selected, captures: false)) Mark(_hint, c, "Marker_Move");
                    foreach (var c in _match.Reach(_selected, captures: true)) Mark(_hint, c, "Marker_Target");
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

        void AddTrail(Coord from, Coord to, bool capture)
        {
            if (from != to && _trails.Add((from, to))) DrawTrail(_trailLayer, from, to, capture, 1f);
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
                Ui.Dashed(group, tl, tr, ink, 1.2f, 4f, 4f);
                Ui.Dashed(group, tr, br, ink, 1.2f, 4f, 4f);
                Ui.Dashed(group, br, bl, ink, 1.2f, 4f, 4f);
                Ui.Dashed(group, bl, tl, ink, 1.2f, 4f, 4f);
            }
            var rubric = new Color(Ui.Rubric.r, Ui.Rubric.g, Ui.Rubric.b, alpha);
            if (Vector2.Dot(tip - start, dir) > 1f) Ui.Dashed(group, tip, start, rubric, TrailWidth, TrailDash, TrailGap);
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

        void HoverSquare(Coord c, bool on)
        {
            if (on) _hoverSquare = c;
            else if (_hoverSquare == c) _hoverSquare = null;
            RefreshPreview();
        }

        void RefreshHand()
        {
            if (UiProfile.Mobile)
            {
                RefreshHandMobile();
                return;
            }
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

                // Feel springs the holder (pivot at the card's foot); the hand's own pose moves the card inside it.
                var holder = Ui.Rect(slot.transform, "Holder", 0f, 0f, w, h);
                holder.pivot = new Vector2(0.5f, 0f);
                holder.anchoredPosition = new Vector2(w * 0.5f, -h);
                var card = Ui.Card(holder, info, HandScale);
                card.pivot = new Vector2(0.5f, 0f);
                var graphics = card.GetComponentsInChildren<Graphic>();
                bool selected = _mode == Mode.Card && _cardIndex == i;
                var c = new HandCard
                {
                    Slot = slot.rectTransform,
                    Holder = holder,
                    Card = card,
                    Tilt = -HandTilt * (i - middle),
                    Selected = selected,
                    Off = HumanOwnsTurn && !_match.CanPlay(info.Id),
                    Lift = i == _hoveredCard ? 1f : 0f,
                    Graphics = graphics,
                    Colors = System.Array.ConvertAll(graphics, g => g.color),
                };
                c.Shadow = Ui.Image(card, "Shadow", Ui.Art("Card_Shadow"), -17.5f, -2.5f, w + 35f, h + 35f);
                c.Shadow.type = Image.Type.Sliced;
                c.Shadow.pixelsPerUnitMultiplier = 60f / 25f;
                c.Shadow.transform.SetAsFirstSibling();
                if (i >= hand.Count - _heldDraws) card.gameObject.AddComponent<CanvasGroup>().alpha = 0f;
                _handCards.Add(c);
                PoseHandCard(c);

                int index = i;
                var button = slot.gameObject.AddComponent<Button>();
                button.transition = Selectable.Transition.None;
                button.onClick.AddListener(() => OnCardClicked(index));
                Ui.OnHover(slot.gameObject, () => HoverHandCard(index, true), () => HoverHandCard(index, false));
            }
            OrderHand();
        }

        void HoverHandCard(int index, bool on)
        {
            if (on && _hoveredCard != index) GameAudio.Play(Sfx.CardShuffle);
            if (on) _hoveredCard = index;
            else if (_hoveredCard == index) _hoveredCard = -1;
            OrderHand();
            bool off = index < _handCards.Count && _handCards[index].Off;
            Ui.SetCursor(!on ? CursorKind.Default : off ? CursorKind.Unavailable : CursorKind.Clickable);
            if (on) PlayHandFx(FxCueId.CardHover, index);
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
                new Vector2(Ui.CardWidth * HandScale * 0.5f, Mathf.Lerp(rest, HandY - StageHeight + HandFootGap, t));
            c.Card.localRotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(c.Tilt, 0f, t));
            float scale = Mathf.Lerp(1f, HandZoom, t);
            c.Card.localScale = new Vector3(scale, scale, 1f);
            c.Shadow.color = new Color(1f, 1f, 1f, HandShadowAlpha * t);
            if (!c.Off) return;
            for (int i = 0; i < c.Graphics.Length; i++)
                c.Graphics[i].color = Color.Lerp(c.Colors[i] * OffTint, c.Colors[i], t);
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
            bool aiReacting = live && _match.Phase == Phase.Reaction && _match.Pending.Defender.Side == Opponent;
            if (aiReacting || (live && _match.Current == Opponent))
            {
                var ai = _match.Get(Opponent);
                var intentCard = IntentCard(_aiIntent);
                if (intentCard is CardId shown) Ui.Card(_actionSlot, CardCatalog.Get(shown), ActionCardScale);
                else ArmyPlate(Opponent, "ui.plate.enemyText");
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
                    hint = Loc.Format("ui.action.aiReacting", Loc.Army(Opponent));
                    cells.Add((Loc.Get("ui.cell.target"), Loc.UnitDot(pc.Defender),
                        Loc.Format("ui.cell.from", Loc.UnitDot(pc.Attacker)), true));
                }
                else
                {
                    step = Loc.Get("ui.action.aiTitle");
                    hint = Loc.Format("ui.action.aiHint", Loc.Army(Opponent));
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
                    cells.Add((Loc.Get("ui.cell.moves"), _match.MoveTargets(u).Count.ToString(),
                        Loc.Get("ui.cell.movesSub"), false));
                    cells.Add((Loc.Get("ui.cell.captures"), _match.CaptureTargets(u).Count.ToString(),
                        Loc.Get("ui.cell.capturesSub"), false));
                    cells.Add(GearCell(u));
                    break;
                }
                case Mode.Inspect:
                {
                    var u = _selected;
                    step = Loc.Get("ui.action.enemyTitle");
                    UnitPlate(u.Type, u.Side, "ui.plate.enemyUnit");
                    hint = Loc.Get("ui.action.hintEnemy");
                    var threats = _match.Reach(u, captures: true);
                    cells.Add((Loc.Get("ui.cell.reach"), _match.Reach(u, captures: false).Count.ToString(),
                        Loc.Get("ui.cell.reachSub"), false));
                    cells.Add((Loc.Get("ui.cell.threats"), threats.Count.ToString(), Loc.Get("ui.cell.threatsSub"),
                        threats.Count > 0));
                    cells.Add(GearCell(u));
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
            if (UiProfile.Mobile) RefreshActionMobile(hint, showPrimary, showCancel);
        }

        static CardId? IntentCard(Ai.Choice c) => c?.Kind switch
        {
            Ai.ChoiceKind.Card => c.Card,
            Ai.ChoiceKind.React => c.Reaction,
            _ => null,
        };

        string IntentText(Ai.Choice c)
        {
            string army = Loc.Army(Opponent);
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

        static (string label, string value, string sub, bool marked) GearCell(Unit u)
        {
            var gear = new List<string>();
            if (u.Protection > 0) gear.Add(Loc.Get("ui.action.protection"));
            if (u.RangedProtection > 0) gear.Add(Loc.Format("ui.action.rangedProtection", u.RangedProtection));
            if (u.HasHammer) gear.Add(Loc.Get("ui.action.hammer"));
            if (u.HasHorseshoes) gear.Add(Loc.Get("ui.action.horseshoes"));
            if (u.HasLance) gear.Add(Loc.Get("ui.action.lance"));
            if (u.HasBanner) gear.Add(Loc.Get("ui.action.banner"));
            return (Loc.Get("ui.cell.gear"), gear.Count > 0 ? string.Join(" · ", gear) : Loc.Get("ui.action.none"), "", false);
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
            if (UiProfile.Mobile)
            {
                BuildCellsMobile(cells);
                return;
            }
            bool compact = Compact;
            float w = _actionCells.rect.width, cw = w / cells.Count, pad = compact ? 7f : 10f;
            Ui.Line(_actionCells, 0f, 0f, w, 1f, 0.35f);
            Ui.Line(_actionCells, 0f, CellsHeight - 1f, w, 1f, 0.35f);
            for (int i = 0; i < cells.Count; i++)
            {
                var (label, value, sub, marked) = cells[i];
                float x = cw * i + pad, tw = cw - pad * 2f;
                if (i > 0) Ui.Line(_actionCells, cw * i, 0f, 1f, CellsHeight, 0.35f);
                var title = Ui.Label(_actionCells, label, x, compact ? 5f : 8f, tw);
                title.fontSize = compact ? 15f : 14f;
                title.characterSpacing = compact ? 6f : 8f;
                title.textWrappingMode = TextWrappingModes.NoWrap;
                var top = Ui.Text(_actionCells, value, Ui.BodyFont, compact ? 16f : 18f, marked ? Ui.Rubric : Ui.InkText,
                    x, compact ? 23f : 29f, tw, 22f);
                var bottom = Ui.Text(_actionCells, sub, Ui.BodyFont, compact ? 15f : 14.5f, Ui.InkBody, x,
                    compact ? 43f : 52f, tw, compact ? 19f : 34f);
                if (!compact) continue;
                top.textWrappingMode = bottom.textWrappingMode = TextWrappingModes.NoWrap;
                top.overflowMode = bottom.overflowMode = TextOverflowModes.Ellipsis;
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
            if (_logLatest != null)
            {
                // TMP only measures text in an active hierarchy, so the full list is filled while it is open.
                _historyOpenPanel.SetActive(_historyOpen);
                for (int i = _logLatest.childCount - 1; i >= 0; i--) Destroy(_logLatest.GetChild(i).gameObject);
                if (log.Count > 0)
                {
                    var latest = log[log.Count - 1];
                    float width = _logLatest.sizeDelta.x;
                    Ui.Text(_logLatest, Ui.Roman(latest.Turn), Ui.TitleFont, 15.5f, Ui.Rubric, 0f, 0f, 30f, 22f);
                    var line = Ui.Text(_logLatest, Loc.LogLine(latest), Ui.BodyFont, 15.5f, Ui.InkText, 32f, 0f,
                        width - 32f, 22f);
                    line.textWrappingMode = TextWrappingModes.NoWrap;
                    line.overflowMode = TextOverflowModes.Ellipsis;
                }
                if (!_historyOpen)
                {
                    _logShown = log.Count;
                    return;
                }
            }

            for (int i = _logEntries.childCount - 1; i >= 0; i--) Destroy(_logEntries.GetChild(i).gameObject);
            float font = UiProfile.Mobile ? 15f : Compact ? 15.5f : 17f, textX = UiProfile.Mobile ? 34f : Compact ? 36f : 46f;
            float w = _logEntries.rect.width, textWidth = w - textX, y = 0f;
            for (int i = log.Count - 1; i >= 0; i--)
            {
                string line = Loc.LogLine(log[i]);
                var entry = Ui.Text(_logEntries, line, Ui.BodyFont, font, Ui.InkText, textX, y + 8f, textWidth, 0f);
                float height = entry.GetPreferredValues(line, textWidth, 0f).y;
                entry.rectTransform.sizeDelta = new Vector2(textWidth, height);
                Ui.Line(_logEntries, 0f, y, w, 1f, 0.3f);
                Ui.Text(_logEntries, Ui.Roman(log[i].Turn), Ui.TitleFont, font, Ui.Rubric, 0f, y + 8f, textX - 2f, 22f);
                y += height + 16f;
            }
            _logEntries.sizeDelta = new Vector2(_logEntries.sizeDelta.x, y);
            if (log.Count != _logShown) _logEntries.anchoredPosition = Vector2.zero;
            _logShown = log.Count;
        }

        void ToggleHistory()
        {
            _historyOpen = !_historyOpen;
            RefreshHistory(force: true);
        }

        void RefreshDeck()
        {
            // The phone layout has no deck panel.
            if (_deck == null) return;
            var p = _match.Get(_human);
            int deck = p.Deck.Count + _heldDraws;
            _deck.text = deck.ToString();
            _deckCaption.text = Loc.Get(deck == 1 ? "ui.card" : "ui.cards");
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
            foreach (var layer in new[] { _overlayContent, _overlayNative, _confront, _focusLayer })
                for (int i = layer.childCount - 1; i >= 0; i--) Destroy(layer.GetChild(i).gameObject);
            foreach (var spot in _spot) spot.enabled = false;
            _dock = null;
            _confrontShade = null;
            _overlayFit = 1f;
            // TMP measures text only once awake, so the builders below need an active hierarchy.
            _overlay.gameObject.SetActive(true);
            bool modal = true, discard = false, confront = false;

            if (_netLost && _match.Phase != Phase.GameOver) BuildDisconnected();
            else if (_match.Phase == Phase.GameOver && !_reviewing)
            {
                BuildGameOver();
                PlayGameOverFx();
                GameAudio.PlayMusic(_match.Winner == _human ? Playlist.Victory : Playlist.Defeat);
            }
            else if (_awaitingStart) BuildIntro();
            else if (_weatherNotices.Count > 0) BuildCondition(_match.Log[_weatherNotices[0]].Card, notice: true);
            else if (_match.Phase == Phase.Reaction && _match.Pending.Defender.Side == _human)
            {
                BuildConfront();
                confront = true;
            }
            else if (_match.Phase == Phase.Foresight && _match.Current == _human)
            {
                BuildForesight();
                PlayForesightFx();
            }
            else if (_conditionView is CardId viewed && _match.HasCondition(viewed)) BuildCondition(viewed, notice: false);
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
            _overlayContent.localScale = new Vector3(_overlayFit, _overlayFit, 1f);
            _overlay.gameObject.SetActive(modal || _paused);
            _overlayShade.enabled = !confront || _paused;
            _backdrop.SetActive(discard && !_paused);
        }

        // Modals are drawn for the Medium area: the content shrinks as a whole when the largest one would not fit.
        RectTransform OverlayPanel(float w, float h)
        {
            var design = UiProfile.Design;
            _overlayFit = Mathf.Min(_overlayFit, UiProfile.Fit(w, h));
            return Ui.Modal(_overlayContent, (design.x - w) * 0.5f, (design.y - h) * 0.5f, w, h);
        }

        // The overlay content is stage-sized; the extra margin keeps the scrim edge-to-edge on wider screens.
        void Scrim(float alpha)
        {
            // The phone's own modals sit on the native layer, so a scrim between two of them has to be there as well.
            var parent = UiProfile.Mobile ? _overlayNative : _overlayContent;
            var scrim = Ui.Stretch(Ui.Image(parent, "Scrim", Ui.Solid, 0, 0, 0, 0, raycast: true).rectTransform);
            scrim.offsetMin = -UiProfile.Design;
            scrim.offsetMax = UiProfile.Design;
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
            if (UiProfile.Mobile)
            {
                BuildIntroMobile();
                return;
            }
            const float w = 960f, h = 794f, tipsY = 465f, tipsHeight = 189f;
            var panel = OverlayPanel(w, h);
            Eyebrow(panel, Loc.Get(ModeKey("ui.intro.eyebrow")), 44f);
            Ui.Image(panel, "Emblem", Ui.UnitArt(UnitType.Sovereign, _human), (w - 104f) * 0.5f, 92f, 104f, 104f);
            Eyebrow(panel, Loc.Get("ui.intro.header"), 218f, 32f);
            Title(panel, Loc.Army(_human), 246f, 60f);
            Ui.Text(panel, Loc.Format(ModeKey(_match.Current == _human ? "ui.intro.youFirst" : "ui.intro.aiFirst"), Loc.Army(Opponent)),
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
                if (i == 0) Ui.Image(panel, "Icon", Ui.UnitArt(UnitType.Sovereign, Opponent), center - 26f, tipsY + 22f, 52f, 52f);
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

        void BuildConfront()
        {
            if (UiProfile.Mobile)
            {
                BuildConfrontMobile();
                return;
            }
            var pc = _match.Pending;
            var defender = _match.Get(pc.Defender.Side);
            var applicable = _match.ReactionOptions();
            var cards = new List<CardId>();
            foreach (var id in defender.Hand)
                if (CardCatalog.Get(id).IsReaction && !cards.Contains(id)) cards.Add(id);
            if (_reaction == null || !applicable.Contains(_reaction.Value))
                _reaction = applicable.Count > 0 ? applicable[0] : (CardId?)null;

            FocusBoard(pc);

            var canvas = ((RectTransform)_scaler.transform).rect.size;
            float scale = _boardRig.localScale.x;
            float boardLeft = canvas.x * 0.5f + _boardRig.anchoredPosition.x;
            float holeLeft = boardLeft - 14f, holeTop = HeaderHeight + 2f;
            float holeBottom = -_boardRig.anchoredPosition.y + BoardSize * scale + 2f;

            var shade = Ui.Stretch(Ui.Rect(_confront, "Shade", 0, 0, 0, 0));
            _confrontShade = shade.gameObject.AddComponent<CanvasGroup>();
            ConfrontScrim(shade, new Vector2(0f, 1f), Vector2.one, new Vector2(0f, -holeTop), Vector2.zero);
            ConfrontScrim(shade, Vector2.zero, Vector2.up, Vector2.zero, new Vector2(holeLeft, -holeTop));
            ConfrontScrim(shade, Vector2.zero, Vector2.right, new Vector2(holeLeft, 0f), new Vector2(0f, canvas.y - holeBottom));

            PeekButton(boardLeft + BoardSize * scale * 0.5f - canvas.x * 0.5f, holeBottom + 14f);
            Dock(pc, defender, cards, applicable, (canvas.y - ColumnTop - (StageHeight - ColumnBottom)) / DockScale);
            PosePeek();
        }

        static void ConfrontScrim(RectTransform parent, Vector2 min, Vector2 max, Vector2 offsetMin, Vector2 offsetMax)
        {
            var scrim = Ui.Image(parent, "Scrim", Ui.Solid, 0f, 0f, 0f, 0f, raycast: true);
            scrim.color = new Color(ScrimColor.r, ScrimColor.g, ScrimColor.b, ConfrontScrimAlpha);
            var rt = scrim.rectTransform;
            rt.anchorMin = min;
            rt.anchorMax = max;
            rt.offsetMin = offsetMin;
            rt.offsetMax = offsetMax;
        }

        void PeekButton(float centerX, float top)
        {
            RectTransform Place(RectTransform rt, float dx, float dy)
            {
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
                rt.anchoredPosition = new Vector2(centerX - PeekWidth * 0.5f + dx, -(top + dy));
                return rt;
            }

            var shadow = Ui.Sliced(_confront, "Shadow", Ui.Art("Card_Shadow"), 0f, 0f, PeekWidth + 28f, PeekHeight + 22f);
            shadow.pixelsPerUnitMultiplier = 60f / 18f;
            shadow.color = new Color(1f, 1f, 1f, 0.7f);
            Place(shadow.rectTransform, -14f, 4f);
            var pill = Ui.Sliced(_confront, "Peek", Ui.Pill(Ui.Ink, Ui.InkText, 26, 1.5f), 0f, 0f, PeekWidth, PeekHeight,
                raycast: true);
            Place(pill.rectTransform, 0f, 0f);

            string label = Loc.Get(_peek ? "ui.reaction.back" : "ui.reaction.peek");
            var text = Ui.Text(pill.transform, label, Ui.TitleFont, 20f, Ui.Cream, 0f, 0f, 0f, PeekHeight,
                TextAlignmentOptions.MidlineLeft);
            text.characterSpacing = 5f;
            Fit(text);
            const float icon = 22f, iconGap = 10f;
            float left = (PeekWidth - icon - iconGap - text.rectTransform.sizeDelta.x) * 0.5f;
            Ui.Image(pill.transform, "Eye", Ui.Art("Icon_Eye"), left, (PeekHeight - icon) * 0.5f, icon, icon).color = Ui.Cream;
            text.rectTransform.anchoredPosition = new Vector2(left + icon + iconGap, 0f);

            var button = Ui.MakeButton(pill, () =>
            {
                _peek = !_peek;
                RefreshOverlay();
            });
            button.transition = Selectable.Transition.None;
        }

        void PosePeek()
        {
            float t = Mathf.SmoothStep(0f, 1f, _peekShown);
            if (_dock != null) _dock.anchoredPosition = new Vector2(_dockX * (1f - t) + 20f * t, _dock.anchoredPosition.y);
            if (_confrontShade != null)
            {
                _confrontShade.alpha = 1f - t;
                _confrontShade.blocksRaycasts = !_peek;
            }
            _spotGroup.alpha = 1f - t;
        }

        void Dock(PendingCapture pc, Player defender, List<CardId> cards, List<CardId> applicable, float height)
        {
            const float w = DockWidth, x = DockPadding, inner = DockWidth - DockPadding * 2f, bandHeight = 46f;
            var dock = _dock = Ui.Rect(_confront, "Dock", 0f, 0f, w, height);
            dock.anchorMin = dock.anchorMax = Vector2.one;
            dock.localScale = new Vector3(DockScale, DockScale, 1f);
            _dockX = -(w * DockScale + LeftX);
            dock.anchoredPosition = new Vector2(_dockX, -ColumnTop);
            Ui.Sliced(dock, "Glow", Ui.Glow, -4f, -4f, w + 8f, height + 8f);
            var panel = Ui.Modal(dock, 0f, 0f, w, height);

            var band = Ui.Image(panel, "Band", Ui.Solid, 2f, 2f, w - 4f, bandHeight);
            band.color = Ui.Rubric;
            var bandLabel = Ui.Label(band.transform, Loc.Get("ui.reaction.header"), x - 2f, 0f, 280f, bandHeight,
                TextAlignmentOptions.MidlineLeft);
            bandLabel.fontSize = 17f;
            bandLabel.characterSpacing = 16f;
            bandLabel.color = Ui.Cream;
            Ui.Text(band.transform, Loc.Get("ui.reaction.yours"), Ui.ItalicFont, 18f, Ui.Cream, 0f, 0f, w - 4f - (x - 2f),
                bandHeight, TextAlignmentOptions.MidlineRight);

            Ui.Text(panel, Loc.Get("ui.reaction.title"), Ui.TitleFont, 34f, Ui.InkText, x, 62f, inner, 42f);

            const float combatantY = 114f, arrowWidth = 70f, sideWidth = (inner - arrowWidth - 16f) * 0.5f;
            Combatant(panel, pc.Attacker, x + sideWidth * 0.5f, combatantY, Loc.Get("ui.reaction.attacker"), false);
            Combatant(panel, pc.Defender, x + inner - sideWidth * 0.5f, combatantY, Loc.Get("ui.reaction.yourUnit"), true);
            Arrow(panel, x + sideWidth + 12f, combatantY + 46f, arrowWidth - 8f);

            const float moraleY = 226f, pip = 18f, pipGap = 4f;
            Ui.Label(panel, Loc.Get("ui.morale"), x, moraleY, 120f, 20f);
            int cost = _reaction is CardId pick ? _match.ReactionCost(pick) : 0;
            float pipsX = x + (inner - Match.MaxMorale * (pip + pipGap) + pipGap) * 0.5f;
            for (int i = 0; i < Match.MaxMorale; i++)
            {
                var img = Ui.Image(panel, "Pip", Ui.Art(i < defender.Morale ? "Pip_On" : "Pip_Off"), pipsX + i * (pip + pipGap),
                    moraleY, pip, pip);
                if (i >= defender.Morale - cost && i < defender.Morale) img.color = new Color(1f, 1f, 1f, 0.4f);
            }
            Ui.Text(panel, Loc.Format("ui.moraleValue", defender.Morale, Match.MaxMorale), Ui.BodyFont, 17f, Ui.InkText,
                x, moraleY - 3f, inner, 24f, TextAlignmentOptions.TopRight);

            const float promptY = 260f;
            string promptText = Loc.Get("ui.reaction.prompt");
            var prompt = Ui.Text(panel, promptText, Ui.ItalicFont, 17f, Ui.Ink, x, promptY, inner, 0f);
            float promptHeight = prompt.GetPreferredValues(promptText, inner, 0f).y;
            prompt.rectTransform.sizeDelta = new Vector2(inner, promptHeight);

            float passY = height - 22f - ReactionButtonHeight, playY = passY - 10f - ReactionButtonHeight;
            Ui.Button(panel, Loc.Get("ui.reaction.pass"), false, x, passY, inner, ReactionButtonHeight,
                () => OnReaction(null), 21f);
            if (_reaction is CardId selected)
            {
                var play = Ui.Button(panel, Loc.Format("ui.reaction.play", Loc.CardName(selected), _match.ReactionCost(selected)),
                    true, x, playY, inner, ReactionButtonHeight, () => OnReaction(selected), 21f);
                var label = play.GetComponentInChildren<TextMeshProUGUI>();
                label.margin = new Vector4(14f, 0f, 14f, 0f);
                label.enableAutoSizing = true;
                label.fontSizeMin = 15f;
                label.fontSizeMax = 21f;
            }

            float top = promptY + promptHeight + 14f, areaHeight = playY - 14f - top;
            float listHeight = cards.Count * (OptionHeight + OptionGap) - OptionGap;
            RectTransform area = panel;
            float optionX = x, optionY = top, optionWidth = inner;
            if (listHeight > areaHeight)
            {
                const float pad = 4f;
                var scroll = Ui.Scroll(panel, x, top, inner, areaHeight);
                area = scroll.content;
                area.sizeDelta = new Vector2(0f, listHeight + pad * 2f);
                optionX = optionY = pad;
                optionWidth = inner - 18f - pad * 2f;
            }
            foreach (var id in cards)
            {
                ReactionOption(area, id, optionX, optionY, optionWidth, applicable.Contains(id), pc);
                optionY += OptionHeight + OptionGap;
            }
        }

        static void Combatant(RectTransform panel, Unit unit, float center, float y, string caption, bool target)
        {
            const float size = 58f, width = 190f;
            Ui.Image(panel, "Token", Ui.UnitArt(unit.Type, unit.Side), center - size * 0.5f, y, size, size);
            Ui.Text(panel, Loc.UnitDot(unit), Ui.TitleFont, 17f, target ? Ui.Rubric : Ui.InkText, center - width * 0.5f,
                y + size + 4f, width, 22f, TextAlignmentOptions.Top).textWrappingMode = TextWrappingModes.NoWrap;
            Ui.Text(panel, caption, Ui.BodyFont, 15f, Ui.InkBody, center - width * 0.5f, y + size + 27f, width, 20f,
                TextAlignmentOptions.Top);
        }

        void ReactionOption(RectTransform parent, CardId id, float x, float y, float w, bool ok, PendingCapture pc)
        {
            bool chosen = ok && _reaction == id;
            var rubric = Ui.Rubric;
            if (chosen)
                Ui.Sliced(parent, "Glow", Ui.Pill(Color.clear, new Color(rubric.r, rubric.g, rubric.b, 0.16f), 7, 3f),
                    x - 3f, y - 3f, w + 6f, OptionHeight + 6f);
            var frame = chosen ? Ui.Pill(new Color(rubric.r, rubric.g, rubric.b, 0.07f), rubric, 4, 2f)
                : Ui.Pill(Color.clear, new Color(Ui.Ink.r, Ui.Ink.g, Ui.Ink.b, 0.25f), 4, 2f);
            var box = Ui.Sliced(parent, "Option " + id, frame, x, y, w, OptionHeight, raycast: ok);

            var card = Ui.Card(box.transform, CardCatalog.Get(id), OptionCard / Ui.CardWidth);
            card.anchoredPosition = new Vector2(7f, -(OptionHeight - OptionCard * Ui.CardHeight / Ui.CardWidth) * 0.5f);
            if (!ok) card.gameObject.AddComponent<CanvasGroup>().alpha = 0.45f;

            const float textX = 7f + OptionCard + 14f, titleHeight = 25f, statusHeight = 21f;
            float textWidth = w - textX - 10f;
            string outcome = ReactionOutcome(id, ok, pc);
            var body = Ui.Text(box.transform, outcome, Ui.BodyFont, 16.5f, Ui.InkText, textX, 0f, textWidth, 0f);
            float bodyHeight = body.GetPreferredValues(outcome, textWidth, 0f).y;
            float top = (OptionHeight - titleHeight - statusHeight - bodyHeight) * 0.5f;
            body.rectTransform.anchoredPosition = new Vector2(textX, -(top + titleHeight + statusHeight));
            body.rectTransform.sizeDelta = new Vector2(textWidth, bodyHeight);

            var title = Ui.Text(box.transform, Loc.CardName(id), Ui.TitleFont, 21f, Ui.InkText, textX, top, textWidth, titleHeight);
            title.textWrappingMode = TextWrappingModes.NoWrap;
            title.enableAutoSizing = true;
            title.fontSizeMin = 16f;
            title.fontSizeMax = 21f;
            var status = Ui.Text(box.transform, ReactionStatus(id, ok, pc), Ui.TitleFont, 14f, ok ? Ui.Rubric : Ui.InkSoft,
                textX, top + titleHeight, textWidth, statusHeight);
            status.characterSpacing = 10f;
            status.textWrappingMode = TextWrappingModes.NoWrap;

            if (!ok) return;
            var button = box.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(() =>
            {
                _reaction = id;
                RefreshOverlay();
            });
        }

        string ReactionStatus(CardId id, bool ok, PendingCapture pc)
        {
            if (!ok) return Loc.Get(_match.ReactionApplies(id) ? "ui.reaction.noMorale" : "ui.reaction.notApplicable");
            int cost = _match.ReactionCost(id);
            return id switch
            {
                CardId.OrganizedRetreat => Loc.Format("ui.reaction.retreat", _match.RetreatSquare(pc.Defender), cost),
                CardId.Countercharge => Loc.Format("ui.reaction.countercharge", _match.CounterchargeSquare(pc.Defender), cost),
                CardId.SovereignsGuard or CardId.HeroicInterposition
                    => Loc.Format("ui.reaction.guard", _match.GuardFor(pc.Defender).Pos, cost),
                _ => Loc.Format("ui.reaction.block", cost),
            };
        }

        string ReactionOutcome(CardId id, bool ok, PendingCapture pc)
        {
            if (!ok)
            {
                if (_match.ReactionApplies(id))
                    return Loc.Format("ui.reaction.outcome.noMorale", _match.ReactionCost(id), _match.Get(pc.Defender.Side).Morale);
                if (id == CardId.SovereignsGuard && pc.Defender.Type == UnitType.Sovereign) return Loc.Get("ui.reaction.noGuard");
                return Loc.Get("ui.reaction.no." + id);
            }
            return id switch
            {
                CardId.OrganizedRetreat => pc.Advances ? Loc.Format("ui.reaction.outcome.retreat", pc.Target)
                    : Loc.Format("ui.reaction.outcome.retreatStay", pc.From),
                CardId.Countercharge => Loc.Format("ui.reaction.outcome.countercharge", pc.From),
                CardId.SovereignsGuard or CardId.HeroicInterposition
                    => Loc.Format("ui.reaction.outcome.guard", _match.GuardFor(pc.Defender).Pos),
                CardId.CloseRanks => Loc.Get("ui.reaction.outcome.closeRanks"),
                CardId.SmokeScreen => Loc.Format("ui.reaction.outcome.smoke", pc.Target),
                _ => Loc.Format("ui.reaction.outcome.block", pc.From),
            };
        }

        // The spotlight dims every square off the attack, so the preview of the chosen reaction reads at a glance.
        void FocusBoard(PendingCapture pc)
        {
            var lit = new HashSet<Coord> { pc.From, pc.Target };
            int dx = pc.Target.X - pc.From.X, dy = pc.Target.Y - pc.From.Y;
            if (dx == 0 || dy == 0 || Mathf.Abs(dx) == Mathf.Abs(dy))
            {
                var step = new Coord(System.Math.Sign(dx), System.Math.Sign(dy));
                for (var c = pc.From + step; c != pc.Target; c += step) lit.Add(c);
            }

            switch (_reaction)
            {
                case CardId.OrganizedRetreat:
                case CardId.Countercharge:
                {
                    var back = (_reaction == CardId.OrganizedRetreat
                        ? _match.RetreatSquare(pc.Defender) : _match.CounterchargeSquare(pc.Defender)).Value;
                    lit.Add(back);
                    var o = SquareOrigin(back);
                    Ui.DashedBox(_focusLayer, o.x + 4f, -o.y + 4f, Square - 8f, Square - 8f, Ui.Ink, 1.6f, 5f, 5f);
                    var c = SquareCenter(back);
                    Ui.Image(_focusLayer, "Ghost", Ui.UnitArt(pc.Defender.Type, pc.Defender.Side), c.x - PieceSize * 0.5f,
                        -c.y - PieceSize * 0.5f, PieceSize, PieceSize).color = new Color(1f, 1f, 1f, 0.45f);
                    InkArrow(pc.Target, back, PieceSize * 0.5f, PieceSize * 0.25f, both: false);
                    break;
                }
                case CardId.SovereignsGuard:
                case CardId.HeroicInterposition:
                {
                    var guard = _match.GuardFor(pc.Defender).Pos;
                    lit.Add(guard);
                    var o = SquareOrigin(guard);
                    Ui.DashedBox(_focusLayer, o.x + 4f, -o.y + 4f, Square - 8f, Square - 8f, Ui.Ink, 1.6f, 5f, 5f);
                    InkArrow(guard, pc.Target, PieceSize * 0.42f, PieceSize * 0.42f, both: true);
                    break;
                }
                case CardId _:
                {
                    var o = SquareOrigin(pc.Target);
                    Ui.Image(_focusLayer, "Shield", Ui.Art("Badge_Shield"), o.x + 50f, -o.y + 14f, 30f, 30f);
                    break;
                }
            }

            for (int x = 0; x < Match.Size; x++)
                for (int y = 0; y < Match.Size; y++)
                    _spot[x, y].enabled = !lit.Contains(new Coord(x, y));
            BoardTag(pc.From, pc.Target, Loc.Get("ui.reaction.tag.attacker"), Ui.Rubric);
            BoardTag(pc.Target, pc.From, Loc.Get("ui.reaction.tag.target"), Ui.Ink);
        }

        void InkArrow(Coord from, Coord to, float startInset, float tipInset, bool both)
        {
            Vector2 a = SquareCenter(from), b = SquareCenter(to), dir = (b - a).normalized;
            Vector2 start = a + dir * startInset, tip = b - dir * tipInset;
            var ink = new Color(Ui.Ink.r, Ui.Ink.g, Ui.Ink.b, 0.9f);
            Ui.Dashed(_focusLayer, tip, start, ink, TrailWidth, TrailDash, TrailGap);
            ArrowHead(_focusLayer, tip, dir, ink);
            if (both) ArrowHead(_focusLayer, start, -dir, ink);
        }

        // A tag rides above its square unless the other unit stands right there.
        void BoardTag(Coord c, Coord other, string text, Color fill)
        {
            // The phone draws the board at about half the size, so its tags grow to stay readable.
            float k = UiProfile.Mobile ? 1.6f : 1f, height = 18f * k;
            var o = SquareOrigin(c);
            var p = SquareOrigin(other);
            bool below = Mathf.Approximately(p.x, o.x) && Mathf.Approximately(p.y, o.y + Square);
            var tag = Ui.Sliced(_focusLayer, "Tag", Ui.Pill(fill, fill, 3, 0f), 0f, 0f, 0f, height);
            var label = Ui.Text(tag.transform, text, Ui.TitleFont, 12f * k, Ui.Cream, 7f * k, 0f, 0f, height,
                TextAlignmentOptions.MidlineLeft);
            label.characterSpacing = 8f;
            Fit(label);
            float width = label.rectTransform.sizeDelta.x + 14f * k;
            tag.rectTransform.sizeDelta = new Vector2(width, height);
            tag.rectTransform.anchoredPosition =
                new Vector2(o.x + (Square - width) * 0.5f, below ? o.y - Square + 2f : o.y + height - 2f);
        }

        void BuildForesight()
        {
            if (UiProfile.Mobile)
            {
                BuildForesightMobile();
                return;
            }
            _foresightCards.Clear();
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
                var shown = Ui.Card(box.transform, info, scale);
                shown.anchoredPosition = new Vector2(12f, -12f);
                _foresightCards.Add(shown);
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

        // The same sheet announces a condition as it is played and, from the conditions panel, describes one in effect.
        string ConditionLead(CardId card, bool notice)
        {
            var log = _match.Log;
            int index = notice ? _weatherNotices[0] : log.Count - 1;
            if (!notice)
                while (index > 0 && (log[index].Kind != LogKind.CardPlayed || log[index].Card != card)) index--;
            var played = log[index];
            string lead = Loc.Format("ui.weather.lead", Loc.Army(played.Side), Ui.Roman(played.Turn));
            if (notice && index + 1 < log.Count && log[index + 1] is { Kind: LogKind.WeatherCleared, Flag: true } displaced)
                lead += " " + Loc.Format("log.weatherDisplaced", Loc.CardName(displaced.Card));
            if (!notice && card == CardId.LightningStorm)
                foreach (var m in _match.Markers)
                    if (m.Kind == MarkerKind.Lightning) lead += " " + Loc.Format("ui.conditions.lightning", m.Pos);
            return lead;
        }

        void BuildCondition(CardId card, bool notice)
        {
            if (UiProfile.Mobile)
            {
                BuildConditionMobile(card, notice);
                return;
            }
            const float w = 1140f, padX = 56f, padY = 48f, cardW = 285f, cardH = 400f, gap = 18f;
            const float columnX = padX + cardW + 52f, columnWidth = w - columnX - padX;

            // Built off-panel first: the panel's height follows the wrapped text.
            var column = Ui.Rect(_overlayContent, "Column", 0f, 0f, columnWidth, 0f);
            float y = 0f;
            var eyebrow = Ui.Label(column, Loc.Get(notice ? "ui.weather.eyebrow" : "ui.weather.eyebrowActive"), 0f, y,
                columnWidth, 24f);
            eyebrow.fontSize = 18f;
            eyebrow.characterSpacing = 20f;
            eyebrow.color = Ui.Rubric;
            y += 24f + gap;

            TextMeshProUGUI Wrapped(string text, TMP_FontAsset font, float size, Color color, float x, float width)
            {
                var t = Ui.Text(column, text, font, size, color, x, y, width, 0f);
                t.rectTransform.sizeDelta = new Vector2(width, t.GetPreferredValues(text, width, 0f).y);
                return t;
            }

            y += Wrapped(Ui.Initial(Loc.Get("ui.weather.title." + card), 122), Ui.TitleFont, 54f, Ui.InkText, 0f, columnWidth)
                .rectTransform.sizeDelta.y + gap;

            y += Wrapped(ConditionLead(card, notice), Ui.ItalicFont, 22f, Ui.Ink, 0f, columnWidth).rectTransform.sizeDelta.y + gap;

            const float boxPadX = 22f, boxPadY = 18f;
            var box = Ui.Sliced(column, "Effect", Ui.Framed, 0f, y, columnWidth, 0f);
            float effectY = y;
            y += boxPadY;
            float effectHeight = Wrapped(Loc.CardText(card), Ui.BodyFont, 25f, Ui.InkText, boxPadX, columnWidth - boxPadX * 2f)
                .rectTransform.sizeDelta.y;
            box.rectTransform.sizeDelta = new Vector2(columnWidth, effectHeight + boxPadY * 2f);
            y = effectY + effectHeight + boxPadY * 2f + gap;

            const float chipHeight = 32f, chipGap = 8f;
            float chipX = 0f;
            foreach (var label in new[]
                     {
                         Loc.Get("ui.weather.both"), Loc.Get("ui.weather.lasting"),
                         Loc.Format("ui.weather.listed", Loc.Get("ui.conditions.header")),
                     })
            {
                var chip = Chip(column, label, chipX, y).rectTransform;
                if (chipX > 0f && chipX + chip.sizeDelta.x > columnWidth)
                {
                    chipX = 0f;
                    y += chipHeight + chipGap;
                    chip.anchoredPosition = new Vector2(0f, -y);
                }
                chipX += chip.sizeDelta.x + chipGap;
            }
            y += chipHeight + gap + 8f;

            const float buttonHeight = 58f;
            var button = (RectTransform)FitButton(column, Loc.Get(notice ? "ui.weather.button" : "ui.weather.close"), true,
                buttonHeight, 22f, 40f, notice ? DismissWeather : CloseCondition).transform;
            button.anchoredPosition = new Vector2(0f, -y);
            float noteX = button.sizeDelta.x + 18f;
            Ui.Text(column, Loc.Format("ui.weather.remove", Loc.CardName(CardId.DawnPrayer)), Ui.ItalicFont, 18f,
                Ui.InkBody, noteX, y, columnWidth - noteX, buttonHeight, TextAlignmentOptions.MidlineLeft);
            y += buttonHeight;

            float h = padY * 2f + Mathf.Max(cardH, y);
            var panel = OverlayPanel(w, h);
            Ui.Card(panel, CardCatalog.Get(card), cardW / Ui.CardWidth).anchoredPosition = new Vector2(padX, -(h - cardH) * 0.5f);
            column.SetParent(panel, false);
            column.anchoredPosition = new Vector2(columnX, -(h - y) * 0.5f);
        }

        void OpenCondition(CardId card)
        {
            _conditionView = card;
            RefreshOverlay();
        }

        void CloseCondition()
        {
            _conditionView = null;
            RefreshOverlay();
        }

        void DismissWeather()
        {
            if (_weatherNotices.Count == 0) return;
            var card = _match.Log[_weatherNotices[0]].Card;
            _matchFx.OnWeatherAcknowledged(card);
            OnWeatherBoardFx(card);
            _weatherNotices.RemoveAt(0);
            Refresh();
            ScheduleAi();
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

            var tabs = new[] { _human, Opponent };
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

        (LogEntry fall, int captures, int saves, int played) GameOverStats()
        {
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
            return (fall, captures, saves, played);
        }

        void BuildGameOver()
        {
            if (UiProfile.Mobile)
            {
                BuildGameOverMobile();
                return;
            }
            var winner = _match.Winner ?? Side.White;
            var loser = winner.Other();
            bool won = winner == _human;
            var (fall, captures, saves, played) = GameOverStats();

            const float w = 980f, h = 657f, statsY = 421f, statsHeight = 104f, statsX = 56f;
            var panel = OverlayPanel(w, h);
            Eyebrow(panel, Loc.Get("ui.gameOver.header"), 48f);
            const float token = 110f;
            Ui.Image(panel, "Sovereign", Ui.UnitArt(UnitType.Sovereign, loser), (w - token) * 0.5f, 90f, token, token);
            Cross(panel, (w - token) * 0.5f, 90f, token, Ui.Rubric, 5f);
            _gameOverTitle = Ui.Text(panel, Loc.Get(won ? "ui.gameOver.victory" : "ui.gameOver.defeat"), Ui.FrakturFont, 120f,
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

            // An online match has no rematch yet: both players go back to the menu to open a new session.
            if (_net != null)
            {
                Row(panel, 551f, 14f,
                    FitButton(panel, Loc.Get("ui.gameOver.menu"), true, 58f, 22f, 30f, LoadMenu),
                    FitButton(panel, Loc.Get("ui.gameOver.review"), false, 58f, 22f, 30f, Review));
                return;
            }
            Row(panel, 551f, 14f,
                FitButton(panel, Loc.Get("ui.gameOver.button"), true, 58f, 22f, 30f, NewMatch),
                FitButton(panel, Loc.Get("ui.gameOver.review"), false, 58f, 22f, 30f, Review),
                FitButton(panel, Loc.Get("ui.gameOver.menu"), false, 58f, 22f, 30f, LoadMenu));
        }

        void Review()
        {
            _reviewing = true;
            Refresh();
        }

        void BuildPause()
        {
            if (UiProfile.Mobile)
            {
                BuildPauseMobile();
                return;
            }
            const float w = 580f, h = 632f, buttonX = 52f, buttonY = 214f, step = 68f;
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
            Ui.Button(panel, Loc.Get("ui.pause.settings"), false, buttonX, buttonY + step * 2f, bw, 56f, OpenOptions, 22f);
            string reduce = FxSettings.ReduceFlashesAndShakes ? "ui.pause.reduceMotion.on" : "ui.pause.reduceMotion.off";
            Ui.Button(panel, Loc.Get(reduce), false, buttonX, buttonY + step * 3f, bw, 56f, () =>
            {
                FxSettings.ReduceFlashesAndShakes = !FxSettings.ReduceFlashesAndShakes;
                RefreshOverlay();
            }, 20f);
            Ui.Danger(Ui.Button(panel, Loc.Get("ui.pause.abandon"), false, buttonX, buttonY + step * 4f, bw, 56f, () =>
            {
                _confirmAbandon = true;
                RefreshOverlay();
            }, 22f));
            Ui.Text(panel, Loc.Get("ui.pause.hint"), Ui.ItalicFont, 18f, Ui.InkBody, 0f, 564f, w, 26f, TextAlignmentOptions.Top);
        }

        // The options sit on their own layer: the overlay content is rebuilt on every refresh.
        void OpenOptions()
        {
            if (_options == null) _options = new OptionsModal(() => _overlay, Relocalize, () => _options = null);
        }

        void Relocalize()
        {
            foreach (var (text, get) in _localized) text.text = get();
            RefreshHistory(force: true);
            Refresh();
        }

        TextMeshProUGUI Localized(TextMeshProUGUI text, System.Func<string> get)
        {
            text.text = get();
            _localized.Add((text, get));
            return text;
        }

        void BuildConfirm()
        {
            if (UiProfile.Mobile)
            {
                BuildConfirmMobile();
                return;
            }
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
                FitButton(panel, Loc.Get("ui.confirm.abandon"), true, 58f, 22f, 30f, LoadMenu));
        }

        void BuildDisconnected()
        {
            if (UiProfile.Mobile)
            {
                BuildNoticeMobile(Loc.Get("ui.mp.lost.eyebrow"), Loc.Get("ui.mp.lost.title"), Loc.Get("ui.mp.lost.text"),
                    Loc.Get("ui.gameOver.menu"), LoadMenu);
                return;
            }
            const float w = 760f, h = 318f;
            var panel = OverlayPanel(w, h);
            Eyebrow(panel, Loc.Get("ui.mp.lost.eyebrow"), 44f).color = Ui.Rubric;
            Title(panel, Loc.Get("ui.mp.lost.title"), 84f, 44f);
            Ui.Text(panel, Loc.Get("ui.mp.lost.text"), Ui.BodyFont, 22f, Ui.InkText, 0f, 160f, w, 32f, TextAlignmentOptions.Top);
            Row(panel, 217f, 14f, FitButton(panel, Loc.Get("ui.gameOver.menu"), true, 58f, 22f, 30f, LoadMenu));
        }

        static void LoadMenu()
        {
            var net = NetSession.Online;
            if (net != null) net.Leave();
            SceneTransition.Load(MainMenu.SceneName);
        }

        static float ToastW => UiProfile.Mobile ? MToastWidth : ToastWidth;

        void ShowTurnBanner(LogEntry e)
        {
            bool m = UiProfile.Mobile;
            float w = ToastW, height = m ? 84f : 128f, token = m ? 36f : 60f;
            var previous = _turnBanner;
            _turnBanner = null;
            if (previous != null) DismissToast(previous);
            var toast = _turnBanner = Toast(height, dark: true);
            bool human = e.Side == _human;
            var eyebrow = Ui.Label(toast, Loc.Format("ui.toast.turn", Ui.Roman(e.Turn)), 0f, m ? 11f : 18f, w, m ? 16f : 22f,
                TextAlignmentOptions.Top);
            eyebrow.fontSize = m ? 12f : 16f;
            eyebrow.characterSpacing = m ? 14f : 20f;
            eyebrow.color = Ui.Table;
            var title = Ui.Text(toast, Loc.Format("ui.toast.turnOf", Loc.Army(e.Side)), Ui.TitleFont, m ? 24f : 38f,
                Ui.Parchment, 0f, m ? 27f : 42f, w, m ? 30f : 44f, TextAlignmentOptions.Top);
            title.textWrappingMode = TextWrappingModes.NoWrap;
            Ui.Text(toast, human
                    ? Loc.Format("ui.toast.you", e.Amount, Loc.Get(e.Flag ? "log.cardDrawn" : "log.deckEmpty"))
                    : Loc.Get(ModeKey("ui.toast.ai")),
                Ui.ItalicFont, m ? 13.5f : 19f, new Color(0.89f, 0.83f, 0.69f), 0f, m ? 57f : 88f, w, m ? 18f : 26f,
                TextAlignmentOptions.Top);
            float half = title.GetPreferredValues(title.text).x * 0.5f + (m ? 12f : 26f);
            foreach (float side in new[] { -1f, 1f })
                Ui.Image(toast, "Sovereign", Ui.UnitArt(UnitType.Sovereign, e.Side),
                    w * 0.5f + side * (half + token * 0.5f) - token * 0.5f, (height - token) * 0.5f, token, token);
            PlayTurnBannerFx(toast, title);
        }

        void ShowResult(int index)
        {
            var log = _match.Log;
            var e = log[index];
            var declared = FindDeclared(index);
            CardId? reaction = null;
            int paid = 0;
            for (int i = index - 1; i >= 0 && log[i] != declared; i--)
                if (log[i].Kind == LogKind.Reacted)
                {
                    reaction = log[i].Card;
                    paid = log[i].Amount;
                }
            Side attackerSide = declared.Side, defenderSide = attackerSide.Other();
            string attacker = Loc.UnitName(declared.Unit), defender = Loc.UnitName(declared.OtherUnit);
            string title, text;
            var chips = new List<string>();
            bool m = UiProfile.Mobile;
            float w = ToastW, height = m ? 136f : 188f;
            var toast = Toast(height, dark: false);
            // The visuals are drawn for a 100-unit square and shrink with it on a phone.
            float visualX = m ? 12f : 28f, visual = m ? 56f : 100f, k = visual / 100f;
            float visualY = (height - visual) * 0.5f;

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
                    if (e.Flag)
                        Ui.Image(toast, "Shield", Ui.Art("Badge_Shield"), visualX + 14f * k, visualY + 14f * k, 72f * k, 72f * k);
                    else
                        Ui.Card(toast, CardCatalog.Get(e.Card), 89f * k / Ui.CardWidth).anchoredPosition =
                            new Vector2(visualX + 5f * k, -(height - 125f * k) * 0.5f);
                    break;
                case LogKind.Retreated:
                    title = Loc.CardName(e.Card);
                    text = e.Card == CardId.Countercharge ? Loc.Format("ui.toast.countercharge", defender, declared.To, e.To)
                        : e.Flag ? Loc.Format("ui.toast.retreatRanged", defender, declared.To, e.To)
                        : Loc.Format("ui.toast.retreat", defender, declared.To, e.To, attacker);
                    chips.Add(Loc.Get("ui.chip.noLoss"));
                    if (!e.Flag) chips.Add(Loc.Format("ui.chip.ground", Loc.SideName(defenderSide)));
                    chips.Add(Loc.Format("ui.chip.morale", Loc.SideName(defenderSide), paid));
                    Ui.Image(toast, "Token", Ui.UnitArt(declared.OtherUnit, defenderSide), visualX + 12f * k, visualY + 18f * k,
                        76f * k, 76f * k);
                    Arrow(toast, visualX + 22f * k, visualY + 6f * k, 56f * k);
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
                    Ui.Image(toast, "Token", Ui.UnitArt(e.Unit, e.Side), visualX + 12f * k, visualY + 12f * k, 76f * k, 76f * k);
                    Cross(toast, visualX + 12f * k, visualY + 12f * k, 76f * k, Ui.Rubric, m ? 3f : 4f);
                    break;
            }

            if (m)
            {
                ResultTextMobile(toast, title, text, chips, visualX + visual + 12f);
                return;
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
            var toast = Ui.Rect(_toasts, "Toast", 0f, 0f, ToastW, height);
            var group = toast.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            Ui.Modal(toast, 0f, 0f, ToastW, height, dark);
            var button = toast.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(() => DismissToast(toast));
            _toastList.Add(toast);
            while (_toastList.Count > (UiProfile.Mobile ? 2 : MaxToasts)) DismissToast(_toastList[0]);
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
            int held = TakeHeldDraws();
            Refresh();
            if (FxDirector != null) PlayDraws(held);
            ScheduleAi();
        }

        void LayoutToasts()
        {
            float y = 0f;
            foreach (var toast in _toastList)
            {
                toast.anchoredPosition = new Vector2(0f, -y);
                y += toast.sizeDelta.y + (UiProfile.Mobile ? 8f : ToastGap);
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
                    _spot[x, y].rectTransform.anchoredPosition = origin;
                    ((RectTransform)_hint[x, y].transform.parent).anchoredPosition = origin;
                }
            for (int i = 0; i < Match.Size; i++)
            {
                _fileLabels[i].text = ((char)(_flipped ? 'h' - i : 'a' + i)).ToString();
                _rankLabels[i].text = (_flipped ? i + 1 : Match.Size - i).ToString();
            }
            BuildArmies();
        }

        void BuildArmies()
        {
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
            // Feel animates the Holder at scale 1, leaving the root to the board's own move and pop routines.
            var holder = Ui.Centered(Ui.Rect(rt, "Holder", 0, 0, PieceSize, PieceSize));
            var piece = new Piece
            {
                Id = unit.Id,
                Rt = rt,
                Holder = holder,
                Group = rt.gameObject.AddComponent<CanvasGroup>(),
                Body = Ui.Image(holder, "Token", Ui.UnitArt(unit.Type, unit.Side), 0, 0, PieceSize, PieceSize),
                Shield = Ui.Image(holder, "Shield", Ui.Art("Badge_Shield"), -4, 44, 28, 28),
                Hammer = Ui.Image(holder, "Hammer", Ui.Art("Badge_Hammer"), 46, 44, 28, 28),
            };
            piece.Group.blocksRaycasts = false;
            _pieces[unit.Id] = piece;
            return piece;
        }

        void BuildUi()
        {
            // Everything under this object is generated here; a saved scene may still hold an old copy of it.
            for (int i = transform.childCount - 1; i >= 0; i--) Destroy(transform.GetChild(i).gameObject);
            if (FindAnyObjectByType<EventSystem>() == null)
                new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            var camera = Camera.main;
            if (camera != null) camera.backgroundColor = Ui.Table;
            ApplyProfile();
            _fx = FxRoot.Create(transform, camera);
            if (_fx != null) _fx.UseFont(Ui.ItalicFont);

            // The board sits on its own canvas, under the world camera, so particles and post-processing reach it
            // while the HUD canvas stays crisp above. Both canvases share one scaler setup, so their stages line up.
            var world = CreateCanvas("World Canvas", _fx != null ? _fx.WorldCamera : camera, FxLayers.Board,
                FxLayers.World, out _worldScaler);
            Ui.Stretch(Ui.Image(world, "Background", Ui.Art("Background"), 0, 0, 0, 0).rectTransform);
            _worldStage = Ui.Stretch(Ui.Rect(world, "Stage", 0, 0, 0, 0));
            _worldRaycaster = world.GetComponent<GraphicRaycaster>();

            var root = CreateCanvas("Canvas", _fx != null ? _fx.HudCamera : camera, FxLayers.Hud, FxLayers.Ui,
                out _scaler);
            _stage = Ui.Stretch(Ui.Rect(root, "Stage", 0, 0, 0, 0));
            _hudRaycaster = root.GetComponent<GraphicRaycaster>();

            BuildBoard();
            if (FxDirector != null) FxDirector.LockChanged += OnFxLock;
            BuildHud();

            _toasts = Ui.Rect(root, "Toasts", 0f, 0f, ToastW, 0f);
            _toasts.anchorMin = _toasts.anchorMax = _toasts.pivot = new Vector2(0.5f, 1f);
            PlaceToasts();
            BuildCardFxLayer(root);

            _overlay = Ui.Stretch(Ui.Rect(root, "Overlay", 0, 0, 0, 0));
            var modals = _overlay.gameObject.AddComponent<Canvas>();
            modals.overrideSorting = true;
            modals.sortingLayerName = FxLayers.Modals;
            _overlay.gameObject.AddComponent<GraphicRaycaster>();
            _overlayShade = _overlay.gameObject.AddComponent<Image>();
            _overlayShade.color = ScrimColor;
            var backdrop = Ui.Stretch(Ui.Image(_overlay, "Backdrop", Ui.Solid, 0, 0, 0, 0, raycast: true).rectTransform);
            backdrop.GetComponent<Image>().color = Color.clear;
            backdrop.gameObject.AddComponent<Button>().onClick.AddListener(CloseDiscard);
            _backdrop = backdrop.gameObject;
            _confront = Ui.Stretch(Ui.Rect(_overlay, "Confront", 0, 0, 0, 0));
            _overlayContent = Ui.Rect(_overlay, "Content", 0, 0, UiProfile.Design.x, UiProfile.Design.y);
            _overlayContent.anchorMin = _overlayContent.anchorMax = _overlayContent.pivot = new Vector2(0.5f, 0.5f);
            _overlayContent.anchoredPosition = Vector2.zero;
            // Unscaled, above the content: the phone's own modals are drawn here at the size of its area.
            _overlayNative = Ui.Stretch(Ui.Rect(_overlay, "Native", 0, 0, 0, 0));
        }

        RectTransform CreateCanvas(string name, Camera camera, string sortingLayer, int layer, out CanvasScaler scaler)
        {
            var go = new GameObject(name, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster)) { layer = layer };
            go.transform.SetParent(transform, false);
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = CanvasDistance;
            canvas.sortingLayerName = sortingLayer;
            scaler = go.GetComponent<CanvasScaler>();
            UiProfile.Apply(scaler, s_size);
            return (RectTransform)go.transform;
        }

        void BuildHud()
        {
            if (UiProfile.Mobile)
            {
                BuildMobileHud();
                return;
            }
            BuildHeader();
            BuildConditions();
            _hand = Ui.Rect(_stage, "Hand", CenterX, HandY, CenterWidth, StageHeight - HandY);
            Ui.Anchor(_hand, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), Stage);
            _handGroup = _hand.gameObject.AddComponent<CanvasGroup>();
            BuildRightColumn();
        }

        // Toasts are drawn for the Medium board column; they follow its center and shrink to a narrower one.
        void PlaceToasts()
        {
            _toasts.sizeDelta = new Vector2(ToastW, 0f);
            if (UiProfile.Mobile)
            {
                PlaceToastsMobile();
                return;
            }
            _toasts.anchorMin = _toasts.anchorMax = new Vector2(0.5f, 1f);
            float fit = Mathf.Min(1f, (CenterWidth + 40f) / ToastWidth);
            _toasts.localScale = new Vector3(fit, fit, 1f);
            _toasts.anchoredPosition = new Vector2(CenterX + CenterWidth * 0.5f - StageWidth * 0.5f, -(ColumnTop + 16f));
        }

        // The HUD is laid out for one profile, so another one means building it again. The board, its pieces and
        // the overlays stay: they only take a new fit.
        void ChangeProfile()
        {
            ApplyProfile();
            UiProfile.Apply(_scaler, s_size);
            UiProfile.Apply(_worldScaler, s_size);
            if (_waitingFade != null) StopCoroutine(_waitingFade);
            _waitingFade = null;
            for (int i = _stage.childCount - 1; i >= 0; i--) Destroy(_stage.GetChild(i).gameObject);
            _localized.Clear();
            _handCards.Clear();
            _hoveredCard = -1;
            _historyOpen = false;
            _zoom = -1;
            _infoTab = InfoTab.None;
            _armies[0] = _armies[1] = null;
            BuildHud();
            PlaceToasts();
            BuildArmies();
            _handGroup.alpha = _rightColumn.alpha = _armies[(int)_human].Group.alpha = _waiting ? WaitingAlpha : 1f;
            _canvasSize = default;
            _logShown = -1;
            Refresh();
            if (_options != null) _options.Relayout();
        }

        void BuildHeader()
        {
            bool compact = Compact;
            float font = compact ? 17f : 20f;
            var size = new Vector2(StageWidth, HeaderHeight);
            Vector2 left = new Vector2(0f, 1f), center = new Vector2(0.5f, 1f), right = new Vector2(1f, 1f);
            var header = Ui.Anchor(Ui.Rect(_stage, "Header", 0, 0, StageWidth, HeaderHeight), left, right, Stage);
            Ui.Anchor(Ui.Line(header, 0, HeaderHeight - 1.5f, StageWidth, 1.5f).rectTransform, left, right, size);
            Ui.Anchor(Ui.Line(header, 0, HeaderHeight + 2f, StageWidth, 1f).rectTransform, left, right, size);
            Ui.Text(header, Ui.Brand(Loc.Get("ui.title"), 120), Ui.TitleFont, compact ? 24f : 30f, Ui.InkText, LeftX, 0,
                LeftWidth, HeaderHeight, TextAlignmentOptions.Left).textWrappingMode = TextWrappingModes.NoWrap;
            _phases = Ui.Text(header, "", Ui.TitleFont, font, Ui.InkSoft, CenterX, 0, CenterWidth, HeaderHeight,
                TextAlignmentOptions.Center);
            _phases.characterSpacing = 5f;
            Ui.Anchor(_phases.rectTransform, center, center, size);
            float buttonWidth = compact ? 164f : 196f, buttonHeight = compact ? 38f : 46f;
            _turn = Ui.Text(header, "", Ui.ItalicFont, font, Ui.InkBody, RightX, 0, RightWidth - buttonWidth - 16f,
                HeaderHeight, TextAlignmentOptions.Right);
            Ui.Anchor(_turn.rectTransform, right, right, size);
            _endTurn = Ui.Button(header, Loc.Get("ui.endTurn"), false, RightX + RightWidth - buttonWidth,
                (HeaderHeight - buttonHeight) * 0.5f, buttonWidth, buttonHeight, OnEndTurn, font);
            Ui.Anchor((RectTransform)_endTurn.transform, right, right, size);
        }

        ArmyPanel BuildArmy(Side side, bool human)
        {
            if (UiProfile.Mobile) return BuildArmyMobile(side, human);
            bool compact = Compact;
            float w = LeftWidth, pad = PanelPad, top = compact ? 12f : 18f, emblem = compact ? 38f : 50f;
            float height = human ? PlayerPanelHeight : OpponentPanelHeight;
            float y = human ? ColumnBottom - height : ColumnTop;
            var panel = Ui.Panel(_stage, $"Army {side}", LeftX, y, w, height).rectTransform;
            if (human) Ui.Anchor(panel, Vector2.zero, Vector2.zero, Stage);
            var ui = new ArmyPanel { Root = panel, Group = panel.gameObject.AddComponent<CanvasGroup>() };

            float textX = pad + emblem + 12f, textWidth = w - textX - pad;
            Ui.Image(panel, "Emblem", Ui.UnitArt(UnitType.Sovereign, side), pad, top, emblem, emblem);
            Localized(Ui.Text(panel, "", Ui.TitleFont, compact ? 21f : 26f, Ui.InkText, textX, top - 2f, textWidth, 32),
                () => Loc.Army(side)).textWrappingMode = TextWrappingModes.NoWrap;
            ui.Subtitle = Ui.Label(panel, "", textX, top + (compact ? 22f : 30f), textWidth);
            ui.Subtitle.textWrappingMode = TextWrappingModes.NoWrap;

            float moraleY = top + emblem + (compact ? 9f : 16f);
            float pip = compact ? 14f : 18f, pipStep = compact ? 17f : 23f, pipsX = pad + (compact ? 64f : 102f);
            Localized(Ui.Label(panel, "", pad, moraleY, 100), () => Loc.Get("ui.morale"));
            ui.Pips = new Image[Match.MaxMorale];
            for (int i = 0; i < ui.Pips.Length; i++)
                ui.Pips[i] = Ui.Image(panel, "Pip", Ui.Art("Pip_Off"), pipsX + i * pipStep, moraleY + 1f, pip, pip);
            ui.Morale = Ui.Text(panel, "", Ui.BodyFont, compact ? 15.5f : 17f, Ui.InkBody, w - pad - 80f, moraleY - 3f,
                80, 24, TextAlignmentOptions.TopRight);

            float rowY = moraleY + (compact ? 27f : 36f), tilesY;
            if (human)
            {
                float orderWidth = compact ? 36f : 44f, orderHeight = compact ? 33f : 40f;
                ui.OrderY = compact ? rowY - 4f : rowY;
                ui.OrderStep = orderWidth + 4f;
                ui.OrderRight = w - pad + 4f;
                ui.Orders = Ui.Label(panel, "", pad, rowY + (compact ? 4f : 10f), 220);
                ui.OrderIcons = new Image[Match.OrdersPerTurn + 2];
                for (int i = 0; i < ui.OrderIcons.Length; i++)
                    ui.OrderIcons[i] = Ui.Image(panel, "Order", Ui.Art("Order_Off"), 0, ui.OrderY, orderWidth, orderHeight);
                float headerY = rowY + (compact ? 38f : 56f);
                Localized(Ui.Label(panel, "", pad, headerY, 220),
                    () => Loc.Format("ui.reserveHeader", Loc.Get("ui.ranks." + side)));
                ui.ReserveHint = Ui.Text(panel, "", Ui.BodyFont, 15f, Ui.InkBody, w - pad - 180f, headerY - 1f, 180, 22,
                    TextAlignmentOptions.TopRight);
                ui.ReserveHint.gameObject.SetActive(!compact);
                tilesY = headerY + (compact ? 24f : 33f);
            }
            else
            {
                ui.Summary = Ui.Text(panel, "", Ui.BodyFont, compact ? 15.5f : 17f, Ui.InkBody, pad, rowY, w - pad * 2f, 24);
                tilesY = rowY + (compact ? 29f : 38f);
            }
            BuildReserve(panel, ui, side, human, tilesY);
            return ui;
        }

        void BuildReserve(RectTransform panel, ArmyPanel ui, Side side, bool human, float y)
        {
            int n = Match.Recruitable.Length;
            bool compact = Compact;
            int columns = compact ? n : 3;
            const float textX = 49f;
            float gap = compact ? 5f : 8f, height = compact ? 50f : 52f;
            float w = (LeftWidth - PanelPad * 2f - gap * (columns - 1)) / columns;
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
                float x = PanelPad + i % columns * (w + gap), top = y + i / columns * (height + gap);
                var tile = Ui.Image(panel, "Reserve " + type, Ui.Tile, x, top, w, height, raycast: human);
                tile.type = Image.Type.Sliced;
                if (compact)
                {
                    const float icon = 27f, seal = 17f, count = 22f;
                    float left = (w - (human ? count + 2f + seal : count)) * 0.5f;
                    Ui.Image(tile.transform, "Icon", Ui.UnitArt(type, side), (w - icon) * 0.5f, 3f, icon, icon);
                    ui.ReserveCounts[i] = Ui.Text(tile.transform, "", Ui.BodyFont, 15f, Ui.InkText, left, 30f, count, 18,
                        TextAlignmentOptions.Top);
                    if (human) Ui.Seal(tile.transform, Match.Cost(type), left + count + 2f, 30f, seal);
                }
                else
                {
                    Ui.Image(tile.transform, "Icon", Ui.UnitArt(type, side), 6, 8, 36, 36);
                    Localized(Ui.Text(tile.transform, "", Ui.BodyFont, 16f, Ui.InkText, textX, 6, w - textX - 3f, 22),
                        () => Loc.UnitName(type)).textWrappingMode = TextWrappingModes.NoWrap;
                    ui.ReserveCounts[i] = Ui.Text(tile.transform, "", Ui.BodyFont, 15f, Ui.InkText, textX, 27, 22, 19);
                    if (human) Ui.Seal(tile.transform, Match.Cost(type), textX + 22f, 26, 19);
                }
                if (!human) continue;
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
            float pad = PanelPad, top = Compact ? 12f : 18f, listY = Compact ? 38f : 48f;
            float y = ColumnTop + OpponentPanelHeight + Gap;
            float height = ColumnBottom - PlayerPanelHeight - Gap - y;
            var panel = Ui.Panel(_stage, "Conditions Panel", LeftX, y, LeftWidth, height).rectTransform;
            Ui.Anchor(panel, Vector2.zero, Vector2.up, Stage);
            Localized(Ui.Label(panel, "", pad, top, 220), () => Loc.Get("ui.conditions.header")).color = Ui.Rubric;
            _conditionsState = Ui.Label(panel, "", LeftWidth - pad - 200f, top, 200, align: TextAlignmentOptions.TopRight);
            _conditions = Ui.Rect(panel, "Conditions", 0, listY, LeftWidth, height - listY - top);
            Ui.Anchor(_conditions, Vector2.zero, Vector2.up, new Vector2(LeftWidth, height));
        }

        void RefreshConditions()
        {
            if (UiProfile.Mobile)
            {
                RefreshConditionsMobile();
                return;
            }
            for (int i = _conditions.childCount - 1; i >= 0; i--) Destroy(_conditions.GetChild(i).gameObject);
            bool compact = Compact;
            var active = _match.Conditions;
            // The narrow column has no room for the sentence next to the header.
            _conditionsState.text = compact
                ? active.Count == 0 ? "" : Loc.Format("ui.moraleValue", active.Count, Match.MaxConditions)
                : active.Count == 0 ? Loc.Get("ui.conditions.none")
                : Loc.Format("ui.conditions.active", active.Count, Match.MaxConditions);
            if (active.Count == 0)
            {
                float margin = compact ? 20f : 40f;
                var empty = Ui.Text(_conditions, Loc.Get("ui.conditions.empty"), Ui.ItalicFont, compact ? 16f : 19f,
                    Ui.InkBody, margin, 0, LeftWidth - margin * 2f, 0f, TextAlignmentOptions.Center);
                Ui.Stretch(empty.rectTransform).offsetMin = new Vector2(margin, 0f);
                empty.rectTransform.offsetMax = new Vector2(-margin, 0f);
                return;
            }

            const float rowGap = 8f;
            float x = PanelPad, rowHeight = compact ? 64f : 74f, pad = compact ? 5f : 7f;
            float artW = compact ? 56f : 84f, artH = compact ? 40f : 60f;
            float textX = pad + artW + (compact ? 9f : 12f), rowWidth = LeftWidth - x * 2f;
            float textWidth = rowWidth - textX - (compact ? 6f : 10f);
            for (int i = 0; i < active.Count; i++)
            {
                var card = active[i];
                var row = Ui.Image(_conditions, "Condition " + card, Ui.Tile, x, i * (rowHeight + rowGap), rowWidth, rowHeight);
                row.type = Image.Type.Sliced;
                Ui.MakeButton(row, () => OpenCondition(card));
                ConditionArt(row.transform, card, pad, pad, artW, artH);
                Ui.Text(row.transform, Loc.CardName(card), Ui.TitleFont, compact ? 17f : 20f, Ui.InkText, textX, pad,
                    textWidth, 24f).textWrappingMode = TextWrappingModes.NoWrap;
                string detail = Loc.Get("ui.conditions." + card);
                if (card == CardId.LightningStorm)
                    foreach (var m in _match.Markers)
                        if (m.Kind == MarkerKind.Lightning) detail = Loc.Format("ui.conditions.lightning", m.Pos);
                Ui.Text(row.transform, detail, Ui.BodyFont, compact ? 15f : 16f, Ui.InkText, textX, compact ? 25f : 31f,
                    textWidth, compact ? 36f : 40f);
            }
            if (active.Count < Match.MaxConditions)
                Ui.Text(_conditions, Loc.Get("ui.conditions.stacking"), Ui.ItalicFont, compact ? 15f : 16f, Ui.InkBody, x,
                    active.Count * (rowHeight + rowGap) + 4f, rowWidth, 44f, TextAlignmentOptions.Top);
        }

        // The arched window of the condition frame: a rounded mask cropped flat at the bottom by the outer rect.
        static void ConditionArt(Transform parent, CardId card, float x, float y, float w, float h)
        {
            const int radius = 22;
            var clip = Ui.Rect(parent, "Art", x, y, w, h);
            clip.gameObject.AddComponent<RectMask2D>();
            var window = Ui.Image(clip, "Window", Ui.Pill(Ui.Parchment, Ui.Parchment, radius, 0f), 0f, 0f, w, h + radius);
            window.type = Image.Type.Sliced;
            window.gameObject.AddComponent<Mask>();
            var art = Ui.Art("Card_" + card);
            float artWidth = art != null ? (h - 5f) * art.rect.width / art.rect.height : w;
            Ui.Image(window.transform, "Picture", art, (w - artWidth) * 0.5f, 5f, artWidth, h - 5f);
            Ui.Image(clip, "Rim", Ui.Pill(Color.clear, Ui.Ink, radius), 0f, 0f, w, h + radius).type = Image.Type.Sliced;
            Ui.Line(clip, 0f, h - 1f, w, 1f);
        }

        void FitBoard(Vector2 canvas)
        {
            if (UiProfile.Mobile)
            {
                FitBoardMobile(canvas);
                return;
            }
            float width = canvas.x - (StageWidth - CenterWidth);
            float height = canvas.y - ColumnTop - BoardClearance;
            float scale = Mathf.Min(width / BoardSize, height / (BoardSize - BoardMargin));
            float centerOffset = CenterX + CenterWidth * 0.5f - StageWidth * 0.5f;
            _boardRig.localScale = new Vector3(scale, scale, 1f);
            _boardRig.anchoredPosition =
                new Vector2(centerOffset - BoardSize * scale * 0.5f, -(ColumnTop - BoardMargin * scale));
        }

        // The rig takes the fit-to-screen scale, so the board inside stays at scale 1 for Feel to shake and pulse.
        void BuildBoard()
        {
            _boardRig = Ui.Rect(_worldStage, "Board Rig", 0, 0, BoardSize, BoardSize);
            _boardRig.anchorMin = _boardRig.anchorMax = new Vector2(0.5f, 1f);
            var board = Ui.Image(_boardRig, "Board", Ui.Art("Board"), 0, 0, BoardSize, BoardSize).rectTransform;
            FitBoard(Stage);
            _matchFx = new MatchFx(FxDirector, this);
            _board = board;
            _markRing = Resources.Load<Sprite>("MarkRing");
            // Known before the events are read: projectiles declared in this sync count from its very first event.
            _matchFx.PresentedElsewhere = square => _claims.Contains(square) || _projectileTargets.Contains(square);

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
            // Board FX get their own layers: Refresh and the confront clear the others.
            _boardFxUnder = Ui.Rect(board, "FX Under", 0, 0, BoardSize, BoardSize);
            _piecesLayer = Ui.Rect(board, "Pieces", 0, 0, BoardSize, BoardSize);
            _boardFxOver = Ui.Rect(board, "FX Over", 0, 0, BoardSize, BoardSize);
            var spotlight = Ui.Rect(board, "Spotlight", 0, 0, BoardSize, BoardSize);
            _spotGroup = spotlight.gameObject.AddComponent<CanvasGroup>();
            _focusLayer = Ui.Rect(board, "Focus", 0, 0, BoardSize, BoardSize);
            var over = Ui.Rect(board, "Squares", 0, 0, BoardSize, BoardSize);
            var dim = new Color(ScrimColor.r, ScrimColor.g, ScrimColor.b, SpotAlpha);

            for (int x = 0; x < Match.Size; x++)
                for (int y = 0; y < Match.Size; y++)
                {
                    var c = new Coord(x, y);
                    _under[x, y] = Ui.Image(under, "Under " + c, null, 0, 0, Square, Square);
                    _spot[x, y] = Ui.Image(spotlight, "Spot " + c, Ui.Solid, 0, 0, Square, Square);
                    _spot[x, y].color = dim;
                    _spot[x, y].enabled = false;

                    var hit = Ui.Image(over, c.ToString(), Ui.Solid, 0, 0, Square, Square, raycast: true);
                    hit.color = Color.clear;
                    var button = hit.gameObject.AddComponent<Button>();
                    button.transition = Selectable.Transition.None;
                    button.onClick.AddListener(() => OnSquareClicked(c));
                    Ui.OnHover(hit.gameObject, () => HoverSquare(c, true), () => HoverSquare(c, false));
                    _hint[x, y] = Ui.Image(hit.transform, "Hint", null, 0, 0, Square, Square);
                }
        }

        void BuildRightColumn()
        {
            bool compact = Compact;
            float w = RightWidth, pad = PanelPad;
            var column = Ui.Rect(_stage, "Right Column", RightX, 0f, w, StageHeight);
            Ui.Anchor(column, Vector2.right, Vector2.one, Stage);
            _rightColumn = column.gameObject.AddComponent<CanvasGroup>();
            _logLatest = null;
            _historyOpenPanel = null;

            var halo = Ui.Image(column, "Halo", Ui.Solid, -4f, ColumnTop - 4f, w + 8f, ActionPanelHeight + 8f);
            halo.color = new Color(Ui.Rubric.r, Ui.Rubric.g, Ui.Rubric.b, 0.22f);
            var action = Ui.Panel(column, "Action Panel", 0f, ColumnTop, w, ActionPanelHeight).rectTransform;
            float bandHeight = compact ? 36f : 44f, stepX = compact ? 150f : 190f;
            var band = Ui.Image(action, "Band", Ui.Solid, 2f, 2f, w - 4f, bandHeight);
            band.color = Ui.Rubric;
            var bandLabel = Localized(Ui.Label(band.transform, "", pad - 2f, 0f, 200f, bandHeight,
                TextAlignmentOptions.MidlineLeft), () => Loc.Get("ui.action.header"));
            bandLabel.fontSize = compact ? 15f : 17f;
            bandLabel.characterSpacing = compact ? 12f : 16f;
            bandLabel.color = Ui.Cream;
            _actionStep = Ui.Text(band.transform, "", Ui.ItalicFont, compact ? 16f : 18f, Ui.Cream, stepX, 0f,
                w - pad - 2f - stepX, bandHeight, TextAlignmentOptions.MidlineRight);
            float cardWidth = Ui.CardWidth * ActionCardScale, cardHeight = Ui.CardHeight * ActionCardScale;
            float cardGap = compact ? 9f : 14f, cardY = 2f + bandHeight + cardGap;
            _playSlot = _actionSlot = Ui.Rect(action, "Subject", (w - cardWidth) * 0.5f, cardY, cardWidth, cardHeight);
            float cellsY = cardY + cardHeight + cardGap;
            _actionCells = Ui.Rect(action, "Cells", pad, cellsY, w - pad * 2f, CellsHeight);
            float hintY = cellsY + CellsHeight + (compact ? 6f : 8f), hintHeight = compact ? 40f : 44f;
            _actionHint = Ui.Text(action, "", Ui.ItalicFont, compact ? 15.5f : 17f, Ui.InkBody, pad, hintY, w - pad * 2f,
                hintHeight, TextAlignmentOptions.Top);
            float buttonsY = hintY + hintHeight + (compact ? 4f : 6f);
            float cancelWidth = compact ? 112f : 130f, buttonHeight = compact ? 44f : 52f, buttonFont = compact ? 18f : 20f;
            _actionPrimary = Ui.Button(action, Loc.Get("ui.action.playCard"), true, pad, buttonsY,
                w - pad * 2f - 10f - cancelWidth, buttonHeight, OnActionPrimary, buttonFont);
            _actionCancel = Ui.Button(action, Loc.Get("ui.action.cancel"), false, w - pad - cancelWidth, buttonsY,
                cancelWidth, buttonHeight, () => { ClearMode(); Refresh(); }, buttonFont);
            Localized(_actionCancel.GetComponentInChildren<TextMeshProUGUI>(), () => Loc.Get("ui.action.cancel"));

            float deckY = ColumnTop + ActionPanelHeight + Gap;
            var deck = Ui.Panel(column, "Deck Panel", 0f, deckY, w, DeckPanelHeight).rectTransform;
            float pile = compact ? 0.66f : 1f, pileX = compact ? 14f : 18f, pileY = compact ? 7f : 15f;
            float pileStep = compact ? 3f : 4f, backWidth = 58f * pile, backHeight = 81f * pile;
            for (int i = 2; i >= 0; i--)
                _deckPile = Ui.Image(deck, "Back", Ui.Art("CardBack"), pileX + i * pileStep, pileY + i * pileStep,
                    backWidth, backHeight).rectTransform;
            float textGap = compact ? 10f : 12f, labelY = compact ? 12f : 18f, countY = compact ? 32f : 40f;
            float countFont = compact ? 20f : 24f, discardY = pileY - 2f;
            float deckTextX = pileX + backWidth + pileStep * 2f + textGap;
            float discardX = Mathf.Round(w * 0.5f) + 7f, discardTextX = discardX + 74f * pile + textGap;
            Localized(Ui.Label(deck, "", deckTextX, labelY, 110f), () => Loc.Get("ui.deck")).textWrappingMode =
                TextWrappingModes.NoWrap;
            _deck = Ui.Text(deck, "", Ui.TitleFont, countFont, Ui.InkText, deckTextX, countY, 110f, 30f);
            _deckCaption = Ui.Text(deck, "", Ui.BodyFont, 15f, Ui.InkBody, deckTextX, 72f, 110f, 22f);
            _discardPile = Ui.Rect(deck, "Discard Pile", discardX, discardY, 74f, 93f);
            _discardPile.localScale = new Vector3(pile, pile, 1f);
            float discardTextWidth = w - pileX - discardTextX;
            Localized(Ui.Label(deck, "", discardTextX, labelY, discardTextWidth), () => Loc.Get("ui.discard"))
                .textWrappingMode = TextWrappingModes.NoWrap;
            _discard = Ui.Text(deck, "", Ui.TitleFont, countFont, Ui.InkText, discardTextX, countY, discardTextWidth, 30f);
            _discardLast = Ui.Text(deck, "", Ui.BodyFont, 15f, Ui.InkBody, discardTextX, 72f, discardTextWidth, 40f);
            _deckCaption.gameObject.SetActive(!compact);
            _discardLast.gameObject.SetActive(!compact);
            var discardHit = Ui.Image(deck, "Open Discard", Ui.Solid, discardX - 6f, 6f, w - discardX, DeckPanelHeight - 12f,
                raycast: true);
            discardHit.color = Color.clear;
            var discardButton = discardHit.gameObject.AddComponent<Button>();
            discardButton.transition = Selectable.Transition.None;
            discardButton.onClick.AddListener(OnDiscardClicked);
            Ui.OnHover(discardHit.gameObject, () => _discardPile.anchoredPosition = new Vector2(discardX, 4f - discardY),
                () => _discardPile.anchoredPosition = new Vector2(discardX, -discardY));

            float historyY = deckY + DeckPanelHeight + Gap;
            float historyHeight = ColumnBottom - historyY;
            var history = Ui.Panel(column, "History Panel", 0f, historyY, w, historyHeight).rectTransform;
            Ui.Anchor(history, Vector2.zero, Vector2.up, new Vector2(w, StageHeight));
            if (compact)
            {
                BuildCompactHistory(history);
                return;
            }
            Localized(Ui.Label(history, "", pad, 16, 140), () => Loc.Get("ui.history"));
            Localized(Ui.Label(history, "", 150, 16, w - 150f - pad, align: TextAlignmentOptions.TopRight),
                () => Loc.Get("ui.historyOrder"));
            var scroll = Ui.Scroll(history, pad, 40f, w - pad * 2f, historyHeight - 54f);
            Ui.Anchor((RectTransform)scroll.transform, Vector2.zero, Vector2.up, new Vector2(w, historyHeight));
            _logEntries = scroll.content;
        }

        // The small profile only has room for the latest entry; the full list opens over the whole column.
        void BuildCompactHistory(RectTransform history)
        {
            float w = RightWidth, pad = PanelPad;
            Localized(Ui.Label(history, "", pad, 10f, 140f), () => Loc.Get("ui.history"));
            HistoryToggle(history, 8f, "ui.history.all");
            _logLatest = Ui.Rect(history, "Latest", pad, 36f, w - pad * 2f, 22f);

            // Outside the column, so that its fade on the opponent's turn does not let the panels beneath show through.
            float height = ColumnBottom - ColumnTop;
            var open = Ui.Panel(_stage, "History Open", RightX, ColumnTop, w, height).rectTransform;
            Ui.Anchor(open, Vector2.right, Vector2.one, Stage);
            Localized(Ui.Label(open, "", pad, 12f, 140f), () => Loc.Get("ui.history"));
            HistoryToggle(open, 10f, "ui.history.collapse");
            var scroll = Ui.Scroll(open, pad, 40f, w - pad * 2f, height - 52f);
            Ui.Anchor((RectTransform)scroll.transform, Vector2.zero, Vector2.up, new Vector2(w, height));
            _logEntries = scroll.content;
            _historyOpenPanel = open.gameObject;
            _historyOpenPanel.SetActive(false);
        }

        void HistoryToggle(RectTransform panel, float y, string key)
        {
            const float width = 150f, height = 24f;
            var hit = Ui.Image(panel, "Toggle", Ui.Solid, RightWidth - PanelPad - width, y, width, height, raycast: true);
            hit.color = Color.clear;
            Localized(Ui.Text(hit.transform, "", Ui.ItalicFont, 15.5f, Ui.Rubric, 0f, 0f, width, height,
                TextAlignmentOptions.MidlineRight), () => $"<u>{Loc.Get(key)}</u>");
            Ui.MakeButton(hit, ToggleHistory).transition = Selectable.Transition.None;
        }
    }
}
