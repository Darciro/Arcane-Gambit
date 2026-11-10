using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace ArcaneGambit.FX
{
    /// <summary>
    /// Plays FX cues through the scheduler, each on a pooled instance of its MMF_Player prefab. The game asks for cues
    /// by id; it never touches Feel. A player is done when Feel reports it no longer playing (checked every frame).
    /// </summary>
    public sealed class FxDirector : MonoBehaviour
    {
        const string CuesPath = "FxCues";

        [SerializeField, Tooltip("Pooled player instances are kept under this transform")]
        Transform _players;

        readonly FxScheduler _scheduler = new FxScheduler();
        readonly Dictionary<FxCueId, FxCue> _cues = new Dictionary<FxCueId, FxCue>();
        readonly Dictionary<FxCueId, List<FeelFxPlayer>> _pool = new Dictionary<FxCueId, List<FeelFxPlayer>>();
        readonly HashSet<FxCueId> _missing = new HashSet<FxCueId>();

        public bool Locked => _scheduler.Locked;
        public int Queued => _scheduler.Queued;
        public int Playing => _scheduler.Playing;

        public event Action<bool> LockChanged;

        void Awake()
        {
            foreach (var cue in Resources.LoadAll<FxCue>(CuesPath)) _cues[cue.Id] = cue;
            if (_players == null) _players = transform;
            _scheduler.LockChanged += locked => LockChanged?.Invoke(locked);
        }

        // Scaled time: a pause or a freeze frame holds the timeouts along with the players themselves. A player whose
        // target was destroyed is ended here, before Feel's coroutines resume this frame and write to it.
        void Update()
        {
            foreach (var list in _pool.Values)
                foreach (var player in list)
                    if (player.IsPlaying && player.LostTarget) player.Abort();
            _scheduler.Update(Time.deltaTime);
        }

        // The scene is going away with whoever listened: release everything without calling back into it.
        void OnDestroy()
        {
            LockChanged = null;
            _scheduler.CancelAll();
        }

        public bool HasPlayer(FxCueId id) => _cues.TryGetValue(id, out var cue) && cue.Player != null;

        public void NewTurn() => _scheduler.NewTurn();

        public void StopAll() => _scheduler.CancelAll();

        public void Stop(FxJob job) => _scheduler.Cancel(job);

        /// <summary>Completes when the job is done, or as soon as <paramref name="ct"/> is cancelled.</summary>
        public static async Awaitable WaitAsync(FxJob job, CancellationToken ct)
        {
            if (job.IsDone) return;
            var done = new AwaitableCompletionSource();
            job.Done += _ => done.TrySetResult();
            using (ct.Register(() => done.TrySetResult()))
                await done.Awaitable;
            ct.ThrowIfCancellationRequested();
        }

        /// <summary>Completes once the job has left the queue: its player is playing, or it is already over.</summary>
        public static async Awaitable WaitStartedAsync(FxJob job, CancellationToken ct)
        {
            while (job.State == FxJobState.Queued) await Awaitable.NextFrameAsync(ct);
        }

        public FxJob Play(FxCueId id, Vector3 position, float intensity = 1f, FxContext context = default)
        {
            if (!_cues.TryGetValue(id, out var cue))
            {
                if (_missing.Add(id)) Debug.LogWarning($"FX: no FxCue asset for {id} in Resources/{CuesPath}.", this);
                return FxJob.Skipped(id, FxTier.Whisper);
            }
            if (cue.Player == null) return FxJob.Skipped(id, cue.Tier);
            return _scheduler.Enqueue(id, cue.Settings, intensity, applied => Begin(cue, position, applied, context));
        }

        /// <summary>Completes when the cue's player has finished, or at once if the cue has nothing to play.</summary>
        public async Awaitable PlayAsync(FxCueId id, Vector3 position, float intensity, CancellationToken ct,
            FxContext context = default)
        {
            var job = Play(id, position, intensity, context);
            if (job.IsDone) return;
            var done = new AwaitableCompletionSource();
            job.Done += _ => done.TrySetResult();
            using (ct.Register(() => _scheduler.Cancel(job)))
                await done.Awaitable;
        }

        /// <summary>Spawns a particle prefab (an _AG variant) and destroys it once its systems are over.</summary>
        public GameObject Spawn(GameObject particles, Vector3 position, float scale)
        {
            var instance = Instantiate(particles, position, particles.transform.rotation, transform);
            instance.transform.localScale = particles.transform.localScale * scale;
            float life = 0f;
            foreach (var system in instance.GetComponentsInChildren<ParticleSystem>())
            {
                var main = system.main;
                if (main.loop) return instance;
                life = Mathf.Max(life, main.startDelay.constantMax + main.duration + main.startLifetime.constantMax);
            }
            Destroy(instance, life);
            return instance;
        }

        IFxPlayback Begin(FxCue cue, Vector3 position, float intensity, FxContext context)
        {
            try
            {
                var player = Acquire(cue);
                player.Bind(context);
                player.Play(position, intensity);
                return player;
            }
            catch (Exception e)
            {
                Debug.LogException(e, this);
                return null;
            }
        }

        FeelFxPlayer Acquire(FxCue cue)
        {
            if (!_pool.TryGetValue(cue.Id, out var list)) _pool[cue.Id] = list = new List<FeelFxPlayer>();
            foreach (var pooled in list)
                if (pooled.Player != null && !pooled.IsPlaying) return pooled;
            var instance = Instantiate(cue.Player, _players);
            instance.name = cue.Player.name;
            // Initialized by Bind, once its targets are known.
            var player = new FeelFxPlayer(instance);
            list.Add(player);
            return player;
        }
    }
}
