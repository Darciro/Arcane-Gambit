using System.Collections.Generic;
using MoreMountains.Feedbacks;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace ArcaneGambit.FX
{
    /// <summary>How a lasting condition tints the board's post-processing.</summary>
    public readonly struct FxLook
    {
        public readonly float Vignette;
        public readonly float Exposure;
        public readonly float Saturation;

        public FxLook(float vignette, float exposure = 0f, float saturation = 0f)
        {
            Vignette = vignette;
            Exposure = exposure;
            Saturation = saturation;
        }
    }

    /// <summary>
    /// The resting post-processing while weather lasts, blended in and out. Several conditions never overwrite each
    /// other: the strongest wins on each channel (the largest vignette, the darkest exposure, the lowest saturation).
    /// Feel's shakers pulse on top of this baseline; it is only written while no shaker is running, so a pulse that
    /// restores its starting value cannot leave the board stuck on a stale look.
    /// </summary>
    public sealed class FxAmbience : MonoBehaviour
    {
        [SerializeField] Volume _volume;
        [SerializeField, Min(0.05f), Tooltip("Seconds for the board to settle into a new look")]
        float _blendTime = 1.2f;

        readonly Dictionary<string, FxLook> _layers = new Dictionary<string, FxLook>();
        MMShaker[] _shakers;
        Vignette _vignette;
        ColorAdjustments _color;
        float _vignetteValue, _exposure, _saturation;

        void Start()
        {
            if (_volume == null) _volume = GetComponentInChildren<Volume>();
            if (_volume == null) return;
            var profile = _volume.profile;
            profile.TryGet(out _vignette);
            profile.TryGet(out _color);
            _shakers = _volume.GetComponents<MMShaker>();
        }

        public void Set(string key, FxLook look) => _layers[key] = look;

        public void Clear(string key) => _layers.Remove(key);

        public void ClearAll() => _layers.Clear();

        void Update()
        {
            if (_vignette == null && _color == null) return;
            foreach (var shaker in _shakers)
                if (shaker != null && shaker.Shaking) return;

            float vignette = 0f, exposure = 0f, saturation = 0f;
            foreach (var look in _layers.Values)
            {
                vignette = Mathf.Max(vignette, look.Vignette);
                exposure = Mathf.Min(exposure, look.Exposure);
                saturation = Mathf.Min(saturation, look.Saturation);
            }
            float t = 1f - Mathf.Exp(-Time.unscaledDeltaTime * 4f / _blendTime);
            _vignetteValue = Approach(_vignetteValue, vignette, t, 0.001f);
            _exposure = Approach(_exposure, exposure, t, 0.002f);
            _saturation = Approach(_saturation, saturation, t, 0.1f);
            if (_vignette != null) _vignette.intensity.value = _vignetteValue;
            if (_color != null)
            {
                _color.postExposure.value = _exposure;
                _color.saturation.value = _saturation;
            }
        }

        static float Approach(float value, float target, float t, float snap)
        {
            value = Mathf.Lerp(value, target, t);
            return Mathf.Abs(value - target) < snap ? target : value;
        }
    }
}
