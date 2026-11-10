using System.Collections.Generic;
using ArcaneGambit.FX;
using NUnit.Framework;

namespace ArcaneGambit.Tests
{
    public sealed class FxSchedulerTests
    {
        sealed class StubPlayback : IFxPlayback
        {
            public bool IsPlaying { get; set; } = true;
            public bool Stopped { get; private set; }

            public void Stop()
            {
                Stopped = true;
                IsPlaying = false;
            }
        }

        static readonly FxCueSettings Whisper = new FxCueSettings(FxTier.Whisper);
        static readonly FxCueSettings Strike = new FxCueSettings(FxTier.Strike);
        static readonly FxCueSettings Boom = new FxCueSettings(FxTier.Boom);

        readonly List<FxCueId> _started = new List<FxCueId>();
        readonly Dictionary<FxCueId, StubPlayback> _playbacks = new Dictionary<FxCueId, StubPlayback>();
        FxScheduler _scheduler;

        [SetUp]
        public void SetUp()
        {
            _started.Clear();
            _playbacks.Clear();
            _scheduler = new FxScheduler();
        }

        FxJob Play(FxCueId cue, FxCueSettings settings, float intensity = 1f) =>
            _scheduler.Enqueue(cue, settings, intensity, _ =>
            {
                _started.Add(cue);
                var playback = new StubPlayback();
                _playbacks[cue] = playback;
                return playback;
            });

        void Finish(FxCueId cue)
        {
            _playbacks[cue].IsPlaying = false;
            _scheduler.Update(0.016f);
        }

        [Test]
        public void BoomWaitsForThePreviousBoomToEnd()
        {
            var first = Play(FxCueId.StormStrike, Boom);
            var second = Play(FxCueId.Victory, Boom);

            Assert.AreEqual(FxJobState.Playing, first.State);
            Assert.AreEqual(FxJobState.Queued, second.State);
            Assert.IsTrue(_scheduler.Locked);

            Finish(FxCueId.StormStrike);
            Assert.AreEqual(FxJobState.Finished, first.State);
            Assert.AreEqual(FxJobState.Playing, second.State);
            Assert.IsTrue(_scheduler.Locked);

            Finish(FxCueId.Victory);
            Assert.IsFalse(_scheduler.Locked);
        }

        [Test]
        public void WhispersNeverWaitAndNeverLock()
        {
            Play(FxCueId.StormStrike, Boom);
            var hover = Play(FxCueId.CardHover, Whisper);

            Assert.AreEqual(FxJobState.Playing, hover.State);
            CollectionAssert.AreEqual(new[] { FxCueId.StormStrike, FxCueId.CardHover }, _started);

            Finish(FxCueId.StormStrike);
            Play(FxCueId.Move, Whisper);
            Assert.IsFalse(_scheduler.Locked);
        }

        [Test]
        public void QueuedEffectsStartInOrder()
        {
            Play(FxCueId.StormStrike, Boom);
            Play(FxCueId.Capture, Strike);
            Play(FxCueId.Victory, Boom);
            Play(FxCueId.CaptureBlocked, Strike);

            CollectionAssert.AreEqual(new[] { FxCueId.StormStrike }, _started);
            Finish(FxCueId.StormStrike);
            CollectionAssert.AreEqual(new[] { FxCueId.StormStrike, FxCueId.Capture, FxCueId.Victory }, _started);
            Finish(FxCueId.Victory);
            CollectionAssert.AreEqual(
                new[] { FxCueId.StormStrike, FxCueId.Capture, FxCueId.Victory, FxCueId.CaptureBlocked }, _started);
        }

        [Test]
        public void RepeatedStrikeInATurnIsSofterUntilTheTurnChanges()
        {
            var settings = new FxCueSettings(FxTier.Strike, intensity: 1f, repeatIntensity: 0.7f);
            var first = _scheduler.Enqueue(FxCueId.Capture, settings, 1f, _ => null);
            var second = _scheduler.Enqueue(FxCueId.Capture, settings, 1f, _ => null);
            var other = _scheduler.Enqueue(FxCueId.Equip, settings, 0.5f, _ => null);

            Assert.AreEqual(1f, first.Intensity, 1e-5f);
            Assert.AreEqual(0.7f, second.Intensity, 1e-5f);
            Assert.AreEqual(0.5f, other.Intensity, 1e-5f);

            _scheduler.NewTurn();
            var afterTurn = _scheduler.Enqueue(FxCueId.Capture, settings, 1f, _ => null);
            Assert.AreEqual(1f, afterTurn.Intensity, 1e-5f);
        }

        [Test]
        public void LockIsReleasedWhenTheBoomIsCancelled()
        {
            var boom = Play(FxCueId.StormStrike, Boom);
            var queued = Play(FxCueId.Victory, Boom);
            int changes = 0;
            _scheduler.LockChanged += _ => changes++;

            _scheduler.Cancel(boom);
            Assert.IsTrue(_playbacks[FxCueId.StormStrike].Stopped);
            Assert.AreEqual(FxJobState.Cancelled, boom.State);
            Assert.AreEqual(FxJobState.Playing, queued.State);

            _scheduler.CancelAll();
            Assert.AreEqual(FxJobState.Cancelled, queued.State);
            Assert.IsFalse(_scheduler.Locked);
            Assert.AreEqual(1, changes);
        }

        [Test]
        public void LockIsReleasedWhenThePlayerIsDestroyed()
        {
            var boom = Play(FxCueId.StormArrival, Boom);
            // A destroyed MMF_Player reads as not playing.
            _playbacks[FxCueId.StormArrival].IsPlaying = false;
            _scheduler.Update(0.016f);

            Assert.AreEqual(FxJobState.Finished, boom.State);
            Assert.IsFalse(_scheduler.Locked);
        }

        [Test]
        public void StuckPlayerIsStoppedAtItsTimeout()
        {
            var boom = _scheduler.Enqueue(FxCueId.StormArrival, new FxCueSettings(FxTier.Boom, timeout: 1f), 1f,
                _ => _playbacks[FxCueId.StormArrival] = new StubPlayback());

            _scheduler.Update(0.6f);
            Assert.IsTrue(_scheduler.Locked);
            _scheduler.Update(0.6f);

            Assert.IsTrue(_playbacks[FxCueId.StormArrival].Stopped);
            Assert.AreEqual(FxJobState.Finished, boom.State);
            Assert.IsFalse(_scheduler.Locked);
        }

        [Test]
        public void CueWithoutAPlayerFinishesAtOnceWithoutLocking()
        {
            int changes = 0;
            _scheduler.LockChanged += _ => changes++;
            var job = _scheduler.Enqueue(FxCueId.Victory, Boom, 1f, _ => null);

            Assert.AreEqual(FxJobState.Finished, job.State);
            Assert.IsFalse(_scheduler.Locked);
            Assert.AreEqual(0, changes);
        }

        [Test]
        public void NoMoreThanThreeFlashesInAnySecond()
        {
            var limiter = new FxFlashLimiter();
            Assert.IsTrue(limiter.TryFlash(0.0));
            Assert.IsTrue(limiter.TryFlash(0.2));
            Assert.IsTrue(limiter.TryFlash(0.4));
            Assert.IsFalse(limiter.TryFlash(0.6));
            Assert.IsFalse(limiter.TryFlash(0.99));
            Assert.IsTrue(limiter.TryFlash(1.0));
        }
    }
}
