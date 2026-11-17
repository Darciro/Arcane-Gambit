using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace ArcaneGambit.Game
{
    public sealed partial class MainMenu : MonoBehaviour
    {
        public const string SceneName = "MainMenu";

        const float DividerHeight = 16f, HoverTime = 0.15f, ShadowAlpha = 0.8f;

        // The layout of the current screen profile, set by ApplyProfile before the menu is built.
        static UiSize s_size;
        static float StageWidth, StageHeight;
        static Vector2 Stage;
        static float BrandX, BrandY, BrandWidth, LogoSize, BrandGap, BrandFont, TaglineFont, OrnamentWidth;
        static float PanelX, PanelY, PanelWidth, PanelTop, PanelPadding, HeaderFont, HeaderMargin;
        static float ButtonHeight, ButtonGap, ButtonFont, SoonFont, VersionX, VersionBottom, VersionFont;
        static float HeaderHeight, s_side;

        static void ApplyProfile()
        {
            s_size = UiProfile.Size;
            Stage = UiProfile.Reference(s_size);
            StageWidth = Stage.x;
            StageHeight = Stage.y;
            HeaderHeight = 24f;
            if (UiProfile.Mobile)
            {
                ApplyMobileProfile();
                return;
            }
            bool small = s_size == UiSize.Small, large = s_size == UiSize.Large;

            BrandX = small ? 64f : large ? 160f : 120f;
            BrandY = small ? 72f : large ? 126f : 90f;
            BrandWidth = small ? 700f : 880f;
            LogoSize = small ? 230f : 330f;
            BrandGap = small ? 12f : 18f;
            BrandFont = small ? 84f : 112f;
            TaglineFont = small ? 23f : 30f;
            OrnamentWidth = small ? 260f : 320f;

            PanelX = small ? 840f : large ? 1236f : 1140f;
            PanelY = small ? 136f : large ? 256f : 220f;
            PanelWidth = small ? 470f : large ? 580f : 560f;
            PanelTop = small ? 32f : 44f;
            PanelPadding = small ? 36f : 48f;
            HeaderFont = small ? 15f : 18f;
            HeaderMargin = small ? 6f : 8f;
            ButtonHeight = small ? 56f : 70f;
            ButtonGap = small ? 12f : 16f;
            ButtonFont = small ? 25f : 30f;
            SoonFont = small ? 15f : 17f;
            VersionX = small ? 36f : large ? 56f : 48f;
            VersionBottom = small ? 24f : large ? 40f : 32f;
            VersionFont = small ? 15f : 17f;
        }

        // The "mobile-horizontal" board: 844 x 390, the brand on the left half and the menu on the right.
        static void ApplyMobileProfile()
        {
            s_side = UiProfile.SideInset;
            BrandX = 47f;
            BrandY = 62f;
            BrandWidth = 390f;
            LogoSize = 92f;
            BrandGap = 8f;
            BrandFont = 44f;
            TaglineFont = 15.5f;
            OrnamentWidth = 180f;

            PanelX = 472f;
            PanelY = 40f;
            PanelWidth = 325f;
            PanelTop = 18f;
            PanelPadding = 20f;
            HeaderFont = 12.5f;
            HeaderHeight = 16f;
            HeaderMargin = 2f;
            ButtonHeight = 50f;
            ButtonGap = 8f;
            ButtonFont = 21f;
            SoonFont = 13.5f;
            VersionX = s_side;
            VersionBottom = 20f;
            VersionFont = 12.5f;
        }

        static readonly Color ScrimColor = new Color(0.125f, 0.082f, 0.039f, 0.62f);

        sealed class MenuButton
        {
            public RectTransform Root;
            public Image Hover, Shadow;
            public TextMeshProUGUI Label;
            public Image[] Diamonds;
            public UnityAction OnClick;
            public float Y, Lift;
        }

        readonly List<MenuButton> _buttons = new List<MenuButton>();
        int _focus = -1;
        RectTransform _root, _credits;
        OptionsModal _options;

        void Start()
        {
            Time.timeScale = 1f;
            Ui.SetCursor(CursorKind.Default);
            // Every scene change goes through the sheet, so a menu reached without it is the game opening.
            bool opening = !SceneTransition.Busy;
            if (opening) GameAudio.PlayMusic(Playlist.Menu, IntroMusicFade);
            else GameAudio.PlayMusic(Playlist.Menu);
            BuildUi();
            if (opening) BeginIntro();
            else _intro.Clear();
        }

        void Update()
        {
            if (UiProfile.Size != s_size || (UiProfile.Mobile && !Mathf.Approximately(UiProfile.SideInset, s_side)))
                ChangeProfile();

            for (int i = 0; i < _buttons.Count; i++)
            {
                var b = _buttons[i];
                float target = i == _focus ? 1f : 0f;
                if (Mathf.Approximately(b.Lift, target)) continue;
                b.Lift = Mathf.MoveTowards(b.Lift, target, Time.unscaledDeltaTime / HoverTime);
                Pose(b);
            }

            bool intro = UpdateIntro();
            UpdateMultiplayer();

            // A clicked button keeps UI focus, and the UI module would click it again on Enter.
            // The code field is the exception: it needs the focus to be typed into.
            var events = EventSystem.current;
            var selected = events != null ? events.currentSelectedGameObject : null;
            if (selected != null && (_codeInput == null || selected != _codeInput.gameObject))
                events.SetSelectedGameObject(null);

            var keyboard = Keyboard.current;
            bool keys = keyboard != null && !SceneTransition.Busy && !intro;
            if (_options != null) _options.Update(keys ? keyboard : null);
            else if (keys) OnKeys(keyboard);
        }

        void OnKeys(Keyboard keys)
        {
            bool enter = keys.enterKey.wasPressedThisFrame || keys.numpadEnterKey.wasPressedThisFrame;
            if (_mp != MpScreen.None)
            {
                OnMultiplayerKeys(keys, enter);
                return;
            }
            if (_credits != null)
            {
                if (enter || keys.escapeKey.wasPressedThisFrame) CloseCredits();
                return;
            }

            int step = keys.downArrowKey.wasPressedThisFrame ? 1 : keys.upArrowKey.wasPressedThisFrame ? -1 : 0;
            int count = _buttons.Count;
            if (step != 0 && count > 0)
            {
                int from = _focus >= 0 ? _focus : step > 0 ? -1 : count;
                _focus = (from + step + count) % count;
            }
            else if (enter && _focus >= 0) _buttons[_focus].OnClick();
        }

        void BuildUi()
        {
            if (FindAnyObjectByType<EventSystem>() == null)
                new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            if (Camera.main != null) Camera.main.backgroundColor = Ui.Table;

            var canvasGo = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);
            canvasGo.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            ApplyProfile();
            UiProfile.Apply(canvasGo.GetComponent<CanvasScaler>(), s_size);
            _root = (RectTransform)canvasGo.transform;

            Ui.Stretch(Ui.Image(_root, "Background", Ui.Art("Background"), 0, 0, 0, 0).rectTransform);
            var backdrop = Ui.Stretch(Ui.Image(_root, "Backdrop", Ui.Art("Menu_Backdrop"), 0, 0, 0, 0).rectTransform);
            AddIntro(backdrop, 0f, 0.8f, EaseOut);

            var stage = CenteredStage(_root);
            BuildBrand(stage);
            BuildMenu(stage);

            var version = Ui.Label(_root, Loc.Format("ui.menu.version", Application.version), VersionX,
                StageHeight - VersionBottom - 24f, 400f, 24f);
            version.fontSize = VersionFont;
            version.characterSpacing = 14f;
            version.color = Ui.InkBody;
            Ui.Anchor(version.rectTransform, Vector2.zero, Vector2.zero, Stage);
            AddIntro(version.rectTransform, 1.6f, 0.4f, EaseOut);
        }

        static RectTransform CenteredStage(RectTransform parent)
        {
            var stage = Ui.Rect(parent, "Stage", 0, 0, StageWidth, StageHeight);
            stage.anchorMin = stage.anchorMax = stage.pivot = new Vector2(0.5f, 0.5f);
            stage.anchoredPosition = Vector2.zero;
            return stage;
        }

        void BuildBrand(RectTransform stage)
        {
            float titleHeight = BrandFont * 1.09f, taglineHeight = TaglineFont * 2.8f, ornamentWidth = OrnamentWidth;
            float center = BrandX + BrandWidth * 0.5f;
            var logo = Ui.Image(stage, "Logo", Ui.Art("Logo"), center - LogoSize * 0.5f, BrandY, LogoSize, LogoSize);
            AddIntroRise(logo.rectTransform, 0f, 0.6f);

            float y = BrandY + LogoSize + BrandGap;
            var title = Ui.Text(stage, Ui.Brand(Loc.Get("ui.title"), 114), Ui.TitleFont, BrandFont, Ui.InkText, BrandX, y,
                BrandWidth, titleHeight, TextAlignmentOptions.Bottom);
            title.textWrappingMode = TextWrappingModes.NoWrap;
            AddIntroTitle(stage, title, center, y, titleHeight);
            y += titleHeight + BrandGap;

            // Centred pivot: the filet opens from its diamond outwards.
            var ornament = Ui.Centered(Ui.Rect(stage, "Ornament", center - ornamentWidth * 0.5f, y, ornamentWidth,
                DividerHeight));
            Ornament(ornament, ornamentWidth, DividerHeight * 0.5f);
            AddIntro(ornament, 0.95f, 0.4f, EaseRise, t => ornament.localScale = new Vector3(t, 1f, 1f));
            y += DividerHeight + BrandGap;

            var tagline = Ui.Text(stage, Loc.Get("ui.menu.tagline"), Ui.ItalicFont, TaglineFont, Ui.Ink, BrandX, y, BrandWidth,
                taglineHeight, TextAlignmentOptions.Top);
            AddIntro(tagline.rectTransform, 1.05f, 0.4f, EaseOut);
        }

        static void Ornament(RectTransform parent, float width, float y)
        {
            float center = width * 0.5f;
            Ui.Line(parent, 0f, y - 0.6f, center - 20f, 1.2f);
            Ui.Line(parent, center + 20f, y - 0.6f, center - 20f, 1.2f);
            foreach (float dx in new[] { -14f, 14f })
                Ui.Image(parent, "Dot", Ui.Pill(Ui.Ink, Ui.Ink, 4, 0f), center + dx - 2f, y - 2f, 4f, 4f);
            Ui.Diamond(parent, center, y, 10f, Ui.Rubric);
        }

        void BuildMenu(RectTransform stage)
        {
            // A phone leaves the app through the system, so its menu has no Exit and ends on the last button.
            bool exit = !UiProfile.Mobile;
            float headerHeight = HeaderHeight, headerMargin = HeaderMargin;
            float height = PanelTop + headerHeight + headerMargin + ButtonGap + 4f * (ButtonHeight + ButtonGap)
                + (exit ? DividerHeight + ButtonGap + ButtonHeight : -ButtonGap) + PanelPadding;
            // The panel's shadow is a sibling, so both ride in one layer.
            var layer = Ui.Rect(stage, "Menu", 0f, 0f, StageWidth, StageHeight);
            AddIntroRise(layer, 1.2f, 0.6f);
            var panel = Ui.Modal(layer, PanelX, PanelY, PanelWidth, height);

            var header = Ui.Label(panel, Loc.Get("ui.menu.header"), 0f, PanelTop, PanelWidth, headerHeight,
                TextAlignmentOptions.Top);
            header.fontSize = HeaderFont;
            header.characterSpacing = 30f;

            float y = PanelTop + headerHeight + headerMargin + ButtonGap;
            y = AddButton(panel, "ui.menu.singlePlayer", y, () => SceneTransition.Load(GameManager.SceneName));
            y = AddButton(panel, "ui.menu.multiplayer", y, OpenHall);
            y = AddButton(panel, "ui.menu.options", y, OpenOptions);
            y = AddButton(panel, "ui.menu.credits", y, OpenCredits);
            if (!exit) return;

            float center = PanelWidth * 0.5f, line = y + DividerHeight * 0.5f;
            float rule = center - PanelPadding - 60f;
            Ui.Line(panel, PanelPadding + 40f, line - 0.6f, rule, 1.2f);
            Ui.Line(panel, center + 20f, line - 0.6f, rule, 1.2f);
            Ui.Diamond(panel, center, line, 8.5f, Ui.Rubric);
            y += DividerHeight + ButtonGap;

            AddButton(panel, "ui.menu.exit", y, Quit);
        }

        float AddButton(RectTransform panel, string key, float y, UnityAction onClick)
        {
            float w = PanelWidth - PanelPadding * 2f, h = ButtonHeight;
            string text = Loc.Get(key);
            var root = Ui.Rect(panel, text, PanelPadding, y, w, h);

            if (onClick == null)
            {
                var fill = Ui.Image(root, "Face", Ui.Solid, 0f, 0f, w, h);
                fill.color = new Color(Ui.Parchment.r, Ui.Parchment.g, Ui.Parchment.b, 0.45f);
                Ui.DashedBox(root, 0.75f, 0.75f, w - 1.5f, h - 1.5f, Ui.Ink, 1.5f, 4f, 3f);
                Ui.Text(root, text, Ui.TitleFont, ButtonFont, Ui.InkFaded, 0f, 0f, w, h, TextAlignmentOptions.Center)
                    .characterSpacing = 6f;
                Ui.Text(root, Loc.Get("ui.menu.soon"), Ui.ItalicFont, SoonFont, Ui.InkSoft, 0f, 0f, w - 20f, h,
                    TextAlignmentOptions.MidlineRight);
                return y + h + ButtonGap;
            }

            int index = _buttons.Count;
            var b = new MenuButton { Root = root, Y = y, OnClick = onClick };
            b.Shadow = Ui.Sliced(root, "Shadow", Ui.Art("Card_Shadow"), -14f, 4f, w + 28f, h + 22f);
            b.Shadow.pixelsPerUnitMultiplier = 60f / 18f;
            var face = Ui.Sliced(root, "Face", Ui.MenuButton, 0f, 0f, w, h, raycast: true);
            b.Hover = Ui.Sliced(root, "Hover", Ui.MenuButtonHover, 0f, 0f, w, h);
            b.Label = Ui.Text(root, text, Ui.TitleFont, ButtonFont, Ui.InkText, 0f, 0f, w, h, TextAlignmentOptions.Center);
            b.Label.characterSpacing = 6f;
            b.Label.textWrappingMode = TextWrappingModes.NoWrap;
            float half = b.Label.GetPreferredValues(text).x * 0.5f + 20f;
            b.Diamonds = new[]
            {
                Ui.Diamond(root, w * 0.5f - half, h * 0.5f, 8f, Ui.RubricLight),
                Ui.Diamond(root, w * 0.5f + half, h * 0.5f, 8f, Ui.RubricLight),
            };

            var button = face.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(onClick);
            Ui.OnHover(face.gameObject, () => _focus = index, () =>
            {
                if (_focus == index) _focus = -1;
            });
            _buttons.Add(b);
            Pose(b);
            return y + h + ButtonGap;
        }

        static void Pose(MenuButton b)
        {
            float t = b.Lift;
            b.Hover.color = new Color(1f, 1f, 1f, t);
            b.Shadow.color = new Color(1f, 1f, 1f, ShadowAlpha * t);
            b.Label.color = Color.Lerp(Ui.InkText, Ui.Cream, t);
            foreach (var d in b.Diamonds) d.color = new Color(Ui.RubricLight.r, Ui.RubricLight.g, Ui.RubricLight.b, t);
            b.Root.anchoredPosition = new Vector2(b.Root.anchoredPosition.x, -b.Y + t);
        }

        void OpenOptions()
        {
            if (_options == null) _options = new OptionsModal(() => _root, ReloadUi, () => _options = null);
        }

        // The menu is built once from Loc, so a new language means building it again.
        void ReloadUi()
        {
            Destroy(_root.gameObject);
            _buttons.Clear();
            _focus = -1;
            BuildUi();
            _intro.Clear();
        }

        // Everything is laid out for one profile, so another one means building it all again, open screens included.
        void ChangeProfile()
        {
            bool credits = _credits != null;
            ReloadUi();
            _credits = null;
            if (credits) OpenCredits();
            if (_mp != MpScreen.None)
            {
                _mpLayer = null;
                Rebuild();
            }
            if (_options != null) _options.Relayout();
        }

        void OpenCredits()
        {
            if (_credits != null) return;
            _credits = Ui.Stretch(Ui.Rect(_root, "Credits", 0, 0, 0, 0));
            var scrim = Ui.Stretch(Ui.Image(_credits, "Scrim", Ui.Solid, 0, 0, 0, 0, raycast: true).rectTransform);
            scrim.GetComponent<Image>().color = ScrimColor;
            var close = scrim.gameObject.AddComponent<Button>();
            close.transition = Selectable.Transition.None;
            close.onClick.AddListener(CloseCredits);

            const float w = 760f, h = 404f;
            var design = UiProfile.Design;
            var panel = Ui.Modal(UiProfile.ModalStage(_credits, w, h), (design.x - w) * 0.5f, (design.y - h) * 0.5f, w, h);
            var eyebrow = Ui.Label(panel, Loc.Get("ui.credits.eyebrow"), 0f, 44f, w, 26f, TextAlignmentOptions.Top);
            eyebrow.fontSize = 18f;
            eyebrow.characterSpacing = 20f;
            Ui.Text(panel, Ui.Initial(Loc.Get("ui.credits.title"), 122), Ui.TitleFont, 52f, Ui.InkText, 0f, 76f, w, 72f,
                TextAlignmentOptions.Top);
            Ui.Text(panel, Loc.Get("ui.credits.type") + "\n\n" + Loc.Get("ui.credits.engine"), Ui.BodyFont, 21f,
                Ui.InkText, 72f, 164f, w - 144f, 120f, TextAlignmentOptions.Top);
            const float buttonWidth = 220f;
            Ui.Button(panel, Loc.Get("ui.credits.back"), false, (w - buttonWidth) * 0.5f, h - 56f - 40f, buttonWidth, 56f,
                CloseCredits, 22f);
        }

        void CloseCredits()
        {
            if (_credits == null) return;
            Destroy(_credits.gameObject);
            _credits = null;
        }

        static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
