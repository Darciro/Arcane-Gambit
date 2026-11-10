using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Rendering.Universal.ShaderGUI;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ArcaneGambit.FX.EditorTools
{
    public enum FxTarget { Board, Hud }

    /// <summary>
    /// Turns an Epic Toon FX prefab into a recolored Prefab Variant under FX/ETFX_AG: white Start Color, the palette's
    /// Color over Lifetime, unlit URP particle materials without Soft Particles (the 2D renderer has no depth texture),
    /// our sorting layer and a size that fits the board. The package's own prefabs and materials are never touched.
    /// </summary>
    static class EtfxVariantTool
    {
        public const string EtfxRoot = "Assets/Epic Toon FX/";
        const string VariantFolder = "Assets/_Game/FX/ETFX_AG";
        const string MaterialFolder = "Assets/_Game/FX/Materials";
        const string PaletteFolder = "Assets/_Game/FX/Palettes";
        const string ChoicesFile = "Docs/FX/FX_ESCOLHAS.md";
        const string ParticleShader = "Universal Render Pipeline/Particles/Unlit";
        const float UrpBlendAlpha = 0f, UrpBlendAdditive = 2f;

        public struct Request
        {
            public GameObject Source;
            public FxPalette Palette;
            public FxTarget Target;
            public float Scale;
            public float Alpha;
            public string Name;
        }

        [MenuItem("Arcane Gambit/FX/Create _AG Variants from FX_ESCOLHAS")]
        static void CreateFromChoices()
        {
            if (!File.Exists(ChoicesFile))
            {
                Debug.LogError("FX: " + ChoicesFile + " not found.");
                return;
            }
            int made = 0, failed = 0;
            foreach (var request in ReadChoices(File.ReadAllLines(ChoicesFile)))
                if (CreateVariant(request) != null) made++;
                else failed++;
            Debug.Log($"FX: {made} variant(s) created from {ChoicesFile}" + (failed > 0 ? $", {failed} failed." : "."));
            FxPlayerBuilder.LinkVariants(true);
        }

        /// <summary>Rows whose "Escolhido" column is filled; the other columns fall back to safe defaults.</summary>
        static IEnumerable<Request> ReadChoices(string[] lines)
        {
            Dictionary<string, int> columns = null;
            foreach (var raw in lines)
            {
                var line = raw.Trim();
                if (!line.StartsWith("|")) { columns = null; continue; }
                var cells = line.Trim('|').Split('|');
                for (int i = 0; i < cells.Length; i++) cells[i] = cells[i].Trim().Trim('`').Trim();
                if (columns == null)
                {
                    columns = new Dictionary<string, int>();
                    for (int i = 0; i < cells.Length; i++) columns[cells[i]] = i;
                    continue;
                }
                if (cells.Length > 0 && cells[0].StartsWith("---")) continue;

                string Cell(string name) => columns.TryGetValue(name, out int i) && i < cells.Length ? cells[i] : "";
                string chosen = Cell("Escolhido");
                if (string.IsNullOrEmpty(chosen)) continue;

                string path = chosen.StartsWith("Assets/") ? chosen : EtfxRoot + chosen;
                if (!path.EndsWith(".prefab")) path += ".prefab";
                var source = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                var palette = AssetDatabase.LoadAssetAtPath<FxPalette>($"{PaletteFolder}/{Cell("Paleta")}.asset");
                if (source == null || palette == null)
                {
                    Debug.LogError($"FX: row \"{Cell("Variante")}\" skipped: " +
                                   (source == null ? $"prefab {path} not found" : $"palette \"{Cell("Paleta")}\" not found") + ".");
                    continue;
                }
                yield return new Request
                {
                    Source = source,
                    Palette = palette,
                    Target = Cell("Camada").StartsWith("Interface") ? FxTarget.Hud : FxTarget.Board,
                    Scale = Number(Cell("Escala"), 1f),
                    Alpha = Number(Cell("Alfa"), 1f),
                    Name = string.IsNullOrEmpty(Cell("Variante")) ? source.name + "_AG" : Cell("Variante"),
                };
            }
        }

        // The choices file is written in Portuguese, where the decimal separator is a comma.
        static float Number(string text, float fallback) =>
            float.TryParse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out float value)
                ? value : fallback;

        public static GameObject CreateVariant(Request request)
        {
            var source = request.Source;
            if (source == null || !PrefabUtility.IsPartOfPrefabAsset(source) || request.Palette == null)
            {
                Debug.LogError("FX: pick an Epic Toon FX prefab asset and an FxPalette.");
                return null;
            }
            string name = request.Name.EndsWith("_AG") ? request.Name : request.Name + "_AG";
            string assetPath = AssetDatabase.GetAssetPath(source);
            var log = new StringBuilder($"FX: {name} ← {assetPath} · palette {request.Palette.name} · " +
                                        $"{request.Target} · scale {request.Scale:0.###} · alpha {request.Alpha:0.##}\n");
            if (!assetPath.StartsWith(EtfxRoot)) log.AppendLine("  ! source is not under " + EtfxRoot);

            var scene = EditorSceneManager.NewPreviewScene();
            try
            {
                var root = (GameObject)PrefabUtility.InstantiatePrefab(source, scene);
                root.transform.localPosition = Vector3.zero;
                root.transform.localScale = source.transform.localScale * request.Scale;

                string sortingLayer = request.Target == FxTarget.Hud ? FxLayers.HudFx : FxLayers.BoardFx;
                int layer = request.Target == FxTarget.Hud ? FxLayers.Ui : FxLayers.World;
                foreach (var t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;

                foreach (var ps in root.GetComponentsInChildren<ParticleSystem>(true))
                    Recolor(ps, request, sortingLayer, log);
                Silence(root, log);

                foreach (var component in root.GetComponentsInChildren<Component>(true))
                    if (component != null) PrefabUtility.RecordPrefabInstancePropertyModifications(component);
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    PrefabUtility.RecordPrefabInstancePropertyModifications(t.gameObject);

                var variant = PrefabUtility.SaveAsPrefabAsset(root, $"{VariantFolder}/{name}.prefab");
                Debug.Log(log.ToString(), variant);
                return variant;
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        static void Recolor(ParticleSystem ps, Request request, string sortingLayer, StringBuilder log)
        {
            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            var role = RoleOf(ps, renderer);
            var ramp = request.Palette.For(role);
            var changes = new List<string> { role.ToString().ToLowerInvariant() };

            var main = ps.main;
            if (main.startColor.mode != ParticleSystemGradientMode.Color || main.startColor.color != Color.white)
                changes.Add($"start color {main.startColor.mode} → white");
            main.startColor = Color.white;
            if (main.scalingMode != ParticleSystemScalingMode.Hierarchy)
            {
                changes.Add($"scaling {main.scalingMode} → Hierarchy");
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            }
            // The root's scale reaches positions and sizes, but not world-space velocities.
            if (main.simulationSpace == ParticleSystemSimulationSpace.World && !Mathf.Approximately(request.Scale, 1f))
            {
                main.startSpeedMultiplier *= request.Scale;
                main.gravityModifierMultiplier *= request.Scale;
                var velocity = ps.velocityOverLifetime;
                if (velocity.enabled)
                {
                    velocity.xMultiplier *= request.Scale;
                    velocity.yMultiplier *= request.Scale;
                    velocity.zMultiplier *= request.Scale;
                }
                changes.Add("world-space speeds scaled");
            }

            var colorOverLifetime = ps.colorOverLifetime;
            colorOverLifetime.enabled = true;
            colorOverLifetime.color = new ParticleSystem.MinMaxGradient(Faded(ramp.Colors, request.Alpha));
            var colorBySpeed = ps.colorBySpeed;
            if (colorBySpeed.enabled)
            {
                colorBySpeed.enabled = false;
                changes.Add("color by speed off");
            }
            var trails = ps.trails;
            if (trails.enabled)
            {
                trails.inheritParticleColor = true;
                trails.colorOverLifetime = Color.white;
                trails.colorOverTrail = Color.white;
                changes.Add("trail colors → particle");
            }
            var lights = ps.lights;
            if (lights.enabled)
            {
                lights.enabled = false;
                changes.Add("lights module off");
            }

            if (renderer != null)
            {
                renderer.sharedMaterial = AgMaterial(renderer.sharedMaterial, ramp.Blend, changes);
                if (renderer.trailMaterial != null) renderer.trailMaterial = AgMaterial(renderer.trailMaterial, ramp.Blend, changes);
                renderer.sortingLayerName = sortingLayer;
            }
            log.AppendLine($"  {ps.name}: {string.Join(", ", changes)}");
        }

        static FxRole RoleOf(ParticleSystem ps, ParticleSystemRenderer renderer)
        {
            string name = ps.name.ToLowerInvariant();
            if (name.Contains("glow") || name.Contains("halo") || name.Contains("flare")) return FxRole.Halo;
            var material = renderer != null ? renderer.sharedMaterial : null;
            if (material == null) return FxRole.Body;
            bool additive = material.HasProperty("_Blend") && Mathf.Approximately(material.GetFloat("_Blend"), UrpBlendAdditive);
            return additive || material.name.ToLowerInvariant().Contains("add") ? FxRole.Core : FxRole.Body;
        }

        static Gradient Faded(Gradient source, float alpha)
        {
            var gradient = new Gradient { mode = source.mode };
            var alphas = source.alphaKeys;
            for (int i = 0; i < alphas.Length; i++) alphas[i].alpha *= alpha;
            gradient.SetKeys(source.colorKeys, alphas);
            return gradient;
        }

        static Material AgMaterial(Material source, FxBlend blend, List<string> changes)
        {
            if (source == null) return null;
            if (source.name.EndsWith("_AG")) return source;
            string path = $"{MaterialFolder}/{source.name}_{(blend == FxBlend.Additive ? "Add" : "Alpha")}_AG.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;

            material = new Material(source);
            var shader = Shader.Find(ParticleShader);
            if (material.shader != shader)
            {
                var texture = source.mainTexture;
                material.shader = shader;
                if (texture != null) material.SetTexture("_BaseMap", texture);
                changes.Add($"material {source.name}: shader {source.shader.name} → {ParticleShader}");
            }
            material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", blend == FxBlend.Additive ? UrpBlendAdditive : UrpBlendAlpha);
            material.SetFloat("_SoftParticlesEnabled", 0f);
            BaseShaderGUI.SetMaterialKeywords(material, null, ParticleGUI.SetMaterialKeywords);
            AssetDatabase.CreateAsset(material, path);
            changes.Add("material " + Path.GetFileName(path));
            return material;
        }

        // Sound arrives in a later stage, 3D lights do nothing under the 2D renderer, and our code moves projectiles.
        static void Silence(GameObject root, StringBuilder log)
        {
            foreach (var audio in root.GetComponentsInChildren<AudioSource>(true))
            {
                audio.enabled = false;
                log.AppendLine($"  {audio.name}: AudioSource off");
            }
            foreach (var light in root.GetComponentsInChildren<Light>(true))
            {
                light.enabled = false;
                log.AppendLine($"  {light.name}: Light off");
            }
            foreach (var behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (behaviour == null) continue;
                string type = behaviour.GetType().Name;
                if (type != "ETFXLightFade" && type != "ETFXPitchRandomizer" && type != "ETFXProjectileScript") continue;
                behaviour.enabled = false;
                log.AppendLine($"  {behaviour.name}: {type} off");
            }
            foreach (var body in root.GetComponentsInChildren<Rigidbody>(true))
            {
                body.isKinematic = true;
                log.AppendLine($"  {body.name}: Rigidbody kinematic");
            }
            foreach (var collider in root.GetComponentsInChildren<Collider>(true))
            {
                collider.enabled = false;
                log.AppendLine($"  {collider.name}: Collider off");
            }
            foreach (var mesh in root.GetComponentsInChildren<MeshRenderer>(true))
                log.AppendLine($"  ! {mesh.name}: MeshRenderer keeps its original colors");
        }

        sealed class Window : EditorWindow
        {
            GameObject _source;
            FxPalette _palette;
            FxTarget _target = FxTarget.Board;
            float _scale = 0.25f, _alpha = 1f;
            string _name = "";

            [MenuItem("Arcane Gambit/FX/Create _AG Variant…")]
            static void Open()
            {
                var window = GetWindow<Window>(true, "Create _AG Variant");
                window.minSize = new Vector2(360f, 200f);
                if (Selection.activeObject is GameObject selected && PrefabUtility.IsPartOfPrefabAsset(selected))
                {
                    window._source = selected;
                    window._name = selected.name + "_AG";
                }
            }

            void OnGUI()
            {
                EditorGUI.BeginChangeCheck();
                _source = (GameObject)EditorGUILayout.ObjectField("ETFX prefab", _source, typeof(GameObject), false);
                if (EditorGUI.EndChangeCheck() && _source != null) _name = _source.name + "_AG";
                _palette = (FxPalette)EditorGUILayout.ObjectField("Palette", _palette, typeof(FxPalette), false);
                _target = (FxTarget)EditorGUILayout.EnumPopup(new GUIContent("Layer", "Board: FX_Board. Hud: FX_UI."), _target);
                _scale = EditorGUILayout.FloatField(new GUIContent("Scale", "An impact should fit one square: about 0.25."), _scale);
                _alpha = EditorGUILayout.Slider("Alpha", _alpha, 0f, 1f);
                _name = EditorGUILayout.TextField("Variant name", _name);
                using (new EditorGUI.DisabledScope(_source == null || _palette == null || string.IsNullOrEmpty(_name)))
                    if (GUILayout.Button("Create"))
                    {
                        var variant = CreateVariant(new Request
                        {
                            Source = _source, Palette = _palette, Target = _target, Scale = _scale, Alpha = _alpha, Name = _name,
                        });
                        if (variant != null) EditorGUIUtility.PingObject(variant);
                    }
            }
        }
    }
}
