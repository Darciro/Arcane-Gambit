using System.Collections.Generic;
using MoreMountains.Feedbacks;
using UnityEngine;

namespace ArcaneGambit.FX
{
    public interface IFxPlayer : IFxPlayback
    {
        void Play(Vector3 position, float intensity);
    }

    /// <summary>
    /// What one play of an effect acts on and with. A player's MMF_ReferenceHolder feedbacks are filled by label:
    /// "Board", any label found in <see cref="Named"/>, otherwise <see cref="Target"/>; the feedbacks after a holder
    /// take it as their target. Values reach the feedbacks that need them, when set.
    /// </summary>
    public struct FxContext
    {
        public GameObject Target;
        public GameObject Board;
        public IReadOnlyDictionary<string, GameObject> Named;
        /// <summary>Start and end of every MMF_Position labelled "Fly".</summary>
        public Transform From, To;
        /// <summary>Every MMF_TMPCountTo, when a format is given (a C# numeric format, such as "0' / 5'").</summary>
        public float CountFrom, CountTo;
        public string CountFormat;
        /// <summary>Every MMF_FloatingText, when set.</summary>
        public string Text;
    }

    /// <summary>Reference holder labels shared by the player prefabs and the game's FxContext.</summary>
    public static class FxLabels
    {
        public const string Target = "Target";
        public const string Board = "Board";
        public const string Holder = "Holder";
        public const string Seal = "Seal";
        public const string Frame = "Frame";
        public const string Back = "Back";
        public const string Outline = "Outline";
        public const string Circle = "Circle";
        public const string Counter = "Counter";
        public const string Token = "Token";
        public const string Badge = "Badge";
        public const string Ring = "Ring";
        public const string Shield = "Shield";
        public const string Fill = "Fill";
        public const string Title = "Title";
    }

    /// <summary>The "Reduce flashes and shakes" option, saved per player.</summary>
    public static class FxSettings
    {
        const string ReduceKey = "fx.reduceFlashesAndShakes";
        public const float ReducedShake = 0.3f;

        static bool? s_reduce;

        public static FxFlashLimiter Flashes { get; private set; } = new FxFlashLimiter();

        public static bool ReduceFlashesAndShakes
        {
            get => s_reduce ??= PlayerPrefs.GetInt(ReduceKey, 0) == 1;
            set
            {
                s_reduce = value;
                PlayerPrefs.SetInt(ReduceKey, value ? 1 : 0);
                PlayerPrefs.Save();
            }
        }

        // Domain reload is disabled in this project: statics must be cleared on every Play.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            s_reduce = null;
            Flashes = new FxFlashLimiter();
        }
    }

    /// <summary>
    /// Thin adapter over one pooled MMF_Player instance. Before each play it points the reference holders at the
    /// current targets and applies the accessibility option, restoring the player's authored setup otherwise.
    /// </summary>
    public sealed class FeelFxPlayer : IFxPlayer
    {
        public const string FlyLabel = "Fly";

        readonly MMF_Player _player;
        readonly GameObject _unbound;
        readonly List<GameObject> _bound = new List<GameObject>();
        readonly List<MMF_ReferenceHolder> _holders;
        bool _initialized;
        readonly List<MMF_Position> _flights = new List<MMF_Position>();
        readonly List<MMF_TMPCountTo> _counters;
        readonly List<MMF_FloatingText> _texts;
        readonly List<(MMF_Feedback feedback, bool active)> _flashes = new List<(MMF_Feedback, bool)>();
        readonly List<(MMF_Feedback feedback, bool active)> _freezes = new List<(MMF_Feedback, bool)>();
        readonly List<(MMF_Feedback feedback, bool randomize, Vector2 range)> _shakes =
            new List<(MMF_Feedback, bool, Vector2)>();

        public FeelFxPlayer(MMF_Player player)
        {
            _player = player;
            // Feel reads a holder's reference without a null check: an absent optional target points here instead.
            _unbound = new GameObject("Unbound");
            _unbound.transform.SetParent(player.transform, false);
            _holders = player.GetFeedbacksOfType<MMF_ReferenceHolder>();
            _counters = player.GetFeedbacksOfType<MMF_TMPCountTo>();
            _texts = player.GetFeedbacksOfType<MMF_FloatingText>();
            foreach (var feedback in player.FeedbacksList)
                switch (feedback)
                {
                    case MMF_Position position when position.Label == FlyLabel:
                        _flights.Add(position);
                        break;
                    case MMF_Flash _:
                        _flashes.Add((feedback, feedback.Active));
                        break;
                    case MMF_FreezeFrame _:
                        _freezes.Add((feedback, feedback.Active));
                        break;
                    case MMF_PositionShake _:
                    case MMF_RotationShake _:
                    case MMF_ScaleShake _:
                    case MMF_Wiggle _:
                    case MMF_CameraShake _:
                        _shakes.Add((feedback, feedback.RandomizeOutput, feedback.RandomMultiplier));
                        break;
                }
        }

        public MMF_Player Player => _player;
        public bool IsPlaying => _player != null && _player.IsPlaying;

        /// <summary>A target of the current play was destroyed (a hand card rebuilt, a unit captured).</summary>
        public bool LostTarget
        {
            get
            {
                foreach (var target in _bound)
                    if (target == null) return true;
                return false;
            }
        }

        /// <summary>
        /// Ends the play without touching its targets: Feel's feedbacks run as coroutines on the player, and stopping
        /// them normally would write to the destroyed target once more.
        /// </summary>
        public void Abort()
        {
            if (_player == null) return;
            _player.StopAllCoroutines();
            _bound.Clear();
            try
            {
                _player.StopFeedbacks();
            }
            catch (MissingReferenceException)
            {
            }
            catch (System.NullReferenceException)
            {
            }
        }

        public void Bind(FxContext context)
        {
            if (_player == null) return;
            foreach (var flight in _flights)
            {
                flight.InitialPositionTransform = context.From;
                flight.DestinationPositionTransform = context.To;
            }
            if (context.CountFormat != null)
                foreach (var counter in _counters)
                {
                    counter.CountFrom = context.CountFrom;
                    counter.CountTo = context.CountTo;
                    counter.Format = context.CountFormat;
                }
            if (context.Text != null)
                foreach (var text in _texts) text.Value = context.Text;

            if (_holders.Count == 0)
            {
                if (!_initialized) _player.Initialization(true);
                _initialized = true;
                return;
            }
            _bound.Clear();
            foreach (var holder in _holders)
            {
                var reference = holder.Label == FxLabels.Board ? context.Board
                    : context.Named != null && context.Named.TryGetValue(holder.Label, out var named) ? named
                    : context.Target;
                holder.GameObjectReference = reference != null ? reference : _unbound;
                if (reference != null) _bound.Add(reference);
            }
            // Feedbacks acquire their targets and cache the targets' starting state (scale, rotation...) during
            // initialization, so a player whose targets change from play to play is initialized again each time.
            _player.Initialization(true);
            _initialized = true;
        }

        public void Play(Vector3 position, float intensity)
        {
            if (_player == null) return;
            bool reduce = FxSettings.ReduceFlashesAndShakes;
            foreach (var (feedback, active) in _freezes) feedback.Active = active && !reduce;
            foreach (var (feedback, active) in _flashes)
                feedback.Active = active && !reduce && FxSettings.Flashes.TryFlash(Time.unscaledTimeAsDouble);
            foreach (var (feedback, randomize, range) in _shakes)
            {
                feedback.RandomizeOutput = reduce || randomize;
                feedback.RandomMultiplier = reduce ? new Vector2(FxSettings.ReducedShake, FxSettings.ReducedShake) : range;
            }
            _player.transform.position = position;
            _player.PlayFeedbacks(position, intensity);
        }

        public void Stop()
        {
            if (_player == null) return;
            if (LostTarget)
            {
                Abort();
                return;
            }
            _player.StopFeedbacks();
            // A looping player (a marked square's flicker) stops halfway: put its targets back as they were,
            // unless the game already destroyed them.
            try
            {
                _player.RestoreInitialValues();
            }
            catch (MissingReferenceException)
            {
            }
            catch (System.NullReferenceException)
            {
            }
        }
    }
}
