using System;
using System.Collections.Generic;

// Plain C#, no UnityEngine: the scheduler's rules are tested without Feel or a running Editor.
namespace ArcaneGambit.FX
{
    /// <summary>
    /// How loud an effect is (FX_SPEC 5.1). Whispers never wait. Strikes keep their order behind a boom.
    /// Booms play one at a time and hold the player's input while they play or wait.
    /// </summary>
    public enum FxTier { Whisper, Strike, Boom }

    /// <summary>One per row of the FX catalog. Values are stored in assets, so never renumber them.</summary>
    public enum FxCueId
    {
        CardHover = 0, CardPlay = 1, CardInvalid = 2, CardDraw = 3, CardDiscard = 4, ReactionReveal = 5, ArcaneCast = 6,
        Move = 20, CavalryJump = 21, Recruit = 22, Capture = 23, CaptureBlocked = 24, Equip = 25, MoraleGain = 26,
        CrossbowVolley = 40, QuickMove = 41, SmokeScreen = 42, Fortify = 43, SupplyDraw = 44,
        FireballCast = 60, FireballShot = 61, FireballHit = 62, FireballBlocked = 63,
        SeersOmen = 64, DawnPrayer = 65, VigilSeal = 66, OracleEcho = 67,
        StormArrival = 80, StormMark = 81, StormStrike = 82, StormThunder = 83,
        Blizzard = 84, Gale = 85, Fog = 86, Mud = 87, MoonlessNight = 88, ConditionRemoved = 89,
        MatchStart = 100, NewTurn = 101, Victory = 102, Defeat = 103,
    }

    public interface IFxPlayback
    {
        bool IsPlaying { get; }
        void Stop();
    }

    public readonly struct FxCueSettings
    {
        public readonly FxTier Tier;
        public readonly float Intensity;
        public readonly float RepeatIntensity;
        /// <summary>Seconds after which a job is stopped anyway, so a stuck player can never keep the input locked.</summary>
        public readonly float Timeout;

        public FxCueSettings(FxTier tier, float intensity = 1f, float repeatIntensity = FxScheduler.DefaultRepeatIntensity,
            float timeout = 6f)
        {
            Tier = tier;
            Intensity = intensity;
            RepeatIntensity = repeatIntensity;
            Timeout = timeout;
        }
    }

    public enum FxJobState { Queued, Playing, Finished, Cancelled }

    public sealed class FxJob
    {
        internal readonly Func<float, IFxPlayback> Start;
        internal readonly float Timeout;
        internal IFxPlayback Playback;
        internal float Elapsed;

        internal FxJob(FxCueId cue, FxTier tier, float intensity, float timeout, Func<float, IFxPlayback> start)
        {
            Cue = cue;
            Tier = tier;
            Intensity = intensity;
            Timeout = timeout;
            Start = start;
        }

        public FxCueId Cue { get; }
        public FxTier Tier { get; }
        /// <summary>The intensity the player gets: the caller's, times the cue's base or repeat intensity.</summary>
        public float Intensity { get; }
        public FxJobState State { get; private set; }
        public bool IsDone => State == FxJobState.Finished || State == FxJobState.Cancelled;

        public event Action<FxJob> Done;

        internal void Begin() => State = FxJobState.Playing;

        internal void End(FxJobState state)
        {
            if (IsDone) return;
            State = state;
            Done?.Invoke(this);
        }

        /// <summary>A job with nothing to play (a cue without a player yet): already over, never locks.</summary>
        public static FxJob Skipped(FxCueId cue, FxTier tier)
        {
            var job = new FxJob(cue, tier, 0f, 0f, null);
            job.State = FxJobState.Finished;
            return job;
        }
    }

    public sealed class FxScheduler
    {
        public const float DefaultRepeatIntensity = 0.7f;

        readonly List<FxJob> _queue = new List<FxJob>();
        readonly List<FxJob> _playing = new List<FxJob>();
        readonly HashSet<FxCueId> _playedThisTurn = new HashSet<FxCueId>();

        /// <summary>True while a boom plays or waits: input stays blocked and the turn must not move on.</summary>
        public bool Locked { get; private set; }
        public int Queued => _queue.Count;
        public int Playing => _playing.Count;

        public event Action<bool> LockChanged;

        /// <param name="start">Plays the effect at the given intensity; returns null when there is nothing to play.</param>
        public FxJob Enqueue(FxCueId cue, FxCueSettings settings, float intensity, Func<float, IFxPlayback> start)
        {
            bool repeat = settings.Tier == FxTier.Strike && !_playedThisTurn.Add(cue);
            float applied = intensity * (repeat ? settings.RepeatIntensity : settings.Intensity);
            var job = new FxJob(cue, settings.Tier, applied, settings.Timeout, start);
            if (settings.Tier == FxTier.Whisper)
            {
                Begin(job);
                return job;
            }
            _queue.Add(job);
            Pump();
            UpdateLock();
            return job;
        }

        /// <summary>Repetition only tires within a turn.</summary>
        public void NewTurn() => _playedThisTurn.Clear();

        public void Update(float deltaTime)
        {
            for (int i = _playing.Count - 1; i >= 0; i--)
            {
                var job = _playing[i];
                job.Elapsed += deltaTime;
                bool over = job.Playback == null || !job.Playback.IsPlaying;
                if (!over && job.Elapsed < job.Timeout) continue;
                if (!over) job.Playback.Stop();
                _playing.RemoveAt(i);
                job.End(FxJobState.Finished);
            }
            Pump();
            UpdateLock();
        }

        public void Cancel(FxJob job)
        {
            if (job == null || job.IsDone) return;
            if (_queue.Remove(job) || _playing.Remove(job)) job.Playback?.Stop();
            job.End(FxJobState.Cancelled);
            Pump();
            UpdateLock();
        }

        public void CancelAll()
        {
            var jobs = new List<FxJob>(_playing);
            jobs.AddRange(_queue);
            _playing.Clear();
            _queue.Clear();
            foreach (var job in jobs)
            {
                job.Playback?.Stop();
                job.End(FxJobState.Cancelled);
            }
            UpdateLock();
        }

        // Starts queued jobs in order until a boom is on stage; whatever follows a boom waits for it to end.
        void Pump()
        {
            while (_queue.Count > 0 && !BoomPlaying())
            {
                var job = _queue[0];
                _queue.RemoveAt(0);
                Begin(job);
            }
        }

        void Begin(FxJob job)
        {
            job.Begin();
            IFxPlayback playback;
            try
            {
                playback = job.Start?.Invoke(job.Intensity);
            }
            catch
            {
                job.End(FxJobState.Cancelled);
                throw;
            }
            if (playback == null || !playback.IsPlaying)
            {
                job.End(FxJobState.Finished);
                return;
            }
            job.Playback = playback;
            _playing.Add(job);
        }

        bool BoomPlaying()
        {
            foreach (var job in _playing)
                if (job.Tier == FxTier.Boom) return true;
            return false;
        }

        void UpdateLock()
        {
            bool locked = BoomPlaying();
            if (!locked)
                foreach (var job in _queue)
                    if (job.Tier == FxTier.Boom) { locked = true; break; }
            if (locked == Locked) return;
            Locked = locked;
            LockChanged?.Invoke(locked);
        }
    }

    /// <summary>Never more than three flashes in any one second (photosensitivity guideline).</summary>
    public sealed class FxFlashLimiter
    {
        public const int MaxPerSecond = 3;
        readonly Queue<double> _recent = new Queue<double>();

        public bool TryFlash(double now)
        {
            while (_recent.Count > 0 && now - _recent.Peek() >= 1.0) _recent.Dequeue();
            if (_recent.Count >= MaxPerSecond) return false;
            _recent.Enqueue(now);
            return true;
        }

        public void Clear() => _recent.Clear();
    }
}
