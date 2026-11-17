using UnityEditor;
using UnityEngine;

namespace ArcaneGambit.EditorTools
{
    sealed class MusicImporter : AssetPostprocessor
    {
        public override uint GetVersion() => 1;

        // Long tracks: streaming from disk keeps them out of memory, unlike the default decompress-on-load.
        void OnPreprocessAudio()
        {
            if (!assetPath.Contains("/Resources/Audio/Music/")) return;
            var importer = (AudioImporter)assetImporter;
            importer.forceToMono = false;
            var settings = importer.defaultSampleSettings;
            settings.loadType = AudioClipLoadType.Streaming;
            settings.compressionFormat = AudioCompressionFormat.Vorbis;
            settings.quality = 0.7f;
            importer.defaultSampleSettings = settings;
        }
    }
}
