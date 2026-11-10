using System;
using UnityEngine;

namespace ArcaneGambit.FX
{
    public enum FxBlend { AlphaBlended, Additive }

    /// <summary>The part a particle layer plays in an effect, which picks its ramp.</summary>
    public enum FxRole { Body, Core, Halo }

    [Serializable]
    public struct FxRamp
    {
        public Gradient Colors;
        public FxBlend Blend;

        public FxRamp(Gradient colors, FxBlend blend)
        {
            Colors = colors;
            Blend = blend;
        }
    }

    /// <summary>
    /// The only colors an effect may use, as Color over Lifetime ramps. Variants keep a white Start Color, so all
    /// color comes from here: ink that darkens the parchment, and light only where fire and lightning glow.
    /// </summary>
    [CreateAssetMenu(menuName = "Arcane Gambit/FX Palette", fileName = "FxPalette")]
    public sealed class FxPalette : ScriptableObject
    {
        [SerializeField, Tooltip("Every particle layer that is neither a core nor a halo")]
        FxRamp _body;
        [SerializeField, Tooltip("Layers that were additive in the original effect: the heart of a fire, the bolt itself")]
        bool _hasCore;
        [SerializeField] FxRamp _core;
        [SerializeField, Tooltip("Layers named Glow, Halo or Flare in the original effect")]
        bool _hasHalo;
        [SerializeField] FxRamp _halo;

        public FxRamp For(FxRole role) => role switch
        {
            FxRole.Halo when _hasHalo => _halo,
            FxRole.Halo or FxRole.Core when _hasCore => _core,
            _ => _body,
        };

        public void Configure(FxRamp body, FxRamp? core = null, FxRamp? halo = null)
        {
            _body = body;
            _hasCore = core.HasValue;
            _core = core ?? body;
            _hasHalo = halo.HasValue;
            _halo = halo ?? body;
        }
    }
}
