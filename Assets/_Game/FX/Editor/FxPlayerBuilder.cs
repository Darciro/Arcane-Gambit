using System;
using System.IO;
using MoreMountains.Feedbacks;
using MoreMountains.FeedbacksForThirdParty;
using MoreMountains.Tools;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ArcaneGambit.FX.EditorTools
{
    /// <summary>
    /// Builds the MMF_Player prefab of each card event (FX_SPEC 6) and of the two rehearsals, Lightning Storm and
    /// Fireball (FX_SPEC 7), under FX/Players, and links it to its FxCue.
    /// Each player targets what the game names in FxContext: a reference holder per target, followed by the feedbacks
    /// that act on it. Timings are starting points meant to be tuned in the Inspector; existing prefabs are kept.
    /// </summary>
    static class FxPlayerBuilder
    {
        const string Folder = "Assets/_Game/FX/Players";
        const string Cues = "Assets/_Game/FX/Resources/FxCues";

        static readonly Color Rubric = new Color32(0x8E, 0x2F, 0x21, 0xFF);
        static readonly Color RubricLight = new Color32(0xC0, 0x57, 0x3F, 0xFF);
        // The discard's "sepia" is the palette's parchment: tinting by it yellows the card without leaving the palette.
        static readonly Color Sepia = new Color32(0xEC, 0xDF, 0xC4, 0xFF);
        static readonly Color Candle = new Color32(0xF3, 0xE8, 0xD2, 0xFF);

        // Timeout 0 keeps the cue's own. Looping players (the marked square, the conjuration) wait for the game to stop them.
        static readonly (FxCueId cue, Action<MMF_Player> build, float timeout)[] Players =
        {
            (FxCueId.CardHover, Hover, 0f),
            (FxCueId.CardInvalid, Invalid, 0f),
            (FxCueId.CardDraw, Draw, 0f),
            (FxCueId.CardDiscard, Discard, 0f),
            (FxCueId.CardPlay, PlayCard, 0f),
            (FxCueId.Equip, Equip, 0f),
            (FxCueId.ReactionReveal, Reaction, 0f),
            (FxCueId.ArcaneCast, Arcane, 0f),
            (FxCueId.StormArrival, StormArrival, 4f),
            (FxCueId.StormMark, StormMark, 600f),
            (FxCueId.StormStrike, StormStrike, 4f),
            (FxCueId.StormThunder, StormThunder, 0f),
            (FxCueId.ConditionRemoved, ConditionRemoved, 0f),
            (FxCueId.FireballCast, FireballCast, 600f),
            (FxCueId.FireballShot, FireballShot, 0f),
            (FxCueId.FireballHit, FireballHit, 0f),
            (FxCueId.FireballBlocked, FireballBlocked, 0f),
            (FxCueId.Move, Move, 0f),
            (FxCueId.CavalryJump, CavalryJump, 0f),
            (FxCueId.Recruit, Recruit, 0f),
            (FxCueId.Capture, Capture, 0f),
            (FxCueId.CaptureBlocked, CaptureBlocked, 0f),
            (FxCueId.MoraleGain, MoraleGain, 0f),
            (FxCueId.SeersOmen, SeersOmen, 0f),
            (FxCueId.OracleEcho, OracleEcho, 0f),
            (FxCueId.DawnPrayer, DawnPrayer, 0f),
            (FxCueId.VigilSeal, VigilSeal, 0f),
            (FxCueId.Blizzard, Blizzard, 0f),
            (FxCueId.Gale, Gale, 0f),
            (FxCueId.Fog, Fog, 0f),
            (FxCueId.Mud, Mud, 0f),
            (FxCueId.MoonlessNight, MoonlessNight, 0f),
            (FxCueId.CrossbowVolley, CrossbowVolley, 0f),
            (FxCueId.QuickMove, QuickMove, 0f),
            (FxCueId.SmokeScreen, SmokeScreen, 0f),
            (FxCueId.Fortify, Fortify, 0f),
            (FxCueId.MatchStart, MatchStart, 0f),
            (FxCueId.NewTurn, NewTurn, 0f),
            (FxCueId.Victory, Victory, 0f),
            (FxCueId.Defeat, Defeat, 0f),
        };

        const string VariantFolder = "Assets/_Game/FX/ETFX_AG";
        static readonly System.Text.RegularExpressions.Regex VariantSlot =
            new System.Text.RegularExpressions.Regex(@"assign (\w+_AG)");

        public static void EnsurePlayers()
        {
            Build(false);
            LinkVariants(false);
        }

        [MenuItem("Arcane Gambit/FX/Link _AG Variants to Players")]
        static void LinkVariantsMenu() => LinkVariants(true);

        /// <summary>
        /// Fills each player's particle slot ("… assign Dust_AG") with that variant once it exists, in pool mode, and
        /// switches it on. Slots already filled are left alone, so a hand-picked effect is never replaced.
        /// </summary>
        public static void LinkVariants(bool report)
        {
            if (!AssetDatabase.IsValidFolder(VariantFolder)) return;
            int linked = 0;
            var missing = new System.Collections.Generic.SortedSet<string>();
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { Folder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath<MMF_Player>(path);
                if (asset == null || !NeedsVariant(asset, missing)) continue;
                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    bool changed = false;
                    foreach (var slot in root.GetComponent<MMF_Player>().GetFeedbacksOfType<MMF_ParticlesInstantiation>())
                    {
                        var match = VariantSlot.Match(slot.Label);
                        if (slot.ParticlesPrefab != null || !match.Success) continue;
                        var variant = AssetDatabase.LoadAssetAtPath<GameObject>($"{VariantFolder}/{match.Groups[1].Value}.prefab");
                        var system = variant != null ? variant.GetComponent<ParticleSystem>() : null;
                        if (system == null) continue;
                        slot.ParticlesPrefab = system;
                        slot.Mode = MMF_ParticlesInstantiation.Modes.Pool;
                        slot.Active = true;
                        changed = true;
                        linked++;
                    }
                    if (changed) PrefabUtility.SaveAsPrefabAsset(root, path);
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }
            if (linked > 0 || report)
                Debug.Log($"FX: {linked} particle slot(s) linked to _AG variants." +
                          (missing.Count > 0 ? " Waiting for: " + string.Join(", ", missing) + "." : ""));
        }

        // True when an empty slot names a variant that exists; records the names still missing.
        static bool NeedsVariant(MMF_Player player, System.Collections.Generic.SortedSet<string> missing)
        {
            bool needed = false;
            foreach (var slot in player.GetFeedbacksOfType<MMF_ParticlesInstantiation>())
            {
                var match = VariantSlot.Match(slot.Label);
                if (slot.ParticlesPrefab != null || !match.Success) continue;
                var variant = AssetDatabase.LoadAssetAtPath<GameObject>($"{VariantFolder}/{match.Groups[1].Value}.prefab");
                if (variant != null && variant.GetComponent<ParticleSystem>() != null) needed = true;
                else missing.Add(match.Groups[1].Value);
            }
            return needed;
        }

        [MenuItem("Arcane Gambit/FX/Rebuild Players")]
        static void RebuildPlayers()
        {
            if (EditorUtility.DisplayDialog("Rebuild players",
                    $"Overwrite the {Players.Length} players in {Folder}? Inspector tuning on them is lost.", "Rebuild", "Cancel"))
                Build(true);
        }

        static void Build(bool overwrite)
        {
            foreach (var (cue, build, timeout) in Players)
            {
                string path = $"{Folder}/{cue}.prefab";
                if (overwrite || !File.Exists(path)) Save(cue, build, path);
                Link(cue, AssetDatabase.LoadAssetAtPath<MMF_Player>(path), timeout, overwrite);
            }
        }

        static void Save(FxCueId cue, Action<MMF_Player> build, string path)
        {
            var scene = EditorSceneManager.NewPreviewScene();
            try
            {
                var go = new GameObject(cue.ToString());
                SceneManager.MoveGameObjectToScene(go, scene);
                var player = go.AddComponent<MMF_Player>();
                // Optional targets (a counter, a badge) are often absent on purpose.
                player.LogMissingTargets = false;
                build(player);
                PrefabUtility.SaveAsPrefabAsset(go, path);
                Debug.Log("FX: player " + path + " built.");
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        static void Link(FxCueId id, MMF_Player player, float timeout, bool overwrite)
        {
            var cue = AssetDatabase.LoadAssetAtPath<FxCue>($"{Cues}/{id}.asset");
            if (cue == null || player == null || (cue.Player != null && !overwrite)) return;
            var so = new SerializedObject(cue);
            so.FindProperty("_player").objectReferenceValue = player;
            if (timeout > 0f) so.FindProperty("_timeout").floatValue = timeout;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // I · Hover in hand. The hand already lifts, zooms and straightens the card; the player adds the spring.
        static void Hover(MMF_Player p)
        {
            Holder(p, FxLabels.Holder);
            var spring = Add<MMF_ScaleSpring>(p, "Spring", true);
            spring.Mode = MMF_ScaleSpring.Modes.Bump;
            spring.BumpScaleMin = spring.BumpScaleMax = new Vector3(6f, 6f, 0f);
            spring.DeclaredDuration = 0.18f;
            Sound(p, "Sound · paper");
        }

        // III · Invalid play.
        static void Invalid(MMF_Player p)
        {
            Holder(p, FxLabels.Holder);
            Wobble(Add<MMF_Position>(p, "Shake X 8 px", true), x: true, amplitude: 8f, duration: 0.3f);
            Holder(p, FxLabels.Seal);
            var seal = Add<MMF_Image>(p, "Seal glows", true);
            seal.Mode = MMF_Image.Modes.OverTime;
            seal.Duration = 0.5f;
            seal.ColorOverTime = Gradient((0f, Color.white), (0.4f, RubricLight), (1f, Color.white));
            Text(p, 0f);
            Sound(p, "Sound · dull");
        }

        // IV · Draw: the card leaves the deck face down and turns over on its way to the hand.
        static void Draw(MMF_Player p)
        {
            Holder(p, FxLabels.Target);
            Fly(p, 0.5f, MMTween.MMTweenCurve.EaseOutCubic);
            Holder(p, FxLabels.Holder);
            Arc(p, 40f, 0.5f);
            var flip = Add<MMF_Rotation>(p, "Turn over (Y 180 → 0)", true);
            Turn(flip, z: false, from: 180f, to: 0f, duration: 0.5f);
            Spring(p, 0.45f);
            Holder(p, FxLabels.Back);
            var back = Add<MMF_SetActive>(p, "Hide back at 90°", true);
            back.SetStateOnPlay = true;
            back.StateOnPlay = MMF_SetActive.PossibleStates.Inactive;
            back.Timing.InitialDelay = 0.25f;
            Counter(p, 0.3f, 0.2f);
            Sound(p, "Sound · card");
        }

        // VIII · Discard: the ink fades as the card goes to the pile.
        static void Discard(MMF_Player p)
        {
            Holder(p, FxLabels.Target);
            Fly(p, 0.45f, MMTween.MMTweenCurve.EaseInOutCubic);
            Fade(p, 0.78f, 0.45f);
            Holder(p, FxLabels.Holder);
            Arc(p, 30f, 0.45f);
            Turn(Add<MMF_Rotation>(p, "Tilt 8°", true), z: true, from: 0f, to: 8f, duration: 0.45f);
            ScaleTo(p, "Shrink to the pile", 0.3f, 0.45f);
            Holder(p, FxLabels.Frame);
            var sepia = Add<MMF_Image>(p, "Sepia", true);
            sepia.Mode = MMF_Image.Modes.ToDestinationColor;
            sepia.ToDestinationColor = Sepia;
            sepia.Duration = 0.45f;
            Counter(p, 0.3f, 0.3f);
            Sound(p, "Sound · card");
        }

        // II · Play: the wax seal stamps as the Morale is paid.
        static void PlayCard(MMF_Player p)
        {
            Holder(p, FxLabels.Holder);
            var squash = Add<MMF_SquashAndStretch>(p, "Squash on landing", true);
            squash.Mode = MMF_SquashAndStretch.Modes.Absolute;
            squash.Axis = MMF_SquashAndStretch.PossibleAxis.YtoXZ;
            squash.RemapCurveZero = 1f;
            squash.RemapCurveOne = 1.07f;
            squash.AnimateCurve = Curve((0f, 0f), (0.4f, 1f), (1f, 0f));
            squash.AnimateScaleDuration = 0.2f;
            squash.DetermineScaleOnPlay = true;
            squash.Timing.ConstantIntensity = true;
            Holder(p, FxLabels.Seal);
            var stamp = Add<MMF_Scale>(p, "Stamp 1 → 1.8 → 1", true);
            Pulse(stamp, 1.8f, 0.3f, Curve((0f, 0f), (0.35f, 1f), (1f, 0f)));
            Turn(Add<MMF_Rotation>(p, "Stamp −14° → 0°", true), z: true, from: -14f, to: 0f, duration: 0.3f);
            Counter(p, 0.4f, 0.1f);
            Particles(p, "Dust · assign Dust_AG");
            Sound(p, "Sound · stamp");
        }

        // VI · Equip: the card shrinks into the unit and becomes the badge on its token.
        static void Equip(MMF_Player p)
        {
            Holder(p, FxLabels.Target);
            Fly(p, 0.6f, MMTween.MMTweenCurve.EaseInCubic);
            Fade(p, 0f, 0.6f);
            Holder(p, FxLabels.Holder);
            ScaleTo(p, "Shrink → 0.15", 0.15f, 0.6f);
            Holder(p, FxLabels.Token);
            Spring(p, 0.55f);
            Holder(p, FxLabels.Badge);
            var pop = Add<MMF_Scale>(p, "Badge pops 1.6 → 1", true);
            Pulse(pop, 1.6f, 0.25f, Curve((0f, 1f), (1f, 0f)));
            pop.Timing.InitialDelay = 0.55f;
            Particles(p, "Collect glow · assign CollectGlow_AG");
            Sound(p, "Sound · equip");
        }

        // V · The opponent's reaction drops in from above, before the result is shown.
        static void Reaction(MMF_Player p)
        {
            Holder(p, FxLabels.Target);
            Fly(p, 0.45f, MMTween.MMTweenCurve.EaseOutOverhead);
            Holder(p, FxLabels.Holder);
            var wiggle = Add<MMF_Rotation>(p, "Wiggle ±2.5°", true);
            Wiggle(wiggle, 2.5f, 0.6f);
            wiggle.Timing.InitialDelay = 0.35f;
            Holder(p, FxLabels.Outline);
            var outline = Add<MMF_ImageAlpha>(p, "Red outline pulses ×3", true);
            outline.Mode = MMF_ImageAlpha.Modes.OverTime;
            outline.Duration = 0.6f;
            outline.Curve = new MMTweenType(Curve((0f, 0f), (1f / 6f, 1f), (2f / 6f, 0.2f), (0.5f, 1f), (4f / 6f, 0.2f),
                (5f / 6f, 1f), (1f, 0f)));
            outline.Timing.InitialDelay = 0.35f;
            Text(p, 0.35f);
            Sound(p, "Sound · reaction");
        }

        // VII · Arcane cast: a rune circle draws itself behind the card and turns.
        static void Arcane(MMF_Player p)
        {
            Holder(p, FxLabels.Circle);
            var fill = Add<MMF_ImageFill>(p, "Rune circle draws", true);
            fill.Mode = MMF_ImageFill.Modes.OverTime;
            fill.Duration = 0.6f;
            fill.Curve = new MMTweenType(MMTween.MMTweenCurve.EaseOutCubic);
            fill.CurveRemapZero = 0f;
            fill.CurveRemapOne = 1f;
            // One turn every 8 s.
            Turn(Add<MMF_Rotation>(p, "Circle turns", true), z: true, from: 0f, to: -63f, duration: 1.4f, linear: true);
            Holder(p, FxLabels.Holder);
            var swell = Add<MMF_Scale>(p, "Card 0.96 → 1.07 → 1.03", true);
            swell.Mode = MMF_Scale.Modes.Absolute;
            swell.UniformScaling = true;
            swell.AnimateZ = false;
            swell.RemapCurveZero = 0f;
            swell.RemapCurveOne = 1f;
            swell.AnimateScaleTweenX = new MMTweenType(Curve((0f, 0.96f), (0.45f, 1.07f), (1f, 1.03f)));
            swell.AnimateScaleDuration = 0.8f;
            swell.DetermineScaleOnPlay = true;
            Holder(p, FxLabels.Outline);
            var outline = Add<MMF_ImageAlpha>(p, "Dashed outline pulses", true);
            outline.Mode = MMF_ImageAlpha.Modes.OverTime;
            outline.Duration = 1.2f;
            outline.Curve = new MMTweenType(Curve((0f, 0f), (0.25f, 1f), (0.5f, 0.3f), (0.75f, 1f), (1f, 0f)));
            Counter(p, 0.4f, 0.3f);
            Particles(p, "Embers · assign Embers_AG");
            Sound(p, "Sound · arcane");
        }

        // 7.1 A · The storm arrives, after the player reads its notice. The lasting look (vignette 0.2, darker board)
        // is FxAmbience's; this pulse rides on top of it.
        static void StormArrival(MMF_Player p)
        {
            Vignette(p, "Sky closes · vignette +0.35", 0.35f, 2.1f, 0.3f, Curve((0f, 0f), (0.2f, 1f), (0.8f, 0.8f), (1f, 0f)));
            Exposure(p, "Sky closes · exposure −0.5", -0.5f, 2.1f, 0.3f);
            Particles(p, "Ink clouds descend · assign InkCloud_AG", 0.4f);
            Particles(p, "Rain · assign Rain_AG", 0.9f);
            Flash(p, "Distant thunder", 0.08f, 0.3f, 1.4f);
            Particles(p, "Small bolt far away · assign LightningBolt_AG", 1.4f);
            Sound(p, "Sound · thunder", 1.4f);
        }

        // 7.1 B · A square is marked at the start of the round. It loops until the bolt falls or the storm ends.
        static void StormMark(MMF_Player p)
        {
            Particles(p, "Cloud gathers over the square · assign InkCloud_AG", 0f);
            Holder(p, FxLabels.Ring);
            var stamp = Add<MMF_Scale>(p, "Ring digs in 1.6 → 1", true);
            Pulse(stamp, 1.6f, 0.3f, Curve((0f, 1f), (1f, 0f)));
            stamp.Timing.InitialDelay = 0.3f;
            Flicker(Add<MMF_ImageAlpha>(p, "Ring flickers · loop", true), 0.3f);
            Holder(p, FxLabels.Target);
            var shiver = Add<MMF_Rotation>(p, "Unit shivers · loop", true);
            Wiggle(shiver, 3f, 0.5f);
            shiver.Timing.InitialDelay = 0.7f;
            shiver.Timing.RepeatForever = true;
            shiver.Timing.DelayBetweenRepeats = 0.6f;
            Text(p, 0.7f);
            Sound(p, "Sound · rumble", 0.7f);
        }

        // 7.1 C · The bolt. An empty square plays it at intensity 0.6.
        static void StormStrike(MMF_Player p)
        {
            Vignette(p, "Anticipation · vignette pulse", 0.2f, 0.45f, 0f, Curve((0f, 0f), (0.5f, 1f), (1f, 0f)));
            Holder(p, FxLabels.Board);
            var swell = Add<MMF_Scale>(p, "Board holds its breath 1 → 1.02", true);
            Pulse(swell, 1.02f, 0.35f, Curve((0f, 0f), (0.7f, 1f), (1f, 0f)));
            Particles(p, "Bolt · assign LightningBolt_AG", 0.25f);
            Flash(p, "Flash", 0.06f, 1f, 0.25f);
            Freeze(p, 0.08f, 0.25f);
            Bloom(p, 0.25f);
            var shake = Add<MMF_Position>(p, "Board shakes 12 px", true);
            Shake(shake, 12f, 0.35f);
            shake.Timing.InitialDelay = 0.35f;
            Sound(p, "Sound · thunderclap", 0.35f);
            Holder(p, FxLabels.Target);
            var squash = Add<MMF_SquashAndStretch>(p, "Unit squashes", true);
            Squash(squash, 1.25f, 0.25f);
            squash.Timing.InitialDelay = 0.5f;
            Particles(p, "Small blast and splatter · assign FireExplosion_AG / InkSplatter_AG", 0.5f);
            Text(p, 0.95f);
        }

        // Ambient thunder while the storm lasts: no strong flash, never blocks.
        static void StormThunder(MMF_Player p)
        {
            Flash(p, "Faint flash", 0.1f, 0.15f, 0f);
            Vignette(p, "Vignette flicker", 0.08f, 0.4f, 0f, Curve((0f, 0f), (0.3f, 1f), (1f, 0f)));
            Particles(p, "Far bolt · assign LightningBolt_AG", 0f);
            Sound(p, "Sound · distant thunder", 0f);
        }

        // A condition leaves: FxAmbience lifts its look; the particle loops stop here once they exist.
        static void ConditionRemoved(MMF_Player p)
        {
            var hold = Add<MMF_Pause>(p, "Look lifts (FxAmbience)", false);
            hold.PauseDuration = 1f;
            Sound(p, "Sound · weather clears", 0f);
        }

        // 7.2 · 1 Conjuration: runes under the allied unit, a ring on the target; it loops through the reaction window.
        static void FireballCast(MMF_Player p)
        {
            Holder(p, FxLabels.Circle);
            var grow = Add<MMF_Scale>(p, "Runes 0.4 → 1", true);
            grow.Mode = MMF_Scale.Modes.Absolute;
            grow.UniformScaling = true;
            grow.AnimateZ = false;
            grow.RemapCurveZero = 0.4f;
            grow.RemapCurveOne = 1f;
            grow.AnimateScaleTweenX = new MMTweenType(Curve((0f, 0f), (1f, 1f)));
            grow.AnimateScaleDuration = 0.3f;
            grow.DetermineScaleOnPlay = true;
            grow.Timing.InitialDelay = 0.1f;
            var spin = Add<MMF_Rotation>(p, "Runes turn · loop (8 s per turn)", true);
            Turn(spin, z: true, from: 0f, to: -360f, duration: 8f, linear: true);
            spin.Timing.RepeatForever = true;
            spin.Timing.DelayBetweenRepeats = 0f;
            Holder(p, FxLabels.Target);
            Spring(p, 0.3f);
            Particles(p, "Embers rise · assign Embers_AG", 0.3f);
            Holder(p, FxLabels.Ring);
            var ring = Add<MMF_Scale>(p, "Target ring 1.5 → 1", true);
            Pulse(ring, 1.5f, 0.3f, Curve((0f, 1f), (1f, 0f)));
            ring.Timing.InitialDelay = 0.6f;
            Flicker(Add<MMF_ImageAlpha>(p, "Target ring flickers · loop", true), 0.6f);
            Sound(p, "Sound · conjuration", 0f);
        }

        // 7.2 · 2 Shot: the fireball arcs from the allied unit to the target.
        static void FireballShot(MMF_Player p)
        {
            Holder(p, FxLabels.Target);
            Fly(p, 0.55f, MMTween.MMTweenCurve.EaseInQuadratic);
            Holder(p, FxLabels.Holder);
            Arc(p, 70f, 0.55f);
            Particles(p, "Fireball · assign Fireball_AG", 0f);
            Sound(p, "Sound · whoosh", 0f);
        }

        // 7.2 · 3a Impact, captured.
        static void FireballHit(MMF_Player p)
        {
            Particles(p, "Fire blast · assign FireExplosion_AG", 0f);
            Flash(p, "Flash", 0.05f, 1f, 0f);
            Freeze(p, 0.06f, 0f);
            Bloom(p, 0f);
            Holder(p, FxLabels.Board);
            var shake = Add<MMF_Position>(p, "Board shakes 8 px", true);
            Shake(shake, 8f, 0.3f);
            shake.Timing.InitialDelay = 0.05f;
            Sound(p, "Sound · blast", 0.05f);
            Holder(p, FxLabels.Target);
            var squash = Add<MMF_SquashAndStretch>(p, "Unit squashes", true);
            Squash(squash, 1.25f, 0.25f);
            squash.Timing.InitialDelay = 0.15f;
            Particles(p, "Smoke · assign InkCloud_AG", 0.15f);
            Text(p, 0.5f);
        }

        // 7.2 · 3b Impact, blocked: a shield of ink takes the hit. Any shield reaction against a ranged attack uses it.
        static void FireballBlocked(MMF_Player p)
        {
            Holder(p, FxLabels.Shield);
            var rise = Add<MMF_Scale>(p, "Shield rises 0 → 1", true);
            rise.Mode = MMF_Scale.Modes.Absolute;
            rise.UniformScaling = true;
            rise.AnimateZ = false;
            rise.RemapCurveZero = 0f;
            rise.RemapCurveOne = 1f;
            rise.AnimateScaleTweenX = new MMTweenType(Curve((0f, 0f), (0.6f, 1.15f), (1f, 1f)));
            rise.AnimateScaleDuration = 0.25f;
            rise.DetermineScaleOnPlay = true;
            Sound(p, "Sound · metal", 0f);
            var jolt = Add<MMF_Rotation>(p, "Shield jolts", true);
            Wiggle(jolt, 10f, 0.3f);
            jolt.Timing.InitialDelay = 0.2f;
            Particles(p, "Sparks · assign Sparks_AG", 0.2f);
            Flash(p, "Flash", 0.05f, 0.35f, 0.2f);
            var fade = Add<MMF_ImageAlpha>(p, "Shield fades", true);
            fade.Mode = MMF_ImageAlpha.Modes.ToDestination;
            fade.DestinationAlpha = 0f;
            fade.Duration = 0.4f;
            fade.Timing.InitialDelay = 0.8f;
            Holder(p, FxLabels.Target);
            var recoil = Add<MMF_Position>(p, "Target recoils 3 px", true);
            Shake(recoil, 3f, 0.3f);
            recoil.Timing.InitialDelay = 0.25f;
            Particles(p, "Smoke · assign InkCloud_AG", 0.25f);
            Text(p, 0.7f);
        }

        // ── FX_SPEC 8 · B · Pieces ──────────────────────────────────────────────────────────────────────────────

        // The board's own routine slides the piece; the holder hops over that path and lands. Mud plays it at 1.25:
        // this squash follows the intensity, so the landing gets heavier.
        static void Move(MMF_Player p)
        {
            Holder(p, FxLabels.Target);
            Arc(p, 14f, 0.22f);
            var land = Add<MMF_SquashAndStretch>(p, "Lands", true);
            Squash(land, 1.12f, 0.12f, constant: false);
            land.Timing.InitialDelay = 0.2f;
            Particles(p, "Dust on landing · assign Dust_AG", 0.22f);
            Sound(p, "Sound · step", 0.22f);
        }

        static void CavalryJump(MMF_Player p)
        {
            Holder(p, FxLabels.Target);
            Arc(p, 40f, 0.22f);
            var land = Add<MMF_SquashAndStretch>(p, "Lands", true);
            Squash(land, 1.15f, 0.14f, constant: false);
            land.Timing.InitialDelay = 0.2f;
            Particles(p, "Dust on landing · assign Dust_AG", 0.22f);
            Sound(p, "Sound · hooves", 0.22f);
        }

        static void Recruit(MMF_Player p)
        {
            Holder(p, FxLabels.Target);
            Spring(p, 0.15f);
            Counter(p, 0.3f, 0f);
            Particles(p, "Poof · assign Dust_AG", 0f);
            Sound(p, "Sound · muster", 0f);
        }

        static void Capture(MMF_Player p)
        {
            Holder(p, FxLabels.Target);
            var hit = Add<MMF_SquashAndStretch>(p, "Attacker strikes", true);
            Squash(hit, 1.15f, 0.15f);
            Freeze(p, 0.04f, 0f);
            Holder(p, FxLabels.Board);
            Shake(Add<MMF_Position>(p, "Board shakes 4 px", true), 4f, 0.25f);
            Particles(p, "Impact · assign Impact_AG", 0f);
            Particles(p, "Ink blot · assign InkSplatter_AG", 0.1f);
            Sound(p, "Sound · clash", 0f);
        }

        static void CaptureBlocked(MMF_Player p)
        {
            Holder(p, FxLabels.Target);
            Spring(p, 0f);
            Wiggle(Add<MMF_Rotation>(p, "Shield jolts", true), 6f, 0.3f);
            Shake(Add<MMF_Position>(p, "Defender shakes 3 px", true), 3f, 0.3f);
            Particles(p, "Sparks · assign Sparks_AG", 0f);
            Text(p, 0.1f);
            Sound(p, "Sound · parry", 0f);
        }

        static void MoraleGain(MMF_Player p)
        {
            Counter(p, 0.3f, 0f);
            Holder(p, FxLabels.Target);
            Spring(p, 0f);
            Text(p, 0f);
            Particles(p, "Glint · assign Glint_AG", 0f);
            Sound(p, "Sound · morale", 0f);
        }

        // ── FX_SPEC 8 · D · Arcana ──────────────────────────────────────────────────────────────────────────────

        // Seer's Omen: the three cards turn over as the choice opens.
        static void SeersOmen(MMF_Player p)
        {
            Holder(p, FxLabels.Holder);
            Turn(Add<MMF_Rotation>(p, "Turns over (Y 180 → 0)", true), z: false, from: 180f, to: 0f, duration: 0.45f);
            Holder(p, FxLabels.Target);
            Fade(p, 1f, 0.3f);
            Particles(p, "Magic glint · assign Glint_AG", 0f);
            Sound(p, "Sound · cards", 0f);
        }

        // Oracle's Echo: the cards to choose from rise out of the discard pile.
        static void OracleEcho(MMF_Player p)
        {
            Holder(p, FxLabels.Target);
            Fly(p, 0.5f, MMTween.MMTweenCurve.EaseOutCubic);
            Fade(p, 1f, 0.3f);
            Holder(p, FxLabels.Holder);
            Arc(p, 50f, 0.5f);
            Turn(Add<MMF_Rotation>(p, "Spins in", true), z: true, from: -180f, to: 0f, duration: 0.5f);
            Particles(p, "Magic trail · assign MagicTrail_AG", 0f);
            Sound(p, "Sound · echo", 0f);
        }

        static void DawnPrayer(MMF_Player p)
        {
            ColorPulse(p, "Dawn · exposure +0.4", 0.4f, 0f, 1.2f, 0f);
            Flash(p, "Soft parchment flash", 0.5f, 0.4f, 0.2f);
            Particles(p, "Light rays · assign LightRays_AG", 0.2f);
            Sound(p, "Sound · choir", 0f);
        }

        static void VigilSeal(MMF_Player p)
        {
            Holder(p, FxLabels.Target);
            var stamp = Add<MMF_Scale>(p, "Seal stamps 1.6 → 1", true);
            Pulse(stamp, 1.6f, 0.3f, Curve((0f, 1f), (1f, 0f)));
            Flash(p, "Rubric flash", 0.15f, 0.3f, 0.1f, Rubric);
            Holder(p, FxLabels.Board);
            var shake = Add<MMF_Position>(p, "Board shakes 3 px", true);
            Shake(shake, 3f, 0.25f);
            shake.Timing.InitialDelay = 0.1f;
            Particles(p, "Impact ring · assign ImpactRing_AG", 0.1f);
            Sound(p, "Sound · seal", 0.1f);
        }

        // ── FX_SPEC 8 · E · Weather. The lasting look is FxAmbience's; these are the entrances. ─────────────────

        static void Blizzard(MMF_Player p)
        {
            Vignette(p, "Vignette +0.15", 0.15f, 1.8f, 0f, Curve((0f, 0f), (0.3f, 1f), (1f, 0f)));
            ColorPulse(p, "Saturation −20", 0f, -20f, 1.8f, 0f);
            Particles(p, "Snow · assign Snow_AG", 0f);
            Sound(p, "Sound · wind and snow", 0f);
        }

        static void Gale(MMF_Player p)
        {
            Holder(p, FxLabels.Board);
            var gust = Add<MMF_Position>(p, "Gusts push the board", true);
            Wobble(gust, x: true, amplitude: 6f, duration: 1.4f);
            Particles(p, "Wind, leaves, dust · assign Wind_AG", 0f);
            Sound(p, "Sound · gale", 0f);
        }

        static void Fog(MMF_Player p)
        {
            Vignette(p, "Vignette +0.2", 0.2f, 1.6f, 0f, Curve((0f, 0f), (0.4f, 1f), (1f, 0f)));
            Particles(p, "Low fog · assign Fog_AG", 0f);
            Sound(p, "Sound · fog", 0f);
        }

        static void Mud(MMF_Player p)
        {
            Holder(p, FxLabels.Board);
            var sink = Add<MMF_Position>(p, "Board sinks 3 px", true);
            Wobble(sink, x: false, amplitude: 3f, duration: 0.5f);
            Particles(p, "Mud splashes · assign InkSplatter_AG", 0f);
            Sound(p, "Sound · mud", 0f);
        }

        static void MoonlessNight(MMF_Player p)
        {
            ColorPulse(p, "Night falls · exposure −0.4", -0.4f, 0f, 1.6f, 0f);
            Vignette(p, "Vignette +0.2", 0.2f, 1.6f, 0f, Curve((0f, 0f), (0.4f, 1f), (1f, 0f)));
            Particles(p, "Torch embers · assign Embers_AG", 0.4f);
            Sound(p, "Sound · night", 0f);
        }

        // ── FX_SPEC 8 · C · Tactics, supplies and fortifications ────────────────────────────────────────────────

        // One bolt of the volley; the game shoots three, staggered.
        static void CrossbowVolley(MMF_Player p)
        {
            Holder(p, FxLabels.Target);
            Fly(p, 0.35f, MMTween.MMTweenCurve.EaseInQuadratic);
            Holder(p, FxLabels.Holder);
            Arc(p, 18f, 0.35f);
            Particles(p, "Bolts · assign Bolts_AG", 0f);
            Sound(p, "Sound · loose", 0f);
        }

        static void QuickMove(MMF_Player p)
        {
            Holder(p, FxLabels.Target);
            Spring(p, 0f);
            Particles(p, "Trail · assign InkTrail_AG", 0f);
            Sound(p, "Sound · march", 0f);
        }

        static void SmokeScreen(MMF_Player p)
        {
            Holder(p, FxLabels.Fill);
            var cover = Add<MMF_ImageAlpha>(p, "Smoke covers the square", true);
            cover.Mode = MMF_ImageAlpha.Modes.OverTime;
            cover.Duration = 0.8f;
            cover.Curve = new MMTweenType(Curve((0f, 0f), (1f, 1f)));
            cover.CurveRemapZero = 0f;
            cover.CurveRemapOne = 0.4f;
            Particles(p, "Smoke · loop · assign InkCloud_AG", 0f);
            Sound(p, "Sound · smoke", 0f);
        }

        static void Fortify(MMF_Player p)
        {
            Holder(p, FxLabels.Holder);
            var rise = Add<MMF_Scale>(p, "Rises 0 → 1.1 → 1", true);
            rise.Mode = MMF_Scale.Modes.Absolute;
            rise.UniformScaling = true;
            rise.AnimateZ = false;
            rise.RemapCurveZero = 0f;
            rise.RemapCurveOne = 1f;
            rise.AnimateScaleTweenX = new MMTweenType(Curve((0f, 0f), (0.6f, 1.1f), (1f, 1f)));
            rise.AnimateScaleDuration = 0.35f;
            rise.DetermineScaleOnPlay = true;
            var settle = Add<MMF_SquashAndStretch>(p, "Settles", true);
            Squash(settle, 1.1f, 0.2f);
            settle.Timing.InitialDelay = 0.3f;
            Particles(p, "Dust and earth · assign Dust_AG", 0.3f);
            Sound(p, "Sound · wood or earth", 0.3f);
        }

        // ── FX_SPEC 8 · F · Match ───────────────────────────────────────────────────────────────────────────────

        // One unit of the opening muster; the game staggers the units rank by rank.
        static void MatchStart(MMF_Player p)
        {
            Holder(p, FxLabels.Holder);
            var drop = Add<MMF_Position>(p, "Drops into place", true);
            drop.Mode = MMF_Position.Modes.AlongCurve;
            drop.Space = MMF_Position.Spaces.Local;
            drop.RelativePosition = true;
            drop.AnimateX = drop.AnimateZ = false;
            drop.AnimateY = true;
            drop.AnimatePositionTweenY = new MMTweenType(Curve((0f, 1f), (0.7f, -0.08f), (1f, 0f)));
            drop.RemapCurveZero = 0f;
            drop.RemapCurveOne = 40f;
            drop.AnimatePositionDuration = 0.35f;
            drop.DeterminePositionsOnPlay = true;
            Holder(p, FxLabels.Target);
            Fade(p, 1f, 0.25f);
            Sound(p, "Sound · ranks form", 0f);
        }

        static void NewTurn(MMF_Player p)
        {
            Holder(p, FxLabels.Target);
            var slide = Add<MMF_Position>(p, "Banner slides in", true);
            slide.Mode = MMF_Position.Modes.AlongCurve;
            slide.Space = MMF_Position.Spaces.Local;
            slide.RelativePosition = true;
            slide.AnimateX = slide.AnimateZ = false;
            slide.AnimateY = true;
            slide.AnimatePositionTweenY = new MMTweenType(Curve((0f, 1f), (1f, 0f)));
            slide.RemapCurveZero = 0f;
            slide.RemapCurveOne = 24f;
            slide.AnimatePositionDuration = 0.35f;
            slide.DeterminePositionsOnPlay = true;
            Holder(p, FxLabels.Title);
            var reveal = Add<MMF_TMPTextReveal>(p, "Title is written", true);
            reveal.RevealMode = MMF_TMPTextReveal.RevealModes.Character;
            reveal.DurationMode = MMF_TMPTextReveal.DurationModes.TotalDuration;
            reveal.RevealDuration = 0.45f;
            Sound(p, "Sound · turn", 0f);
        }

        static void Victory(MMF_Player p)
        {
            Freeze(p, 0.1f, 0f);
            Flash(p, "Rubric flash", 0.2f, 0.35f, 0f, Rubric);
            Holder(p, FxLabels.Title);
            var spring = Add<MMF_ScaleSpring>(p, "Title springs", true);
            spring.Mode = MMF_ScaleSpring.Modes.Bump;
            spring.BumpScaleMin = spring.BumpScaleMax = new Vector3(10f, 10f, 0f);
            spring.DeclaredDuration = 0.6f;
            spring.Timing.InitialDelay = 0.1f;
            Particles(p, "Glints and petals · assign Petals_AG", 0.2f);
            var hold = Add<MMF_Pause>(p, "Let it ring", false);
            hold.PauseDuration = 1.6f;
            Sound(p, "Sound · fanfare", 0f);
        }

        static void Defeat(MMF_Player p)
        {
            ColorPulse(p, "Colour drains · saturation −40", 0f, -40f, 1.6f, 0f);
            Vignette(p, "Vignette +0.1", 0.1f, 1.6f, 0f, Curve((0f, 0f), (0.5f, 1f), (1f, 0f)));
            Particles(p, "Smoke · assign InkCloud_AG", 0f);
            Sound(p, "Sound · defeat", 0f);
        }

        static void Vignette(MMF_Player p, string label, float peak, float duration, float delay, AnimationCurve curve)
        {
            var vignette = Add<MMF_Vignette_URP>(p, label, false);
            vignette.RelativeIntensity = true;
            vignette.Intensity = curve;
            vignette.RemapIntensityZero = 0f;
            vignette.RemapIntensityOne = peak;
            vignette.Duration = duration;
            vignette.Timing.InitialDelay = delay;
        }

        static void Exposure(MMF_Player p, string label, float exposure, float duration, float delay) =>
            ColorPulse(p, label, exposure, 0f, duration, delay);

        static void ColorPulse(MMF_Player p, string label, float exposure, float saturation, float duration, float delay)
        {
            var color = Add<MMF_ColorAdjustments_URP>(p, label, false);
            var shape = Curve((0f, 0f), (0.2f, 1f), (0.8f, 0.8f), (1f, 0f));
            color.RelativeIntensity = true;
            color.ModifyHueShift = color.ModifyContrast = false;
            color.ModifyPostExposure = exposure != 0f;
            color.ShakePostExposure = shape;
            color.RemapPostExposureZero = 0f;
            color.RemapPostExposureOne = exposure;
            color.ModifySaturation = saturation != 0f;
            color.ShakeSaturation = shape;
            color.RemapSaturationZero = 0f;
            color.RemapSaturationOne = saturation;
            color.ShakeDuration = duration;
            color.Timing.InitialDelay = delay;
        }

        // Bloom only ever pulses, and never for more than 0.3 s.
        static void Bloom(MMF_Player p, float delay)
        {
            var bloom = Add<MMF_Bloom_URP>(p, "Bloom pulse", false);
            bloom.RelativeValues = true;
            bloom.ModifyThreshold = false;
            bloom.ShakeIntensity = Curve((0f, 0f), (0.25f, 1f), (1f, 0f));
            bloom.RemapIntensityZero = 0f;
            bloom.RemapIntensityOne = 3f;
            bloom.ShakeDuration = 0.3f;
            bloom.Timing.InitialDelay = delay;
        }

        static void Flash(MMF_Player p, string label, float duration, float alpha, float delay, Color? color = null)
        {
            var flash = Add<MMF_Flash>(p, label, false);
            flash.FlashColor = color ?? Candle;
            flash.FlashDuration = duration;
            flash.FlashAlpha = alpha;
            flash.Timing.InitialDelay = delay;
        }

        static void Freeze(MMF_Player p, float duration, float delay)
        {
            var freeze = Add<MMF_FreezeFrame>(p, "Freeze frame", false);
            freeze.FreezeFrameDuration = duration;
            freeze.Timing.InitialDelay = delay;
        }

        static void Flicker(MMF_ImageAlpha alpha, float delay)
        {
            alpha.Mode = MMF_ImageAlpha.Modes.OverTime;
            alpha.Duration = 0.8f;
            alpha.Curve = new MMTweenType(Curve((0f, 1f), (0.25f, 0.35f), (0.5f, 1f), (0.75f, 0.55f), (1f, 1f)));
            alpha.CurveRemapZero = 0f;
            alpha.CurveRemapOne = 1f;
            alpha.Timing.InitialDelay = delay;
            alpha.Timing.RepeatForever = true;
            alpha.Timing.DelayBetweenRepeats = 0f;
        }

        // A shake on both axes, without the shaker component MMF_PositionShake needs on its target.
        static void Shake(MMF_Position shake, float amplitude, float duration)
        {
            Wobble(shake, x: true, amplitude: amplitude, duration: duration);
            shake.AnimateY = true;
            shake.AnimatePositionTweenY = new MMTweenType(Curve((0f, 0f), (0.2f, -0.6f), (0.4f, 0.9f), (0.6f, -0.5f),
                (0.8f, 0.3f), (1f, 0f)));
        }

        static void Squash(MMF_SquashAndStretch squash, float peak, float duration, bool constant = true)
        {
            squash.Mode = MMF_SquashAndStretch.Modes.Absolute;
            squash.Axis = MMF_SquashAndStretch.PossibleAxis.YtoXZ;
            squash.RemapCurveZero = 1f;
            squash.RemapCurveOne = peak;
            squash.AnimateCurve = Curve((0f, 0f), (0.4f, 1f), (1f, 0f));
            squash.AnimateScaleDuration = duration;
            squash.DetermineScaleOnPlay = true;
            squash.Timing.ConstantIntensity = constant;
        }

        static void Holder(MMF_Player p, string label)
        {
            var holder = (MMF_ReferenceHolder)p.AddFeedback(typeof(MMF_ReferenceHolder));
            holder.Label = label;
        }

        static T Add<T>(MMF_Player p, string label, bool fromPreviousHolder) where T : MMF_Feedback
        {
            var feedback = (T)p.AddFeedback(typeof(T));
            feedback.Label = label;
            if (fromPreviousHolder)
                feedback.AutomatedTargetAcquisition = new MMFeedbackTargetAcquisition
                {
                    Mode = MMFeedbackTargetAcquisition.Modes.PreviousReferenceHolder,
                };
            return feedback;
        }

        static void Fly(MMF_Player p, float duration, MMTween.MMTweenCurve ease)
        {
            var fly = Add<MMF_Position>(p, FeelFxPlayer.FlyLabel, true);
            fly.Mode = MMF_Position.Modes.AtoB;
            fly.Space = MMF_Position.Spaces.World;
            fly.RelativePosition = false;
            fly.DeterminePositionsOnPlay = true;
            fly.AnimatePositionDuration = duration;
            fly.AnimatePositionTween = new MMTweenType(ease);
        }

        static void Arc(MMF_Player p, float height, float duration)
        {
            var arc = Add<MMF_Position>(p, "Arc", true);
            arc.Mode = MMF_Position.Modes.AlongCurve;
            arc.Space = MMF_Position.Spaces.Local;
            arc.RelativePosition = true;
            arc.AnimateX = false;
            arc.AnimateY = true;
            arc.AnimateZ = false;
            arc.AnimatePositionTweenY = new MMTweenType(Curve((0f, 0f), (0.5f, 1f), (1f, 0f)));
            arc.RemapCurveZero = 0f;
            arc.RemapCurveOne = height;
            arc.AnimatePositionDuration = duration;
            arc.DeterminePositionsOnPlay = true;
        }

        static void Wobble(MMF_Position wobble, bool x, float amplitude, float duration)
        {
            wobble.Mode = MMF_Position.Modes.AlongCurve;
            wobble.Space = MMF_Position.Spaces.Local;
            wobble.RelativePosition = true;
            wobble.AnimateX = x;
            wobble.AnimateY = !x;
            wobble.AnimateZ = false;
            var curve = new MMTweenType(Curve((0f, 0f), (0.15f, 1f), (0.35f, -1f), (0.55f, 0.7f), (0.75f, -0.4f), (1f, 0f)));
            if (x) wobble.AnimatePositionTweenX = curve;
            else wobble.AnimatePositionTweenY = curve;
            wobble.RemapCurveZero = 0f;
            wobble.RemapCurveOne = amplitude;
            wobble.AnimatePositionDuration = duration;
            wobble.DeterminePositionsOnPlay = true;
        }

        // Absolute angles: Feel scales a rotation's remap by intensity, which would bend a 180° flip on a softer replay.
        static void Turn(MMF_Rotation rotation, bool z, float from, float to, float duration, bool linear = false)
        {
            rotation.Timing.ConstantIntensity = true;
            rotation.Mode = MMF_Rotation.Modes.Absolute;
            rotation.RotationSpace = Space.Self;
            rotation.AnimateX = false;
            rotation.AnimateY = !z;
            rotation.AnimateZ = z;
            var curve = new MMTweenType(linear ? Curve((0f, 0f), (1f, 1f)) : Curve((0f, 0f), (0.6f, 0.9f), (1f, 1f)));
            if (z) rotation.AnimateRotationTweenZ = curve;
            else rotation.AnimateRotationTweenY = curve;
            rotation.RemapCurveZero = from;
            rotation.RemapCurveOne = to;
            rotation.AnimateRotationDuration = duration;
            rotation.DetermineRotationOnPlay = true;
        }

        static void Wiggle(MMF_Rotation rotation, float degrees, float duration)
        {
            rotation.Mode = MMF_Rotation.Modes.Absolute;
            rotation.RotationSpace = Space.Self;
            rotation.AnimateX = rotation.AnimateY = false;
            rotation.AnimateZ = true;
            rotation.AnimateRotationTweenZ = new MMTweenType(Curve((0f, 0f), (1f / 6f, 1f), (2f / 6f, -1f), (0.5f, 1f),
                (4f / 6f, -1f), (5f / 6f, 0.5f), (1f, 0f)));
            rotation.RemapCurveZero = 0f;
            rotation.RemapCurveOne = degrees;
            rotation.AnimateRotationDuration = duration;
            rotation.DetermineRotationOnPlay = true;
        }

        static void Pulse(MMF_Scale scale, float peak, float duration, AnimationCurve curve)
        {
            scale.Mode = MMF_Scale.Modes.Absolute;
            scale.UniformScaling = true;
            scale.AnimateZ = false;
            scale.RemapCurveZero = 1f;
            scale.RemapCurveOne = peak;
            scale.AnimateScaleTweenX = new MMTweenType(curve);
            scale.AnimateScaleDuration = duration;
            scale.DetermineScaleOnPlay = true;
        }

        static void ScaleTo(MMF_Player p, string label, float to, float duration)
        {
            var scale = Add<MMF_Scale>(p, label, true);
            Pulse(scale, to, duration, Curve((0f, 0f), (1f, 1f)));
        }

        static void Spring(MMF_Player p, float delay)
        {
            var spring = Add<MMF_ScaleSpring>(p, "Spring", true);
            spring.Mode = MMF_ScaleSpring.Modes.Bump;
            spring.BumpScaleMin = spring.BumpScaleMax = new Vector3(8f, 8f, 0f);
            spring.DeclaredDuration = 0.3f;
            spring.Timing.InitialDelay = delay;
        }

        static void Fade(MMF_Player p, float to, float duration)
        {
            var fade = Add<MMF_CanvasGroup>(p, $"Alpha → {to:0.##}", true);
            fade.Mode = MMF_FeedbackBase.Modes.ToDestination;
            fade.DestinationAlpha = to;
            fade.Duration = duration;
        }

        static void Counter(MMF_Player p, float duration, float delay)
        {
            Holder(p, FxLabels.Counter);
            var count = Add<MMF_TMPCountTo>(p, "Count to", true);
            count.Duration = duration;
            count.FloorValues = true;
            count.Timing.InitialDelay = delay;
        }

        static void Text(MMF_Player p, float delay)
        {
            var text = Add<MMF_FloatingText>(p, "Floating text", false);
            text.PositionMode = MMF_FloatingText.PositionModes.FeedbackPosition;
            text.ForceLifetime = true;
            text.Lifetime = 0.9f;
            text.ForceColor = true;
            text.AnimateColorGradient = Gradient((0f, Rubric), (1f, Rubric));
            text.Timing.InitialDelay = delay;
        }

        // Epic Toon FX is not in the project yet: the slot is there, off, waiting for its _AG variant.
        static void Particles(MMF_Player p, string label, float delay = 0f)
        {
            var particles = Add<MMF_ParticlesInstantiation>(p, label, false);
            particles.Active = false;
            particles.Timing.InitialDelay = delay;
        }

        // Sound comes in a later stage: the slot is there, off, waiting for a clip.
        static void Sound(MMF_Player p, string label, float delay = 0f)
        {
            var sound = Add<MMF_MMSoundManagerSound>(p, label, false);
            sound.Active = false;
            sound.Timing.InitialDelay = delay;
        }

        static AnimationCurve Curve(params (float time, float value)[] keys)
        {
            var curve = new AnimationCurve();
            foreach (var (time, value) in keys) curve.AddKey(time, value);
            return curve;
        }

        static Gradient Gradient(params (float time, Color color)[] keys)
        {
            var colors = new GradientColorKey[keys.Length];
            for (int i = 0; i < keys.Length; i++) colors[i] = new GradientColorKey(keys[i].color, keys[i].time);
            var gradient = new Gradient();
            gradient.SetKeys(colors, new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            return gradient;
        }
    }
}
