using System.IO;
using MoreMountains.Feedbacks;
using MoreMountains.FeedbacksForThirdParty;
using MoreMountains.Tools;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace ArcaneGambit.FX.EditorTools
{
    /// <summary>
    /// Creates what the match's effects rely on whenever it is missing: the sorting layers, the palettes, the
    /// post-processing profile, the floating text, the FX_Root prefab and the FX_Sandbox scene. Existing assets are
    /// never overwritten, so tuning
    /// done in the Inspector survives; the Rebuild menu item is the one explicit way to regenerate FX_Root.
    /// </summary>
    [InitializeOnLoad]
    static class FxSetup
    {
        const string Root = "Assets/_Game/FX";
        const string RootPrefab = Root + "/Resources/FX_Root.prefab";
        const string PostProfile = Root + "/Settings/FX_Post.asset";
        const string FloatingTextPrefab = Root + "/Prefabs/FloatingText_AG.prefab";
        const string Sandbox = Root + "/Scenes/FX_Sandbox.unity";
        const string Palettes = Root + "/Palettes";
        const string Cues = Root + "/Resources/FxCues";
        const string RuneCircle = Root + "/Resources/RuneCircle.png";
        const string MarkRing = Root + "/Resources/MarkRing.png";
        const float CanvasDistance = 10f;

        static readonly Color Ink = new Color32(0x2B, 0x1D, 0x10, 0xFF);
        static readonly Color InkDeep = new Color32(0x1E, 0x14, 0x0A, 0xFF);
        static readonly Color Candle = new Color32(0xF3, 0xE8, 0xD2, 0xFF);
        static readonly Color Table = new Color32(0xD9, 0xC8, 0xA5, 0xFF);

        // A recompile during Play mode skips the setup, so it also runs on the way back to Edit mode.
        static FxSetup()
        {
            EditorApplication.delayCall += Ensure;
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredEditMode) Ensure();
            };
        }

        static void Ensure()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            EnsureSortingLayers();
            EnsurePalettes();
            EnsureFloatingText();
            EnsureCues();
            EnsureRuneCircle();
            EnsureMarkRing();
            FxPlayerBuilder.EnsurePlayers();
            if (!File.Exists(RootPrefab)) RebuildRoot();
            else EnsureDirector();
            if (!File.Exists(Sandbox)) CreateSandbox();
        }

        // White on transparent, tinted rubric by the game: two rings, a dashed one between them, ticks and marks.
        static void EnsureRuneCircle()
        {
            if (File.Exists(RuneCircle)) return;
            WriteSprite(RuneCircle, 512, (r, angle) =>
            {
                float a = Ring(r, 244f, 4f) + Ring(r, 196f, 2f);
                if (angle % 15f < 9f) a += Ring(r, 220f, 2.2f);
                if (angle % 30f < 1.6f && r > 198f && r < 242f) a += 1f;
                float mark = angle % 45f - 22.5f;
                if (Mathf.Abs(mark) < 3.5f - Mathf.Abs(r - 170f) * 0.3f && Mathf.Abs(r - 170f) < 12f) a += 1f;
                return a;
            });
        }

        // The marked square's ring: one dashed circle, white to be tinted rubric.
        static void EnsureMarkRing()
        {
            if (File.Exists(MarkRing)) return;
            WriteSprite(MarkRing, 256, (r, angle) => angle % 24f < 15f ? Ring(r, 112f, 5f) : 0f);
        }

        static void WriteSprite(string path, int size, System.Func<float, float, float> alpha)
        {
            var pixels = new Color32[size * size];
            float c = (size - 1) * 0.5f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = x - c, dy = y - c;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float angle = (Mathf.Atan2(dy, dx) * Mathf.Rad2Deg + 360f) % 360f;
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(alpha(r, angle)) * 255f));
                }
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.SetPixels32(pixels);
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.SaveAndReimport();
            Debug.Log("FX: " + path + " created.");
        }

        static float Ring(float r, float radius, float halfWidth) => Mathf.Clamp01(halfWidth + 0.75f - Mathf.Abs(r - radius));

        // Tiers from the FX_SPEC catalog. The conjuration loop waits for the opponent's reaction, so it must not lock.
        static FxTier DefaultTier(FxCueId id)
        {
            switch (id)
            {
                case FxCueId.CardHover: case FxCueId.CardInvalid: case FxCueId.CardDraw: case FxCueId.CardDiscard:
                case FxCueId.Move: case FxCueId.CavalryJump: case FxCueId.MoraleGain: case FxCueId.QuickMove:
                case FxCueId.SupplyDraw: case FxCueId.NewTurn: case FxCueId.StormThunder:
                    return FxTier.Whisper;
                case FxCueId.ArcaneCast: case FxCueId.FireballHit: case FxCueId.FireballBlocked: case FxCueId.DawnPrayer:
                case FxCueId.StormArrival: case FxCueId.StormStrike: case FxCueId.Blizzard: case FxCueId.Gale:
                case FxCueId.Fog: case FxCueId.MoonlessNight: case FxCueId.Victory:
                    return FxTier.Boom;
                default:
                    return FxTier.Strike;
            }
        }

        static void EnsureCues()
        {
            if (!AssetDatabase.IsValidFolder(Cues)) AssetDatabase.CreateFolder(Root + "/Resources", "FxCues");
            foreach (FxCueId id in System.Enum.GetValues(typeof(FxCueId)))
            {
                string path = $"{Cues}/{id}.asset";
                if (File.Exists(path)) continue;
                var cue = ScriptableObject.CreateInstance<FxCue>();
                var tier = DefaultTier(id);
                float timeout = id == FxCueId.FireballCast ? 60f : tier == FxTier.Boom ? 6f : 3f;
                cue.Configure(id, tier, timeout);
                AssetDatabase.CreateAsset(cue, path);
            }
        }

        // FX_Root grew over the phases: bring an existing prefab up to date without touching its tuning.
        static void EnsureDirector()
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(RootPrefab);
            if (asset == null || (asset.GetComponent<FxDirector>() != null && asset.GetComponent<FxAmbience>() != null))
                return;
            var root = PrefabUtility.LoadPrefabContents(RootPrefab);
            try
            {
                if (root.GetComponent<FxDirector>() == null) AddDirector(root);
                if (root.GetComponent<FxAmbience>() == null) AddAmbience(root);
                PrefabUtility.SaveAsPrefabAsset(root, RootPrefab);
                Debug.Log("FX: " + RootPrefab + " updated (FxDirector, FxAmbience).");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        static void AddAmbience(GameObject root)
        {
            var volume = root.GetComponentInChildren<Volume>(true);
            SetRef(root.AddComponent<FxAmbience>(), "_volume", volume);
        }

        static void AddDirector(GameObject root)
        {
            var players = root.transform.Find("Players");
            if (players == null) players = Child(root, "Players", FxLayers.World).transform;
            SetRef(root.AddComponent<FxDirector>(), "_players", players);
        }

        // FX_SPEC 4.2. Only fire and lightning glow (additive cores); everything else is ink that darkens the page.
        static void EnsurePalettes()
        {
            const int candle = 0xF3E8D2, parchment = 0xECDFC4, rubricLight = 0xC0573F, rubric = 0x8E2F21;
            const int ink = 0x3B2A1A, inkDark = 0x2B1D10, inkDeep = 0x1E140A, inkThin = 0x5A4128;
            var fire = Ramp(new[] { (0f, candle), (0.3f, rubricLight), (0.58f, rubric), (0.82f, ink), (1f, ink) },
                new[] { (0f, 1f), (0.82f, 0.8f), (1f, 0f) });
            var fade = new[] { (0f, 1f), (0.7f, 1f), (1f, 0f) };

            Palette("Fire", new FxRamp(fire, FxBlend.AlphaBlended), new FxRamp(fire, FxBlend.Additive));
            Palette("Lightning",
                new FxRamp(Ramp(new[] { (0f, inkDeep), (1f, inkDeep) }, fade), FxBlend.AlphaBlended),
                new FxRamp(Ramp(new[] { (0f, candle), (1f, candle) }, fade), FxBlend.Additive),
                new FxRamp(Ramp(new[] { (0f, rubricLight), (1f, rubricLight) }, new[] { (0f, 0.45f), (1f, 0f) }),
                    FxBlend.AlphaBlended));
            Palette("Smoke", new FxRamp(Ramp(new[] { (0f, inkThin), (1f, ink) }, new[] { (0f, 0.6f), (1f, 0f) }),
                FxBlend.AlphaBlended));
            Palette("Rain", new FxRamp(Ramp(new[] { (0f, ink), (1f, ink) }, new[] { (0f, 0.35f), (1f, 0.35f) }),
                FxBlend.AlphaBlended));
            Palette("Snow", new FxRamp(Ramp(new[] { (0f, parchment), (1f, parchment) }, new[] { (0f, 0.9f), (1f, 0f) }),
                FxBlend.AlphaBlended));
            Palette("Arcane", new FxRamp(Ramp(new[] { (0f, rubric), (0.55f, rubricLight), (1f, rubricLight) },
                new[] { (0f, 1f), (0.55f, 1f), (1f, 0f) }), FxBlend.AlphaBlended));
            Palette("InkBlot", new FxRamp(Ramp(new[] { (0f, inkDark), (1f, inkDark) }, new[] { (0f, 0.85f), (1f, 0.85f) }),
                FxBlend.AlphaBlended));
        }

        static void Palette(string name, FxRamp body, FxRamp? core = null, FxRamp? halo = null)
        {
            string path = $"{Palettes}/{name}.asset";
            if (File.Exists(path)) return;
            var palette = ScriptableObject.CreateInstance<FxPalette>();
            palette.Configure(body, core, halo);
            AssetDatabase.CreateAsset(palette, path);
            Debug.Log("FX: palette " + path + " created.");
        }

        static Gradient Ramp((float time, int rgb)[] colors, (float time, float alpha)[] alphas)
        {
            var colorKeys = new GradientColorKey[colors.Length];
            for (int i = 0; i < colors.Length; i++)
            {
                int rgb = colors[i].rgb;
                var color = new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f);
                colorKeys[i] = new GradientColorKey(color, colors[i].time);
            }
            var alphaKeys = new GradientAlphaKey[alphas.Length];
            for (int i = 0; i < alphas.Length; i++) alphaKeys[i] = new GradientAlphaKey(alphas[i].alpha, alphas[i].time);
            var gradient = new Gradient();
            gradient.SetKeys(colorKeys, alphaKeys);
            return gradient;
        }

        static void EnsureSortingLayers()
        {
            var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
            if (assets.Length == 0) return;
            var tags = new SerializedObject(assets[0]);
            var layers = tags.FindProperty("m_SortingLayers");
            bool changed = false;
            foreach (var name in FxLayers.Order)
            {
                if (IndexOf(layers, name) >= 0) continue;
                int index = layers.arraySize;
                layers.InsertArrayElementAtIndex(index);
                var entry = layers.GetArrayElementAtIndex(index);
                entry.FindPropertyRelative("name").stringValue = name;
                entry.FindPropertyRelative("uniqueID").longValue = NewLayerId(layers);
                entry.FindPropertyRelative("locked").boolValue = false;
                changed = true;
            }
            if (changed)
            {
                tags.ApplyModifiedPropertiesWithoutUndo();
                Debug.Log("FX: sorting layers created: " + string.Join(" → ", FxLayers.Order));
            }

            for (int i = 1; i < FxLayers.Order.Length; i++)
                if (IndexOf(layers, FxLayers.Order[i]) < IndexOf(layers, FxLayers.Order[i - 1]))
                    Debug.LogWarning("FX: reorder the sorting layers in Project Settings > Tags and Layers to " +
                                     string.Join(" → ", FxLayers.Order) + ".");
        }

        static int IndexOf(SerializedProperty layers, string name)
        {
            for (int i = 0; i < layers.arraySize; i++)
                if (layers.GetArrayElementAtIndex(i).FindPropertyRelative("name").stringValue == name) return i;
            return -1;
        }

        static long NewLayerId(SerializedProperty layers)
        {
            while (true)
            {
                long id = Random.Range(1, int.MaxValue);
                bool taken = false;
                for (int i = 0; i < layers.arraySize && !taken; i++)
                    taken = layers.GetArrayElementAtIndex(i).FindPropertyRelative("uniqueID").longValue == id;
                if (!taken) return id;
            }
        }

        [MenuItem("Arcane Gambit/FX/Rebuild FX_Root")]
        static void RebuildRoot()
        {
            EnsureSortingLayers();
            var profile = EnsurePostProfile();
            var floatingText = EnsureFloatingText();

            var scene = EditorSceneManager.NewPreviewScene();
            try
            {
                var root = new GameObject("FX_Root");
                SceneManager.MoveGameObjectToScene(root, scene);
                var fx = root.AddComponent<FxRoot>();

                // Nothing in the game uses physics, so freeze frames only need to touch Time.timeScale.
                var time = Child(root, "Time Manager", FxLayers.World).AddComponent<MMTimeManager>();
                time.UpdateFixedDeltaTime = false;
                time.UpdateMaximumDeltaTime = false;

                Child(root, "Sound Manager", FxLayers.World).AddComponent<MMSoundManager>();

                var post = Child(root, "Post Volume", FxLayers.World);
                var volume = post.AddComponent<Volume>();
                volume.isGlobal = true;
                volume.priority = 10f;
                volume.sharedProfile = profile;
                post.AddComponent<MMVignetteShaker_URP>();
                post.AddComponent<MMColorAdjustmentsShaker_URP>();
                post.AddComponent<MMBloomShaker_URP>();

                var flashCanvas = Child(root, "Flash Canvas", FxLayers.Ui).AddComponent<Canvas>();
                flashCanvas.renderMode = RenderMode.ScreenSpaceCamera;
                flashCanvas.planeDistance = CanvasDistance;
                flashCanvas.sortingLayerName = FxLayers.HudFx;
                flashCanvas.sortingOrder = 100;
                var flash = Child(flashCanvas.gameObject, "Flash", FxLayers.Ui);
                var flashRect = flash.AddComponent<RectTransform>();
                flashRect.anchorMin = Vector2.zero;
                flashRect.anchorMax = Vector2.one;
                flashRect.offsetMin = flashRect.offsetMax = Vector2.zero;
                var image = flash.AddComponent<Image>();
                image.color = Candle;
                image.raycastTarget = false;
                var group = flash.AddComponent<CanvasGroup>();
                group.alpha = 0f;
                group.blocksRaycasts = false;
                group.interactable = false;
                flash.AddComponent<MMFlash>();

                var spawner = Child(root, "Floating Text", FxLayers.Ui).AddComponent<MMFloatingTextSpawner>();
                spawner.PooledSimpleMMFloatingText = floatingText;
                spawner.PoolSize = 12;
                spawner.PoolCanExpand = false;
                spawner.UseUnscaledTime = true;
                spawner.Lifetime = new Vector2(1.1f, 1.1f);
                spawner.RemapYOne = new Vector2(0.45f, 0.55f);

                AddDirector(root);
                AddAmbience(root);

                SetRef(fx, "_flashCanvas", flashCanvas);
                SetRef(fx, "_floatingText", spawner);
                PrefabUtility.SaveAsPrefabAsset(root, RootPrefab);
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
            }
            Debug.Log("FX: " + RootPrefab + " built.");
        }

        static VolumeProfile EnsurePostProfile()
        {
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(PostProfile);
            if (profile != null) return profile;
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, PostProfile);

            // Every effect starts at rest; Feel's shakers pulse them and conditions shift them.
            var vignette = profile.Add<Vignette>();
            vignette.intensity.Override(0f);
            vignette.color.Override(InkDeep);
            vignette.smoothness.Override(0.4f);
            var color = profile.Add<ColorAdjustments>();
            color.postExposure.Override(0f);
            color.saturation.Override(0f);
            var bloom = profile.Add<Bloom>();
            bloom.intensity.Override(0f);
            bloom.threshold.Override(0.9f);
            bloom.tint.Override(Candle);

            foreach (var component in profile.components) AssetDatabase.AddObjectToAsset(component, profile);
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            return profile;
        }

        // The game builds its fonts at runtime, so FxRoot.UseFont swaps this placeholder font on the pooled texts.
        static MMFloatingText EnsureFloatingText()
        {
            var existing = AssetDatabase.LoadAssetAtPath<MMFloatingText>(FloatingTextPrefab);
            if (existing != null)
            {
                if (existing.GetComponent<MMPoolableObject>() == null) AddPoolable();
                return existing;
            }

            var scene = EditorSceneManager.NewPreviewScene();
            try
            {
                var root = new GameObject("FloatingText_AG") { layer = FxLayers.Ui };
                SceneManager.MoveGameObjectToScene(root, scene);
                var moving = Child(root, "Moving Part", FxLayers.Ui);
                var billboard = Child(moving, "Billboard", FxLayers.Ui);
                var text = Child(billboard, "Text", FxLayers.Ui).AddComponent<TextMeshPro>();
                text.text = "Arcane Gambit";
                text.fontSize = 2.8f;
                text.color = Ink;
                text.alignment = TextAlignmentOptions.Center;
                text.textWrappingMode = TextWrappingModes.NoWrap;
                text.rectTransform.sizeDelta = new Vector2(8f, 1f);
                var renderer = text.GetComponent<MeshRenderer>();
                renderer.sortingLayerName = FxLayers.HudFx;
                renderer.sortingOrder = 10;

                // The spawner calls into it on every spawn without a null check.
                root.AddComponent<MMPoolableObject>();
                var floating = root.AddComponent<MMFloatingTextMeshPro>();
                floating.MovingPart = moving.transform;
                floating.Billboard = billboard.transform;
                floating.TargetTextMeshPro = text;
                return PrefabUtility.SaveAsPrefabAsset(root, FloatingTextPrefab).GetComponent<MMFloatingText>();
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        static void AddPoolable()
        {
            var root = PrefabUtility.LoadPrefabContents(FloatingTextPrefab);
            try
            {
                root.AddComponent<MMPoolableObject>();
                PrefabUtility.SaveAsPrefabAsset(root, FloatingTextPrefab);
                Debug.Log("FX: MMPoolableObject added to " + FloatingTextPrefab + ".");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        [MenuItem("Arcane Gambit/FX/Create FX_Sandbox")]
        static void CreateSandbox()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (string.IsNullOrEmpty(SceneManager.GetSceneAt(i).path))
                {
                    Debug.LogWarning("FX: save or close the untitled scene, then run Arcane Gambit > FX > Create FX_Sandbox.");
                    return;
                }

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            var cameraObject = new GameObject("Main Camera") { tag = "MainCamera" };
            SceneManager.MoveGameObjectToScene(cameraObject, scene);
            cameraObject.transform.position = new Vector3(0f, 0f, -10f);
            var camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 5f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Table;
            cameraObject.AddComponent<UniversalAdditionalCameraData>();
            cameraObject.AddComponent<AudioListener>();

            // The match bootstraps itself here too (with its own FX_Root) and adds the debug panel.
            EditorSceneManager.SaveScene(scene, Sandbox);
            EditorSceneManager.CloseScene(scene, true);
            Debug.Log("FX: " + Sandbox + " created.");
        }

        static GameObject Child(GameObject parent, string name, int layer)
        {
            var go = new GameObject(name) { layer = layer };
            go.transform.SetParent(parent.transform, false);
            return go;
        }

        static void SetRef(Object target, string field, Object value)
        {
            var so = new SerializedObject(target);
            so.FindProperty(field).objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
