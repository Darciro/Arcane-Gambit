using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ArcaneGambit.EditorTools
{
    /// <summary>
    /// SceneManager.LoadScene only finds scenes listed in the build settings, and the first one listed is where a
    /// build starts: the brand intro must lead, then the menu, then the match. Scenes added later by hand are kept
    /// after them. Unity's splash also gets the brand's graphite, so it hands over to the intro without a flash.
    /// The Playground is listed too, so the Editor can load it, but only development builds take it along.
    /// </summary>
    [InitializeOnLoad]
    static class BuildScenes
    {
        static readonly string[] Required =
        {
            "Assets/Scenes/Boot.unity", "Assets/Scenes/MainMenu.unity", "Assets/Scenes/Main.unity", DevOnlyScene,
        };

        internal const string DevOnlyScene = "Assets/Scenes/Playground.unity";

        static readonly Color SplashBackground = new Color32(0x0D, 0x0E, 0x10, 0xFF);

        static BuildScenes()
        {
            EditorApplication.delayCall += Ensure;
            BuildPlayerWindow.RegisterBuildPlayerHandler(Build);
        }

        static void Build(BuildPlayerOptions options)
        {
            if ((options.options & BuildOptions.Development) == 0)
                options.scenes = System.Array.FindAll(options.scenes, path => path != DevOnlyScene);
            BuildPlayerWindow.DefaultBuildMethods.BuildPlayer(options);
        }

        static void Ensure()
        {
            if (PlayerSettings.SplashScreen.backgroundColor != SplashBackground)
                PlayerSettings.SplashScreen.backgroundColor = SplashBackground;

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

    /// <summary>
    /// A release build that still lists the Playground (built by a script that skipped the handler above) gets an
    /// empty scene: its code is compiled out of such builds anyway.
    /// </summary>
    sealed class StripDevOnlyScene : IProcessSceneWithReport
    {
        public int callbackOrder => 0;

        public void OnProcessScene(Scene scene, BuildReport report)
        {
            if (report == null || scene.path != BuildScenes.DevOnlyScene) return;
            if ((report.summary.options & BuildOptions.Development) != 0) return;
            Debug.LogWarning("[Arcane Gambit] The Playground scene is in a release build; it was emptied.");
            foreach (var root in scene.GetRootGameObjects()) Object.DestroyImmediate(root);
        }
    }
}
