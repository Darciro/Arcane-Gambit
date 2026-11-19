#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using ArcaneGambit.FX;
using ArcaneGambit.Rules;
using MoreMountains.Feedbacks;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace ArcaneGambit.Game
{
    /// <summary>
    /// The development bench of the Playground design doc: every card, sound and FX cue of the game, each with its
    /// file name. It only shows and plays; card data, saved volumes and the player's options are left as they were.
    /// Laid out once on the 1920 x 1080 board, whatever the screen.
    /// </summary>
    public sealed partial class Playground : MonoBehaviour
    {
        public const string SceneName = "Playground";

        const float W = 1920f, H = 1080f, CanvasDistance = 10f;
        const float SideX = 40f, SideW = 260f, ContentTop = 120f, FilterH = 44f, FilterStep = 50f;
        const float PanelX = 1373f, PanelW = 507f, PanelH = 930f;
        const float GridX = 330f, GridW = 1020f, GridCard = 0.59f, GridPitchX = 168f, GridPitchY = 232f, GridPad = 16f;
        const int GridColumns = 6, WaveBars = 64;
        const float TableX = 330f, TableW = 1022f, RowH = 55f, GroupH = 52f;
        static readonly Color Faded = new Color(0.6f, 0.53f, 0.42f);
        static readonly string FadedHex = "#" + ColorUtility.ToHtmlStringRGB(Faded);
        static readonly Color PauseScrim = new Color(0.125f, 0.082f, 0.039f, 0.62f);

        enum Tab { Cards, Sounds, Effects }

        enum SoundGroup { Cards, Transitions, Effects, Music, Other }

        sealed class SoundEntry
        {
            public string Name, Ext = "", Folder, Event, Detail;
            public SoundGroup Group;
            public AudioClip Clip;
        }

        // Kept across a reload: the transition preview brings the scene back where it was.
        static Tab s_tab;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            s_tab = Tab.Cards;
            s_effect = DefaultEffect;
        }

        FxRoot _fx;
        RectTransform _hud, _world, _page, _worldPage;
        AudioSource _voice;
        Sprite _playGlyph, _box, _boxActive, _circle;
        readonly List<(Image face, TextMeshProUGUI label, Tab tab)> _tabs = new List<(Image, TextMeshProUGUI, Tab)>();
        bool _reduceAtStart;
        RectTransform _hudCanvas, _pause;
        OptionsModal _options;
        bool _paused;

        readonly List<CardId> _cards = new List<CardId>();
        CardKind? _kind;
        CardId _card = CardId.CrossbowVolley;
        RectTransform _cardDetail;
        readonly Dictionary<CardId, Image> _cardOutlines = new Dictionary<CardId, Image>();

        readonly List<SoundEntry> _sounds = new List<SoundEntry>();
        SoundGroup? _soundGroup;
        SoundEntry _sound;
        RectTransform _soundDetail;
        readonly List<(SoundEntry entry, Image fill, Image bar)> _soundRows = new List<(SoundEntry, Image, Image)>();
        Image[] _wave;
        int _waveShown = -1;
        GameObject _detailPlay, _detailStop;
        Image _repeatFace;
        TextMeshProUGUI _repeatLabel, _volumeValue;
        bool _soundLoop;
        float _volume = 80f;

        void Start()
        {
            FxRoot.SetTimeScale(1f);
            Ui.SetCursor(CursorKind.Default);
            GameAudio.StopMusic();
            _reduceAtStart = FxSettings.ReduceFlashesAndShakes;
            _voice = gameObject.AddComponent<AudioSource>();
            _voice.playOnAwake = false;
            _voice.spatialBlend = 0f;
            _playGlyph = Triangle(48);
            _box = Ui.Boxed(new Color(Ui.Parchment.r, Ui.Parchment.g, Ui.Parchment.b, 0.45f),
                new Color(Ui.Ink.r, Ui.Ink.g, Ui.Ink.b, 0.55f), 1, 0, 0);
            _boxActive = Ui.Boxed(Ui.Ink, Ui.Ink, 1, 0, 0);
            _circle = Ui.Pill(new Color(Ui.Parchment.r, Ui.Parchment.g, Ui.Parchment.b, 0.5f), Ui.Ink, 20, 1.3f);

            foreach (CardId id in Enum.GetValues(typeof(CardId))) _cards.Add(id);
            LoadEffects();
            LoadSounds();
            BuildUi();
            ShowTab(s_tab);
        }

        void OnDestroy()
        {
            FxRoot.SetTimeScale(1f);
            if (FxSettings.ReduceFlashesAndShakes != _reduceAtStart) FxSettings.ReduceFlashesAndShakes = _reduceAtStart;
        }

        void Update()
        {
            var events = EventSystem.current;
            if (events != null && events.currentSelectedGameObject != null) events.SetSelectedGameObject(null);

            var keys = Keyboard.current;
            if (_options != null)
            {
                _options.Update(SceneTransition.Busy ? null : keys);
                return;
            }
            if (_paused)
            {
                if (keys != null && !SceneTransition.Busy && keys.escapeKey.wasPressedThisFrame) SetPaused(false);
                return;
            }
            UpdateSound();
            UpdateEffects();

            if (keys == null || SceneTransition.Busy) return;
            if (keys.escapeKey.wasPressedThisFrame) SetPaused(true);
            else if (keys.spaceKey.wasPressedThisFrame && s_tab == Tab.Effects) PlayEffect();
        }

        void Back() => Back(MainMenu.SceneName);

        void Back(string scene)
        {
            StopAll();
            FxRoot.SetTimeScale(1f);
            SceneTransition.Load(scene);
        }

        void SetPaused(bool paused)
        {
            _paused = paused;
            FxRoot.SetTimeScale(paused ? 0f : s_tab == Tab.Effects ? _speed : 1f);
            if (paused) _voice.Pause();
            else _voice.UnPause();
            if (_pause != null) Destroy(_pause.gameObject);
            _pause = paused ? BuildPause() : null;
        }

        RectTransform BuildPause()
        {
            const float w = 580f, h = 496f, buttonX = 52f, buttonY = 214f, step = 68f;
            var layer = Ui.Stretch(Ui.Rect(_hudCanvas, "Pause", 0, 0, 0, 0));
            var canvas = layer.gameObject.AddComponent<Canvas>();
            canvas.overrideSorting = true;
            canvas.sortingLayerName = FxLayers.Modals;
            layer.gameObject.AddComponent<GraphicRaycaster>();
            layer.gameObject.AddComponent<Image>().color = PauseScrim;

            var stage = UiProfile.ModalStage(layer, w, h);
            var panel = Ui.Modal(stage, (UiProfile.Design.x - w) * 0.5f, (UiProfile.Design.y - h) * 0.5f, w, h);
            var eyebrow = Ui.Label(panel, Loc.Get("pg.pause.eyebrow"), 0f, 44f, w, 26f, TextAlignmentOptions.Top);
            eyebrow.fontSize = 18f;
            eyebrow.characterSpacing = 20f;
            Ui.Text(panel, Ui.Initial(Loc.Get("ui.pause.title"), 122), Ui.TitleFont, 52f, Ui.InkText, 0f, 84f, w, 73f,
                TextAlignmentOptions.Top);
            float bw = w - buttonX * 2f;
            Ui.Button(panel, Loc.Get("ui.pause.resume"), true, buttonX, buttonY, bw, 56f, () => SetPaused(false), 22f);
            Ui.Button(panel, Loc.Get("ui.pause.settings"), false, buttonX, buttonY + step, bw, 56f, OpenOptions, 22f);
            Ui.Button(panel, Loc.Get("ui.gameOver.menu"), false, buttonX, buttonY + step * 2f, bw, 56f, Back, 22f);
            Ui.Text(panel, Loc.Get("ui.pause.hint"), Ui.ItalicFont, 18f, Ui.InkBody, 0f, 428f, w, 26f,
                TextAlignmentOptions.Top);
            return layer;
        }

        // The bench is laid out once from Loc, so a new language reloads the scene.
        void OpenOptions()
        {
            if (_options == null) _options = new OptionsModal(() => _pause, () => Back(SceneName), () => _options = null);
        }

        void StopAll()
        {
            _voice.Stop();
            StopEffects();
        }

        void BuildUi()
        {
            if (FindAnyObjectByType<EventSystem>() == null)
                new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            var camera = Camera.main;
            if (camera != null) camera.backgroundColor = Ui.Table;
            _fx = FxRoot.Create(transform, camera);
            if (_fx != null) _fx.UseFont(Ui.ItalicFont);

            // As in the match: the stage sits under the world camera, where particles and post-processing reach it,
            // and everything else on a HUD canvas above it.
            var world = CreateCanvas("World Canvas", _fx != null ? _fx.WorldCamera : camera, FxLayers.Board, FxLayers.World);
            Ui.Stretch(Ui.Image(world, "Background", Ui.Art("Background"), 0, 0, 0, 0).rectTransform);
            _world = Centered(world);
            var hud = CreateCanvas("Canvas", _fx != null ? _fx.HudCamera : camera, FxLayers.Hud, FxLayers.Ui);
            _hudCanvas = hud;
            _hud = Centered(hud);
            BuildHeader(_hud);
        }

        RectTransform CreateCanvas(string name, Camera camera, string sortingLayer, int layer)
        {
            var go = new GameObject(name, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster)) { layer = layer };
            go.transform.SetParent(transform, false);
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = CanvasDistance;
            canvas.sortingLayerName = sortingLayer;
            UiProfile.Apply(go.GetComponent<CanvasScaler>(), UiSize.Medium);
            return (RectTransform)go.transform;
        }

        static RectTransform Centered(RectTransform canvas)
        {
            var stage = Ui.Rect(canvas, "Stage", 0, 0, W, H);
            stage.anchorMin = stage.anchorMax = stage.pivot = new Vector2(0.5f, 0.5f);
            stage.anchoredPosition = Vector2.zero;
            return stage;
        }

        void BuildHeader(RectTransform stage)
        {
            var back = Ui.Sliced(stage, "Back", _box, 40f, 25f, 98f, 45f, raycast: true);
            Ui.MakeButton(back, Back);
            Ui.Chevron(back.transform, 20f, 22.5f, 9f, -1, Ui.InkText);
            Text(back.transform, Loc.Get("pg.back"), Ui.TitleFont, 19f, Ui.InkText, 32f, 0f, 62f, 45f,
                TextAlignmentOptions.Left);

            Text(stage, Ui.RomanInitial(Loc.Get("pg.title"), 135), Ui.TitleFont, 40f, Ui.InkText, 168f, 4f, 700f, 54f,
                TextAlignmentOptions.Left);
            Text(stage, Loc.Get("pg.subtitle"), Ui.ItalicFont, 17f, Ui.InkBody, 168f, 56f, 1000f, 26f);

            float[] widths = { 138f, 122f, 146f };
            int[] counts = { _cards.Count, _sounds.Count, _effects.Count };
            float x = 1645f - (widths[0] + widths[1] + widths[2]), y = 23f, h = 50f;
            Ui.Sliced(stage, "Tabs", Ui.Boxed(new Color(1f, 1f, 1f, 0f), Ui.Ink, 1, 0, 0), x - 1f, y - 1f,
                widths[0] + widths[1] + widths[2] + 2f, h + 2f);
            for (int i = 0; i < 3; i++)
            {
                var tab = (Tab)i;
                var face = Ui.Image(stage, "Tab " + tab, Ui.Solid, x, y, widths[i], h, raycast: true);
                var button = face.gameObject.AddComponent<Button>();
                button.transition = Selectable.Transition.None;
                button.onClick.AddListener(() => ShowTab(tab));
                var label = Text(face.transform, $"{Loc.Get("pg.tab." + tab)}  <size=68%><font=\"{Ui.BodyFont.name}\">{counts[i]}</font></size>",
                    Ui.TitleFont, 23f, Ui.InkText, 0f, 0f, widths[i], h, TextAlignmentOptions.Center);
                _tabs.Add((face, label, tab));
                if (i > 0) Ui.Line(stage, x, y, 1f, h, 0.55f);
                x += widths[i];
            }

            const float badgeX = 1673f, badgeW = 207f;
            Ui.DashedBox(stage, badgeX, 31f, badgeW, 33f, Ui.Rubric, 1f, 3f, 2f);
            Text(stage, Loc.Get("pg.devBuild"), Ui.TitleFont, 11.5f, Ui.Rubric, badgeX, 31f, badgeW, 33f,
                TextAlignmentOptions.Center).characterSpacing = 14f;

            // Far wider than the board, so the rule reaches the edges of any screen.
            Ui.Line(stage, -W, 95f, W * 3f, 1.5f, 0.85f);
            Ui.Line(stage, -W, 99f, W * 3f, 0.8f, 0.45f);
        }

        void ShowTab(Tab tab)
        {
            if (s_tab == Tab.Effects && tab != Tab.Effects) LeaveEffects();
            s_tab = tab;
            foreach (var (face, label, t) in _tabs)
            {
                bool on = t == tab;
                face.color = on ? Ui.Ink : new Color(Ui.Parchment.r, Ui.Parchment.g, Ui.Parchment.b, 0.5f);
                label.color = on ? Ui.Cream : Ui.InkText;
            }
            RebuildPage();
        }

        void RebuildPage()
        {
            if (_page != null) Destroy(_page.gameObject);
            if (_worldPage != null) Destroy(_worldPage.gameObject);
            _page = Ui.Rect(_hud, "Page", 0f, 0f, W, H);
            _worldPage = Ui.Rect(_world, "Page", 0f, 0f, W, H);
            _cardOutlines.Clear();
            _soundRows.Clear();
            _cardDetail = _soundDetail = null;
            _wave = null;
            switch (s_tab)
            {
                case Tab.Cards: BuildCards(); break;
                case Tab.Sounds: BuildSounds(); break;
                default: BuildEffects(); break;
            }
        }

        void BuildCards()
        {
            Ui.Label(_page, Loc.Get("pg.cards.kind"), SideX, 128f, SideW);
            float y = 156f;
            SideButton(_page, y, Loc.Get("pg.cards.all"), _cards.Count, _kind == null, null, () => FilterCards(null));
            foreach (CardKind kind in Enum.GetValues(typeof(CardKind)))
            {
                y += FilterStep;
                int count = _cards.FindAll(id => CardCatalog.Get(id).Kind == kind).Count;
                var k = kind;
                SideButton(_page, y, Loc.KindName(kind), count, _kind == kind, Ui.KindIcon(kind), () => FilterCards(k));
            }
            var shown = _kind == null ? _cards : _cards.FindAll(id => CardCatalog.Get(id).Kind == _kind);
            Text(_page, Loc.Format("pg.cards.shown", shown.Count), Ui.ItalicFont, 17f, Ui.InkBody, SideX,
                y + FilterH + 22f, SideW, 26f);

            float cw = Ui.CardWidth * GridCard, ch = Ui.CardHeight * GridCard;
            var scroll = Ui.Scroll(_page, GridX, ContentTop - 2f, GridW, H - ContentTop + 2f);
            int rows = (shown.Count + GridColumns - 1) / GridColumns;
            scroll.content.sizeDelta = new Vector2(0f, GridPad * 2f + rows * GridPitchY);
            for (int i = 0; i < shown.Count; i++)
            {
                var id = shown[i];
                float cx = GridPad + i % GridColumns * GridPitchX, cy = GridPad + i / GridColumns * GridPitchY;
                var cell = Ui.Rect(scroll.content, "Cell " + id, cx, cy, cw, ch);
                var shadow = Ui.Sliced(cell, "Shadow", Ui.Art("Card_Shadow"), -10f, 3f, cw + 20f, ch + 16f);
                shadow.color = new Color(1f, 1f, 1f, 0.45f);
                Ui.Card(cell, CardCatalog.Get(id), GridCard);
                var outline = Ui.Sliced(cell, "Selected", Ui.Pill(new Color(1f, 1f, 1f, 0f), Ui.Rubric, 10, 2.5f),
                    -9f, -9f, cw + 18f, ch + 18f);
                outline.enabled = id == _card;
                _cardOutlines[id] = outline;
                var button = cell.gameObject.AddComponent<Button>();
                button.transition = Selectable.Transition.None;
                button.onClick.AddListener(() => SelectCard(id));
            }
            BuildCardDetail();
        }

        void FilterCards(CardKind? kind)
        {
            _kind = kind;
            RebuildPage();
        }

        void SelectCard(CardId id)
        {
            _card = id;
            foreach (var pair in _cardOutlines) pair.Value.enabled = pair.Key == id;
            BuildCardDetail();
        }

        void BuildCardDetail()
        {
            if (_cardDetail != null) Destroy(_cardDetail.gameObject);
            _cardDetail = Ui.Rect(_page, "Detail", PanelX, ContentTop, PanelW, PanelH);
            Panel(_cardDetail, PanelW, PanelH);
            var info = CardCatalog.Get(_card);

            const float scale = 1.1f;
            float cw = Ui.CardWidth * scale, ch = Ui.CardHeight * scale, cx = (PanelW - cw) * 0.5f, top = 34f;
            var shadow = Ui.Sliced(_cardDetail, "Shadow", Ui.Art("Card_Shadow"), cx - 18f, top + 4f, cw + 36f, ch + 30f);
            shadow.color = new Color(1f, 1f, 1f, 0.7f);
            var holder = Ui.Rect(_cardDetail, "Card", cx, top, cw, ch);
            Ui.Card(holder, info, scale);

            float y = top + ch + 26f;
            Text(_cardDetail, Loc.CardName(_card), Ui.TitleFont, 28f, Ui.InkText, 26f, y, PanelW - 52f, 36f);
            y += 40f;
            string cost = info.CostUndefined ? Loc.Format("pg.cards.costUndefined", Loc.CardType(info))
                : Loc.Format("pg.cards.cost", Loc.CardType(info),
                    info.MinCost < info.Cost ? Ui.Roman(info.MinCost) + "–" + Ui.Roman(info.Cost) : Ui.Roman(info.Cost));
            Text(_cardDetail, cost, Ui.BodyFont, 18f, Ui.InkBody, 26f, y, PanelW - 52f, 26f);
            y += 28f;
            var art = Ui.Art("Card_" + _card);
            var (name, ext, folder) = FileOf(art != null ? art.texture : null, "Card_" + _card, ".png", "Resources/Art");
            Text(_cardDetail, FileLabel(name, ext), Ui.BodyFont, 18f, Ui.InkText, 26f, y, PanelW - 52f, 26f);
            y += 24f;
            Text(_cardDetail, folder, Ui.ItalicFont, 14f, Faded, 26f, y, PanelW - 52f, 22f);
            y += 34f;

            var heading = Ui.Label(_cardDetail, Loc.Get("pg.cards.onPlay"), 26f, y, PanelW - 52f);
            heading.textWrappingMode = TextWrappingModes.NoWrap;
            float ruleX = 26f + heading.GetPreferredValues(heading.text).x + 12f;
            Ui.Line(_cardDetail, ruleX, y + 10f, PanelW - 26f - ruleX, 1f, 0.5f);
            y += 30f;

            var scroll = Ui.Scroll(_cardDetail, 18f, y, PanelW - 36f, PanelH - y - 16f);
            var content = scroll.content;
            float rowY = 0f, rowW = PanelW - 36f - 18f;
            foreach (var cue in CardCues(_card))
            {
                var entry = _effects.Find(e => !e.Transition && e.Cue == cue);
                if (entry == null) continue;
                var c = cue;
                LinkRow(content, rowY, rowW, PlayerName(entry), ".prefab", Loc.Format("pg.cards.effect", CueEvent(cue)),
                    entry.Asset != null ? entry.Asset.Tier : (FxTier?)null, entry.Playable, () => OpenEffect(c, _card));
                rowY += 52f;
            }
            foreach (var sound in CardSounds(_card))
            {
                var s = sound;
                LinkRow(content, rowY, rowW, s.Name, s.Ext, Loc.Format("pg.cards.sound", s.Event), null, s.Clip != null,
                    () => PlaySound(s));
                rowY += 52f;
            }
            content.sizeDelta = new Vector2(0f, rowY);
        }

        void LinkRow(RectTransform parent, float y, float w, string name, string ext, string sub, FxTier? tier,
            bool playable, UnityAction play)
        {
            var row = Ui.Rect(parent, name, 0f, y, w, 52f);
            PlayButton(row, 4f, 7f, 38f, playable ? play : null);
            Text(row, FileLabel(name, ext), Ui.BodyFont, 18f, Ui.InkText, 54f, 3f, w - 170f, 26f).overflowMode =
                TextOverflowModes.Ellipsis;
            Text(row, sub, Ui.BodyFont, 14.5f, Ui.InkBody, 54f, 26f, w - 170f, 22f);
            if (tier is FxTier t) TierPill(row, t, w - 4f, 14f);
            Ui.Line(row, 0f, 51f, w, 1f, 0.18f);
        }

        void OpenEffect(FxCueId cue, CardId card)
        {
            _stageCard = card;
            int index = _effects.FindIndex(e => !e.Transition && e.Cue == cue);
            if (index >= 0) s_effect = index;
            _fxGroup = null;
            ShowTab(Tab.Effects);
            PlayEffect();
        }

        // What the match plays around a card: the cues of GameManagerFx, GameManagerBoardFx and MatchFx.
        static List<FxCueId> CardCues(CardId id)
        {
            var info = CardCatalog.Get(id);
            var cues = new List<FxCueId>();
            switch (id)
            {
                case CardId.LightningStorm:
                    cues.AddRange(new[] { FxCueId.StormArrival, FxCueId.StormMark, FxCueId.StormStrike, FxCueId.StormThunder });
                    break;
                case CardId.Blizzard: cues.Add(FxCueId.Blizzard); break;
                case CardId.Gale: cues.Add(FxCueId.Gale); break;
                case CardId.Fog: cues.Add(FxCueId.Fog); break;
                case CardId.Mud: cues.Add(FxCueId.Mud); break;
                case CardId.MoonlessNight: cues.Add(FxCueId.MoonlessNight); break;
            }
            if (info.IsWeather)
            {
                cues.Add(FxCueId.ConditionRemoved);
                return cues;
            }

            if (info.IsReaction) cues.Add(FxCueId.ReactionReveal);
            cues.Add(info.Kind == CardKind.Arcane ? FxCueId.ArcaneCast : FxCueId.CardPlay);
            switch (id)
            {
                case CardId.CrossbowVolley:
                case CardId.SiegeOrder:
                    cues.Add(FxCueId.CrossbowVolley);
                    cues.Add(FxCueId.Capture);
                    break;
                case CardId.Fireball:
                    cues.AddRange(new[] { FxCueId.FireballCast, FxCueId.FireballShot, FxCueId.FireballHit, FxCueId.FireballBlocked });
                    break;
                case CardId.DawnPrayer: cues.Add(FxCueId.DawnPrayer); break;
                case CardId.SeersOmen: cues.Add(FxCueId.SeersOmen); break;
                case CardId.OracleEcho: cues.Add(FxCueId.OracleEcho); break;
                case CardId.VigilSeal: cues.Add(FxCueId.VigilSeal); break;
                case CardId.SmokeScreen: cues.Add(FxCueId.SmokeScreen); break;
                case CardId.ImprovisedPalisade:
                case CardId.AntiCavalryStakes:
                case CardId.ShallowDitch:
                    cues.Add(FxCueId.Fortify);
                    break;
                case CardId.ForcedMarch:
                case CardId.CavalryFeint:
                case CardId.CoordinatedAdvance:
                case CardId.LineSwap:
                    cues.Add(FxCueId.QuickMove);
                    break;
                case CardId.RaiseShields:
                case CardId.BracedSpears:
                case CardId.CloseRanks:
                    cues.Add(FxCueId.CaptureBlocked);
                    break;
            }
            if (info.Kind == CardKind.Equipment) cues.Add(FxCueId.Equip);
            else cues.Add(FxCueId.CardDiscard);
            return cues;
        }

        IEnumerable<SoundEntry> CardSounds(CardId id)
        {
            var info = CardCatalog.Get(id);
            if (!info.IsWeather)
                foreach (var s in _sounds)
                    if (s.Group == SoundGroup.Cards)
                        yield return s;
            var cues = CardCues(id);
            foreach (var s in _sounds)
                if (s.Group == SoundGroup.Effects && s.Clip != null && _soundCues.TryGetValue(s, out var cue) && cues.Contains(cue))
                    yield return s;
        }

        readonly Dictionary<SoundEntry, FxCueId> _soundCues = new Dictionary<SoundEntry, FxCueId>();

        void LoadSounds()
        {
            var known = new HashSet<AudioClip>();
            foreach (Sfx sfx in Enum.GetValues(typeof(Sfx)))
            {
                var clip = Resources.Load<AudioClip>(GameAudio.SfxRoot + sfx);
                if (clip != null) known.Add(clip);
                var entry = Sound(clip, "Sfx_" + sfx, "Resources/" + GameAudio.SfxFolder, Loc.Get("pg.sfx." + sfx),
                    sfx switch
                    {
                        Sfx.CardShuffle => SoundGroup.Cards,
                        Sfx.PageTurn or Sfx.SealStamp => SoundGroup.Transitions,
                        _ => SoundGroup.Other,
                    });
                if (clip == null) entry.Detail = Loc.Get("pg.sounds.missing");
            }
            foreach (var clip in Resources.LoadAll<AudioClip>(GameAudio.SfxFolder))
                if (!known.Contains(clip))
                    Sound(clip, clip.name, "Resources/" + GameAudio.SfxFolder, "—", SoundGroup.Other).Detail =
                        Loc.Get("pg.sounds.unused");

            foreach (var fx in _effects)
            {
                if (fx.Transition || fx.Asset == null || fx.Asset.Player == null) continue;
                foreach (var slot in fx.Asset.Player.GetFeedbacksOfType<MMF_MMSoundManagerSound>())
                {
                    var clips = new List<AudioClip>();
                    if (slot.Sfx != null) clips.Add(slot.Sfx);
                    if (slot.RandomSfx != null)
                        foreach (var c in slot.RandomSfx)
                            if (c != null) clips.Add(c);
                    if (clips.Count == 0)
                    {
                        var empty = Sound(null, fx.Cue.ToString(), PlayerFolder(fx), CueEvent(fx.Cue), SoundGroup.Effects);
                        empty.Detail = Loc.Get("pg.sounds.unassigned");
                        _soundCues[empty] = fx.Cue;
                    }
                    foreach (var clip in clips)
                        _soundCues[Sound(clip, clip.name, PlayerFolder(fx), CueEvent(fx.Cue), SoundGroup.Effects)] = fx.Cue;
                }
            }

            foreach (Playlist playlist in Enum.GetValues(typeof(Playlist)))
                foreach (var clip in Resources.LoadAll<AudioClip>(GameAudio.MusicRoot + playlist))
                {
                    var entry = Sound(clip, clip.name, "Resources/" + GameAudio.MusicRoot + playlist,
                        Loc.Get("pg.music." + playlist), SoundGroup.Music);
                    if (clip.loadType == AudioClipLoadType.Streaming) entry.Detail = Loc.Get("pg.sounds.streaming");
                }
            _sound = _sounds.Find(s => s.Clip != null);
        }

        SoundEntry Sound(AudioClip clip, string name, string folder, string evt, SoundGroup group)
        {
            var (file, ext, path) = FileOf(clip, name, "", folder);
            var entry = new SoundEntry { Name = file, Ext = ext, Folder = path, Event = evt, Group = group, Clip = clip };
            _sounds.Add(entry);
            return entry;
        }

        void BuildSounds()
        {
            Ui.Label(_page, Loc.Get("pg.sounds.group"), SideX, 128f, SideW);
            float y = 156f;
            SideButton(_page, y, Loc.Get("pg.sounds.all"), _sounds.Count, _soundGroup == null, null, () => FilterSounds(null));
            foreach (SoundGroup group in Enum.GetValues(typeof(SoundGroup)))
            {
                int count = _sounds.FindAll(s => s.Group == group).Count;
                if (count == 0) continue;
                y += FilterStep;
                var g = group;
                SideButton(_page, y, Loc.Get("pg.sounds." + group), count, _soundGroup == group, null, () => FilterSounds(g));
            }
            y += FilterStep + 20f;
            var stop = Ui.Sliced(_page, "Stop All", Ui.Boxed(new Color(1f, 1f, 1f, 0f), Ui.Ink, 1, 2, 1), SideX, y, SideW,
                46f, raycast: true);
            Ui.MakeButton(stop, StopAll);
            Ui.Image(stop.transform, "Glyph", Ui.Solid, 76f, 18f, 10f, 10f).color = Ui.InkText;
            Text(stop.transform, Loc.Get("pg.sounds.stopAll"), Ui.TitleFont, 19f, Ui.InkText, 96f, 0f, SideW - 100f, 46f,
                TextAlignmentOptions.Left).characterSpacing = 4f;

            float[] columns = { 78f, 472f, 724f, 854f };
            string[] headers = { "pg.sounds.file", "pg.sounds.event", "pg.sounds.length", "pg.sounds.details" };
            for (int i = 0; i < columns.Length; i++)
                Ui.Label(_page, Loc.Get(headers[i]), TableX + columns[i], 124f, 240f);
            Ui.Line(_page, TableX, 148f, TableW - 18f, 1.2f, 0.8f);

            var scroll = Ui.Scroll(_page, TableX, 152f, TableW, H - 152f);
            var content = scroll.content;
            float rowW = TableW - 18f, rowY = 4f;
            foreach (SoundGroup group in Enum.GetValues(typeof(SoundGroup)))
            {
                if (_soundGroup != null && _soundGroup != group) continue;
                var entries = _sounds.FindAll(s => s.Group == group);
                if (entries.Count == 0) continue;
                GroupHeader(content, rowY, rowW, Loc.Get("pg.sounds." + group), entries.Count);
                rowY += GroupH;
                foreach (var entry in entries)
                {
                    SoundRow(content, rowY, rowW, entry, columns);
                    rowY += RowH;
                }
            }
            content.sizeDelta = new Vector2(0f, rowY + 24f);
            BuildSoundDetail();
        }

        void FilterSounds(SoundGroup? group)
        {
            _soundGroup = group;
            RebuildPage();
        }

        void GroupHeader(RectTransform parent, float y, float w, string title, int count)
        {
            var text = Text(parent, $"{title}  <size=70%><font=\"{Ui.BodyFont.name}\">{count}</font></size>", Ui.TitleFont,
                22f, Ui.InkText, 12f, y + 14f, w, 30f);
            text.textWrappingMode = TextWrappingModes.NoWrap;
            float x = 12f + text.GetPreferredValues(text.text).x + 16f;
            Ui.Line(parent, x, y + 30f, w - x - 8f, 1.2f, 0.6f);
            Ui.Line(parent, x, y + 33f, w - x - 8f, 0.6f, 0.35f);
        }

        void SoundRow(RectTransform parent, float y, float w, SoundEntry entry, float[] columns)
        {
            var row = Ui.Rect(parent, entry.Name, 0f, y, w, RowH);
            var fill = Ui.Image(row, "Fill", Ui.Solid, 0f, 0f, w, RowH, raycast: true);
            var select = fill.gameObject.AddComponent<Button>();
            select.transition = Selectable.Transition.None;
            select.onClick.AddListener(() => SelectSound(entry));
            var bar = Ui.Image(row, "Selected", Ui.Solid, 0f, 0f, 3f, RowH);
            bar.color = Ui.Rubric;
            _soundRows.Add((entry, fill, bar));
            PoseRow(entry, fill, bar);

            PlayButton(row, 12f, 7.5f, 40f, entry.Clip != null ? () => PlaySound(entry) : null);
            var name = Text(row, FileLabel(entry.Name, entry.Ext), Ui.BodyFont, 18f,
                entry.Clip != null ? Ui.InkText : Faded, columns[0], 0f, columns[1] - columns[0] - 16f, RowH,
                TextAlignmentOptions.MidlineLeft);
            name.overflowMode = TextOverflowModes.Ellipsis;
            Text(row, entry.Event, Ui.BodyFont, 18f, Ui.InkText, columns[1], 0f, columns[2] - columns[1] - 16f, RowH,
                TextAlignmentOptions.MidlineLeft).overflowMode = TextOverflowModes.Ellipsis;
            if (entry.Clip != null)
                Text(row, Length(entry.Clip.length), Ui.BodyFont, 16f, Ui.InkBody, columns[2], 0f, 120f, RowH,
                    TextAlignmentOptions.MidlineLeft);
            if (entry.Detail != null) Tag(row, entry.Detail, columns[3], RowH * 0.5f, entry.Clip == null);
            Ui.Dashed(row, new Vector2(0f, -RowH + 0.5f), new Vector2(w, -RowH + 0.5f),
                new Color(Ui.Ink.r, Ui.Ink.g, Ui.Ink.b, 0.3f), 1f, 2f, 3f);
        }

        void PoseRow(SoundEntry entry, Image fill, Image bar)
        {
            bool on = entry == _sound;
            fill.color = on ? new Color(Ui.Cream.r, Ui.Cream.g, Ui.Cream.b, 0.75f) : new Color(1f, 1f, 1f, 0f);
            bar.enabled = on;
        }

        void SelectSound(SoundEntry entry)
        {
            _sound = entry;
            foreach (var (e, fill, bar) in _soundRows) PoseRow(e, fill, bar);
            BuildSoundDetail();
        }

        void PlaySound(SoundEntry entry)
        {
            if (entry.Clip == null) return;
            if (s_tab == Tab.Sounds && _sound != entry) SelectSound(entry);
            if (_voice.isPlaying && _voice.clip == entry.Clip)
            {
                _voice.Stop();
                return;
            }
            _voice.Stop();
            _voice.clip = entry.Clip;
            _voice.loop = _soundLoop;
            _voice.volume = _volume / 100f;
            _voice.Play();
        }

        void BuildSoundDetail()
        {
            if (_soundDetail != null) Destroy(_soundDetail.gameObject);
            _soundDetail = Ui.Rect(_page, "Detail", PanelX, ContentTop, PanelW, PanelH);
            _wave = null;
            _waveShown = -1;
            Panel(_soundDetail, PanelW, PanelH);
            const float pad = 26f, inner = PanelW - pad * 2f;
            Ui.Label(_soundDetail, Loc.Get("pg.sounds.selected"), pad, 28f, inner);
            if (_sound == null)
            {
                Text(_soundDetail, Loc.Get("pg.sounds.none"), Ui.ItalicFont, 17f, Ui.InkBody, pad, 70f, inner, 26f);
                return;
            }
            var entry = _sound;
            Text(_soundDetail, FileLabel(entry.Name, entry.Ext), Ui.BodyFont, 25f, Ui.InkText, pad, 66f, inner, 60f);
            Text(_soundDetail, entry.Event, Ui.BodyFont, 19f, Ui.InkText, pad, 136f, inner, 26f);
            Tag(_soundDetail, Loc.Get("pg.sounds." + entry.Group), pad, 182f, false);
            if (entry.Detail != null)
                Text(_soundDetail, entry.Detail, Ui.ItalicFont, 16f, Ui.InkBody, pad + 150f, 170f, inner - 150f, 24f);

            const float waveY = 214f, waveH = 120f;
            Ui.Sliced(_soundDetail, "Wave", Ui.Boxed(new Color(1f, 0.98f, 0.93f, 0.35f), new Color(Ui.Ink.r, Ui.Ink.g, Ui.Ink.b, 0.35f), 1, 0, 0),
                pad, waveY, inner, waveH);
            var peaks = Peaks(entry.Clip, WaveBars);
            if (peaks == null)
                Text(_soundDetail, Loc.Get("pg.sounds.noWave"), Ui.ItalicFont, 15f, Faded, pad, waveY, inner, waveH,
                    TextAlignmentOptions.Center);
            else
            {
                _wave = new Image[WaveBars];
                float pitch = (inner - 20f) / WaveBars, mid = waveY + waveH * 0.5f;
                for (int i = 0; i < WaveBars; i++)
                {
                    float h = Mathf.Max(2f, peaks[i] * (waveH - 20f));
                    _wave[i] = Ui.Image(_soundDetail, "Bar", Ui.Solid, pad + 10f + i * pitch, mid - h * 0.5f, pitch * 0.62f, h);
                    _wave[i].color = Ui.InkSoft;
                }
            }

            float y = waveY + waveH + 16f;
            var play = PlayButton(_soundDetail, pad, y, 56f, entry.Clip != null ? () => PlaySound(entry) : null);
            _detailPlay = play.Find("Glyph").gameObject;
            var stop = Ui.Image(play, "Stop", Ui.Solid, 21f, 21f, 14f, 14f);
            stop.color = Ui.InkText;
            _detailStop = stop.gameObject;
            _repeatFace = Ui.Sliced(_soundDetail, "Repeat", _box, pad + 70f, y + 5f, 118f, 46f, raycast: true);
            Ui.MakeButton(_repeatFace, ToggleSoundLoop);
            _repeatLabel = Text(_repeatFace.transform, Loc.Get("pg.sounds.repeat"), Ui.TitleFont, 19f, Ui.InkText, 0f, 0f,
                118f, 46f, TextAlignmentOptions.Center);
            PoseRepeat();

            y += 84f;
            Text(_soundDetail, Loc.Get("pg.sounds.volume"), Ui.BodyFont, 17f, Ui.InkText, pad, y, 200f, 24f);
            Slider(_soundDetail, pad + 210f, y + 2f, inner - 260f, _volume, v =>
            {
                _volume = v;
                _voice.volume = v / 100f;
                if (_volumeValue != null) _volumeValue.text = v.ToString("0");
            });
            _volumeValue = Text(_soundDetail, _volume.ToString("0"), Ui.BodyFont, 17f, Ui.InkText, PanelW - pad - 40f, y,
                40f, 24f, TextAlignmentOptions.Right);

            y += 56f;
            var clip = entry.Clip;
            InfoRow(_soundDetail, ref y, "pg.sounds.folder", entry.Folder);
            InfoRow(_soundDetail, ref y, "pg.sounds.file", entry.Name + entry.Ext);
            if (clip != null)
            {
                InfoRow(_soundDetail, ref y, "pg.sounds.length", Length(clip.length));
                InfoRow(_soundDetail, ref y, "pg.sounds.format", Loc.Format("pg.sounds.formatValue", clip.frequency, clip.channels));
                InfoRow(_soundDetail, ref y, "pg.sounds.load", clip.loadType.ToString());
            }
            Text(_soundDetail, Loc.Get("pg.sounds.note"), Ui.ItalicFont, 15f, Ui.InkBody, pad, PanelH - 60f, inner, 40f);
        }

        void InfoRow(RectTransform parent, ref float y, string key, string value)
        {
            Ui.Label(parent, Loc.Get(key), 26f, y + 2f, 120f).fontSize = 13f;
            var text = Text(parent, value, Ui.BodyFont, 16f, Ui.InkText, 150f, y, PanelW - 176f, 24f);
            text.overflowMode = TextOverflowModes.Ellipsis;
            y += 28f;
        }

        void ToggleSoundLoop()
        {
            _soundLoop = !_soundLoop;
            _voice.loop = _soundLoop;
            PoseRepeat();
        }

        void PoseRepeat()
        {
            if (_repeatFace == null) return;
            _repeatFace.sprite = _soundLoop ? _boxActive : _box;
            _repeatLabel.color = _soundLoop ? Ui.Cream : Ui.InkText;
        }

        void UpdateSound()
        {
            if (s_tab != Tab.Sounds || _sound == null) return;
            bool playing = _voice.isPlaying && _voice.clip == _sound.Clip && _sound.Clip != null;
            if (_detailPlay != null)
            {
                _detailPlay.SetActive(!playing);
                _detailStop.SetActive(playing);
            }
            if (_wave == null) return;
            int shown = playing ? Mathf.FloorToInt(_voice.time / _sound.Clip.length * WaveBars) : -1;
            if (shown == _waveShown) return;
            _waveShown = shown;
            for (int i = 0; i < _wave.Length; i++) _wave[i].color = i <= shown ? Ui.Rubric : Ui.InkSoft;
        }

        static float[] Peaks(AudioClip clip, int bars)
        {
            if (clip == null || clip.loadType == AudioClipLoadType.Streaming) return null;
            try
            {
                if (clip.loadState != AudioDataLoadState.Loaded && !clip.LoadAudioData()) return null;
                var data = new float[clip.samples * clip.channels];
                if (data.Length == 0 || !clip.GetData(data, 0)) return null;
                var peaks = new float[bars];
                int chunk = Mathf.Max(1, data.Length / bars);
                float max = 0f;
                for (int b = 0; b < bars; b++)
                {
                    float peak = 0f;
                    for (int i = b * chunk, end = Mathf.Min(data.Length, i + chunk); i < end; i++)
                        peak = Mathf.Max(peak, Mathf.Abs(data[i]));
                    peaks[b] = peak;
                    max = Mathf.Max(max, peak);
                }
                if (max > 0f)
                    for (int b = 0; b < bars; b++) peaks[b] /= max;
                return peaks;
            }
            catch (Exception)
            {
                return null;
            }
        }

        void SideButton(RectTransform parent, float y, string label, int count, bool active, Sprite icon, UnityAction onClick)
        {
            var face = Ui.Sliced(parent, label, active ? _boxActive : _box, SideX, y, SideW, FilterH, raycast: true);
            var button = face.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(onClick);
            float x = 12f;
            if (icon != null)
            {
                var badge = Ui.Image(face.transform, "Badge", Ui.Art("Kind_Badge"), 9f, 8f, 28f, 28f);
                Ui.Image(badge.transform, "Icon", icon, 5f, 5f, 18f, 18f).color = Ui.Ink;
                x = 46f;
            }
            var color = active ? Ui.Cream : Ui.InkText;
            Text(face.transform, label, Ui.TitleFont, 19f, color, x, 0f, SideW - x - 40f, FilterH,
                TextAlignmentOptions.MidlineLeft).overflowMode = TextOverflowModes.Ellipsis;
            Text(face.transform, count.ToString(), Ui.BodyFont, 16f, color, SideW - 52f, 0f, 40f, FilterH,
                TextAlignmentOptions.MidlineRight);
        }

        RectTransform PlayButton(RectTransform parent, float x, float y, float size, UnityAction onClick)
        {
            var face = Ui.Sliced(parent, "Play", _circle, x, y, size, size, raycast: onClick != null);
            if (onClick != null) Ui.MakeButton(face, onClick);
            else face.color = new Color(1f, 1f, 1f, 0.4f);
            float glyph = size * 0.32f;
            var g = Ui.Image(face.transform, "Glyph", _playGlyph, (size - glyph) * 0.5f + glyph * 0.12f, (size - glyph) * 0.5f,
                glyph, glyph);
            g.color = onClick != null ? Ui.InkText : Faded;
            return face.rectTransform;
        }

        RectTransform TierPill(RectTransform parent, FxTier tier, float right, float y)
        {
            string text = Loc.Get("pg.tier." + tier);
            bool boom = tier == FxTier.Boom;
            var line = tier == FxTier.Whisper ? Ui.InkSoft : Ui.Rubric;
            var label = Text(parent, text, Ui.TitleFont, 13f, boom ? Ui.Cream : line, 0f, 0f, 200f, 24f,
                TextAlignmentOptions.Center);
            label.characterSpacing = 8f;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            float w = label.GetPreferredValues(text).x + 24f;
            var pill = Ui.Sliced(parent, "Tier", Ui.Pill(boom ? Ui.Rubric : new Color(1f, 1f, 1f, 0f), line, 12, 1.3f),
                right - w, y, w, 24f);
            label.transform.SetParent(pill.transform, false);
            label.rectTransform.anchoredPosition = Vector2.zero;
            label.rectTransform.sizeDelta = new Vector2(w, 24f);
            return pill.rectTransform;
        }

        void Tag(RectTransform parent, string text, float x, float centerY, bool rubric)
        {
            var color = rubric ? Ui.Rubric : Ui.InkSoft;
            var label = Text(parent, text, Ui.TitleFont, 12f, color, 0f, 0f, 300f, 22f, TextAlignmentOptions.Center);
            label.characterSpacing = 8f;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            float w = label.GetPreferredValues(text).x + 16f;
            var box = Ui.Rect(parent, "Tag", x, centerY - 11f, w, 22f);
            Ui.Sliced(box, "Frame", Ui.Boxed(new Color(1f, 1f, 1f, 0f), color, 1, 0, 0), 0f, 0f, w, 22f);
            label.transform.SetParent(box, false);
            label.rectTransform.anchoredPosition = Vector2.zero;
            label.rectTransform.sizeDelta = new Vector2(w, 22f);
        }

        void Panel(RectTransform rt, float w, float h)
        {
            var shadow = Ui.Sliced(rt, "Shadow", Ui.Art("Card_Shadow"), -18f, 4f, w + 36f, h + 30f);
            shadow.color = new Color(1f, 1f, 1f, 0.35f);
            Ui.Sliced(rt, "Panel", Ui.Boxed(new Color(Ui.Parchment.r, Ui.Parchment.g, Ui.Parchment.b, 0.7f),
                Ui.InkSoft, 1, 4, 1), 0f, 0f, w, h, raycast: true);
        }

        void Slider(RectTransform parent, float x, float y, float w, float value, UnityAction<float> onChange)
        {
            var root = Ui.Rect(parent, "Slider", x, y, w, 20f);
            var track = Ui.Image(root, "Track", Ui.Solid, 0f, 8.5f, w, 3f, raycast: true);
            track.color = new Color(Ui.Ink.r, Ui.Ink.g, Ui.Ink.b, 0.25f);
            var fillArea = Ui.Rect(root, "Fill Area", 0f, 8.5f, w, 3f);
            var fill = Ui.Image(fillArea, "Fill", Ui.Solid, 0f, 0f, 0f, 0f);
            fill.color = Ui.Rubric;
            Ui.Stretch(fill.rectTransform);
            var handleArea = Ui.Stretch(Ui.Rect(root, "Handle Area", 0f, 0f, 0f, 0f));
            var handle = Ui.Image(handleArea, "Handle", Ui.Solid, 0f, 0f, 16f, 16f, raycast: true);
            handle.color = Ui.Ink;
            var rt = handle.rectTransform;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.localRotation = Quaternion.Euler(0f, 0f, 45f);
            Ui.Image(handle.transform, "Inner", Ui.Solid, 4f, 4f, 8f, 8f).color = Ui.Cream;

            var slider = root.gameObject.AddComponent<Slider>();
            slider.fillRect = fill.rectTransform;
            slider.handleRect = rt;
            slider.targetGraphic = handle;
            slider.transition = Selectable.Transition.None;
            slider.minValue = 0f;
            slider.maxValue = 100f;
            slider.wholeNumbers = true;
            slider.SetValueWithoutNotify(value);
            slider.onValueChanged.AddListener(onChange);
        }

        static TextMeshProUGUI Text(Transform parent, string text, TMP_FontAsset font, float size, Color color, float x,
            float y, float w, float h, TextAlignmentOptions align = TextAlignmentOptions.TopLeft) =>
            Ui.Text(parent, text, font, size, color, x, y, w, h, align);

        static string FileLabel(string name, string ext) =>
            string.IsNullOrEmpty(ext) ? name : $"{name}<color={FadedHex}>{ext}</color>";

        static string Length(float seconds) =>
            seconds < 60f ? seconds.ToString("0.00") + " s" : $"{(int)(seconds / 60f)}:{(int)(seconds % 60f):00}";

        /// <summary>The asset's real file name, extension and folder in the Editor; the given fallbacks in a build.</summary>
        static (string name, string ext, string folder) FileOf(UnityEngine.Object asset, string name, string ext,
            string folder)
        {
#if UNITY_EDITOR
            string path = asset != null ? UnityEditor.AssetDatabase.GetAssetPath(asset) : null;
            if (!string.IsNullOrEmpty(path))
                return (System.IO.Path.GetFileNameWithoutExtension(path), System.IO.Path.GetExtension(path),
                    System.IO.Path.GetDirectoryName(path)?.Replace('\\', '/') + "/");
#endif
            return (name, ext, folder + "/");
        }

        static Sprite Triangle(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave,
            };
            var pixels = new Color[size * size];
            const int samples = 4;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    int inside = 0;
                    for (int sy = 0; sy < samples; sy++)
                        for (int sx = 0; sx < samples; sx++)
                        {
                            float u = (x + (sx + 0.5f) / samples) / size, v = (y + (sy + 0.5f) / samples) / size;
                            float slope = (u - 0.08f) * 0.5f;
                            if (u >= 0.08f && v >= 0.04f + slope && v <= 0.96f - slope) inside++;
                        }
                    pixels[y * size + x] = new Color(1f, 1f, 1f, inside / (float)(samples * samples));
                }
            tex.SetPixels(pixels);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        }
    }
}
#endif
