using System.Collections.Generic;
using ArcaneGambit.Rules;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ArcaneGambit.Game
{
    public enum CursorKind { Default, Clickable, Attack, Unavailable, Wait, Drag }

    public enum UiSize { Small, Medium, Large, Mobile }

    /// <summary>
    /// The screen profiles of the Resoluções design doc: the height of the screen picks the logical area the
    /// interface is laid out in. Medium is the area every modal is drawn for. The Android player has a single
    /// profile of its own, the landscape phone of the "mobile-horizontal" design doc.
    /// </summary>
    public static class UiProfile
    {
        public static readonly Vector2 Design = new Vector2(1920f, 1080f);
        public static readonly Vector2 MobileArea = new Vector2(844f, 390f);
        const int MediumHeight = 900, LargeHeight = 1300;
        const float ModalMargin = 20f, MobileModalMargin = 8f, MobileSide = 16f;

        // A compile-time switch, so the Editor shows the phone layout while Android is the build target.
#if UNITY_ANDROID
        public static readonly bool Mobile = true;
#else
        public static readonly bool Mobile = false;
#endif

        public static UiSize Size => Mobile ? UiSize.Mobile
            : Screen.height < MediumHeight ? UiSize.Small : Screen.height < LargeHeight ? UiSize.Medium : UiSize.Large;

        public static Vector2 Reference(UiSize size) => size switch
        {
            UiSize.Small => new Vector2(1440f, 810f),
            UiSize.Large => new Vector2(2048f, 1152f),
            UiSize.Mobile => MobileArea,
            _ => Design,
        };

        /// <summary>
        /// The margin, in canvas units, the phone layout keeps on both sides. It is the wider of the two safe-area
        /// insets, because the cutout changes side when the phone is turned around.
        /// </summary>
        public static float SideInset
        {
            get
            {
                float scale = Mathf.Min(Screen.width / MobileArea.x, Screen.height / MobileArea.y);
                var safe = Screen.safeArea;
                return Mathf.Max(MobileSide, safe.xMin / scale, (Screen.width - safe.xMax) / scale);
            }
        }

        // Expand keeps the logical area at least as large as the reference on any aspect ratio.
        public static void Apply(CanvasScaler scaler, UiSize size)
        {
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = Reference(size);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
        }

        /// <summary>The scale that makes a modal drawn for the Medium area fit the current one.</summary>
        public static float Fit(float w, float h)
        {
            var area = Reference(Size);
            float margin = Mobile ? MobileModalMargin : ModalMargin;
            return Mathf.Min(1f, (area.x - margin * 2f) / w, (area.y - margin * 2f) / h);
        }

        /// <summary>A centered, Medium-sized stage, shrunk when a <paramref name="w"/> by <paramref name="h"/> modal would not fit.</summary>
        public static RectTransform ModalStage(Transform parent, float w, float h)
        {
            var stage = Ui.Rect(parent, "Stage", 0, 0, Design.x, Design.y);
            stage.anchorMin = stage.anchorMax = stage.pivot = new Vector2(0.5f, 0.5f);
            stage.anchoredPosition = Vector2.zero;
            float fit = Fit(w, h);
            stage.localScale = new Vector3(fit, fit, 1f);
            return stage;
        }
    }

    public static class Ui
    {
        public static readonly Color Parchment = Hex(0xECDFC4);
        public static readonly Color Table = Hex(0xD9C8A5);
        public static readonly Color Ink = Hex(0x3B2A1A);
        public static readonly Color InkSoft = Hex(0x5A4128);
        public static readonly Color InkText = Hex(0x2B1D10);
        public static readonly Color InkBody = Hex(0x4A3520);
        public static readonly Color Rubric = Hex(0x8E2F21);
        public static readonly Color RubricDark = Hex(0x5E1D14);
        public static readonly Color Cream = Hex(0xF3E8D2);
        public static readonly Color InkFaded = Hex(0x6B5434);
        public static readonly Color RubricLight = Hex(0xC0573F);

        public const float CardWidth = 248f;
        public const float CardHeight = 348f;

        const float NormalWeight = 0.75f;

        static TMP_FontAsset s_title, s_body, s_italic, s_roman, s_fraktur;
        static Sprite s_panel, s_primary, s_secondary, s_solid, s_tile, s_highlight;
        static Sprite s_modal, s_dark, s_danger, s_selection, s_glow, s_menu, s_menuHover;
        static readonly Dictionary<string, Sprite> s_art = new Dictionary<string, Sprite>();
        static readonly Dictionary<string, Sprite> s_pills = new Dictionary<string, Sprite>();
        static readonly Dictionary<string, Sprite> s_boxes = new Dictionary<string, Sprite>();
        static int s_generation;
        static readonly string[] CursorFiles = { "padrao", "clicavel", "ataque", "indisponivel", "aguardar", "arrastar" };
        static readonly Vector2[] CursorHotspots =
        {
            new Vector2(1f, 1f), new Vector2(12f, 1f), new Vector2(16f, 16f),
            new Vector2(1f, 1f), new Vector2(16f, 16f), new Vector2(15f, 14f),
        };
        static readonly Texture2D[] s_cursors = new Texture2D[CursorFiles.Length];
        static CursorKind? s_cursor;

        // Domain reload is disabled in this project: statics must be cleared on every Play.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            s_cursor = null;
            s_title = s_body = s_italic = s_roman = s_fraktur = null;
            s_panel = s_primary = s_secondary = s_solid = s_tile = s_highlight = null;
            s_modal = s_dark = s_danger = s_selection = s_glow = s_menu = s_menuHover = null;
            s_art.Clear();
            s_pills.Clear();
            s_boxes.Clear();
            s_generation++;
        }

        static Color Hex(int rgb) =>
            new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, 1f);

        public static string ToHex(Color c) => "#" + ColorUtility.ToHtmlStringRGB(c);

        public static void SetCursor(CursorKind kind)
        {
            if (s_cursor == kind) return;
            int i = (int)kind;
            if (s_cursors[i] == null) s_cursors[i] = Resources.Load<Texture2D>("Art/Cursors/png/" + CursorFiles[i] + "_32");
            s_cursor = kind;
            Cursor.SetCursor(s_cursors[i], CursorHotspots[i], CursorMode.Auto);
        }

        public static string Roman(int n)
        {
            if (n <= 0) return "0";
            int[] values = { 1000, 900, 500, 400, 100, 90, 50, 40, 10, 9, 5, 4, 1 };
            string[] symbols = { "M", "CM", "D", "CD", "C", "XC", "L", "XL", "X", "IX", "V", "IV", "I" };
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < values.Length; i++)
                while (n >= values[i]) { sb.Append(symbols[i]); n -= values[i]; }
            return sb.ToString();
        }

        public static TMP_FontAsset TitleFont { get { if (s_title == null) LoadFonts(); return s_title; } }
        public static TMP_FontAsset BodyFont { get { if (s_body == null) LoadFonts(); return s_body; } }
        public static TMP_FontAsset ItalicFont { get { if (s_italic == null) LoadFonts(); return s_italic; } }
        public static TMP_FontAsset RomanFont { get { if (s_roman == null) LoadFonts(); return s_roman; } }
        public static TMP_FontAsset FrakturFont { get { if (s_fraktur == null) LoadFonts(); return s_fraktur; } }

        static void LoadFonts()
        {
            s_body = CreateFont("EBGaramond", "Body");
            s_title = CreateFont("IMFellEnglishSC", "Title");
            s_italic = CreateFont("IMFellEnglish-Italic", "Italic");
            s_roman = CreateFont("IMFellEnglish-Regular", "Roman");
            s_fraktur = CreateFont("UnifrakturMaguntia", "Fraktur");
            var fallback = new List<TMP_FontAsset> { s_body };
            s_title.fallbackFontAssetTable = fallback;
            s_italic.fallbackFontAssetTable = fallback;
            s_roman.fallbackFontAssetTable = fallback;
            s_fraktur.fallbackFontAssetTable = new List<TMP_FontAsset> { s_title };
        }

        static TMP_FontAsset CreateFont(string file, string tag)
        {
            var font = Resources.Load<Font>("Fonts/" + file);
            var asset = TMP_FontAsset.CreateFontAsset(font);

            // MaterialReferenceManager is static and survives Play sessions: names must be unique for <font="..."> tags.
            asset.name = $"AG_{tag}_{s_generation}";
            asset.hashCode = TMP_TextUtilities.GetHashCode(asset.name);
            asset.material.name = asset.name + " Material";
            asset.materialHashCode = TMP_TextUtilities.GetSimpleHashCode(asset.material.name);

            asset.normalStyle = NormalWeight;
            asset.material.SetFloat(ShaderUtilities.ID_WeightNormal, NormalWeight);
            if (!MaterialReferenceManager.TryGetFontAsset(asset.hashCode, out _))
                MaterialReferenceManager.AddFontAsset(asset);
            return asset;
        }

        public static string Initial(string text, int percent = 135) => Capital(text, FrakturFont, percent);

        public static string RomanInitial(string text, int percent = 120) => Capital(text, RomanFont, percent);

        /// <summary>The game's name: every word opens with a rubric IM Fell English capital.</summary>
        public static string Brand(string text, int percent)
        {
            var words = text.Split(' ');
            for (int i = 0; i < words.Length; i++) words[i] = Capital(words[i], RomanFont, percent);
            return string.Join(" ", words);
        }

        static string Capital(string text, TMP_FontAsset font, int percent)
        {
            if (string.IsNullOrEmpty(text)) return text;
            return $"<font=\"{font.name}\"><color={ToHex(Rubric)}><size={percent}%>{text[0]}</size></color></font>{text.Substring(1)}";
        }

        public static string SmallCaps(string text) => $"<font=\"{TitleFont.name}\">{text}</font>";

        public static Sprite Art(string name)
        {
            if (s_art.TryGetValue(name, out var cached) && cached != null) return cached;
            var sprite = Resources.Load<Sprite>("Art/" + name);
            if (sprite == null)
            {
                var tex = Resources.Load<Texture2D>("Art/" + name);
                if (tex != null)
                    sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
            }
            if (sprite == null) Debug.LogWarning($"[Arcane Gambit] Missing art: Resources/Art/{name}");
            s_art[name] = sprite;
            return sprite;
        }

        public static Sprite UnitArt(UnitType type, Side side) => Art($"Unit_{type}_{side}");

        public static Sprite KindIcon(CardKind kind) => Art("Kind_" + kind);

        static Sprite PanelSprite => s_panel != null ? s_panel : s_panel = Frame(Parchment, Ink, 2, 2, 1);
        static Sprite PrimarySprite => s_primary != null ? s_primary : s_primary = Frame(Rubric, RubricDark, 2, 0, 0);
        static Sprite SecondarySprite => s_secondary != null ? s_secondary : s_secondary = Frame(Color.clear, Ink, 2, 0, 0);
        public static Sprite Solid => s_solid != null ? s_solid : s_solid = Frame(Color.white, Color.white, 0, 0, 1);
        public static Sprite Tile => s_tile != null ? s_tile
            : s_tile = Frame(new Color(1f, 0.98f, 0.93f, 0.25f), new Color(Ink.r, Ink.g, Ink.b, 0.5f), 1, 0, 0);
        public static Sprite Highlight => s_highlight != null ? s_highlight
            : s_highlight = Frame(Color.clear, Rubric, 2, 0, 0);
        public static Sprite Outline => SecondarySprite;
        static Sprite ModalSprite => s_modal != null ? s_modal : s_modal = Frame(Parchment, Ink, 2, 4, 2);
        static Sprite DarkSprite => s_dark != null ? s_dark : s_dark = Frame(Ink, Parchment, 0, 4, 2, solid: true);
        static Sprite DangerSprite => s_danger != null ? s_danger : s_danger = Frame(Color.clear, Rubric, 2, 0, 0);
        public static Sprite Selection => s_selection != null ? s_selection
            : s_selection = Frame(new Color(Rubric.r, Rubric.g, Rubric.b, 0.07f), Rubric, 2, 0, 0);
        public static Sprite Glow => s_glow != null ? s_glow
            : s_glow = Frame(Color.clear, new Color(Rubric.r, Rubric.g, Rubric.b, 0.18f), 4, 0, 0);
        public static Sprite MenuButton => s_menu != null ? s_menu : s_menu = Frame(Parchment, Ink, 2, 3, 1);
        public static Sprite Framed => MenuButton;
        public static Sprite MenuButtonHover => s_menuHover != null ? s_menuHover
            : s_menuHover = Frame(Ink, Parchment, 0, 5, 1, solid: true);

        /// <summary>A sliced box with an outer line, a band of fill, then a thin inner line, as the parchment panels draw them.</summary>
        public static Sprite Boxed(Color fill, Color line, int outer = 2, int gap = 3, int inner = 1)
        {
            string key = $"{fill}{line}{outer}{gap}{inner}";
            if (s_boxes.TryGetValue(key, out var cached) && cached != null) return cached;
            return s_boxes[key] = Frame(fill, line, outer, gap, inner);
        }

        public static Sprite Pill(Color fill, Color line, int radius, float width = 1f)
        {
            string key = $"{fill}{line}{radius}{width}";
            if (s_pills.TryGetValue(key, out var cached) && cached != null) return cached;
            if (width <= 0f) line = fill;
            if (fill.a <= 0f) fill = new Color(line.r, line.g, line.b, 0f);
            int size = radius * 2 + 2;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave,
            };
            var pixels = new Color[size * size];
            float center = size * 0.5f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float qx = Mathf.Max(Mathf.Abs(x + 0.5f - center) - 1f, 0f);
                    float qy = Mathf.Max(Mathf.Abs(y + 0.5f - center) - 1f, 0f);
                    float d = Mathf.Sqrt(qx * qx + qy * qy);
                    var color = Color.Lerp(fill, line, Mathf.Clamp01(d - (radius - width) + 0.5f));
                    color.a *= Mathf.Clamp01(radius - d + 0.5f);
                    pixels[y * size + x] = color;
                }
            tex.SetPixels(pixels);
            tex.Apply();
            var sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
                SpriteMeshType.FullRect, new Vector4(radius, radius, radius, radius));
            s_pills[key] = sprite;
            return sprite;
        }

        /// <summary>
        /// The stretched center is only 2 texels wide, so bilinear filtering bleeds the inner line into it as a soft
        /// vignette; <paramref name="solid"/> slices one fill texel further in to keep the center flat.
        /// </summary>
        static Sprite Frame(Color fill, Color line, int outer, int gap, int inner, bool solid = false)
        {
            int border = outer + gap + inner;
            int pad = solid ? 1 : 0;
            int size = (border + pad) * 2 + 2;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave,
            };
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    int d = Mathf.Min(Mathf.Min(x, y), Mathf.Min(size - 1 - x, size - 1 - y));
                    pixels[y * size + x] = d < outer ? line : d < outer + gap ? fill : d < border ? line : fill;
                }
            tex.SetPixels(pixels);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
                SpriteMeshType.FullRect, Vector4.one * (border + pad));
        }

        public static RectTransform Rect(Transform parent, string name, float x, float y, float w, float h)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            // The board and the HUD are drawn by different cameras, which pick what they render by layer.
            go.layer = parent.gameObject.layer;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(w, h);
            return rt;
        }

        public static RectTransform Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            return rt;
        }

        public static RectTransform Anchor(RectTransform rt, Vector2 min, Vector2 max, Vector2 reference)
        {
            float left = rt.anchoredPosition.x, top = reference.y + rt.anchoredPosition.y;
            float right = left + rt.sizeDelta.x, bottom = top - rt.sizeDelta.y;
            rt.anchorMin = min;
            rt.anchorMax = max;
            rt.offsetMin = new Vector2(left - min.x * reference.x, bottom - min.y * reference.y);
            rt.offsetMax = new Vector2(right - max.x * reference.x, top - max.y * reference.y);
            return rt;
        }

        public static Image Image(Transform parent, string name, Sprite sprite, float x, float y, float w, float h,
            bool raycast = false)
        {
            var img = Rect(parent, name, x, y, w, h).gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.raycastTarget = raycast;
            return img;
        }

        public static Image Line(Transform parent, float x, float y, float w, float h, float alpha = 1f)
        {
            var img = Image(parent, "Line", Solid, x, y, w, h);
            img.color = new Color(Ink.r, Ink.g, Ink.b, alpha);
            return img;
        }

        public static Image Panel(Transform parent, string name, float x, float y, float w, float h)
        {
            var img = Image(parent, name, PanelSprite, x, y, w, h, raycast: true);
            img.type = UnityEngine.UI.Image.Type.Sliced;
            return img;
        }

        public static RectTransform Modal(Transform parent, float x, float y, float w, float h, bool dark = false)
        {
            var shadow = Image(parent, "Shadow", Art("Card_Shadow"), x - 30f, y + 4f, w + 60f, h + 66f);
            shadow.type = UnityEngine.UI.Image.Type.Sliced;
            shadow.color = new Color(1f, 1f, 1f, 0.75f);
            var img = Image(parent, dark ? "Band" : "Modal", dark ? DarkSprite : ModalSprite, x, y, w, h, raycast: true);
            img.type = UnityEngine.UI.Image.Type.Sliced;
            return img.rectTransform;
        }

        public static Image Sliced(Transform parent, string name, Sprite sprite, float x, float y, float w, float h,
            bool raycast = false)
        {
            var img = Image(parent, name, sprite, x, y, w, h, raycast);
            img.type = UnityEngine.UI.Image.Type.Sliced;
            return img;
        }

        /// <summary>Moves the pivot to the center without moving the rect, so rotations turn around the middle.</summary>
        public static RectTransform Centered(RectTransform rt)
        {
            rt.anchoredPosition += new Vector2(rt.sizeDelta.x * 0.5f, -rt.sizeDelta.y * 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            return rt;
        }

        public static ScrollRect Scroll(Transform parent, float x, float y, float w, float h)
        {
            const float bar = 8f, gap = 10f;
            var root = Rect(parent, "Scroll", x, y, w, h);
            var viewport = Stretch(Rect(root, "Viewport", 0, 0, 0, 0));
            viewport.offsetMax = new Vector2(-(bar + gap), 0f);
            viewport.gameObject.AddComponent<RectMask2D>();
            viewport.gameObject.AddComponent<Image>().color = Color.clear;
            var content = Rect(viewport, "Content", 0, 0, 0, 0);
            content.anchorMax = Vector2.one;

            var track = Sliced(root, "Scrollbar", Pill(new Color(Ink.r, Ink.g, Ink.b, 0.12f), Color.clear, 4, 0f),
                0, 0, 0, 0, raycast: true);
            var trackRt = track.rectTransform;
            trackRt.anchorMin = new Vector2(1f, 0f);
            trackRt.anchorMax = Vector2.one;
            trackRt.pivot = Vector2.one;
            trackRt.offsetMin = new Vector2(-bar, 0f);
            trackRt.offsetMax = Vector2.zero;
            var handle = Sliced(track.transform, "Handle", Pill(InkSoft, Color.clear, 4, 0f), 0, 0, 0, 0, raycast: true);
            var scrollbar = track.gameObject.AddComponent<Scrollbar>();
            scrollbar.handleRect = Stretch(handle.rectTransform);
            scrollbar.targetGraphic = handle;
            scrollbar.direction = Scrollbar.Direction.BottomToTop;

            var scroll = root.gameObject.AddComponent<ScrollRect>();
            scroll.content = content;
            scroll.viewport = viewport;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 24f;
            scroll.verticalScrollbar = scrollbar;
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
            return scroll;
        }

        public static TextMeshProUGUI Text(Transform parent, string text, TMP_FontAsset font, float size, Color color,
            float x, float y, float w, float h, TextAlignmentOptions align = TextAlignmentOptions.TopLeft)
        {
            var tmp = Rect(parent, "Text", x, y, w, h).gameObject.AddComponent<TextMeshProUGUI>();
            tmp.font = font;
            tmp.fontSize = size;
            tmp.color = color;
            tmp.alignment = align;
            tmp.textWrappingMode = TextWrappingModes.Normal;
            tmp.overflowMode = TextOverflowModes.Overflow;
            tmp.raycastTarget = false;
            tmp.text = text;
            return tmp;
        }

        public static TextMeshProUGUI Label(Transform parent, string text, float x, float y, float w, float h = 21f,
            TextAlignmentOptions align = TextAlignmentOptions.TopLeft)
        {
            var tmp = Text(parent, text, TitleFont, 15f, InkSoft, x, y, w, h, align);
            tmp.characterSpacing = 14f;
            return tmp;
        }

        public static Button Button(Transform parent, string label, bool primary, float x, float y, float w, float h,
            UnityAction onClick, float fontSize = 20f)
        {
            var img = Image(parent, "Button", primary ? PrimarySprite : SecondarySprite, x, y, w, h, raycast: true);
            img.type = UnityEngine.UI.Image.Type.Sliced;
            var button = MakeButton(img, onClick);
            var text = Text(img.transform, label, TitleFont, fontSize, primary ? Cream : InkText, 0, 0, w, h,
                TextAlignmentOptions.Center);
            text.characterSpacing = 4f;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            return button;
        }

        public static Button MakeButton(Image img, UnityAction onClick)
        {
            img.raycastTarget = true;
            var button = img.gameObject.AddComponent<Button>();
            button.targetGraphic = img;
            var colors = button.colors;
            colors.highlightedColor = new Color(0.9f, 0.86f, 0.8f);
            colors.pressedColor = new Color(0.75f, 0.7f, 0.62f);
            colors.selectedColor = Color.white;
            colors.disabledColor = new Color(1f, 1f, 1f, 0.35f);
            button.colors = colors;
            button.onClick.AddListener(onClick);
            return button;
        }

        public static void OnHover(GameObject target, System.Action enter, System.Action exit)
        {
            var trigger = target.AddComponent<EventTrigger>();
            var enterEntry = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
            enterEntry.callback.AddListener(_ => enter());
            var exitEntry = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
            exitEntry.callback.AddListener(_ => exit());
            trigger.triggers.Add(enterEntry);
            trigger.triggers.Add(exitEntry);
        }

        public static void Dashed(RectTransform parent, Vector2 start, Vector2 end, Color color, float width, float dash,
            float gap)
        {
            var delta = end - start;
            float length = delta.magnitude;
            var line = Rect(parent, "Dashes", 0f, 0f, length, width);
            line.pivot = new Vector2(0f, 0.5f);
            line.anchoredPosition = start;
            line.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
            for (float d = 0f; d < length; d += dash + gap)
                Image(line, "Dash", Solid, d, 0f, Mathf.Min(dash, length - d), width).color = color;
        }

        public static void DashedBox(RectTransform parent, float x, float y, float w, float h, Color color, float width,
            float dash, float gap)
        {
            var tl = new Vector2(x, -y);
            var tr = tl + new Vector2(w, 0f);
            var br = tr + new Vector2(0f, -h);
            var bl = tl + new Vector2(0f, -h);
            Dashed(parent, tl, tr, color, width, dash, gap);
            Dashed(parent, tr, br, color, width, dash, gap);
            Dashed(parent, br, bl, color, width, dash, gap);
            Dashed(parent, bl, tl, color, width, dash, gap);
        }

        public static Image Diamond(Transform parent, float centerX, float centerY, float size, Color color)
        {
            var diamond = Image(parent, "Diamond", Solid, centerX - size * 0.5f, centerY - size * 0.5f, size, size);
            diamond.color = color;
            Centered(diamond.rectTransform).localRotation = Quaternion.Euler(0f, 0f, 45f);
            return diamond;
        }

        /// <summary>The round 44-unit button of the phone layout; the caller draws its glyph inside.</summary>
        public static Button IconButton(Transform parent, float x, float y, UnityAction onClick, bool light = false)
        {
            var face = light
                ? Pill(new Color(0.125f, 0.082f, 0.039f, 0.35f), new Color(Cream.r, Cream.g, Cream.b, 0.8f), 22, 1.5f)
                : Pill(new Color(1f, 0.98f, 0.93f, 0.4f), Ink, 22, 1.5f);
            return MakeButton(Sliced(parent, "Icon Button", face, x, y, 44f, 44f, raycast: true), onClick);
        }

        public static void Stroke(Transform parent, float centerX, float centerY, float length, float angle, Color color,
            float thickness = 2f)
        {
            var stroke = Image(parent, "Stroke", Solid, centerX - length * 0.5f, centerY - thickness * 0.5f, length, thickness);
            stroke.color = color;
            Centered(stroke.rectTransform).localRotation = Quaternion.Euler(0f, 0f, angle);
        }

        /// <summary>An arrowhead pointing left (<paramref name="direction"/> -1) or right (1).</summary>
        public static void Chevron(Transform parent, float centerX, float centerY, float size, int direction, Color color)
        {
            float reach = size * 0.354f;
            Stroke(parent, centerX, centerY - reach, size, -45f * direction, color);
            Stroke(parent, centerX, centerY + reach, size, 45f * direction, color);
        }

        public static void Danger(Button button)
        {
            ((Image)button.targetGraphic).sprite = DangerSprite;
            button.GetComponentInChildren<TextMeshProUGUI>().color = Rubric;
        }

        public static void SetButton(Button button, string label, bool interactable)
        {
            button.interactable = interactable;
            var text = button.GetComponentInChildren<TextMeshProUGUI>();
            text.text = label;
            text.alpha = interactable ? 1f : 0.45f;
        }

        public static void Seal(Transform parent, int value, float x, float y, float size) =>
            Seal(parent, Roman(value), Art("Seal"), Cream, 0.47f, x, y, size);

        static TextMeshProUGUI Seal(Transform parent, string label, Sprite sprite, Color color, float fontScale,
            float x, float y, float size)
        {
            var seal = Image(parent, "Seal", sprite, x, y, size, size);
            return Text(seal.transform, label, TitleFont, size * fontScale, color, 0, 0, size, size,
                TextAlignmentOptions.Center);
        }

        const float FaceLeft = 22.5f, FaceRight = 225.5f, FaceWidth = FaceRight - FaceLeft;
        const float TypeTop = 188.5f, WeatherTypeTop = 186.5f, TypeHeight = 20f;
        const float TextBottom = 324.5f, WeatherBandTop = 312.5f;
        const float TokenSize = 18f, TokenGap = 3f;

        public static RectTransform Card(Transform parent, CardInfo info, float scale)
        {
            bool weather = info.IsWeather;
            string frame = info.Kind == CardKind.Arcane ? "CardFrame_Arcane" : weather ? "CardFrame_Condition" : "CardFrame";
            var root = CardFace(parent, "Card " + info.Id, frame, Loc.CardName(info.Id), 130f, scale, out var face);
            if (weather) Image(face, "Art", Art("Card_" + info.Id), FaceLeft, 71.5f, FaceWidth, 108f);
            else Illustration(face, Art("Card_" + info.Id));

            var badge = Image(face, "Kind", Art("Kind_Badge"), 158.5f, 29.5f, 28f, 28f);
            Image(badge.transform, "Icon", KindIcon(info.Kind), 5f, 5f, 18f, 18f).color = Ink;
            if (info.CostUndefined)
                Seal(face, "?", Art("Seal_Undefined"), Rubric, 0.47f, 191.5f, 26.5f, 34f);
            else if (info.MinCost < info.Cost)
                Seal(face, Roman(info.MinCost) + "–" + Roman(info.Cost), Art("Seal"), Cream, 0.35f, 191.5f, 26.5f, 34f)
                    .characterSpacing = -2f;
            else
                Seal(face, info.Cost, 191.5f, 26.5f, 34f);

            float top = weather ? WeatherTypeTop : TypeTop;
            float ruleEnd = info.Pieces is PieceRequirement pieces ? Pieces(face, pieces, top) - 6f : FaceRight;
            TypeLine(face, Loc.CardType(info), info.IsReaction ? Rubric : InkSoft, top, ruleEnd);
            Body(face, Loc.CardText(info.Id), top, weather ? WeatherBandTop - 6f : TextBottom);
            if (weather)
            {
                var band = Text(face, Loc.Get("card.weatherBand"), TitleFont, 10.5f, InkText,
                    11.5f, WeatherBandTop + 1f, CardWidth - 23f, 23f, TextAlignmentOptions.Center);
                band.characterSpacing = 8f;
            }
            return root;
        }

        public const float MiniCardWidth = 66f, MiniCardHeight = 106f;

        /// <summary>The compact card of the phone's hand: kind, cost, art and name; the text is read enlarged.</summary>
        public static RectTransform MiniCard(Transform parent, CardInfo info)
        {
            const float w = MiniCardWidth, h = MiniCardHeight, badge = 17f;
            var root = Rect(parent, "Card " + info.Id, 0, 0, w, h);
            Sliced(root, "Frame", Pill(Parchment, new Color(0.7f, 0.61f, 0.44f), 4), 0, 0, w, h, raycast: true);
            Sliced(root, "Rule", Boxed(new Color(Ink.r, Ink.g, Ink.b, 0f), Ink, 1, 2, 1), 3f, 3f, w - 6f, h - 6f);
            if (info.Kind == CardKind.Arcane) DashedBox(root, 9f, 9f, w - 18f, h - 18f, Rubric, 1f, 3f, 2f);

            var kind = Image(root, "Kind", Art("Kind_Badge"), 7f, 8f, badge, badge);
            Image(kind.transform, "Icon", KindIcon(info.Kind), 3f, 3f, 11f, 11f).color = Ink;
            float sealX = w - 7f - badge;
            if (info.CostUndefined) Seal(root, "?", Art("Seal_Undefined"), Rubric, 0.5f, sealX, 8f, badge);
            else if (info.MinCost < info.Cost)
                Seal(root, Roman(info.MinCost) + "–" + Roman(info.Cost), Art("Seal"), Cream, 0.36f, sealX, 8f, badge)
                    .characterSpacing = -2f;
            else Seal(root, info.Cost, sealX, 8f, badge);

            Line(root, 7f, 28f, w - 14f, 1f);
            var art = Image(root, "Art", Art("Card_" + info.Id), 7f, 29f, w - 14f, 33f);
            art.preserveAspect = true;
            art.enabled = art.sprite != null;
            Line(root, 7f, 62f, w - 14f, 1f);

            var name = Text(root, Loc.CardName(info.Id), TitleFont, 11f, InkText, 5f, 66f, w - 10f, h - 66f - 7f,
                TextAlignmentOptions.Top);
            name.lineSpacing = -12f;
            name.enableAutoSizing = true;
            name.fontSizeMin = 8.5f;
            name.fontSizeMax = 11f;
            return root;
        }

        public static RectTransform Plate(Transform parent, string title, Sprite art, string kind, string text,
            float scale = 1f)
        {
            var root = CardFace(parent, "Plate", "CardFrame", title, FaceWidth, scale, out var face);
            Illustration(face, art);
            TypeLine(face, kind, InkSoft, TypeTop, FaceRight);
            Body(face, text, TypeTop, TextBottom);
            return root;
        }

        static RectTransform CardFace(Transform parent, string name, string frame, string title, float titleWidth,
            float scale, out RectTransform face)
        {
            var root = Rect(parent, name, 0, 0, CardWidth * scale, CardHeight * scale);
            face = Rect(root, "Face", 0, 0, CardWidth, CardHeight);
            face.localScale = new Vector3(scale, scale, 1f);

            Image(face, "Frame", Art(frame), 0, 0, CardWidth, CardHeight, raycast: true);

            var heading = Text(face, Initial(title, 140), TitleFont, 18f, InkText,
                FaceLeft, 19.5f, titleWidth, 48f, TextAlignmentOptions.Left);
            heading.lineSpacing = -18f;
            heading.enableAutoSizing = true;
            heading.fontSizeMin = 15f;
            heading.fontSizeMax = 18f;
            return root;
        }

        static void Illustration(RectTransform face, Sprite art)
        {
            var illustration = Image(face, "Art", art, FaceLeft, 73.5f, FaceWidth, 104f);
            illustration.preserveAspect = true;
            illustration.enabled = art != null;
        }

        /// <summary>Lays the pieces out right to left from the face edge and returns where they start.</summary>
        static float Pieces(RectTransform face, PieceRequirement req, float top)
        {
            float x = FaceRight;
            for (int i = req.Link == PieceLink.None ? 0 : 1; i >= 0; i--)
            {
                x -= TokenSize;
                if (req.IsEnemy(i)) Image(face, "Foe", Art("Token_Foe"), x - 3f, top - 2f, 24f, 24f);
                Image(face, "Piece", Art("Token_" + (i == 0 ? req.First : req.Second)), x, top + 1f, TokenSize, TokenSize);
                if (i == 0) break;

                var link = Text(face, Loc.Get("card.pieces." + req.Link), ItalicFont, 12f, Ink, 0f, top, 0f, TypeHeight,
                    TextAlignmentOptions.Center);
                link.textWrappingMode = TextWrappingModes.NoWrap;
                float width = link.GetPreferredValues(link.text).x;
                x -= TokenGap + width;
                link.rectTransform.anchoredPosition = new Vector2(x, -top);
                link.rectTransform.sizeDelta = new Vector2(width, TypeHeight);
                x -= TokenGap;
            }
            return x;
        }

        static void TypeLine(RectTransform face, string kind, Color color, float top, float ruleEnd)
        {
            var text = Text(face, kind, TitleFont, 13f, color, FaceLeft, top, FaceWidth, TypeHeight,
                TextAlignmentOptions.Left);
            text.characterSpacing = 10f;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            float ruleStart = FaceLeft + text.GetPreferredValues(kind).x + 6f;
            if (ruleEnd <= ruleStart) return;
            Line(face, ruleStart, top + 7.7f, ruleEnd - ruleStart, 1f, 0.7f);
            Line(face, ruleStart, top + 11.7f, ruleEnd - ruleStart, 0.6f, 0.45f);
        }

        static void Body(RectTransform face, string text, float typeTop, float bottom)
        {
            float top = typeTop + TypeHeight + 7f;
            var body = Text(face, text, BodyFont, 13.5f, InkText, FaceLeft, top, FaceWidth, bottom - top,
                TextAlignmentOptions.Top);
            body.lineSpacing = -6f;
            body.enableAutoSizing = true;
            body.fontSizeMin = 12f;
            body.fontSizeMax = 13.5f;
        }
    }
}
