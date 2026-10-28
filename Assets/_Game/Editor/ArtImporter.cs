using UnityEditor;

namespace ArcaneGambit.EditorTools
{
    /// <summary>
    /// Ui.Art loads one sprite per PNG, so art in Resources/Art must never be auto-sliced:
    /// a "Multiple" import crops the transparent margins and returns only the first slice.
    /// </summary>
    sealed class ArtImporter : AssetPostprocessor
    {
        public override uint GetVersion() => 1;

        void OnPreprocessTexture()
        {
            if (!assetPath.Contains("/Resources/Art/")) return;
            var importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
        }
    }
}
