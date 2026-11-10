using System.Collections.Generic;
using System.IO;
using UnityEditor;

namespace ArcaneGambit.EditorTools
{
    /// <summary>
    /// SceneManager.LoadScene only finds scenes listed in the build settings, and the first one listed is where a
    /// build starts: the menu must lead, the match must follow. Scenes added later by hand are kept after them.
    /// </summary>
    [InitializeOnLoad]
    static class BuildScenes
    {
        static readonly string[] Required = { "Assets/Scenes/MainMenu.unity", "Assets/Scenes/Main.unity" };

        static BuildScenes() => EditorApplication.delayCall += Ensure;

        static void Ensure()
        {
            var current = EditorBuildSettings.scenes;
            var scenes = new List<EditorBuildSettingsScene>();
            foreach (var path in Required)
                if (File.Exists(path)) scenes.Add(new EditorBuildSettingsScene(path, true));
            foreach (var scene in current)
                if (System.Array.IndexOf(Required, scene.path) < 0) scenes.Add(scene);

            bool same = scenes.Count == current.Length;
            for (int i = 0; same && i < scenes.Count; i++)
                same = scenes[i].path == current[i].path && scenes[i].enabled == current[i].enabled;
            if (!same) EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
