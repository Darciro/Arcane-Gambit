using System;
using System.Collections.Generic;
using ArcaneGambit.FX;
using ArcaneGambit.Rules;
using UnityEngine;
using UnityEngine.UI;

namespace ArcaneGambit.Game
{
    /// <summary>
    /// The lasting look of a condition ("Clima — como funciona"): a few ink layers over the board, looping slowly while
    /// the condition lasts. Each layer is laid out in the doc's 880 × 854 frame and scaled onto the board; nothing in it
    /// takes a click. Mud lies on the ground under the pieces, the rest over them. At most two show at once: the newest
    /// on top, the one under it at 70%. Reduced motion keeps every layer still and drops the storm's flash.
    /// </summary>
    public sealed class BoardWeather : MonoBehaviour
    {
        const float FrameW = 880f, FrameH = 854f, FadeTime = 0.6f, UnderAlpha = 0.7f, EntranceTimeout = 4f;
        const float HeadPad = 20f, HeadW = 260f, HeadH = 210f;
        const int MaxShown = 2, BreathFrames = 8;

        sealed class Layer
        {
            public CardId Card;
            public readonly List<CanvasGroup> Groups = new List<CanvasGroup>();
            public readonly List<Action<float, float>> Animate = new List<Action<float, float>>();
            public FxJob Entrance;
            public float Waited, Alpha, Time;
            public bool Leaving;
        }

        // CSS keyframes as (percent, value) pairs.
        static readonly float[] BoltKeys = { 0, 0, 70, 0, 71, 1, 73, 1, 74, 0, 75, 0, 76, 1, 78, 1, 80, 0, 100, 0 };
        static readonly float[] FlashKeys = { 0, 0, 70, 0, 71, 0.32f, 74, 0, 76, 0.22f, 80, 0, 100, 0 };
        static readonly float[] TorchScaleKeys = { 0, 1, 20, 1.05f, 45, 0.97f, 70, 1.02f, 100, 1 };
        static readonly float[] TorchAlphaKeys = { 0, 0.85f, 20, 1, 45, 0.7f, 70, 0.95f, 100, 0.85f };

        RectTransform _ground, _sky, _outside;
        Func<IReadOnlyList<Vector3>> _torchSpots;
        readonly List<Layer> _layers = new List<Layer>();
        readonly List<Vector2> _army = new List<Vector2>();

        /// <summary>The three roots share one rect: the ground and the sky clip to it, the outside lets the wind's head
        /// lean past the board's corner.</summary>
        public static BoardWeather Create(RectTransform ground, RectTransform sky, RectTransform outside,
            Func<IReadOnlyList<Vector3>> torchSpots)
        {
            ground.gameObject.AddComponent<RectMask2D>();
            sky.gameObject.AddComponent<RectMask2D>();
            var weather = sky.gameObject.AddComponent<BoardWeather>();
            weather._ground = ground;
            weather._sky = sky;
            weather._outside = outside;
            weather._torchSpots = torchSpots;
            return weather;
        }

        /// <summary>Fades the condition in once its entrance has played.</summary>
        public void Show(CardId card, FxJob entrance = null)
        {
            var layer = _layers.Find(l => l.Card == card);
            if (layer != null)
            {
                if (!layer.Leaving) return;
                _layers.Remove(layer);
                layer.Leaving = false;
            }
            else
            {
                layer = Build(card);
                if (layer == null) return;
            }
            layer.Entrance = entrance;
            layer.Waited = 0f;
            _layers.Add(layer);
            foreach (var group in layer.Groups) group.transform.SetAsLastSibling();
        }

        public void Hide(CardId card)
        {
            foreach (var layer in _layers)
                if (layer.Card == card) layer.Leaving = true;
        }

        public void HideAll()
        {
            foreach (var layer in _layers) layer.Leaving = true;
        }

        public void Clear()
        {
            foreach (var layer in _layers) DestroyLayer(layer);
            _layers.Clear();
        }

        static void DestroyLayer(Layer layer)
        {
            foreach (var group in layer.Groups)
                if (group != null) Destroy(group.gameObject);
        }

        void Update()
        {
            float dt = UnityEngine.Time.deltaTime;
            bool still = FxSettings.ReduceFlashesAndShakes;
            int shown = 0;
            for (int i = _layers.Count - 1; i >= 0; i--)
            {
                var layer = _layers[i];
                float target = 0f;
                if (!layer.Leaving && Ready(layer, dt))
                {
                    target = shown == 0 ? 1f : shown < MaxShown ? UnderAlpha : 0f;
                    shown++;
                }
                layer.Alpha = Mathf.MoveTowards(layer.Alpha, target, dt / FadeTime);
                if (layer.Leaving && layer.Alpha <= 0f)
                {
                    DestroyLayer(layer);
                    _layers.RemoveAt(i);
                    continue;
                }
                foreach (var group in layer.Groups) group.alpha = layer.Alpha;
                if (layer.Alpha <= 0f) continue;
                if (!still) layer.Time += dt;
                foreach (var animate in layer.Animate) animate(layer.Time, dt);
            }
        }

        static bool Ready(Layer layer, float dt)
        {
            if (layer.Entrance == null || layer.Entrance.IsDone) return true;
            layer.Waited += dt;
            return layer.Waited >= EntranceTimeout;
        }

        Layer Build(CardId card)
        {
            var layer = new Layer { Card = card };
            switch (card)
            {
                case CardId.Gale: BuildGale(layer); break;
                case CardId.LightningStorm: BuildStorm(layer); break;
                case CardId.Blizzard: BuildBlizzard(layer); break;
                case CardId.Fog: BuildFog(layer); break;
                case CardId.Mud: BuildMud(layer); break;
                case CardId.MoonlessNight: BuildNight(layer); break;
                default: return null;
            }
            return layer;
        }

        // Manuscript wind maps: a puffing head in the corner, gusts and leaves crossing in two speeds.
        void BuildGale(Layer layer)
        {
            var sky = Frame(layer, _sky);
            var gusts = Raw(sky, "Gale_Gusts", 0f, 0f, FrameW, FrameH, 0.9f, wrapV: TextureWrapMode.Clamp);
            var breeze = Raw(sky, "Gale_Breeze", 0f, 0f, FrameW, FrameH, 0.72f, wrapV: TextureWrapMode.Clamp);
            var outside = Frame(layer, _outside);
            float x = -12f - HeadPad, y = -50f - HeadPad;
            Raw(outside, "Gale_HeadBack", x, y, HeadW, HeadH);
            var cheek = Raw(outside, "Gale_HeadCheek", x, y, HeadW, HeadH).rectTransform;
            Pivot(cheek, (90f + HeadPad) / HeadW, 1f - (97f + HeadPad) / HeadH);
            var breath = Raw(outside, "Gale_Breath", x, y, HeadW, HeadH);
            layer.Animate.Add((t, _) =>
            {
                Scroll(gusts, new Vector2(FrameW, FrameH), FrameW * t / 14f, 0f);
                Scroll(breeze, new Vector2(FrameW, FrameH), FrameW * t / 8f, 0f);
                float puff = 1f + 0.04f * (1f - Mathf.Cos(t / 2.4f * Mathf.PI * 2f));
                cheek.localScale = new Vector3(puff, puff, 1f);
                int frame = Mathf.FloorToInt(Frac(t / 0.6f) * BreathFrames) % BreathFrames;
                breath.uvRect = new Rect(frame % 4 * 0.25f, frame < 4 ? 0.5f : 0f, 0.25f, 0.5f);
            });
        }

        // A dark wash, rain slanting in two layers, a cloud band along the top and a bolt every 6 s.
        void BuildStorm(Layer layer)
        {
            var sky = Frame(layer, _sky);
            Wash(sky, new Color32(32, 24, 16, 46));
            var far = Rain(sky, "Storm_RainB", 0.8f);
            var near = Rain(sky, "Storm_RainA", 1f);
            var cloud = Raw(sky, "Storm_Cloud", 0f, -18f, FrameW, 140f, 1f, wrapV: TextureWrapMode.Clamp);
            var flash = Wash(sky, new Color(0.965f, 0.933f, 0.859f, 0f));
            var bolt = Raw(sky, "Storm_Bolt", 574f, 60f, 80f, 300f);
            layer.Animate.Add((t, _) =>
            {
                Scroll(far, new Vector2(30f, 45f), 0f, 405f / 3.2f * t);
                Scroll(near, new Vector2(40f, 61f), 0f, 427f / 2.2f * t);
                Scroll(cloud, new Vector2(FrameW, 140f), -FrameW * t / 30f, 0f);
                float p = Frac(t / 6f) * 100f;
                bool still = FxSettings.ReduceFlashesAndShakes;
                SetAlpha(bolt, still ? 0f : Keys(p, false, BoltKeys));
                SetAlpha(flash, still ? 0f : Keys(p, false, FlashKeys));
            });
        }

        // A cold wash, ink flakes falling in two layers with a sway, a snowdrift along the bottom edge.
        void BuildBlizzard(Layer layer)
        {
            var sky = Frame(layer, _sky);
            Wash(sky, new Color32(214, 226, 236, 61));
            var flurry = Raw(sky, "Blizzard_Flurry", 0f, 0f, FrameW, FrameH, 0.85f);
            var flakes = Raw(sky, "Blizzard_Flakes", 0f, 0f, FrameW, FrameH);
            Raw(sky, "Blizzard_Drift", 0f, FrameH - 54f, FrameW, 60f, 0.92f, wrapV: TextureWrapMode.Clamp);
            layer.Animate.Add((t, _) =>
            {
                float sway = Mathf.Lerp(-12f, 12f, Ease(Mathf.PingPong(t / 4f, 1f)));
                Scroll(flurry, new Vector2(140f, 140f), sway, 140f * t / 5.5f);
                Scroll(flakes, new Vector2(220f, 220f), sway, 220f * t / 9f);
            });
        }

        // A pale wash and three blurred bands of mist drifting in alternate directions.
        void BuildFog(Layer layer)
        {
            var sky = Frame(layer, _sky);
            Wash(sky, new Color32(246, 238, 219, 51));
            var high = Raw(sky, "Fog_Band1", 0f, 90f, FrameW, 220f, 0.9f, wrapV: TextureWrapMode.Clamp);
            var middle = Raw(sky, "Fog_Band2", 0f, 360f, FrameW, 220f, 0.75f, wrapV: TextureWrapMode.Clamp);
            var low = Raw(sky, "Fog_Band3", 0f, 610f, FrameW, 220f, 0.85f, wrapV: TextureWrapMode.Clamp);
            var band = new Vector2(FrameW, 220f);
            layer.Animate.Add((t, _) =>
            {
                Scroll(high, band, -FrameW * t / 40f, 0f);
                Scroll(middle, band, FrameW * t / 28f, 0f);
                Scroll(low, band, -FrameW * t / 34f, 0f);
            });
        }

        static readonly Vector4[] Puddles =
        {
            new Vector4(189f, 162f, 0f, 1.8f), new Vector4(691f, 263f, 0.5f, 2.2f), new Vector4(88f, 464f, 1f, 2.8f),
            new Vector4(691f, 564f, 1.5f, 3.2f), new Vector4(189f, 665f, 2f, 3.8f), new Vector4(591f, 766f, 2.5f, 4.2f),
            new Vector4(390f, 363f, 3f, 4.8f),
        };

        // On the ground: still puddles on a few squares, ripples opening and fading, mud gathered along the edges.
        void BuildMud(Layer layer)
        {
            var ground = Frame(layer, _ground);
            Raw(ground, "Mud", 0f, 0f, FrameW, FrameH, 1f, TextureWrapMode.Clamp, TextureWrapMode.Clamp);
            foreach (var puddle in Puddles)
                foreach (float lead in new[] { puddle.z, puddle.w })
                {
                    var ripple = Raw(ground, "Mud_Ripple", puddle.x - 28f, puddle.y - 18f, 56f, 36f);
                    Pivot(ripple.rectTransform, 0.5f, 0.5f);
                    layer.Animate.Add((t, _) =>
                    {
                        float e = Ui.CubicBezier(0f, 0f, 0.58f, 1f, Frac((t + lead) / 3.5f));
                        float s = Mathf.Lerp(0.4f, 1.5f, e);
                        ripple.rectTransform.localScale = new Vector3(s, s, 1f);
                        SetAlpha(ripple, Mathf.Lerp(0.7f, 0f, e));
                    });
                }
        }

        static readonly Vector4[] Stars =
        {
            new Vector4(363, 20, 1.03f, 0.7f), new Vector4(44, 7, 0.6f, 0.6f), new Vector4(851, 25, 0.79f, 1.6f),
            new Vector4(294, 6, 1.02f, 2.5f), new Vector4(79, 7, 1.08f, 0.4f), new Vector4(834, 5, 0.79f, 1.9f),
            new Vector4(178, 12, 0.72f, 0.2f), new Vector4(706, 17, 0.62f, 2.9f), new Vector4(569, 14, 1.02f, 2.5f),
            new Vector4(474, 11, 0.83f, 3.2f), new Vector4(311, 29, 1.02f, 0.2f), new Vector4(207, 4, 0.98f, 2.6f),
            new Vector4(856, 15, 0.7f, 0.4f), new Vector4(724, 22, 0.63f, 1.7f), new Vector4(543, 8, 0.69f, 2.6f),
            new Vector4(540, 13, 0.92f, 1.1f),
        };

        static readonly float[] StarCycles =
            { 3.5f, 4f, 4.4f, 3.2f, 4.3f, 4.8f, 4.3f, 4.5f, 4.2f, 4.4f, 3.3f, 4.1f, 3.7f, 3.1f, 4.7f, 3f };

        // An indigo night, closing in at the edges; torchlight flickering by your pieces; stars along the top edge.
        void BuildNight(Layer layer)
        {
            var sky = Frame(layer, _sky);
            Raw(sky, "Night_Wash", 0f, 0f, FrameW, FrameH, 1f, TextureWrapMode.Clamp, TextureWrapMode.Clamp);
            var torches = new[] { Torch(sky), Torch(sky) };
            var spots = new Vector2[2];
            bool placed = false;
            layer.Animate.Add((t, dt) =>
            {
                int count = TorchTargets(sky, spots);
                for (int i = 0; i < torches.Length; i++)
                {
                    var torch = torches[i];
                    if (i >= count)
                    {
                        SetAlpha(torch, 0f);
                        continue;
                    }
                    var rt = torch.rectTransform;
                    rt.anchoredPosition = placed
                        ? Vector2.Lerp(rt.anchoredPosition, spots[i], 1f - Mathf.Exp(-dt * 4f))
                        : spots[i];
                    float p = Frac((t + i * 0.7f) / (i == 0 ? 1.8f : 2.3f)) * 100f;
                    float s = Keys(p, true, TorchScaleKeys);
                    rt.localScale = new Vector3(s, s, 1f);
                    SetAlpha(torch, Keys(p, true, TorchAlphaKeys));
                }
                placed = count > 0;
            });
            for (int i = 0; i < Stars.Length; i++)
            {
                var spec = Stars[i];
                float cycle = StarCycles[i];
                var star = Raw(sky, "Night_Star", spec.x - 7f, spec.y - 7f, 14f, 14f);
                Pivot(star.rectTransform, 0.5f, 0.5f);
                star.rectTransform.localScale = new Vector3(spec.z, spec.z, 1f);
                layer.Animate.Add((t, _) =>
                {
                    float p = Frac((t + spec.w) / cycle);
                    SetAlpha(star, Mathf.Lerp(0.25f, 1f, Ease(p < 0.5f ? p * 2f : 2f - p * 2f)));
                });
            }
        }

        RawImage Torch(RectTransform sky)
        {
            var torch = Raw(sky, "Night_Torch", -150f, -150f, 300f, 300f);
            Pivot(torch.rectTransform, 0.5f, 0.5f);
            SetAlpha(torch, 0f);
            return torch;
        }

        // Two lights for the player's army, one over each half of it as seen on screen, in the frame's local space.
        int TorchTargets(RectTransform frame, Vector2[] spots)
        {
            var world = _torchSpots?.Invoke();
            if (world == null || world.Count == 0) return 0;
            var local = _army;
            local.Clear();
            foreach (var point in world) local.Add(frame.InverseTransformPoint(point));
            local.Sort((a, b) => a.x.CompareTo(b.x));
            int half = (local.Count + 1) / 2;
            spots[0] = Centroid(local, 0, half);
            if (local.Count == 1) return 1;
            spots[1] = Centroid(local, half, local.Count);
            return 2;
        }

        static Vector2 Centroid(List<Vector2> points, int from, int to)
        {
            var sum = Vector2.zero;
            for (int i = from; i < to; i++) sum += points[i];
            return sum / (to - from);
        }

        RectTransform Frame(Layer layer, RectTransform parent)
        {
            var rt = Ui.Rect(parent, layer.Card.ToString(), 0f, 0f, FrameW, FrameH);
            var size = parent.rect.size;
            rt.localScale = new Vector3(size.x / FrameW, size.y / FrameH, 1f);
            var group = rt.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            group.interactable = group.blocksRaycasts = false;
            layer.Groups.Add(group);
            return rt;
        }

        // Rotated 14° like the doc's rain, and just large enough to cover the frame at that angle.
        static RawImage Rain(RectTransform parent, string name, float alpha)
        {
            const float w = 1080f, h = 1060f;
            var rain = Raw(parent, name, (FrameW - w) * 0.5f, (FrameH - h) * 0.5f, w, h, alpha);
            Pivot(rain.rectTransform, 0.5f, 0.5f);
            rain.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -14f);
            return rain;
        }

        static Image Wash(RectTransform parent, Color color)
        {
            var wash = Ui.Image(parent, "Wash", Ui.Solid, 0f, 0f, FrameW, FrameH);
            wash.color = color;
            return wash;
        }

        static RawImage Raw(RectTransform parent, string name, float x, float y, float w, float h, float alpha = 1f,
            TextureWrapMode wrapU = TextureWrapMode.Repeat, TextureWrapMode wrapV = TextureWrapMode.Repeat)
        {
            var raw = Ui.Rect(parent, name, x, y, w, h).gameObject.AddComponent<RawImage>();
            var texture = Resources.Load<Texture2D>("Weather/Weather_" + name);
            if (texture != null)
            {
                texture.wrapModeU = wrapU;
                texture.wrapModeV = wrapV;
            }
            raw.texture = texture;
            raw.raycastTarget = false;
            raw.color = new Color(1f, 1f, 1f, alpha);
            return raw;
        }

        /// <summary>Content moves right by dx and down by dy, in the doc's pixels; the texture repeats every tile.</summary>
        static void Scroll(RawImage raw, Vector2 tile, float dx, float dy)
        {
            var size = raw.rectTransform.sizeDelta;
            raw.uvRect = new Rect(Frac(-dx / tile.x), Frac(dy / tile.y), size.x / tile.x, size.y / tile.y);
        }

        /// <summary>Moves the pivot without moving the rect, so scale and rotation turn around that point.</summary>
        static void Pivot(RectTransform rt, float x, float y)
        {
            var size = rt.sizeDelta;
            var corner = rt.anchoredPosition - new Vector2(rt.pivot.x * size.x, (rt.pivot.y - 1f) * size.y);
            rt.pivot = new Vector2(x, y);
            rt.anchoredPosition = corner + new Vector2(x * size.x, (y - 1f) * size.y);
        }

        static void SetAlpha(Graphic graphic, float alpha)
        {
            var color = graphic.color;
            color.a = alpha;
            graphic.color = color;
        }

        static float Frac(float value) => value - Mathf.Floor(value);

        static float Ease(float x) => Ui.CubicBezier(0.42f, 0f, 0.58f, 1f, x);

        static float Keys(float percent, bool ease, float[] stops)
        {
            for (int i = 2; i < stops.Length; i += 2)
            {
                if (percent > stops[i]) continue;
                float from = stops[i - 2], span = stops[i] - from;
                float x = span > 0f ? (percent - from) / span : 1f;
                return Mathf.Lerp(stops[i - 1], stops[i + 1], ease ? Ease(x) : x);
            }
            return stops[stops.Length - 1];
        }
    }
}
