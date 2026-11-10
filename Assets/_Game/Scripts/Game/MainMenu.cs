using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace ArcaneGambit.Game
{
    public sealed class MainMenu : MonoBehaviour
    {
        public const string SceneName = "MainMenu";

        const float StageWidth = 1920f;
        const float StageHeight = 1080f;
        static readonly Vector2 Stage = new Vector2(StageWidth, StageHeight);
        const float BrandX = 120f, BrandY = 90f, BrandWidth = 880f, LogoSize = 330f, BrandGap = 18f;
        const float PanelX = 1140f, PanelY = 220f, PanelWidth = 560f, PanelTop = 44f, PanelPadding = 48f;
        const float ButtonHeight = 70f, ButtonGap = 16f, ButtonFont = 30f, DividerHeight = 16f;
        const float HoverTime = 0.15f, ShadowAlpha = 0.8f;
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
        CanvasScaler _scaler;
        RectTransform _root, _credits;

        void Start()
        {
            Time.timeScale = 1f;
            Ui.SetCursor(CursorKind.Default);
            BuildUi();
        }

        void Update()
        {
            float aspect = (float)Screen.width / Mathf.Max(1, Screen.height);
            _scaler.matchWidthOrHeight = aspect >= StageWidth / StageHeight ? 1f : 0f;

            for (int i = 0; i < _buttons.Count; i++)
            {
                var b = _buttons[i];
                float target = i == _focus ? 1f : 0f;
                if (Mathf.Approximately(b.Lift, target)) continue;
                b.Lift = Mathf.MoveTowards(b.Lift, target, Time.unscaledDeltaTime / HoverTime);
                Pose(b);
            }

            // A clicked button keeps UI focus, and the UI module would click it again on Enter.
            var events = EventSystem.current;
            if (events != null && events.currentSelectedGameObject != null) events.SetSelectedGameObject(null);

            var keyboard = Keyboard.current;
            if (keyboard != null) OnKeys(keyboard);
        }

        void OnKeys(Keyboard keys)
        {
            bool enter = keys.enterKey.wasPressedThisFrame || keys.numpadEnterKey.wasPressedThisFrame;
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
            _scaler = canvasGo.GetComponent<CanvasScaler>();
            _scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            _scaler.referenceResolution = Stage;
            _scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            _root = (RectTransform)canvasGo.transform;

            Ui.Stretch(Ui.Image(_root, "Background", Ui.Art("Background"), 0, 0, 0, 0).rectTransform);
            Ui.Stretch(Ui.Image(_root, "Backdrop", Ui.Art("Menu_Backdrop"), 0, 0, 0, 0).rectTransform);

            var stage = CenteredStage(_root);
            BuildBrand(stage);
            BuildMenu(stage);

            var version = Ui.Label(_root, Loc.Get("ui.menu.version"), 48f, StageHeight - 32f - 24f, 400f, 24f);
            version.fontSize = 17f;
            version.characterSpacing = 14f;
            version.color = Ui.InkBody;
            Ui.Anchor(version.rectTransform, Vector2.zero, Vector2.zero, Stage);
        }

        static RectTransform CenteredStage(RectTransform parent)
        {
            var stage = Ui.Rect(parent, "Stage", 0, 0, StageWidth, StageHeight);
            stage.anchorMin = stage.anchorMax = stage.pivot = new Vector2(0.5f, 0.5f);
            stage.anchoredPosition = Vector2.zero;
            return stage;
        }

        static void BuildBrand(RectTransform stage)
        {
            const float titleHeight = 122f, taglineHeight = 84f;
            float center = BrandX + BrandWidth * 0.5f;
            Ui.Image(stage, "Logo", Ui.Art("Logo"), center - LogoSize * 0.5f, BrandY, LogoSize, LogoSize);
            float y = BrandY + LogoSize + BrandGap;
            var title = Ui.Text(stage, Rubricate(Loc.Get("ui.title")), Ui.TitleFont, 112f, Ui.InkText, BrandX, y,
                BrandWidth, titleHeight, TextAlignmentOptions.Bottom);
            title.textWrappingMode = TextWrappingModes.NoWrap;
            y += titleHeight + BrandGap;
            Ornament(stage, center, y + DividerHeight * 0.5f);
            y += DividerHeight + BrandGap;
            Ui.Text(stage, Loc.Get("ui.menu.tagline"), Ui.ItalicFont, 30f, Ui.Ink, BrandX, y, BrandWidth, taglineHeight,
                TextAlignmentOptions.Top);
        }

        static string Rubricate(string text)
        {
            var words = text.Split(' ');
            for (int i = 0; i < words.Length; i++)
                if (words[i].Length > 0)
                    words[i] = $"<color={Ui.ToHex(Ui.Rubric)}><size=114%>{words[i][0]}</size></color>{words[i].Substring(1)}";
            return string.Join(" ", words);
        }

        static void Ornament(RectTransform parent, float center, float y)
        {
            Ui.Line(parent, center - 160f, y - 0.6f, 140f, 1.2f);
            Ui.Line(parent, center + 20f, y - 0.6f, 140f, 1.2f);
            foreach (float dx in new[] { -14f, 14f })
                Ui.Image(parent, "Dot", Ui.Pill(Ui.Ink, Ui.Ink, 4, 0f), center + dx - 2f, y - 2f, 4f, 4f);
            Ui.Diamond(parent, center, y, 10f, Ui.Rubric);
        }

        void BuildMenu(RectTransform stage)
        {
            const float headerHeight = 24f, headerMargin = 8f;
            float height = PanelTop + headerHeight + headerMargin + ButtonGap + 4f * (ButtonHeight + ButtonGap)
                + DividerHeight + ButtonGap + ButtonHeight + PanelPadding;
            var panel = Ui.Modal(stage, PanelX, PanelY, PanelWidth, height);

            var header = Ui.Label(panel, Loc.Get("ui.menu.header"), 0f, PanelTop, PanelWidth, headerHeight,
                TextAlignmentOptions.Top);
            header.fontSize = 18f;
            header.characterSpacing = 30f;

            float y = PanelTop + headerHeight + headerMargin + ButtonGap;
            y = AddButton(panel, "ui.menu.singlePlayer", y, () => SceneManager.LoadScene(GameManager.SceneName));
            y = AddButton(panel, "ui.menu.multiplayer", y, null);
            y = AddButton(panel, "ui.menu.options", y, null);
            y = AddButton(panel, "ui.menu.credits", y, OpenCredits);

            float center = PanelWidth * 0.5f, line = y + DividerHeight * 0.5f;
            Ui.Line(panel, PanelPadding + 40f, line - 0.6f, 172f, 1.2f);
            Ui.Line(panel, center + 20f, line - 0.6f, 172f, 1.2f);
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
                Ui.Text(root, Loc.Get("ui.menu.soon"), Ui.ItalicFont, 17f, Ui.InkSoft, 0f, 0f, w - 20f, h,
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
            var panel = Ui.Modal(CenteredStage(_credits), (StageWidth - w) * 0.5f, (StageHeight - h) * 0.5f, w, h);
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
