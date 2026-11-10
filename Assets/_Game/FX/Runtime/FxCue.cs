using MoreMountains.Feedbacks;
using UnityEngine;

namespace ArcaneGambit.FX
{
    /// <summary>One catalog row: how loud it is and which MMF_Player plays it. Read-only at runtime.</summary>
    [CreateAssetMenu(menuName = "Arcane Gambit/FX Cue", fileName = "FxCue")]
    public sealed class FxCue : ScriptableObject
    {
        [SerializeField] FxCueId _id;
        [SerializeField] FxTier _tier;
        [SerializeField, Tooltip("Prefab with the MMF_Player for this event. Empty: the event plays nothing yet.")]
        MMF_Player _player;
        [SerializeField, Range(0f, 2f)] float _intensity = 1f;
        [SerializeField, Range(0f, 2f), Tooltip("Strikes only: intensity from the second time in the same turn")]
        float _repeatIntensity = FxScheduler.DefaultRepeatIntensity;
        [SerializeField, Min(0.1f), Tooltip("Seconds after which the player is stopped anyway, releasing the input")]
        float _timeout = 6f;

        public FxCueId Id => _id;
        public FxTier Tier => _tier;
        public MMF_Player Player => _player;
        public FxCueSettings Settings => new FxCueSettings(_tier, _intensity, _repeatIntensity, _timeout);

        public void Configure(FxCueId id, FxTier tier, float timeout)
        {
            _id = id;
            _tier = tier;
            _timeout = timeout;
        }
    }
}
