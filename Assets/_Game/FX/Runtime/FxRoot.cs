using MoreMountains.Feedbacks;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace ArcaneGambit.FX
{
    public static class FxLayers
    {
        public const string Board = "Board";
        public const string BoardFx = "FX_Board";
        public const string Hud = "HUD";
        public const string HudFx = "FX_UI";
        public const string Modals = "Modals";

        /// <summary>Sorting layers back to front, right after Unity's "Default".</summary>
        public static readonly string[] Order = { Board, BoardFx, Hud, HudFx, Modals };

        public const int World = 0;
        public const int Ui = 5;
    }

    /// <summary>
    /// The scene services Feel needs during a match (time, flash, sound, floating text, post-processing shakers),
    /// built as the FX_Root prefab by the editor setup and spawned by the match.
    /// </summary>
    public sealed class FxRoot : MonoBehaviour
    {
        const string ResourcePath = "FX_Root";

        [SerializeField, Tooltip("Draws the HUD with a second camera that skips post-processing, so weather effects " +
                                 "never darken its text. Off: one camera, and the HUD gets the same post as the board.")]
        bool _hudOnOverlayCamera = true;
        [SerializeField] Canvas _flashCanvas;
        [SerializeField] MMFloatingTextSpawner _floatingText;

        public Camera WorldCamera { get; private set; }
        public Camera HudCamera { get; private set; }
        public FxDirector Director { get; private set; }
        public FxAmbience Ambience { get; private set; }

        void Awake()
        {
            Director = GetComponent<FxDirector>();
            Ambience = GetComponent<FxAmbience>();
        }

        public static FxRoot Create(Transform parent, Camera camera)
        {
            var prefab = Resources.Load<FxRoot>(ResourcePath);
            if (prefab == null)
            {
                Debug.LogWarning("FX_Root is missing from Resources, so effects are off. " +
                                 "Run Arcane Gambit > FX > Rebuild FX_Root.");
                return null;
            }
            var root = Instantiate(prefab, parent);
            root.name = prefab.name;
            root.Bind(camera);
            return root;
        }

        public static void SetPaused(bool paused) => SetTimeScale(paused ? 0f : 1f);

        public static void SetTimeScale(float scale)
        {
            // MMTimeManager restores its normal time scale after every freeze frame, which would undo the change.
            if (MMTimeManager.HasInstance) MMTimeManager.Instance.NormalTimeScale = scale;
            Time.timeScale = scale;
        }

        void Start()
        {
            if (WorldCamera == null) Bind(Camera.main);
        }

        public void Bind(Camera camera)
        {
            WorldCamera = HudCamera = camera;
            if (camera == null) return;
            var data = camera.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = true;
            if (_hudOnOverlayCamera) HudCamera = CreateHudCamera(camera, data);
            if (_flashCanvas != null) _flashCanvas.worldCamera = HudCamera;
        }

        /// <summary>The pool is filled on Awake and never grows, so every floating text gets the font here.</summary>
        public void UseFont(TMP_FontAsset font)
        {
            if (_floatingText == null || font == null) return;
            // Feel parks the pool at the scene root, not under the spawner.
            foreach (var text in FindObjectsByType<MMFloatingTextMeshPro>(FindObjectsInactive.Include))
                if (text.TargetTextMeshPro != null) text.TargetTextMeshPro.font = font;
        }

        static Camera CreateHudCamera(Camera world, UniversalAdditionalCameraData worldData)
        {
            var go = new GameObject("HUD Camera");
            go.transform.SetParent(world.transform, false);
            var hud = go.AddComponent<Camera>();
            hud.orthographic = world.orthographic;
            hud.orthographicSize = world.orthographicSize;
            hud.fieldOfView = world.fieldOfView;
            hud.nearClipPlane = world.nearClipPlane;
            hud.farClipPlane = world.farClipPlane;
            hud.cullingMask = 1 << FxLayers.Ui;
            var data = hud.GetUniversalAdditionalCameraData();
            data.renderType = CameraRenderType.Overlay;
            data.renderPostProcessing = false;
            world.cullingMask &= ~(1 << FxLayers.Ui);
            worldData.cameraStack.Add(hud);
            return hud;
        }
    }
}
