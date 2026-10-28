using System.Collections.Generic;
using ArcaneGambit.Rules;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace ArcaneGambit.Game
{
    /// <summary>
    /// Palette, fonts, sprites and uGUI widget factories for the art direction ("parchment, sepia ink and rubric").
    /// Layout coordinates: (x, y) from the parent's top-left corner, y growing downwards.
    /// </summary>
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

        public const float CardWidth = 248f;
        public const float CardHeight = 348f;

        static TMP_FontAsset s_title, s_body, s_italic, s_fraktur;
        static Sprite s_panel, s_primary, s_secondary, s_solid, s_tile, s_highlight;
        static readonly Dictionary<string, Sprite> s_art = new Dictionary<string, Sprite>();
        static int s_generation;

        // Domain reload is disabled in this project: statics must be cleared on every Play.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            s_title = s_body = s_italic = s_fraktur = null;
            s_panel = s_primary = s_secondary = s_solid = s_tile = s_highlight = null;
            s_art.Clear();
            s_generation++;
        }

        static Color Hex(int rgb) =>
            new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, 1f);

        public static string ToHex(Color c) => "#" + ColorUtility.ToHtmlStringRGB(c);

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

        // ------------------------------------------------------------------ fonts

        /// <summary>IM Fell English SC — names, titles, labels.</summary>
        public static TMP_FontAsset TitleFont { get { if (s_title == null) LoadFonts(); return s_title; } }
        /// <summary>EB Garamond — rules text and history.</summary>
        public static TMP_FontAsset BodyFont { get { if (s_body == null) LoadFonts(); return s_body; } }
        /// <summary>IM Fell English italic — notes and captions.</summary>
        public static TMP_FontAsset ItalicFont { get { if (s_italic == null) LoadFonts(); return s_italic; } }

        static void LoadFonts()
        {
            s_body = CreateFont("EBGaramond", "Body");
            s_title = CreateFont("IMFellEnglishSC", "Title");
            s_italic = CreateFont("IMFellEnglish-Italic", "Italic");
            s_fraktur = CreateFont("UnifrakturMaguntia", "Fraktur");
            var fallback = new List<TMP_FontAsset> { s_body };
            s_title.fallbackFontAssetTable = fallback;
            s_italic.fallbackFontAssetTable = fallback;
            s_fraktur.fallbackFontAssetTable = new List<TMP_FontAsset> { s_title };
        }

        static TMP_FontAsset CreateFont(string file, string tag)
        {
            var font = Resources.Load<Font>("Fonts/" + file);
            var asset = TMP_FontAsset.CreateFontAsset(font);

            // TMP's MaterialReferenceManager is static and keys both the font and its material by name hash.
            // Runtime-created materials share a default name, so each one needs a unique name (per Play session,
            // since that registry survives with domain reload disabled) to be usable in <font="..."> tags.
            asset.name = $"AG_{tag}_{s_generation}";
            asset.hashCode = TMP_TextUtilities.GetHashCode(asset.name);
            asset.material.name = asset.name + " Material";
            asset.materialHashCode = TMP_TextUtilities.GetSimpleHashCode(asset.material.name);
            if (!MaterialReferenceManager.TryGetFontAsset(asset.hashCode, out _))
                MaterialReferenceManager.AddFontAsset(asset);
            return asset;
        }

        /// <summary>Rubric blackletter drop cap on the first letter, as in the art direction.</summary>
        public static string Initial(string text, int percent = 135)
        {
            if (string.IsNullOrEmpty(text)) return text;
            if (s_fraktur == null) LoadFonts();
            return $"<font=\"{s_fraktur.name}\"><color={ToHex(Rubric)}><size={percent}%>{text[0]}</size></color></font>{text.Substring(1)}";
        }

        // ------------------------------------------------------------------ sprites

        /// <summary>PNG from Resources/Art (rasterized from the art direction SVGs).</summary>
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

        static Sprite PanelSprite => s_panel != null ? s_panel : s_panel = Frame(Parchment, Ink, 2, 2, 1);
        static Sprite PrimarySprite => s_primary != null ? s_primary : s_primary = Frame(Rubric, RubricDark, 2, 0, 0);
        static Sprite SecondarySprite => s_secondary != null ? s_secondary : s_secondary = Frame(Color.clear, Ink, 2, 0, 0);
        public static Sprite Solid => s_solid != null ? s_solid : s_solid = Frame(Color.white, Color.white, 0, 0, 1);
        /// <summary>Reserve tile: faint paper fill with a soft ink rule (".rt" class).</summary>
        public static Sprite Tile => s_tile != null ? s_tile
            : s_tile = Frame(new Color(1f, 0.98f, 0.93f, 0.25f), new Color(Ink.r, Ink.g, Ink.b, 0.5f), 1, 0, 0);
        /// <summary>Hollow rubric rule marking the chosen option.</summary>
        public static Sprite Highlight => s_highlight != null ? s_highlight
            : s_highlight = Frame(Color.clear, Rubric, 2, 0, 0);

        /// <summary>Code-generated 9-slice frame: outer rule, gap and inner rule.</summary>
        static Sprite Frame(Color fill, Color line, int outer, int gap, int inner)
        {
            int border = outer + gap + inner;
            int size = border * 2 + 2;
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
                SpriteMeshType.FullRect, new Vector4(border, border, border, border));
        }

        // ------------------------------------------------------------------ widgets

        public static RectTransform Rect(Transform parent, string name, float x, float y, float w, float h)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
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

        /// <summary>Small spaced small-caps label (".ph" class in the art direction).</summary>
        public static TextMeshProUGUI Label(Transform parent, string text, float x, float y, float w, float h = 18f,
            TextAlignmentOptions align = TextAlignmentOptions.TopLeft)
        {
            var tmp = Text(parent, text, TitleFont, 14f, InkSoft, x, y, w, h, align);
            tmp.characterSpacing = 8f;
            return tmp;
        }

        public static Button Button(Transform parent, string label, bool primary, float x, float y, float w, float h,
            UnityAction onClick, float fontSize = 17f)
        {
            var img = Image(parent, "Button", primary ? PrimarySprite : SecondarySprite, x, y, w, h, raycast: true);
            img.type = UnityEngine.UI.Image.Type.Sliced;
            var button = MakeButton(img, onClick);
            var text = Text(img.transform, label, TitleFont, fontSize, primary ? Cream : InkText, 0, 0, w, h,
                TextAlignmentOptions.Center);
            text.textWrappingMode = TextWrappingModes.NoWrap;
            return button;
        }

        /// <summary>Turns a graphic into a button with the parchment hover/press tints.</summary>
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

        public static void SetButton(Button button, string label, bool interactable)
        {
            button.interactable = interactable;
            var text = button.GetComponentInChildren<TextMeshProUGUI>();
            text.text = label;
            text.alpha = interactable ? 1f : 0.45f;
        }

        /// <summary>Wax seal with a Roman numeral (Morale cost).</summary>
        public static void Seal(Transform parent, int value, float x, float y, float size)
        {
            var seal = Image(parent, "Seal", Art("Seal"), x, y, size, size);
            Text(seal.transform, Roman(value), TitleFont, size * 0.47f, Cream, 0, 0, size, size,
                TextAlignmentOptions.Center);
        }

        /// <summary>Card in the "Arsenal" sheet layout (248×348), scaled by <paramref name="scale"/>.
        /// The root already has the scaled size and receives clicks.</summary>
        public static RectTransform Card(Transform parent, CardInfo info, float scale)
        {
            var root = CardFace(parent, "Card " + info.Id, info.Kind == CardKind.Arcane ? "CardFrame_Arcane" : "CardFrame",
                Loc.CardName(info.Id), 162f, Art("Card_" + info.Id), Loc.KindName(info.Kind),
                info.IsReaction ? Rubric : InkSoft, Loc.CardText(info.Id), scale, out var face);
            Seal(face, info.Cost, 191.5f, 26.5f, 34f);
            return root;
        }

        /// <summary>Card-shaped plate for what is not a card (a unit, an army): same frame and typography,
        /// with the token as the illustration.</summary>
        public static RectTransform Plate(Transform parent, string title, Sprite art, string kind, string text,
            float scale = 1f)
        {
            return CardFace(parent, "Plate", "CardFrame", title, 203f, art, kind, InkSoft, text, scale, out _);
        }

        static RectTransform CardFace(Transform parent, string name, string frame, string title, float titleWidth,
            Sprite art, string kind, Color kindColor, string text, float scale, out RectTransform face)
        {
            var root = Rect(parent, name, 0, 0, CardWidth * scale, CardHeight * scale);
            face = Rect(root, "Face", 0, 0, CardWidth, CardHeight);
            face.localScale = new Vector3(scale, scale, 1f);

            Image(face, "Frame", Art(frame), 0, 0, CardWidth, CardHeight, raycast: true);

            var heading = Text(face, Initial(title, 140), TitleFont, 18f, InkText,
                22.5f, 19.5f, titleWidth, 48f, TextAlignmentOptions.Left);
            heading.lineSpacing = -18f;

            var illustration = Image(face, "Art", art, 22.5f, 74.5f, 203f, 122f);
            illustration.preserveAspect = true;

            var kindText = Text(face, Loc.Format("fmt.cardKind", kind), TitleFont, 13f, kindColor,
                22.5f, 208f, 203f, 18f, TextAlignmentOptions.Top);
            kindText.characterSpacing = 16f;

            Text(face, text, BodyFont, 14.5f, InkText, 24f, 228f, 200f, 96f, TextAlignmentOptions.Center);
            return root;
        }
    }
}
