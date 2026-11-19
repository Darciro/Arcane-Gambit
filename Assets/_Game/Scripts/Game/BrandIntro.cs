using System;
using System.Collections.Generic;
using ArcaneGambit.FX;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace ArcaneGambit.Game
{
    /// <summary>
    /// The author's mark, every time the game opens, right after Unity's splash: the rm icon opens into the
    /// &lt;rm/&gt; logo and the signature while the menu loads behind it. It follows the brand guide, not the
    /// game's parchment look.
    /// </summary>
    public sealed class BrandIntro : MonoBehaviour
    {
        public static readonly Color Graphite = new Color32(0x0D, 0x0E, 0x10, 0xFF);
        static readonly Color Lime = new Color32(0xC8, 0xF0, 0x5A, 0xFF);
        static readonly Color Paper = new Color32(0xF2, 0xF1, 0xEC, 0xFF);
        static readonly Color Mist = new Color32(0xA3, 0xA6, 0xAE, 0xFF);

        const string MenuScene = "MainMenu";
        const float StageWidth = 1920f, StageHeight = 1080f, MaxStep = 1f / 30f;
        const float SkipFrom = 0.5f, ExitStart = 3.8f, ExitTime = 0.5f;
        const float TileX = 810f, TileY = 330f, TileSize = 300f, TileBaseline = 200.82f;
        const int TileRadius = 70;
        const float LogoTop = 396f, LogoBaseline = 523.82f, RmHeight = 154f, RmLeft = 4f, RmRight = 9f;
        const float NameBaseline = 650.18f, LabelBaseline = 687.2f, TextWidth = 1200f;
        const float LogoSize = 154f, BracketSize = 116f, NameSize = 46f, LabelSize = 20f;

        static readonly Func<float, float> EaseRise = t => Ui.CubicBezier(0.2f, 0.7f, 0.2f, 1f, t);
        static readonly Func<float, float> EaseAway = t => Ui.CubicBezier(0.4f, 0f, 0.8f, 0.4f, t);
        static readonly Func<float, float> EaseIn = t => Ui.CubicBezier(0.42f, 0f, 1f, 1f, t);

        sealed class Part
        {
            public RectTransform Rect;
            public CanvasGroup Group;
            public Vector2 Rest, From;
            public float In, InTime, FromScale = 1f;
            public float Out = float.MaxValue, OutTime = 1f, ToScale = 1f;
        }

        readonly List<Part> _parts = new List<Part>();
        CanvasScaler _scaler;
        AsyncOperation _load;
        ThreadPriority _priority;
        float _time, _exitAt = ExitStart;
        bool _reduced, _left;

        void Start()
        {
            if (Camera.main != null) Camera.main.backgroundColor = Graphite;
            _reduced = FxSettings.ReduceFlashesAndShakes;
            Build();
            Pose();

            _priority = Application.backgroundLoadingPriority;
            Application.backgroundLoadingPriority = ThreadPriority.Low;
            _load = SceneManager.LoadSceneAsync(MenuScene);
            _load.allowSceneActivation = false;
        }

        void Update()
        {
            FitStage();
            if (_left) return;
            _time += Mathf.Min(Time.unscaledDeltaTime, MaxStep);
            if (_time >= SkipFrom && _time < _exitAt && Ui.AnyPress()) _exitAt = _time;
            Pose();
            if (_time >= _exitAt + ExitTime) Leave();
        }

        void Leave()
        {
            _left = true;
            Application.backgroundLoadingPriority = _priority;
            if (_load.progress >= 0.9f) _load.allowSceneActivation = true;
            else SceneTransition.Finish(_load);
        }

        void FitStage()
        {
            float aspect = (float)Screen.width / Mathf.Max(1, Screen.height);
            _scaler.matchWidthOrHeight = aspect >= StageWidth / StageHeight ? 1f : 0f;
        }

        void Build()
        {
            var canvasGo = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler));
            canvasGo.transform.SetParent(transform, false);
            canvasGo.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            _scaler = canvasGo.GetComponent<CanvasScaler>();
            _scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            _scaler.referenceResolution = new Vector2(StageWidth, StageHeight);
            _scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            FitStage();

            var stage = Ui.Rect(canvasGo.transform, "Stage", 0, 0, StageWidth, StageHeight);
            stage.anchorMin = stage.anchorMax = stage.pivot = new Vector2(0.5f, 0.5f);
            stage.anchoredPosition = Vector2.zero;

            var display = LoadFont("BricolageGrotesque-ExtraBold");
            var mono = LoadFont("JetBrainsMono-Medium");

            var tile = Box(stage, "Icon", TileX, TileY, TileSize, TileSize);
            Ui.Stretch(Ui.Sliced(tile, "Face", Ui.Pill(Lime, Lime, TileRadius, 0f), 0, 0, 0, 0).rectTransform);
            Word(tile, "rm", display, LogoSize, -5f, Graphite, TileSize * 0.5f, TileBaseline);
            var icon = Add(tile, 0.4f, 0.6f, Vector2.zero, 0.9f);
            icon.Out = 1.5f;
            icon.OutTime = 0.6f;
            icon.ToScale = 1.12f;

            float center = StageWidth * 0.5f;
            var open = Word(stage, "<", mono, BracketSize, -10f, Lime, center, LogoBaseline);
            var rm = Word(stage, "rm", display, LogoSize, -3.5f, Lime, center, LogoBaseline);
            var close = Word(stage, "/>", mono, BracketSize, -10f, Lime, center, LogoBaseline);
            float wOpen = Width(open), wRm = Width(rm), wClose = Width(close);
            float left = center - (wOpen + RmLeft + wRm + RmRight + wClose) * 0.5f;
            float rmLeft = left + wOpen + RmLeft;
            Place(open, left + wOpen * 0.5f, LogoBaseline);
            Place(close, rmLeft + wRm + RmRight + wClose * 0.5f, LogoBaseline);

            // The logo's "rm" grows from its own middle, not from its baseline.
            var logo = Box(stage, "Logo", rmLeft, LogoTop, wRm, RmHeight);
            rm.rectTransform.SetParent(logo, false);
            Place(rm, wRm * 0.5f, LogoBaseline - LogoTop);
            Add(logo, 1.5f, 0.6f, Vector2.zero, 1.06f);
            Add(open.rectTransform, 1.6f, 0.6f, new Vector2(80f, 0f));
            Add(close.rectTransform, 1.6f, 0.6f, new Vector2(-80f, 0f));

            var name = Word(stage, Loc.Get("ui.brand.name"), display, NameSize, -3f, Paper, center, NameBaseline);
            Add(name.rectTransform, 2.1f, 0.5f, new Vector2(0f, -10f));
            var label = Word(stage, Loc.Get("ui.brand.presents"), mono, LabelSize, 4f, Mist, center, LabelBaseline);
            Add(label.rectTransform, 2.3f, 0.5f, new Vector2(0f, -8f));
        }

        static TMP_FontAsset LoadFont(string file)
        {
            var font = TMP_FontAsset.CreateFontAsset(Resources.Load<Font>("Fonts/" + file));
            font.name = "Brand_" + file;
            return font;
        }

        static RectTransform Box(Transform parent, string name, float x, float y, float w, float h) =>
            Ui.Centered(Ui.Rect(parent, name, x, y, w, h));

        // TMP's Baseline alignment puts the baseline on the rect's middle: a zero-height rect pins it to a line.
        static TextMeshProUGUI Word(Transform parent, string text, TMP_FontAsset font, float size, float spacing,
            Color color, float centerX, float baseline)
        {
            var tmp = Ui.Text(parent, text, font, size, color, 0f, 0f, TextWidth, 0f, TextAlignmentOptions.Baseline);
            tmp.characterSpacing = spacing;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            Place(tmp, centerX, baseline);
            return tmp;
        }

        static float Width(TMP_Text text) => text.GetPreferredValues(text.text).x;

        static void Place(TMP_Text text, float centerX, float baseline) =>
            text.rectTransform.anchoredPosition = new Vector2(centerX - TextWidth * 0.5f, -baseline);

        Part Add(RectTransform rt, float start, float duration, Vector2 from, float fromScale = 1f)
        {
            var part = new Part
            {
                Rect = rt,
                Group = rt.gameObject.AddComponent<CanvasGroup>(),
                Rest = rt.anchoredPosition,
                From = from,
                In = start,
                InTime = duration,
                FromScale = fromScale,
            };
            part.Group.blocksRaycasts = false;
            _parts.Add(part);
            return part;
        }

        // A skip freezes every entrance where it stands and starts the common fade-out from there.
        void Pose()
        {
            float now = Mathf.Min(_time, _exitAt);
            float fade = 1f - EaseIn(Mathf.Clamp01((_time - _exitAt) / ExitTime));
            foreach (var p in _parts)
            {
                float enter = EaseRise(Mathf.Clamp01((now - p.In) / p.InTime));
                float leave = EaseAway(Mathf.Clamp01((now - p.Out) / p.OutTime));
                p.Group.alpha = enter * (1f - leave) * fade;
                if (_reduced) continue;
                p.Rect.anchoredPosition = p.Rest + p.From * (1f - enter);
                float scale = Mathf.LerpUnclamped(Mathf.LerpUnclamped(p.FromScale, 1f, enter), p.ToScale, leave);
                p.Rect.localScale = new Vector3(scale, scale, 1f);
            }
        }
    }
}
