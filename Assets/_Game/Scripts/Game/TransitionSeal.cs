using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace ArcaneGambit.Game
{
    /// <summary>
    /// The logo seal as pen strokes in its 260-unit art box, so every stroke can be inked up to a fraction of its
    /// length. Paper fills always show: they hide the strokes behind them, as the cards and crown cover the lances.
    /// </summary>
    // Graphic no longer requires a CanvasRenderer, and without one the mesh is silently never drawn.
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class TransitionSeal : MaskableGraphic
    {
        const float Box = 260f, Feather = 1f, SharpTurn = 25f;
        const float InkWidth = 1.9f, ThinWidth = 1f, RingWidth = 4f, RingRadius = 128f;
        static readonly Color Ink = Ui.Ink, Paper = Ui.Parchment, Ring = Ui.Rubric, Gem = Ui.RubricLight;
        static readonly Color Track = new Color(Ui.Ink.r, Ui.Ink.g, Ui.Ink.b, 0.14f);
        static readonly Vector2 Center = new Vector2(130f, 130f);
        static readonly (Vector2 at, float r)[] Gems =
        {
            (new Vector2(130f, 96f), 8f), (new Vector2(108f, 204f), 3.2f),
            (new Vector2(130f, 204f), 4f), (new Vector2(152f, 204f), 3.2f),
        };

        sealed class Stroke
        {
            public Vector2[] Points;
            public float[] Distance;
            public float Width;
            public Vector2? Fill;

            public Stroke(List<Vector2> points, float width, bool closed, Vector2? fill = null)
            {
                if (closed) points.Add(points[0]);
                Points = points.ToArray();
                Distance = new float[Points.Length];
                for (int i = 1; i < Points.Length; i++)
                    Distance[i] = Distance[i - 1] + Vector2.Distance(Points[i - 1], Points[i]);
                Width = width;
                Fill = fill;
            }

            public float Length => Distance[Distance.Length - 1];
        }

        static readonly Stroke RingPath = new Stroke(Circle(RingRadius), RingWidth, closed: false);
        static readonly Stroke[] Strokes = BuildStrokes();

        float _draw, _ring, _gems;
        Rect _rect;

        public void Set(float draw, float ring, float gems)
        {
            if (Mathf.Approximately(draw, _draw) && Mathf.Approximately(ring, _ring) && Mathf.Approximately(gems, _gems))
                return;
            _draw = draw;
            _ring = ring;
            _gems = gems;
            SetVerticesDirty();
        }

        static Stroke[] BuildStrokes()
        {
            var card = new Vector2(130f, 196f);
            return new[]
            {
                new Stroke(Circle(113f), InkWidth, closed: false),
                new Stroke(new List<Vector2> { new Vector2(58f, 206f), new Vector2(196f, 52f) }, InkWidth, false),
                new Stroke(new List<Vector2> { new Vector2(202f, 206f), new Vector2(64f, 52f) }, InkWidth, false),
                Filled(Rotate(RoundRect(96f, 78f, 164f, 178f, 4f), -22f, card)),
                Filled(Rotate(RoundRect(96f, 78f, 164f, 178f, 4f), 22f, card)),
                Filled(RoundRect(96f, 72f, 164f, 172f, 4f)),
                new Stroke(Points(106, 80, 154, 80, 154, 164, 106, 164), ThinWidth, closed: true),
                new Stroke(Points(90, 196, 84, 146, 108, 166, 130, 124, 152, 166, 176, 146, 170, 196), InkWidth, true,
                    new Vector2(130f, 192f)),
                new Stroke(Points(88, 196, 172, 196, 172, 212, 88, 212), InkWidth, true, new Vector2(130f, 204f)),
            };
        }

        static Stroke Filled(List<Vector2> points)
        {
            var sum = Vector2.zero;
            foreach (var p in points) sum += p;
            return new Stroke(points, InkWidth, closed: true, sum / points.Count);
        }

        static List<Vector2> Points(params float[] xy)
        {
            var list = new List<Vector2>(xy.Length / 2);
            for (int i = 0; i < xy.Length; i += 2) list.Add(new Vector2(xy[i], xy[i + 1]));
            return list;
        }

        /// <summary>Starts at the top and runs clockwise on screen, as the SVG arcs do.</summary>
        static List<Vector2> Circle(float r)
        {
            const int segments = 128;
            var list = new List<Vector2>(segments + 1);
            for (int i = 0; i <= segments; i++)
            {
                float a = i * Mathf.PI * 2f / segments;
                list.Add(Center + new Vector2(Mathf.Sin(a), -Mathf.Cos(a)) * r);
            }
            return list;
        }

        /// <summary>Starts after the top-left corner and runs clockwise, with quadratic corners.</summary>
        static List<Vector2> RoundRect(float x0, float y0, float x1, float y1, float r)
        {
            var list = new List<Vector2>();
            void Corner(Vector2 from, Vector2 control, Vector2 to)
            {
                const int steps = 6;
                for (int i = 1; i <= steps; i++)
                {
                    float t = i / (float)steps, u = 1f - t;
                    list.Add(u * u * from + 2f * u * t * control + t * t * to);
                }
            }
            list.Add(new Vector2(x0 + r, y0));
            list.Add(new Vector2(x1 - r, y0));
            Corner(new Vector2(x1 - r, y0), new Vector2(x1, y0), new Vector2(x1, y0 + r));
            list.Add(new Vector2(x1, y1 - r));
            Corner(new Vector2(x1, y1 - r), new Vector2(x1, y1), new Vector2(x1 - r, y1));
            list.Add(new Vector2(x0 + r, y1));
            Corner(new Vector2(x0 + r, y1), new Vector2(x0, y1), new Vector2(x0, y1 - r));
            list.Add(new Vector2(x0, y0 + r));
            Corner(new Vector2(x0, y0 + r), new Vector2(x0, y0), new Vector2(x0 + r, y0));
            list.RemoveAt(list.Count - 1);
            return list;
        }

        /// <summary>SVG rotate(degrees, pivot): positive turns clockwise on screen.</summary>
        static List<Vector2> Rotate(List<Vector2> points, float degrees, Vector2 pivot)
        {
            float a = degrees * Mathf.Deg2Rad, cos = Mathf.Cos(a), sin = Mathf.Sin(a);
            for (int i = 0; i < points.Count; i++)
            {
                var d = points[i] - pivot;
                points[i] = pivot + new Vector2(d.x * cos - d.y * sin, d.x * sin + d.y * cos);
            }
            return points;
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            _rect = GetPixelAdjustedRect();
            float scale = _rect.width / Box;

            Draw(vh, RingPath, 1f, Track, scale, caps: false);
            if (_ring > 0f) Draw(vh, RingPath, _ring, Ring, scale);
            foreach (var stroke in Strokes)
            {
                if (stroke.Fill is Vector2 center) Fan(vh, center, stroke.Points, Paper);
                if (_draw > 0f) Draw(vh, stroke, _draw, Ink, scale);
            }
            if (_gems <= 0f) return;
            var gem = new Color(Gem.r, Gem.g, Gem.b, _gems);
            foreach (var (at, r) in Gems) Disc(vh, Local(at), r * scale, gem);
        }

        Vector2 Local(Vector2 p) =>
            new Vector2(_rect.xMin + p.x / Box * _rect.width, _rect.yMax - p.y / Box * _rect.height);

        void Draw(VertexHelper vh, Stroke stroke, float fraction, Color color, float scale, bool caps = true)
        {
            float length = stroke.Length * fraction, half = stroke.Width * scale * 0.5f;
            var pts = stroke.Points;
            if (caps) Disc(vh, Local(pts[0]), half, color);
            for (int i = 1; i < pts.Length; i++)
            {
                float start = stroke.Distance[i - 1], end = stroke.Distance[i];
                if (start >= length) break;
                var a = Local(pts[i - 1]);
                var b = end <= length ? Local(pts[i]) : Vector2.Lerp(a, Local(pts[i]), (length - start) / (end - start));
                Segment(vh, a, b, half, color);
                if (end >= length && caps) Disc(vh, b, half, color);
                else if (i + 1 < pts.Length && Vector2.Angle(pts[i] - pts[i - 1], pts[i + 1] - pts[i]) > SharpTurn)
                    Disc(vh, b, half, color);
            }
        }

        void Fan(VertexHelper vh, Vector2 center, Vector2[] points, Color color)
        {
            int first = vh.currentVertCount;
            Vertex(vh, Local(center), color);
            for (int i = 0; i < points.Length; i++) Vertex(vh, Local(points[i]), color);
            for (int i = 0; i < points.Length - 1; i++) vh.AddTriangle(first, first + 1 + i, first + 2 + i);
        }

        static void Segment(VertexHelper vh, Vector2 a, Vector2 b, float half, Color color)
        {
            var dir = b - a;
            if (dir.sqrMagnitude < 1e-6f) return;
            var n = new Vector2(-dir.y, dir.x).normalized;
            var clear = new Color(color.r, color.g, color.b, 0f);
            int i = vh.currentVertCount;
            Vertex(vh, a - n * (half + Feather), clear);
            Vertex(vh, a - n * half, color);
            Vertex(vh, a + n * half, color);
            Vertex(vh, a + n * (half + Feather), clear);
            Vertex(vh, b - n * (half + Feather), clear);
            Vertex(vh, b - n * half, color);
            Vertex(vh, b + n * half, color);
            Vertex(vh, b + n * (half + Feather), clear);
            for (int k = 0; k < 3; k++)
            {
                vh.AddTriangle(i + k, i + k + 1, i + k + 5);
                vh.AddTriangle(i + k, i + k + 5, i + k + 4);
            }
        }

        static void Disc(VertexHelper vh, Vector2 center, float radius, Color color)
        {
            const int sides = 16;
            var clear = new Color(color.r, color.g, color.b, 0f);
            int first = vh.currentVertCount;
            Vertex(vh, center, color);
            for (int s = 0; s < sides; s++)
            {
                float a = s * Mathf.PI * 2f / sides;
                var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                Vertex(vh, center + dir * radius, color);
                Vertex(vh, center + dir * (radius + Feather), clear);
            }
            for (int s = 0; s < sides; s++)
            {
                int inner = first + 1 + s * 2, next = first + 1 + (s + 1) % sides * 2;
                vh.AddTriangle(first, inner, next);
                vh.AddTriangle(inner, inner + 1, next + 1);
                vh.AddTriangle(inner, next + 1, next);
            }
        }

        static void Vertex(VertexHelper vh, Vector2 position, Color color)
        {
            var v = UIVertex.simpleVert;
            v.position = position;
            v.color = color;
            vh.AddVert(v);
        }
    }
}
