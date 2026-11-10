using System;
using ArcaneGambit.FX;
using ArcaneGambit.Rules;
using UnityEngine;

namespace ArcaneGambit.Game
{
    /// <summary>
    /// FX_Sandbox only: fires any cue on a chosen square of a live match, and (in the Editor) spawns any _AG variant
    /// there to tune its size against the real board.
    /// </summary>
    sealed class FxDebugPanel : MonoBehaviour
    {
        const string VariantFolder = "Assets/_Game/FX/ETFX_AG";
        const int WindowId = 0x46581;
        static readonly string[] Files = { "a", "b", "c", "d", "e", "f", "g", "h" };
        static readonly string[] Ranks = { "1", "2", "3", "4", "5", "6", "7", "8" };
        static readonly (string label, FxCueId cue)[] CardCues =
        {
            ("Hover", FxCueId.CardHover), ("Invalid", FxCueId.CardInvalid), ("Draw", FxCueId.CardDraw),
            ("Play", FxCueId.CardPlay), ("Arcane", FxCueId.ArcaneCast), ("Reaction", FxCueId.ReactionReveal),
            ("Equip", FxCueId.Equip),
        };
        static readonly (string label, FxCueId cue)[] Rehearsals =
        {
            ("Storm arrives", FxCueId.StormArrival), ("Mark", FxCueId.StormMark), ("Bolt", FxCueId.StormStrike),
            ("Weather ends", FxCueId.ConditionRemoved), ("Fireball · hit", FxCueId.FireballHit),
            ("Fireball · blocked", FxCueId.FireballBlocked), ("Volley", FxCueId.CrossbowVolley),
            ("Muster", FxCueId.MatchStart), ("Blizzard", FxCueId.Blizzard), ("Gale", FxCueId.Gale),
            ("Fog", FxCueId.Fog), ("Mud", FxCueId.Mud), ("Moonless night", FxCueId.MoonlessNight),
        };

        GameManager _game;
        readonly FxCueId[] _cues = (FxCueId[])Enum.GetValues(typeof(FxCueId));
        Rect _window = new Rect(16f, 16f, 380f, 640f);
        Vector2 _cueScroll, _variantScroll;
        int _file = 3, _rank = 3;
        float _intensity = 1f, _variantScale = 1f;
        bool _open = true;
        GameObject[] _variants = Array.Empty<GameObject>();

        public void Init(GameManager game)
        {
            _game = game;
            LoadVariants();
        }

        void LoadVariants()
        {
#if UNITY_EDITOR
            var guids = UnityEditor.AssetDatabase.FindAssets("t:Prefab", new[] { VariantFolder });
            _variants = Array.ConvertAll(guids, guid =>
                UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(UnityEditor.AssetDatabase.GUIDToAssetPath(guid)));
#endif
        }

        void OnGUI()
        {
            if (_game == null) return;
            float scale = Mathf.Max(1f, Screen.height / 1080f);
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            if (!_open)
            {
                if (GUI.Button(new Rect(16f, 16f, 110f, 28f), "FX Sandbox")) _open = true;
                return;
            }
            _window = GUILayout.Window(WindowId, _window, Draw, "FX Sandbox");
        }

        void Draw(int id)
        {
            var director = _game.FxDirector;
            var square = new Coord(_file, _rank);
            if (GUILayout.Button("Hide")) _open = false;

            GUILayout.Label($"Square {Files[_file]}{Ranks[_rank]}");
            _file = GUILayout.SelectionGrid(_file, Files, 8);
            _rank = GUILayout.SelectionGrid(_rank, Ranks, 8);
            GUILayout.Label($"Intensity {_intensity:0.00}");
            _intensity = GUILayout.HorizontalSlider(_intensity, 0f, 1.5f);
            bool reduce = GUILayout.Toggle(FxSettings.ReduceFlashesAndShakes, " Reduce flashes and shakes");
            if (reduce != FxSettings.ReduceFlashesAndShakes) FxSettings.ReduceFlashesAndShakes = reduce;

            if (director == null)
            {
                GUILayout.Label("FX_Root is missing: no effects.");
                GUI.DragWindow();
                return;
            }
            GUILayout.Label($"Locked: {(director.Locked ? "yes" : "no")} · playing {director.Playing} · queued {director.Queued}");

            // Card cues need a card: these replay them with sample cards (Equip lands on the chosen square's unit).
            GUILayout.Label("Cards");
            for (int i = 0; i < CardCues.Length; i += 4)
            {
                GUILayout.BeginHorizontal();
                for (int j = i; j < Mathf.Min(i + 4, CardCues.Length); j++)
                    if (GUILayout.Button(CardCues[j].label)) _game.DebugCardFx(CardCues[j].cue, square);
                GUILayout.EndHorizontal();
            }

            // Projectiles fly from the chosen square to the square two files and two ranks further. Weather sets its
            // lasting look; "Weather ends" lifts every look at once.
            GUILayout.Label("Rehearsals");
            for (int i = 0; i < Rehearsals.Length; i += 3)
            {
                GUILayout.BeginHorizontal();
                for (int j = i; j < Mathf.Min(i + 3, Rehearsals.Length); j++)
                    if (GUILayout.Button(Rehearsals[j].label)) _game.DebugBoardFx(Rehearsals[j].cue, square);
                GUILayout.EndHorizontal();
            }

            GUILayout.Label("Cues");
            _cueScroll = GUILayout.BeginScrollView(_cueScroll, GUILayout.Height(250f));
            foreach (var cue in _cues)
            {
                string label = director.HasPlayer(cue) ? cue.ToString() : cue + "  (no player yet)";
                if (GUILayout.Button(label)) _game.Fx.Play(cue, square, PieceAt(square), _intensity);
            }
            GUILayout.EndScrollView();

            GUILayout.Label($"_AG variants · scale {_variantScale:0.00} × board");
            _variantScale = GUILayout.HorizontalSlider(_variantScale, 0.1f, 3f);
            if (GUILayout.Button("Reload variants")) LoadVariants();
            _variantScroll = GUILayout.BeginScrollView(_variantScroll, GUILayout.Height(150f));
            if (_variants.Length == 0) GUILayout.Label("None yet in " + VariantFolder);
            foreach (var variant in _variants)
                if (variant != null && GUILayout.Button(variant.name))
                    director.Spawn(variant, _game.SquareWorld(square), _variantScale * _game.BoardScale);
            GUILayout.EndScrollView();
            GUI.DragWindow();
        }

        GameObject PieceAt(Coord square)
        {
            var unit = _game.CurrentMatch.At(square);
            return unit != null ? _game.PieceObject(unit.Id) : null;
        }
    }
}
