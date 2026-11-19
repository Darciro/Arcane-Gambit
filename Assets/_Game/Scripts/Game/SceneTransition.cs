using System;
using System.Collections;
using ArcaneGambit.FX;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace ArcaneGambit.Game
{
    /// <summary>
    /// Every scene change: a parchment sheet slides in from the right, a seal is inked while its ring shows the real
    /// load progress, then the sheet leaves to the left. Input stays blocked from start to end.
    /// </summary>
    public sealed class SceneTransition : MonoBehaviour
    {
        const int SortOrder = 1000;
        const float StageWidth = 1920f, StageHeight = 1080f;
        const float SlideTime = 0.5f, FadeTime = 0.3f, MinHold = 1f, MinSealTime = 0.8f, GemTime = 0.3f;
        const float RingCatchUp = 0.25f, Offscreen = 1.05f, MaxStep = 1f / 30f;
        const float SealX = 770f, SealY = 300f, SealSize = 380f;
        const float DotY = 724f, DotSize = 11f, DotPeriod = 1.2f, DotDelay = 0.2f, ReducedDotAlpha = 0.6f;
        const float ShadowWidth = 80f, ShadowAlpha = 0.38f, VignetteAlpha = 0.24f;
        static readonly float[] DotX = { 925f, 955f, 985f };
        static readonly Color ShadowColor = new Color(0.169f, 0.114f, 0.063f);
        static readonly Color VignetteColor = new Color(0.541f, 0.384f, 0.188f);

        static SceneTransition s_instance;

        Canvas _canvas;
        CanvasScaler _scaler;
        RectTransform _root, _sheet;
        CanvasGroup _group;
        Image _blocker;
        TransitionSeal _seal;
        Image[] _dots;
        bool _busy;

        // Domain reload is disabled in this project: statics must be cleared on every Play.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => s_instance = null;

        public static bool Busy => s_instance != null && s_instance._busy;

        public static void Load(string scene)
        {
            if (Busy) return;
            Create();
            s_instance.StartCoroutine(s_instance.Run(scene, null));
        }

        /// <summary>Covers a load started elsewhere with scene activation held back, then lets the scene in.</summary>
        public static void Finish(AsyncOperation load)
        {
            if (Busy)
            {
                load.allowSceneActivation = true;
                return;
            }
            Create();
            s_instance.StartCoroutine(s_instance.Run(null, load));
        }

        // Built ahead of the first use so the canvas already has its scaled size when the sheet starts to move.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Create()
        {
            if (s_instance != null) return;
            var go = new GameObject("Scene Transition", typeof(RectTransform));
            DontDestroyOnLoad(go);
            s_instance = go.AddComponent<SceneTransition>();
            s_instance.Build();
        }

        void Build()
        {
            gameObject.layer = FxLayers.Ui;
            _canvas = gameObject.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = SortOrder;
            _scaler = gameObject.AddComponent<CanvasScaler>();
            _scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            _scaler.referenceResolution = new Vector2(StageWidth, StageHeight);
            _scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            gameObject.AddComponent<GraphicRaycaster>();
            _root = (RectTransform)transform;

            _blocker = Ui.Image(_root, "Blocker", Ui.Solid, 0, 0, 0, 0, raycast: true);
            _blocker.color = Color.clear;
            Ui.Stretch(_blocker.rectTransform);

            _sheet = Ui.Stretch(Ui.Rect(_root, "Sheet", 0, 0, 0, 0));
            _group = _sheet.gameObject.AddComponent<CanvasGroup>();
            _group.blocksRaycasts = false;
            var shadow = ShadowTexture();
            Edge(Raw(_sheet, "Shadow", shadow, new Rect(1f, 0f, -1f, 1f)).rectTransform, 0f, -ShadowWidth, ShadowWidth);
            Edge(Raw(_sheet, "Shadow", shadow, new Rect(0f, 0f, 1f, 1f)).rectTransform, 1f, 0f, ShadowWidth);
            Ui.Stretch(Ui.Image(_sheet, "Face", Ui.Solid, 0, 0, 0, 0).rectTransform).GetComponent<Image>().color = Ui.Parchment;
            Ui.Stretch(Raw(_sheet, "Vignette", VignetteTexture(), new Rect(0f, 0f, 1f, 1f)).rectTransform);
            Filet(0f, 0f, 2f, 1f);
            Filet(0f, 7f, 1f, 0.55f);
            Filet(1f, -2f, 2f, 1f);
            Filet(1f, -8f, 1f, 0.55f);

            var stage = Ui.Rect(_sheet, "Stage", 0, 0, StageWidth, StageHeight);
            stage.anchorMin = stage.anchorMax = stage.pivot = new Vector2(0.5f, 0.5f);
            stage.anchoredPosition = Vector2.zero;
            _seal = Ui.Rect(stage, "Seal", SealX, SealY, SealSize, SealSize).gameObject.AddComponent<TransitionSeal>();
            _seal.raycastTarget = false;
            _dots = new Image[DotX.Length];
            for (int i = 0; i < DotX.Length; i++)
                _dots[i] = Ui.Diamond(stage, DotX[i] + DotSize * 0.5f, DotY + DotSize * 0.5f, DotSize, Ui.Rubric);

            _sheet.gameObject.SetActive(false);
            _blocker.enabled = false;
        }

        void Filet(float side, float x, float width, float alpha)
        {
            var line = Ui.Image(_sheet, "Filet", Ui.Solid, 0, 0, 0, 0);
            line.color = new Color(Ui.Ink.r, Ui.Ink.g, Ui.Ink.b, alpha);
            Edge(line.rectTransform, side, x, width);
        }

        static void Edge(RectTransform rt, float side, float x, float width)
        {
            rt.anchorMin = new Vector2(side, 0f);
            rt.anchorMax = new Vector2(side, 1f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.offsetMin = new Vector2(x, 0f);
            rt.offsetMax = new Vector2(x + width, 0f);
        }

        static RawImage Raw(Transform parent, string name, Texture2D texture, Rect uv)
        {
            var go = Ui.Rect(parent, name, 0, 0, 0, 0).gameObject;
            var raw = go.AddComponent<RawImage>();
            raw.texture = texture;
            raw.uvRect = uv;
            raw.raycastTarget = false;
            return raw;
        }

        static Texture2D ShadowTexture()
        {
            const int size = 64;
            var tex = NewTexture(size, 1);
            for (int x = 0; x < size; x++)
            {
                float t = 1f - (x + 0.5f) / size;
                tex.SetPixel(x, 0, new Color(ShadowColor.r, ShadowColor.g, ShadowColor.b, ShadowAlpha * t * t));
            }
            tex.Apply();
            return tex;
        }

        static Texture2D VignetteTexture()
        {
            const int w = 160, h = 90;
            var tex = NewTexture(w, h);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float u = ((x + 0.5f) / w - 0.5f) / 0.7f, v = (1f - (y + 0.5f) / h - 0.46f) / 0.7f;
                    float d = Mathf.Sqrt(u * u + v * v);
                    float a = VignetteAlpha * Mathf.Clamp01((d - 0.55f) / 0.45f);
                    tex.SetPixel(x, y, new Color(VignetteColor.r, VignetteColor.g, VignetteColor.b, a));
                }
            tex.Apply();
            return tex;
        }

        static Texture2D NewTexture(int w, int h) => new Texture2D(w, h, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.DontSave,
        };

        void Update()
        {
            float aspect = (float)Screen.width / Mathf.Max(1, Screen.height);
            _scaler.matchWidthOrHeight = aspect >= StageWidth / StageHeight ? 1f : 0f;
            if (!_sheet.gameObject.activeSelf) return;

            bool still = FxSettings.ReduceFlashesAndShakes;
            for (int i = 0; i < _dots.Length; i++)
            {
                float phase = Mathf.Repeat((Time.unscaledTime - i * DotDelay) / DotPeriod, 1f);
                float pulse = phase < 0.4f ? Mathf.SmoothStep(0f, 1f, phase / 0.4f)
                    : Mathf.SmoothStep(1f, 0f, (phase - 0.4f) / 0.6f);
                var c = _dots[i].color;
                c.a = still ? ReducedDotAlpha : 0.25f + 0.75f * pulse;
                _dots[i].color = c;
            }
        }

        // Overlay canvases here draw in the order they were enabled, whatever their sortingOrder says, so the sheet
        // re-enables its canvas to land on top of the scene's own canvases.
        void Raise()
        {
            _canvas.enabled = false;
            _canvas.enabled = true;
        }

        IEnumerator Run(string scene, AsyncOperation load)
        {
            Raise();
            _busy = true;
            bool still = FxSettings.ReduceFlashesAndShakes;
            _blocker.enabled = true;
            _seal.Set(0f, 0f, 0f);
            _seal.enabled = false;
            _sheet.gameObject.SetActive(true);
            Ui.SetCursor(CursorKind.Wait);

            GameAudio.Play(Sfx.PageTurn);
            yield return Move(Offscreen, 0f, still);

            // Loading starts only once the sheet covers the screen: its first frames stall the main thread.
            float covered = Time.unscaledTime;
            var priority = Application.backgroundLoadingPriority;
            Application.backgroundLoadingPriority = ThreadPriority.Low;
            if (load == null)
            {
                load = SceneManager.LoadSceneAsync(scene);
                load.allowSceneActivation = false;
            }
            yield return Ink(load);

            load.allowSceneActivation = true;
            while (!load.isDone) yield return null;
            // The new scene builds its UI in Start; two frames let that hitch pass under the sheet, and the sheet
            // rises above each canvas the moment it appears.
            for (int i = 0; i < 2; i++)
            {
                yield return null;
                Raise();
            }
            Application.backgroundLoadingPriority = priority;
            Ui.SetCursor(CursorKind.Wait);

            GameAudio.Play(Sfx.SealStamp);
            yield return Tween(GemTime, t => _seal.Set(1f, 1f, t));
            while (Time.unscaledTime - covered < MinHold) yield return null;

            GameAudio.Play(Sfx.PageTurn);
            yield return Move(0f, -Offscreen, still);

            _sheet.gameObject.SetActive(false);
            _blocker.enabled = false;
            _busy = false;
            Ui.SetCursor(CursorKind.Default);
        }

        IEnumerator Ink(AsyncOperation load)
        {
            _seal.enabled = true;
            float drawn = 0f, ring = 0f;
            while (drawn < 1f || ring < 1f)
            {
                yield return null;
                float dt = Mathf.Min(Time.unscaledDeltaTime, MaxStep);
                drawn = Mathf.Min(1f, drawn + dt / MinSealTime);
                float target = Mathf.Clamp01(load.progress / 0.9f);
                ring = Mathf.Max(ring, Mathf.MoveTowards(ring, target, dt / RingCatchUp));
                _seal.Set(drawn, ring, 0f);
            }
        }

        IEnumerator Move(float from, float to, bool still)
        {
            if (still)
            {
                _sheet.anchoredPosition = Vector2.zero;
                float a = from == 0f ? 1f : 0f, b = to == 0f ? 1f : 0f;
                yield return Tween(FadeTime, t => _group.alpha = Mathf.Lerp(a, b, t));
                yield break;
            }
            _group.alpha = 1f;
            yield return Tween(SlideTime, t =>
                _sheet.anchoredPosition = new Vector2(Mathf.Lerp(from, to, EaseInOut(t)) * _root.rect.width, 0f));
        }

        static IEnumerator Tween(float duration, Action<float> apply)
        {
            apply(0f);
            for (float t = 0f; t < duration;)
            {
                yield return null;
                t += Mathf.Min(Time.unscaledDeltaTime, MaxStep);
                apply(Mathf.Clamp01(t / duration));
            }
        }

        static float EaseInOut(float t) => t < 0.5f ? 4f * t * t * t : 1f - Mathf.Pow(-2f * t + 2f, 3f) * 0.5f;
    }
}
