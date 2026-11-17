using System;
using System.Collections.Generic;
using ArcaneGambit.FX;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace ArcaneGambit.Game
{
    /// <summary>
    /// The menu assembles itself once, when the game opens: each block enters on its own beat, with short moves, in
    /// under two seconds. A click or a key jumps to the end. Coming back from a match, the parchment sheet already
    /// reveals the finished menu.
    /// </summary>
    public sealed partial class MainMenu
    {
        const float IntroRise = 16f, IntroMusicFade = 1.5f, ReducedIntroTime = 0.4f, IntroMaxStep = 1f / 30f;
        const int TitleSoftness = 120;

        static readonly Func<float, float> EaseOut = t => Bezier(0f, 0f, 0.58f, 1f, t);
        static readonly Func<float, float> EaseRise = t => Bezier(0.2f, 0.7f, 0.3f, 1f, t);
        static readonly Func<float, float> EaseWrite = t => Bezier(0.4f, 0f, 0.2f, 1f, t);

        sealed class IntroStep
        {
            public float Start, Duration;
            public Func<float, float> Ease;
            public CanvasGroup Group;
            public bool Fades;
            public Action<float> Apply;
        }

        readonly List<IntroStep> _intro = new List<IntroStep>();
        float _introTime, _introEnd;
        bool _introReduced;

        void AddIntro(RectTransform rt, float start, float duration, Func<float, float> ease,
            Action<float> apply = null, bool fades = true)
        {
            _intro.Add(new IntroStep
            {
                Start = start,
                Duration = duration,
                Ease = ease,
                Group = rt.gameObject.AddComponent<CanvasGroup>(),
                Fades = fades,
                Apply = apply,
            });
        }

        void AddIntroRise(RectTransform rt, float start, float duration)
        {
            float rest = rt.anchoredPosition.y;
            AddIntro(rt, start, duration, EaseRise,
                t => rt.anchoredPosition = new Vector2(rt.anchoredPosition.x, rest - IntroRise * (1f - t)));
        }

        // Revealed left to right behind a soft edge, as if being written; the mask's margins hold that edge.
        void AddIntroTitle(RectTransform stage, TMPro.TextMeshProUGUI title, float center, float y, float height)
        {
            const float pad = 40f;
            float width = title.GetPreferredValues(title.text).x + TitleSoftness * 2f;
            float left = center - width * 0.5f;
            var rt = Ui.Rect(stage, "Title", left, y - pad, width, height + pad * 2f);
            var mask = rt.gameObject.AddComponent<RectMask2D>();
            mask.softness = new Vector2Int(TitleSoftness, 0);
            var text = title.rectTransform;
            text.SetParent(rt, false);
            text.anchoredPosition = new Vector2(text.anchoredPosition.x - left, -pad);
            AddIntro(rt, 0.35f, 0.8f, EaseWrite, t => mask.padding = new Vector4(0f, 0f, (1f - t) * width, 0f), fades: false);
        }

        void BeginIntro()
        {
            _introReduced = FxSettings.ReduceFlashesAndShakes;
            _introTime = 0f;
            _introEnd = 0f;
            foreach (var s in _intro)
            {
                _introEnd = Mathf.Max(_introEnd, s.Start + s.Duration);
                s.Group.blocksRaycasts = false;
                if (_introReduced) s.Apply?.Invoke(1f);
            }
            if (_introReduced) _introEnd = ReducedIntroTime;
            PoseIntro();
        }

        /// <summary>True while the intro runs, the frame it ends included, so that input only skips it.</summary>
        bool UpdateIntro()
        {
            if (_intro.Count == 0) return false;
            _introTime += Mathf.Min(Time.unscaledDeltaTime, IntroMaxStep);
            if (IntroSkipped()) _introTime = _introEnd;
            PoseIntro();
            if (_introTime >= _introEnd)
            {
                foreach (var s in _intro) s.Group.blocksRaycasts = true;
                _intro.Clear();
            }
            return true;
        }

        void PoseIntro()
        {
            foreach (var s in _intro)
            {
                if (_introReduced)
                {
                    s.Group.alpha = Mathf.Clamp01(_introTime / ReducedIntroTime);
                    continue;
                }
                float t = s.Ease(Mathf.Clamp01((_introTime - s.Start) / s.Duration));
                s.Group.alpha = s.Fades ? t : 1f;
                s.Apply?.Invoke(t);
            }
        }

        static bool IntroSkipped()
        {
            var keys = Keyboard.current;
            var mouse = Mouse.current;
            var touch = Touchscreen.current;
            return keys != null && keys.anyKey.wasPressedThisFrame
                   || mouse != null && (mouse.leftButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame)
                   || touch != null && touch.primaryTouch.press.wasPressedThisFrame;
        }

        // CSS cubic-bezier timing, so the curves match the motion spec.
        static float Bezier(float x1, float y1, float x2, float y2, float x)
        {
            if (x <= 0f || x >= 1f) return Mathf.Clamp01(x);
            float lo = 0f, hi = 1f, t = x;
            for (int i = 0; i < 20; i++)
            {
                t = (lo + hi) * 0.5f;
                if (Cubic(x1, x2, t) < x) lo = t;
                else hi = t;
            }
            return Cubic(y1, y2, t);
        }

        static float Cubic(float a, float b, float t)
        {
            float u = 1f - t;
            return 3f * a * t * u * u + 3f * b * t * t * u + t * t * t;
        }
    }
}
