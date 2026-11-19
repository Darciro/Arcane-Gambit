using UnityEditor;

namespace ArcaneGambit.EditorTools
{
    /// <summary>
    /// Ui.Art loads one sprite per PNG, so art in Resources/Art must never be auto-sliced:
    /// a "Multiple" import crops the transparent margins and returns only the first slice.
    /// </summary>
    sealed class ArtImporter : AssetPostprocessor
    {
        public override uint GetVersion() => 2;

        void OnPreprocessTexture()
        {
            // BoardWeather scrolls these through RawImage.uvRect, so they tile and keep mipmaps for small boards.
            if (assetPath.Contains("/Resources/Weather/"))
            {
                var weather = (TextureImporter)assetImporter;
                weather.textureType = TextureImporterType.Default;
                weather.alphaIsTransparency = true;
                weather.mipmapEnabled = true;
                weather.npotScale = TextureImporterNPOTScale.None;
                weather.wrapMode = UnityEngine.TextureWrapMode.Repeat;
                weather.maxTextureSize = 4096;
                return;
            }
            if (!assetPath.Contains("/Resources/Art/")) return;
            var importer = (TextureImporter)assetImporter;
            // Cursor.SetCursor needs a readable, uncompressed texture without mipmaps.
            if (assetPath.Contains("/Resources/Art/Cursors/"))
            {
                importer.textureType = TextureImporterType.Cursor;
                importer.isReadable = true;
                importer.mipmapEnabled = false;
                importer.alphaIsTransparency = true;
                importer.npotScale = TextureImporterNPOTScale.None;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                return;
            }
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
        }
    }
}
