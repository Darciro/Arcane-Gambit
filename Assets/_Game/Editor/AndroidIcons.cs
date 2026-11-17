using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace ArcaneGambit.EditorTools
{
    /// <summary>
    /// Fills the Android adaptive icon slots from Assets/_Game/AppIcon/Android ({background|foreground}_{size}.png).
    /// The Adaptive kind is looked up by name because UnityEditor.Android is only referenced while Android is the
    /// active build target.
    /// </summary>
    [InitializeOnLoad]
    static class AndroidIcons
    {
        public const string Folder = "Assets/_Game/AppIcon/Android/";

        static AndroidIcons() => EditorApplication.delayCall += Ensure;

        static void Ensure()
        {
            var target = NamedBuildTarget.Android;
            var kind = PlayerSettings.GetSupportedIconKinds(target)
                .FirstOrDefault(k => k.ToString().Contains("Adaptive"));
            if (kind == null) return;

            var icons = PlayerSettings.GetPlatformIcons(target, kind);
            bool changed = false;
            foreach (var icon in icons)
            {
                var background = AssetDatabase.LoadAssetAtPath<Texture2D>($"{Folder}background_{icon.width}.png");
                var foreground = AssetDatabase.LoadAssetAtPath<Texture2D>($"{Folder}foreground_{icon.width}.png");
                if (background == null || foreground == null) continue;
                if (icon.GetTexture(0) == background && icon.GetTexture(1) == foreground) continue;
                icon.SetTextures(background, foreground);
                changed = true;
            }
            if (!changed) return;

            PlayerSettings.SetPlatformIcons(target, kind, icons);
            AssetDatabase.SaveAssets();
        }

        class Importer : AssetPostprocessor
        {
            void OnPreprocessTexture()
            {
                if (!assetPath.StartsWith(Folder)) return;
                var importer = (TextureImporter)assetImporter;
                importer.textureType = TextureImporterType.Default;
                importer.npotScale = TextureImporterNPOTScale.None;
                importer.mipmapEnabled = false;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.alphaIsTransparency = true;
            }
        }
    }
}
