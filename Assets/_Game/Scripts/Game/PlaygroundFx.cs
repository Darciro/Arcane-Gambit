#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using ArcaneGambit.FX;
using ArcaneGambit.Rules;
using MoreMountains.Feedbacks;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace ArcaneGambit.Game
{
    /// <summary>
    /// The Effects tab: every FX cue played by the real FxDirector on a small stage built the way the match builds its
    /// own (board, pieces, a card ghost, the deck), so each player gets the same kinds of targets. Every play starts
    /// from a fresh stage. Chains the match runs across several cues (the fireball's cast, shot and hit) play one cue
    /// at a time here; FX_Sandbox still rehearses them on a live match.
    /// </summary>
    public sealed partial class Playground
    {
        const int DefaultEffect = -1, TransitionGroup = 6, VolleyBolts = 3;
        const float StageX = 555f, StageY = 120f, StageW = 1325f, StageH = 648f;
        const float Square = 88f, PieceSize = 76f, StageCardScale = 0.62f, DeckScale = 0.47f;
        const float ReplayDelay = 0.6f, RestDelay = 0.8f, VolleyStagger = 0.08f, MusterStagger = 0.12f, GhostDrop = 320f;
        const float ListW = 485f, FxRowH = 59f, ChipH = 36f;
        const int Files = 6, Ranks = 4;
        static readonly string[] Groups = { "Cards", "Pieces", "Tactics", "Arcane", "Conditions", "Match", "Transitions" };
        static readonly float[] Speeds = { 0.25f, 0.5f, 1f };
        static readonly Vector2Int AllySquare = new Vector2Int(1, 2), FoeSquare = new Vector2Int(4, 1);
        static readonly Vector2Int FortSquare = new Vector2Int(2, 1);

        sealed class FxEntry
        {
            public FxCueId Cue;
            public FxCue Asset;
            public bool Transition;
            public float? Measured;
            public int Group => Transition ? TransitionGroup : GroupOf(Cue);
            public bool Playable => Transition || Asset != null && Asset.Player != null;
            public MMF_Player Player => Asset != null ? Asset.Player : null;
        }

        sealed class StagePiece
        {
            public RectTransform Rt, Holder;
            public CanvasGroup Group;
            public Image Shield;
        }

        static int s_effect = DefaultEffect;

        readonly List<FxEntry> _effects = new List<FxEntry>();
        int? _fxGroup;
        CardId _stageCard = CardId.CrossbowVolley;
        float _speed = 1f;
        bool _fxLoop, _playing;
        int _spawning;
        float _playStart, _doneAt = -1f;
        readonly List<FxJob> _jobs = new List<FxJob>();
        Action _onDone;

        RectTransform _stage, _stageContent, _board, _boardUnder, _pieces, _boardOver, _ghost, _deck;
        Dictionary<string, GameObject> _cardNamed;
        StagePiece _ally, _foe;
        BoardWeather _weather;
        readonly List<Vector3> _torchSpots = new List<Vector3>();
        TextMeshProUGUI _counter, _counterLabel, _stageName;
        Sprite _runeCircle, _markRing;
        RectTransform _fxDetail;
        readonly List<(FxEntry entry, Image fill, Image bar)> _fxRows = new List<(FxEntry, Image, Image)>();
        Image _loopFace, _reduceFace;
        TextMeshProUGUI _loopLabel, _reduceLabel;
        readonly List<(Image face, TextMeshProUGUI label, float speed)> _speedButtons =
            new List<(Image, TextMeshProUGUI, float)>();

        FxEntry Selected => s_effect >= 0 && s_effect < _effects.Count ? _effects[s_effect] : null;
        FxDirector Director => _fx != null ? _fx.Director : null;

        static int GroupOf(FxCueId cue) => Mathf.Min((int)cue / 20, 5);

        void LoadEffects()
        {
            var assets = new Dictionary<FxCueId, FxCue>();
            foreach (var cue in Resources.LoadAll<FxCue>("FxCues")) assets[cue.Id] = cue;
            foreach (FxCueId id in Enum.GetValues(typeof(FxCueId)))
                _effects.Add(new FxEntry { Cue = id, Asset = assets.TryGetValue(id, out var asset) ? asset : null });
            _effects.Add(new FxEntry { Transition = true });
            if (Selected == null) s_effect = _effects.FindIndex(e => !e.Transition && e.Cue == FxCueId.CrossbowVolley);
            _runeCircle = Resources.Load<Sprite>("RuneCircle");
            _markRing = Resources.Load<Sprite>("MarkRing");
        }

        static string CueEvent(FxCueId cue) => cue switch
        {
            FxCueId.CrossbowVolley => Loc.CardName(CardId.CrossbowVolley),
            FxCueId.SmokeScreen => Loc.CardName(CardId.SmokeScreen),
            FxCueId.SeersOmen => Loc.CardName(CardId.SeersOmen),
            FxCueId.DawnPrayer => Loc.CardName(CardId.DawnPrayer),
            FxCueId.VigilSeal => Loc.CardName(CardId.VigilSeal),
            FxCueId.OracleEcho => Loc.CardName(CardId.OracleEcho),
            FxCueId.Blizzard => Loc.CardName(CardId.Blizzard),
            FxCueId.Gale => Loc.CardName(CardId.Gale),
            FxCueId.Fog => Loc.CardName(CardId.Fog),
            FxCueId.Mud => Loc.CardName(CardId.Mud),
            FxCueId.MoonlessNight => Loc.CardName(CardId.MoonlessNight),
            FxCueId.FireballCast => Phase(CardId.Fireball, "cast"),
            FxCueId.FireballShot => Phase(CardId.Fireball, "shot"),
            FxCueId.FireballHit => Phase(CardId.Fireball, "hit"),
            FxCueId.FireballBlocked => Phase(CardId.Fireball, "blocked"),
            FxCueId.StormArrival => Phase(CardId.LightningStorm, "arrival"),
            FxCueId.StormMark => Phase(CardId.LightningStorm, "mark"),
            FxCueId.StormStrike => Phase(CardId.LightningStorm, "strike"),
            FxCueId.StormThunder => Phase(CardId.LightningStorm, "thunder"),
            _ => Loc.Get("pg.cue." + cue),
        };

        static string Phase(CardId card, string phase) => Loc.CardName(card) + " · " + Loc.Get("pg.phase." + phase);

        static string EntryEvent(FxEntry entry) => entry.Transition ? Loc.Get("pg.cue.Transition") : CueEvent(entry.Cue);

        static string PlayerName(FxEntry entry) =>
            entry.Transition ? nameof(SceneTransition) : entry.Player != null ? entry.Player.name : entry.Cue.ToString();

        static string PlayerExt(FxEntry entry) => entry.Transition ? ".cs" : ".prefab";

        static string PlayerFolder(FxEntry entry) =>
            FileOf(entry.Player != null ? entry.Player.gameObject : null, "", "", "Assets/_Game/FX/Players").folder;

        static CardId? WeatherCard(FxCueId cue) => cue switch
        {
            FxCueId.StormArrival => CardId.LightningStorm,
            FxCueId.Blizzard => CardId.Blizzard,
            FxCueId.Gale => CardId.Gale,
            FxCueId.Fog => CardId.Fog,
            FxCueId.Mud => CardId.Mud,
            FxCueId.MoonlessNight => CardId.MoonlessNight,
            _ => null,
        };

        void BuildEffects()
        {
            FxRoot.SetTimeScale(_speed);
            _fxRows.Clear();
            _speedButtons.Clear();

            float x = SideX, y = 118f;
            Chip(ref x, ref y, Loc.Get("pg.fx.all"), _effects.Count, _fxGroup == null, () => FilterEffects(null));
            for (int g = 0; g < Groups.Length; g++)
            {
                int count = _effects.FindAll(e => e.Group == g).Count;
                if (count == 0) continue;
                int group = g;
                Chip(ref x, ref y, Loc.Get("pg.fx." + Groups[g]), count, _fxGroup == g, () => FilterEffects(group));
            }
            y += ChipH + 14f;
            Ui.Line(_page, SideX, y, ListW, 1.2f, 0.7f);
            y += 6f;

            var scroll = Ui.Scroll(_page, SideX, y, ListW, H - y);
            var content = scroll.content;
            float rowW = ListW - 18f, rowY = 0f;
            for (int g = 0; g < Groups.Length; g++)
            {
                if (_fxGroup != null && _fxGroup != g) continue;
                bool header = false;
                for (int i = 0; i < _effects.Count; i++)
                {
                    var entry = _effects[i];
                    if (entry.Group != g) continue;
                    if (!header)
                    {
                        var title = Text(content, Loc.Get("pg.fx." + Groups[g]), Ui.TitleFont, 21f, Ui.InkText, 0f,
                            rowY + 12f, rowW, 28f);
                        title.textWrappingMode = TextWrappingModes.NoWrap;
                        float rule = title.GetPreferredValues(title.text).x + 14f;
                        Ui.Line(content, rule, rowY + 27f, rowW - rule, 1f, 0.5f);
                        rowY += 46f;
                        header = true;
                    }
                    EffectRow(content, rowY, rowW, entry, i);
                    rowY += FxRowH;
                }
            }
            content.sizeDelta = new Vector2(0f, rowY + 24f);

            _stage = Ui.Rect(_worldPage, "Stage", StageX, StageY, StageW, StageH);
            Panel(_stage, StageW, StageH);
            Ui.Label(_stage, Loc.Get("pg.fx.stage"), 24f, 22f, 400f);
            _stageName = Text(_stage, "", Ui.BodyFont, 21f, Ui.InkText, 24f, 52f, 390f, 30f);
            _stageName.overflowMode = TextOverflowModes.Ellipsis;
            BuildControls();
            SelectEffect(s_effect, rebuildRows: false);
        }

        void Chip(ref float x, ref float y, string label, int count, bool active, UnityAction onClick)
        {
            string text = $"{label}  <size=72%><font=\"{Ui.BodyFont.name}\">{count}</font></size>";
            var tmp = Text(_page, text, Ui.TitleFont, 17f, active ? Ui.Cream : Ui.InkText, 0f, 0f, 300f, ChipH,
                TextAlignmentOptions.Center);
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            float w = tmp.GetPreferredValues(text).x + 28f;
            if (x + w > SideX + ListW)
            {
                x = SideX;
                y += ChipH + 8f;
            }
            var face = Ui.Sliced(_page, "Chip", active ? Ui.Pill(Ui.Ink, Ui.Ink, 18, 1f)
                : Ui.Pill(new Color(Ui.Parchment.r, Ui.Parchment.g, Ui.Parchment.b, 0.5f), Ui.InkSoft, 18, 1.2f),
                x, y, w, ChipH, raycast: true);
            var button = face.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(onClick);
            tmp.transform.SetParent(face.transform, false);
            tmp.rectTransform.anchoredPosition = Vector2.zero;
            tmp.rectTransform.sizeDelta = new Vector2(w, ChipH);
            x += w + 8f;
        }

        void FilterEffects(int? group)
        {
            _fxGroup = group;
            StopEffects();
            RebuildPage();
        }

        void EffectRow(RectTransform parent, float y, float w, FxEntry entry, int index)
        {
            var row = Ui.Rect(parent, PlayerName(entry), 0f, y, w, FxRowH);
            var fill = Ui.Image(row, "Fill", Ui.Solid, 0f, 0f, w, FxRowH, raycast: true);
            var select = fill.gameObject.AddComponent<Button>();
            select.transition = Selectable.Transition.None;
            select.onClick.AddListener(() => SelectEffect(index));
            var bar = Ui.Image(row, "Selected", Ui.Solid, 0f, 0f, 3f, FxRowH);
            bar.color = Ui.Rubric;
            _fxRows.Add((entry, fill, bar));
            PoseFxRow(entry, fill, bar);

            PlayButton(row, 8f, 9.5f, 40f, entry.Playable ? () =>
            {
                SelectEffect(index);
                PlayEffect();
            } : null);
            Text(row, FileLabel(PlayerName(entry), PlayerExt(entry)), Ui.BodyFont, 17f,
                entry.Playable ? Ui.InkText : Faded, 60f, 6f, w - 190f, 24f).overflowMode = TextOverflowModes.Ellipsis;
            Text(row, EntryEvent(entry), Ui.BodyFont, 14f, Ui.InkBody, 60f, 31f, w - 190f, 22f).overflowMode =
                TextOverflowModes.Ellipsis;
            if (entry.Asset != null && entry.Playable) TierPill(row, entry.Asset.Tier, w - 6f, 17.5f);
            else if (!entry.Playable) Tag(row, Loc.Get("pg.fx.noPlayer"), w - 110f, FxRowH * 0.5f, true);
            Ui.Dashed(row, new Vector2(0f, -FxRowH + 0.5f), new Vector2(w, -FxRowH + 0.5f),
                new Color(Ui.Ink.r, Ui.Ink.g, Ui.Ink.b, 0.3f), 1f, 2f, 3f);
        }

        void PoseFxRow(FxEntry entry, Image fill, Image bar)
        {
            bool on = entry == Selected;
            fill.color = on ? new Color(Ui.Cream.r, Ui.Cream.g, Ui.Cream.b, 0.75f) : new Color(1f, 1f, 1f, 0f);
            bar.enabled = on;
        }

        void SelectEffect(int index, bool rebuildRows = true)
        {
            if (index < 0 || index >= _effects.Count) return;
            StopEffects();
            s_effect = index;
            if (rebuildRows)
                foreach (var (entry, fill, bar) in _fxRows) PoseFxRow(entry, fill, bar);
            var selected = Selected;
            if (_stageName != null) _stageName.text = FileLabel(PlayerName(selected), PlayerExt(selected));
            BuildStage(selected.Transition ? (FxCueId?)null : selected.Cue);
            BuildFxDetail();
        }

        void BuildControls()
        {
            const float h = 46f, pad = 12f, playW = 120f, loopW = 112f, segW = 58f, reduceW = 184f;
            var speedLabel = Ui.Label(_page, Loc.Get("pg.fx.speed"), 0f, 0f, 200f, h, TextAlignmentOptions.MidlineLeft);
            speedLabel.textWrappingMode = TextWrappingModes.NoWrap;
            float labelW = speedLabel.GetPreferredValues(speedLabel.text).x;
            float w = pad + playW + 10f + loopW + 29f + labelW + 10f + segW * Speeds.Length + 29f + reduceW + pad;
            float bx = StageX + (StageW - w) * 0.5f, by = StageY + StageH - 86f;
            var bar = Ui.Rect(_page, "Controls", bx, by, w, h + 20f);
            Ui.Sliced(bar, "Face", Ui.Boxed(Ui.Parchment, Ui.InkSoft, 1, 3, 1), 0f, 0f, w, h + 20f, raycast: true);

            float x = pad, y = 10f;
            var play = Ui.Sliced(bar, "Play", Ui.Boxed(Ui.Rubric, Ui.RubricDark, 1, 0, 0), x, y, playW, h, raycast: true);
            Ui.MakeButton(play, PlayEffect);
            Ui.Image(play.transform, "Glyph", _playGlyph, 22f, 16f, 14f, 14f).color = Ui.Cream;
            Text(play.transform, Loc.Get("pg.fx.play"), Ui.TitleFont, 20f, Ui.Cream, 44f, 0f, playW - 50f, h,
                TextAlignmentOptions.MidlineLeft).characterSpacing = 4f;
            x += playW + 10f;

            _loopFace = Ui.Sliced(bar, "Repeat", _box, x, y, loopW, h, raycast: true);
            Ui.MakeButton(_loopFace, () =>
            {
                _fxLoop = !_fxLoop;
                PoseControls();
            });
            _loopLabel = Text(_loopFace.transform, Loc.Get("pg.fx.repeat"), Ui.TitleFont, 19f, Ui.InkText, 0f, 0f, loopW, h,
                TextAlignmentOptions.Center);
            x += loopW + 14f;
            Ui.Line(bar, x, y + 6f, 1f, h - 12f, 0.5f);
            x += 15f;

            speedLabel.transform.SetParent(bar, false);
            speedLabel.rectTransform.anchoredPosition = new Vector2(x, -y);
            x += labelW + 10f;
            Ui.Sliced(bar, "Speeds", Ui.Boxed(new Color(1f, 1f, 1f, 0f), Ui.InkSoft, 1, 0, 0), x - 1f, y - 1f,
                segW * Speeds.Length + 2f, h + 2f);
            foreach (float speed in Speeds)
            {
                var face = Ui.Image(bar, "Speed", Ui.Solid, x, y, segW, h, raycast: true);
                var button = face.gameObject.AddComponent<Button>();
                button.transition = Selectable.Transition.None;
                float s = speed;
                button.onClick.AddListener(() =>
                {
                    _speed = s;
                    FxRoot.SetTimeScale(s);
                    PoseControls();
                });
                string value = speed.ToString("0.##", CultureInfo.InvariantCulture);
                if (Loc.Language == Loc.DefaultLanguage) value = value.Replace('.', ',');
                var label = Text(face.transform, value + "×", Ui.BodyFont, 16f, Ui.InkText, 0f, 0f, segW, h,
                    TextAlignmentOptions.Center);
                _speedButtons.Add((face, label, speed));
                x += segW;
            }
            x += 14f;
            Ui.Line(bar, x, y + 6f, 1f, h - 12f, 0.5f);
            x += 15f;

            _reduceFace = Ui.Sliced(bar, "Reduce", _box, x, y, reduceW, h, raycast: true);
            Ui.MakeButton(_reduceFace, () =>
            {
                FxSettings.ReduceFlashesAndShakes = !FxSettings.ReduceFlashesAndShakes;
                PoseControls();
            });
            _reduceLabel = Text(_reduceFace.transform, Loc.Get("pg.fx.reduce"), Ui.TitleFont, 18f, Ui.InkText, 0f, 0f,
                reduceW, h, TextAlignmentOptions.Center);
            PoseControls();
        }

        void PoseControls()
        {
            _loopFace.sprite = _fxLoop ? _boxActive : _box;
            _loopLabel.color = _fxLoop ? Ui.Cream : Ui.InkText;
            bool reduce = FxSettings.ReduceFlashesAndShakes;
            _reduceFace.sprite = reduce ? _boxActive : _box;
            _reduceLabel.color = reduce ? Ui.Cream : Ui.InkText;
            foreach (var (face, label, speed) in _speedButtons)
            {
                bool on = Mathf.Approximately(speed, _speed);
                face.color = on ? Ui.Ink : new Color(Ui.Parchment.r, Ui.Parchment.g, Ui.Parchment.b, 0.4f);
                label.color = on ? Ui.Cream : Ui.InkText;
            }
        }

        void BuildFxDetail()
        {
            if (_fxDetail != null) Destroy(_fxDetail.gameObject);
            float top = StageY + StageH + 20f, h = H - top - 30f;
            _fxDetail = Ui.Rect(_page, "Detail", StageX, top, StageW, h);
            Panel(_fxDetail, StageW, h);
            var entry = Selected;
            if (entry == null) return;

            const float colA = 24f, colB = 452f, colC = 924f, wA = 400f, wB = 444f, wC = 380f;
            Ui.Label(_fxDetail, Loc.Get("pg.fx.effect"), colA, 20f, wA);
            Text(_fxDetail, FileLabel(PlayerName(entry), PlayerExt(entry)), Ui.BodyFont, 23f, Ui.InkText, colA, 46f, wA, 32f)
                .overflowMode = TextOverflowModes.Ellipsis;
            Text(_fxDetail, EntryEvent(entry), Ui.BodyFont, 18f, Ui.InkText, colA, 82f, wA, 26f);
            float x = colA;
            if (entry.Asset != null && entry.Playable)
            {
                var pill = TierPill(_fxDetail, entry.Asset.Tier, 0f, 116f);
                pill.anchoredPosition = new Vector2(colA, pill.anchoredPosition.y);
                x += pill.sizeDelta.x + 12f;
            }
            string measured = entry.Measured is float seconds
                ? Loc.Format("pg.fx.duration", seconds)
                : entry.Transition ? "" : Loc.Get("pg.fx.notMeasured");
            Text(_fxDetail, measured, entry.Measured != null ? Ui.BodyFont : Ui.ItalicFont, 17f,
                entry.Measured != null ? Ui.InkText : Faded, x, 116f, wA, 24f);
            string path = entry.Transition ? Loc.Get("pg.fx.transitionNote") : PlayerFolder(entry) + PlayerName(entry) + ".prefab";
            Text(_fxDetail, path, Ui.ItalicFont, 14.5f, Faded, colA, 152f, wA, 60f);

            Ui.Label(_fxDetail, Loc.Get("pg.fx.feedbacks"), colB, 20f, wB);
            Ui.Label(_fxDetail, Loc.Get("pg.fx.particles"), colB, 140f, wB);
            Ui.Label(_fxDetail, Loc.Get("pg.fx.sounds"), colC, 20f, wC);
            var player = entry.Player;
            if (player == null)
            {
                string none = Loc.Get(entry.Transition ? "pg.fx.none" : "pg.fx.noPlayer");
                Text(_fxDetail, none, Ui.ItalicFont, 16f, Faded, colB, 46f, wB, 24f);
                return;
            }

            var feedbacks = new List<string>();
            var particles = new List<string>();
            foreach (var feedback in player.FeedbacksList)
            {
                if (feedback == null || feedback is MMF_ReferenceHolder) continue;
                string label = feedback.Active ? feedback.Label : $"<color={FadedHex}>{feedback.Label}</color>";
                feedbacks.Add(label);
                if (feedback is MMF_ParticlesInstantiation spawn)
                {
                    var names = new List<string>();
                    if (spawn.ParticlesPrefab != null) names.Add(spawn.ParticlesPrefab.name);
                    if (spawn.RandomParticlePrefabs != null)
                        foreach (var p in spawn.RandomParticlePrefabs)
                            if (p != null) names.Add(p.name);
                    particles.Add(names.Count > 0 ? string.Join(", ", names)
                        : $"{spawn.Label} <color={FadedHex}>· {Loc.Get("pg.fx.unassigned")}</color>");
                }
            }
            var list = Text(_fxDetail, string.Join("  ·  ", feedbacks), Ui.BodyFont, 16f, Ui.InkText, colB, 44f, wB, 90f);
            list.enableAutoSizing = true;
            list.fontSizeMin = 12f;
            list.fontSizeMax = 16f;
            var parts = Text(_fxDetail, particles.Count > 0 ? string.Join("\n", particles) : Loc.Get("pg.fx.none"),
                Ui.BodyFont, 15f, Ui.InkText, colB, 164f, wB, h - 176f);
            parts.enableAutoSizing = true;
            parts.fontSizeMin = 11f;
            parts.fontSizeMax = 15f;

            float y = 48f;
            foreach (var slot in player.GetFeedbacksOfType<MMF_MMSoundManagerSound>())
            {
                var clips = new List<string>();
                if (slot.Sfx != null) clips.Add(slot.Sfx.name);
                if (slot.RandomSfx != null)
                    foreach (var c in slot.RandomSfx)
                        if (c != null) clips.Add(c.name);
                Tag(_fxDetail, "sfx", colC, y + 12f, false);
                string text = clips.Count > 0 ? string.Join(", ", clips)
                    : $"{slot.Label} <color={FadedHex}>· {Loc.Get("pg.sounds.unassigned")}</color>";
                Text(_fxDetail, text, Ui.BodyFont, 16f, Ui.InkText, colC + 48f, y, wC - 48f, 48f);
                y += 50f;
            }
        }

        void UpdateEffects()
        {
            if (_playing)
            {
                if (_spawning > 0) return;
                foreach (var job in _jobs)
                    if (!job.IsDone) return;
                _playing = false;
                var entry = Selected;
                if (entry != null && _jobs.Count > 0)
                {
                    entry.Measured = Time.time - _playStart;
                    if (s_tab == Tab.Effects) BuildFxDetail();
                }
                _onDone?.Invoke();
                _onDone = null;
                _doneAt = Time.unscaledTime;
                return;
            }
            if (_doneAt < 0f || Time.unscaledTime - _doneAt < (_fxLoop ? ReplayDelay : RestDelay)) return;
            _doneAt = -1f;
            if (s_tab != Tab.Effects || Selected == null || Selected.Transition) return;
            if (_fxLoop) PlayEffect();
            else BuildStage(Selected.Cue);
        }

        void StopEffects()
        {
            StopAllCoroutines();
            _spawning = 0;
            _playing = false;
            _doneAt = -1f;
            _jobs.Clear();
            _onDone = null;
            if (Director != null) Director.StopAll();
            if (_fx != null && _fx.Ambience != null) _fx.Ambience.ClearAll();
        }

        void LeaveEffects()
        {
            StopEffects();
            FxRoot.SetTimeScale(1f);
        }

        void PlayEffect()
        {
            var entry = Selected;
            if (entry == null || !entry.Playable) return;
            if (entry.Transition)
            {
                Back(SceneName);
                return;
            }
            if (Director == null) return;
            StopEffects();
            BuildStage(entry.Cue);
            _playStart = Time.time;
            _playing = true;
            PlayCue(Director, entry.Cue);
        }

        void BuildStage(FxCueId? cue)
        {
            if (_stage == null) return;
            if (_stageContent != null) Destroy(_stageContent.gameObject);
            _stageContent = Ui.Rect(_stage, "Content", 0f, 0f, StageW, StageH);

            float bw = Files * Square, bh = Ranks * Square, bx = (StageW - bw) * 0.5f, by = 92f;
            Ui.Sliced(_stageContent, "Frame", Ui.Boxed(Ui.Parchment, Ui.InkSoft, 1, 5, 1), bx - 9f, by - 9f, bw + 18f, bh + 18f);
            _board = Ui.Rect(_stageContent, "Board", bx, by, bw, bh);
            var light = new Color(0.93f, 0.88f, 0.77f);
            var dark = new Color(0.84f, 0.76f, 0.61f);
            for (int r = 0; r < Ranks; r++)
                for (int f = 0; f < Files; f++)
                    Ui.Image(_board, "Square", Ui.Solid, f * Square, r * Square, Square, Square).color =
                        (f + r) % 2 == 0 ? light : dark;
            var weatherGround = Ui.Rect(_board, "Weather Ground", 0f, 0f, bw, bh);
            _boardUnder = Ui.Rect(_board, "Under", 0f, 0f, bw, bh);
            _pieces = Ui.Rect(_board, "Pieces", 0f, 0f, bw, bh);
            _weather = BoardWeather.Create(weatherGround, Ui.Rect(_board, "Weather", 0f, 0f, bw, bh),
                Ui.Rect(_board, "Weather Outside", 0f, 0f, bw, bh), AllySpot);
            _boardOver = Ui.Rect(_board, "Over", 0f, 0f, bw, bh);
            _ally = Piece(UnitType.Crossbowmen, Side.White, AllySquare);
            _foe = Piece(UnitType.Cavalry, Side.Black, FoeSquare);

            BuildStageCard(cue == FxCueId.ArcaneCast || CardCatalog.Get(_stageCard).Kind == CardKind.Arcane);

            float dw = Ui.CardWidth * DeckScale, dh = Ui.CardHeight * DeckScale, dx = StageW - 80f - dw, dy = 300f;
            for (int i = 2; i >= 0; i--)
                Ui.Image(_stageContent, "Back", Ui.Art("CardBack"), dx + i * 4f, dy - i * 4f, dw, dh);
            _deck = Ui.Rect(_stageContent, "Deck", dx, dy, dw, dh);
            _counterLabel = Ui.Label(_stageContent, Loc.Get("pg.stage.deck"), dx - 20f, dy + dh + 14f, dw + 40f, 20f,
                TextAlignmentOptions.Top);
            _counter = Text(_stageContent, "12", Ui.BodyFont, 26f, Ui.InkText, dx - 20f, dy + dh + 36f, dw + 40f, 34f,
                TextAlignmentOptions.Top);
        }

        void BuildStageCard(bool arcane)
        {
            var info = CardCatalog.Get(_stageCard);
            float w = Ui.CardWidth * StageCardScale, h = Ui.CardHeight * StageCardScale;
            _ghost = Ui.Centered(Ui.Rect(_stageContent, "Card", 80f, 290f, w, h));
            _ghost.gameObject.AddComponent<CanvasGroup>();
            var holder = Ui.Centered(Ui.Rect(_ghost, "Holder", 0f, 0f, w, h));
            _cardNamed = new Dictionary<string, GameObject> { [FxLabels.Holder] = holder.gameObject };
            if (arcane && _runeCircle != null)
            {
                float d = w * 1.8f;
                var circle = Ui.Image(holder, "Circle", _runeCircle, (w - d) * 0.5f, (h - d) * 0.5f, d, d);
                Ui.Centered(circle.rectTransform);
                circle.color = Ui.Rubric;
                circle.type = Image.Type.Filled;
                circle.fillMethod = Image.FillMethod.Radial360;
                circle.fillAmount = 0f;
                _cardNamed[FxLabels.Circle] = circle.gameObject;
            }
            var outline = Ui.Image(holder, "Outline", Ui.Highlight, -6f, -6f, w + 12f, h + 12f);
            outline.type = Image.Type.Sliced;
            outline.color = new Color(Ui.Rubric.r, Ui.Rubric.g, Ui.Rubric.b, 0f);
            _cardNamed[FxLabels.Outline] = outline.gameObject;

            var card = Ui.Card(holder, info, StageCardScale);
            var seal = Find<Image>(card, "Seal");
            if (seal != null)
            {
                Ui.Centered(seal.rectTransform);
                _cardNamed[FxLabels.Seal] = seal.gameObject;
            }
            var frame = Find<Image>(card, "Frame");
            if (frame != null) _cardNamed[FxLabels.Frame] = frame.gameObject;
            var back = Ui.Image(holder, "Back", Ui.Art("CardBack"), 0f, 0f, w, h);
            back.gameObject.SetActive(false);
            _cardNamed[FxLabels.Back] = back.gameObject;
        }

        StagePiece Piece(UnitType type, Side side, Vector2Int square)
        {
            var rt = OnSquare(_pieces, $"{side} {type}", square, PieceSize);
            var holder = Ui.Centered(Ui.Rect(rt, "Holder", 0f, 0f, PieceSize, PieceSize));
            Ui.Image(holder, "Token", Ui.UnitArt(type, side), 0f, 0f, PieceSize, PieceSize);
            var shield = Ui.Image(holder, "Shield", Ui.Art("Badge_Shield"), -4f, PieceSize - 28f, 30f, 30f);
            shield.enabled = false;
            return new StagePiece { Rt = rt, Holder = holder, Group = rt.gameObject.AddComponent<CanvasGroup>(), Shield = shield };
        }

        static Vector2 SquareCenter(Vector2Int square) => new Vector2((square.x + 0.5f) * Square, -(square.y + 0.5f) * Square);

        static RectTransform OnSquare(RectTransform layer, string name, Vector2Int square, float size)
        {
            var rt = Ui.Rect(layer, name, 0f, 0f, size, size);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = SquareCenter(square);
            return rt;
        }

        Image BoardImage(Sprite sprite, Vector2Int square, float size, string name)
        {
            if (sprite == null) return null;
            var image = OnSquare(_boardUnder, name, square, size).gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.color = Ui.Rubric;
            image.raycastTarget = false;
            return image;
        }

        static Vector3 World(RectTransform rt) => rt.TransformPoint(rt.rect.center);

        Transform Anchor(Vector3 world)
        {
            var anchor = Ui.Rect(_stageContent, "Anchor", 0f, 0f, 0f, 0f);
            anchor.position = world;
            return anchor;
        }

        void Count(string label, int from, Dictionary<string, GameObject> named)
        {
            _counterLabel.text = Loc.Get(label);
            _counter.text = from.ToString();
            named[FxLabels.Counter] = _counter.gameObject;
        }

        void Run(FxDirector director, FxCueId cue, Vector3 position, FxContext context)
        {
            context.Board = _board.gameObject;
            _jobs.Add(director.Play(cue, position, 1f, context));
        }

        void PlayCue(FxDirector d, FxCueId cue)
        {
            var card = new Dictionary<string, GameObject>(_cardNamed);
            var named = new Dictionary<string, GameObject>();
            Vector3 cardAt = World(_ghost), allyAt = World(_ally.Rt), foeAt = World(_foe.Rt), boardAt = World(_board);
            var cardGo = _ghost.gameObject;
            switch (cue)
            {
                case FxCueId.CardHover:
                    Run(d, cue, cardAt, new FxContext { Target = cardGo, Named = card });
                    break;
                case FxCueId.CardInvalid:
                    Run(d, cue, cardAt, new FxContext { Target = cardGo, Named = card, Text = Loc.Get("fx.invalid.morale") });
                    break;
                case FxCueId.CardPlay:
                case FxCueId.ArcaneCast:
                    Count("pg.stage.morale", 5, card);
                    Run(d, cue, cardAt, new FxContext
                    {
                        Target = cardGo, Named = card, CountFrom = 5, CountTo = 3, CountFormat = "0",
                    });
                    break;
                case FxCueId.CardDraw:
                    card[FxLabels.Back].SetActive(true);
                    Count("pg.stage.deck", 12, card);
                    Run(d, cue, cardAt, new FxContext
                    {
                        Target = cardGo, Named = card, From = Anchor(World(_deck)), To = Anchor(cardAt),
                        CountFrom = 12, CountTo = 11, CountFormat = "0",
                    });
                    break;
                case FxCueId.CardDiscard:
                    Count("pg.stage.discard", 4, card);
                    Run(d, cue, cardAt, new FxContext
                    {
                        Target = cardGo, Named = card, From = Anchor(cardAt), To = Anchor(World(_deck)),
                        CountFrom = 4, CountTo = 5, CountFormat = "0",
                    });
                    break;
                case FxCueId.ReactionReveal:
                    Run(d, cue, cardAt, new FxContext
                    {
                        Target = cardGo, Named = card, Text = Loc.Get("fx.reaction"),
                        From = Anchor(cardAt + Vector3.up * GhostDrop * _ghost.lossyScale.y), To = Anchor(cardAt),
                    });
                    break;
                case FxCueId.Equip:
                    var shield = _ally.Shield;
                    card[FxLabels.Token] = _ally.Holder.gameObject;
                    card[FxLabels.Badge] = shield.gameObject;
                    _onDone = () =>
                    {
                        if (shield != null) shield.enabled = true;
                    };
                    Run(d, cue, cardAt, new FxContext
                    {
                        Target = cardGo, Named = card, From = Anchor(cardAt), To = Anchor(allyAt),
                    });
                    break;
                case FxCueId.SeersOmen:
                case FxCueId.OracleEcho:
                case FxCueId.SupplyDraw:
                    var group = _ghost.GetComponent<CanvasGroup>();
                    group.alpha = 0f;
                    _onDone = () =>
                    {
                        if (group != null) group.alpha = 1f;
                    };
                    Run(d, cue, cardAt, new FxContext
                    {
                        Target = cardGo, Named = card, From = Anchor(World(_deck)), To = Anchor(cardAt),
                    });
                    break;
                case FxCueId.Move:
                case FxCueId.QuickMove:
                case FxCueId.VigilSeal:
                    Run(d, cue, allyAt, new FxContext { Target = _ally.Holder.gameObject });
                    break;
                case FxCueId.CavalryJump:
                    Run(d, cue, foeAt, new FxContext { Target = _foe.Holder.gameObject });
                    break;
                case FxCueId.Recruit:
                    Count("pg.stage.reserve", 3, named);
                    Run(d, cue, allyAt, new FxContext
                    {
                        Target = _ally.Holder.gameObject, Named = named, CountFrom = 3, CountTo = 2, CountFormat = "0",
                    });
                    break;
                case FxCueId.Capture:
                    Run(d, cue, foeAt, new FxContext { Target = _foe.Holder.gameObject });
                    break;
                case FxCueId.CaptureBlocked:
                case FxCueId.FireballBlocked:
                    if (cue == FxCueId.FireballBlocked)
                    {
                        var badge = OnSquare(_boardOver, "Shield", FoeSquare, 46f).gameObject.AddComponent<Image>();
                        badge.sprite = Ui.Art("Badge_Shield");
                        badge.raycastTarget = false;
                        badge.rectTransform.anchoredPosition = Vector2.Lerp(SquareCenter(AllySquare), SquareCenter(FoeSquare), 0.7f);
                        badge.rectTransform.localScale = Vector3.zero;
                        named[FxLabels.Shield] = badge.gameObject;
                    }
                    Run(d, cue, foeAt, new FxContext
                    {
                        Target = _foe.Holder.gameObject, Named = named,
                        Text = Loc.Format("fx.blocked", Loc.CardName(CardId.RaiseShields)),
                    });
                    break;
                case FxCueId.MoraleGain:
                    Count("pg.stage.morale", 3, named);
                    Run(d, cue, World(_counter.rectTransform), new FxContext
                    {
                        Target = _counter.gameObject, Named = named, CountFrom = 3, CountTo = 4, CountFormat = "0",
                        Text = "+" + Ui.Roman(1),
                    });
                    break;
                case FxCueId.CrossbowVolley:
                    StartCoroutine(VolleyRoutine(d));
                    break;
                case FxCueId.FireballShot:
                    var orb = Projectile(true);
                    Run(d, cue, allyAt, new FxContext
                    {
                        Target = orb.gameObject,
                        Named = new Dictionary<string, GameObject> { [FxLabels.Holder] = orb.GetChild(0).gameObject },
                        From = Anchor(allyAt), To = Anchor(foeAt),
                    });
                    break;
                case FxCueId.SmokeScreen:
                case FxCueId.Fortify:
                    var marker = Marker(cue == FxCueId.Fortify);
                    var holder = marker.Find("Holder");
                    named[FxLabels.Holder] = holder.gameObject;
                    var fill = marker.GetComponentInChildren<Image>();
                    if (cue == FxCueId.SmokeScreen && fill != null) named[FxLabels.Fill] = fill.gameObject;
                    if (cue == FxCueId.Fortify)
                    {
                        holder.localScale = Vector3.zero;
                        _onDone = () =>
                        {
                            if (holder != null) holder.localScale = Vector3.one;
                        };
                    }
                    Run(d, cue, World(marker), new FxContext { Target = marker.gameObject, Named = named });
                    break;
                case FxCueId.FireballCast:
                    var runes = BoardImage(_runeCircle, AllySquare, Square * 1.5f, "Runes");
                    var castRing = BoardImage(_markRing, FoeSquare, Square, "Ring");
                    if (runes != null) named[FxLabels.Circle] = runes.gameObject;
                    if (castRing != null) named[FxLabels.Ring] = castRing.gameObject;
                    Run(d, cue, allyAt, new FxContext { Target = _ally.Holder.gameObject, Named = named });
                    break;
                case FxCueId.FireballHit:
                    Run(d, cue, foeAt, new FxContext
                    {
                        Target = _foe.Holder.gameObject, Text = Loc.Get("fx.captured." + UnitType.Cavalry),
                    });
                    break;
                case FxCueId.StormMark:
                    var ring = BoardImage(_markRing, FoeSquare, Square, "Ring");
                    if (ring != null) named[FxLabels.Ring] = ring.gameObject;
                    Run(d, cue, foeAt, new FxContext
                    {
                        Target = _foe.Holder.gameObject, Named = named,
                        Text = Loc.Format("fx.storm.marked", Loc.UnitName(UnitType.Cavalry)),
                    });
                    break;
                case FxCueId.StormStrike:
                    Run(d, cue, foeAt, new FxContext
                    {
                        Target = _foe.Holder.gameObject,
                        Text = Loc.Format("fx.storm.hit", Loc.UnitName(UnitType.Cavalry)),
                    });
                    break;
                case FxCueId.MatchStart:
                    foreach (var piece in new[] { _ally, _foe }) piece.Group.alpha = 0f;
                    var ally = _ally;
                    var foe = _foe;
                    _onDone = () =>
                    {
                        if (ally.Group != null) ally.Group.alpha = 1f;
                        if (foe.Group != null) foe.Group.alpha = 1f;
                    };
                    Muster(d, _ally);
                    StartCoroutine(Later(MusterStagger, () => Muster(d, foe)));
                    break;
                case FxCueId.NewTurn:
                case FxCueId.Victory:
                case FxCueId.Defeat:
                    string text = cue == FxCueId.NewTurn ? Loc.Get("pg.stage.turn")
                        : Loc.Get(cue == FxCueId.Victory ? "ui.gameOver.victory" : "ui.gameOver.defeat");
                    var (banner, title) = Banner(text);
                    named[FxLabels.Title] = title.gameObject;
                    if (cue == FxCueId.Defeat && _fx.Ambience != null) _fx.Ambience.Set("Defeat", new FxLook(0.4f, 0f, -60f));
                    Run(d, cue, World(banner), new FxContext
                    {
                        Target = cue == FxCueId.NewTurn ? banner.gameObject : null, Named = named,
                    });
                    break;
                default:
                    Run(d, cue, boardAt, new FxContext());
                    if (WeatherCard(cue) is CardId weather && _weather != null) _weather.Show(weather, _jobs[^1]);
                    break;
            }
        }

        IReadOnlyList<Vector3> AllySpot()
        {
            _torchSpots.Clear();
            if (_ally?.Rt != null) _torchSpots.Add(_ally.Rt.TransformPoint(_ally.Rt.rect.center));
            return _torchSpots;
        }

        void Muster(FxDirector d, StagePiece piece)
        {
            if (piece.Rt == null) return;
            Run(d, FxCueId.MatchStart, World(piece.Rt), new FxContext
            {
                Target = piece.Rt.gameObject,
                Named = new Dictionary<string, GameObject> { [FxLabels.Holder] = piece.Holder.gameObject },
            });
        }

        IEnumerator Later(float delay, Action action)
        {
            _spawning++;
            yield return new WaitForSeconds(delay);
            _spawning--;
            action();
        }

        IEnumerator VolleyRoutine(FxDirector d)
        {
            _spawning++;
            for (int i = 0; i < VolleyBolts; i++)
            {
                if (i > 0) yield return new WaitForSeconds(VolleyStagger);
                var bolt = Projectile(false);
                Run(d, FxCueId.CrossbowVolley, World(_ally.Rt), new FxContext
                {
                    Target = bolt.gameObject,
                    Named = new Dictionary<string, GameObject> { [FxLabels.Holder] = bolt.GetChild(0).gameObject },
                    From = Anchor(World(_ally.Rt)), To = Anchor(World(_foe.Rt)),
                });
            }
            _spawning--;
        }

        // The stand-ins the match draws until Epic Toon FX supplies the real projectiles: a glowing orb, an ink stroke.
        RectTransform Projectile(bool orb)
        {
            var root = OnSquare(_boardOver, orb ? "Fireball" : "Bolt", AllySquare, 0f);
            var holder = Ui.Rect(root, "Holder", 0f, 0f, 0f, 0f);
            holder.pivot = new Vector2(0.5f, 0.5f);
            holder.anchoredPosition = Vector2.zero;
            Image body;
            if (orb)
            {
                const float size = 26f;
                body = Ui.Image(holder, "Core", Ui.Pill(Ui.RubricLight, Ui.Rubric, Mathf.RoundToInt(size * 0.5f)), 0f, 0f,
                    size, size);
                body.type = Image.Type.Sliced;
            }
            else
            {
                body = Ui.Image(holder, "Stroke", Ui.Solid, 0f, 0f, 22f, 3f);
                body.color = Ui.InkText;
                var delta = SquareCenter(FoeSquare) - SquareCenter(AllySquare);
                body.rectTransform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
            }
            body.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            body.rectTransform.anchoredPosition = Vector2.zero;
            return root;
        }

        RectTransform Marker(bool palisade)
        {
            var rt = OnSquare(_boardUnder, palisade ? "Palisade" : "Smoke", palisade ? FortSquare : FoeSquare, Square);
            var holder = Ui.Centered(Ui.Rect(rt, "Holder", 0f, 0f, Square, Square));
            if (palisade) Ui.Image(holder, "Palisade", Ui.Art("Palisade"), 0f, 0f, Square, Square);
            else Ui.Image(holder, "Fill", Ui.Solid, 3f, 3f, Square - 6f, Square - 6f).color = new Color(0.55f, 0.55f, 0.55f, 0.4f);
            return rt;
        }

        (RectTransform banner, TextMeshProUGUI title) Banner(string text)
        {
            const float w = 440f, h = 58f;
            var banner = Ui.Rect(_stageContent, "Banner", (StageW - w) * 0.5f, 16f, w, h);
            Ui.Sliced(banner, "Face", Ui.Boxed(Ui.Parchment, Ui.Ink, 2, 3, 1), 0f, 0f, w, h);
            var title = Text(banner, text, Ui.TitleFont, 30f, Ui.InkText, 0f, 0f, w, h, TextAlignmentOptions.Center);
            return (banner, title);
        }

        static T Find<T>(Transform root, string name) where T : Component
        {
            foreach (var component in root.GetComponentsInChildren<T>(true))
                if (component.name == name) return component;
            return null;
        }
    }
}
#endif
