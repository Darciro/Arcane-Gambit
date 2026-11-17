using System.Collections.Generic;
using UnityEngine;

namespace ArcaneGambit.Game
{
    public enum DisplayMode { Fullscreen, Borderless, Windowed }

    public struct GameOptions
    {
        public bool Sfx, VSync;
        public int Master, Effects, Music;
        public string Language;
        public DisplayMode Mode;
        public int Width, Height;

        public bool SameScreen(in GameOptions other) =>
            Mode == other.Mode && Width == other.Width && Height == other.Height;

        public bool Same(in GameOptions other) =>
            Sfx == other.Sfx && VSync == other.VSync && Master == other.Master && Effects == other.Effects
            && Music == other.Music && Language == other.Language && SameScreen(other);
    }

    /// <summary>The player's options: saved in PlayerPrefs, applied when the game opens and from the Options modal.</summary>
    public static class GameSettings
    {
        public const int VolumeStep = 5, VolumeMax = 100;
        const string Prefix = "options.";
        const int MinWidth = 1024, MinHeight = 576;

        static GameOptions s_live;

        public static GameOptions Current { get; private set; }

        public static float MusicVolume => s_live.Music / (float)VolumeMax;
        public static float SfxVolume => s_live.Sfx ? s_live.Effects / (float)VolumeMax : 0f;

        /// <summary>A browser owns the canvas size and only goes full screen on its own terms; a phone has one screen.</summary>
        public static bool ScreenAdjustable => Application.platform != RuntimePlatform.WebGLPlayer && !UiProfile.Mobile;

        public static Vector2Int Desktop => new Vector2Int(Display.main.systemWidth, Display.main.systemHeight);

        public static GameOptions Defaults => new GameOptions
        {
            Sfx = true,
            VSync = true,
            Master = 80,
            Effects = 70,
            Music = 50,
            Language = Loc.DefaultLanguage,
            Mode = DisplayMode.Fullscreen,
            Width = Desktop.x,
            Height = Desktop.y,
        };

        // Loc resets its language on SubsystemRegistration, so this has to run after it.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Load()
        {
            var d = Defaults;
            var o = new GameOptions
            {
                Sfx = PlayerPrefs.GetInt(Prefix + "sfx", 1) == 1,
                VSync = PlayerPrefs.GetInt(Prefix + "vsync", 1) == 1,
                Master = Volume("master", d.Master),
                Effects = Volume("effects", d.Effects),
                Music = Volume("music", d.Music),
                Language = PlayerPrefs.GetString(Prefix + "language", d.Language),
                Mode = (DisplayMode)Mathf.Clamp(PlayerPrefs.GetInt(Prefix + "mode", 0), 0, 2),
            };
            if (!Loc.Has(o.Language)) o.Language = Loc.DefaultLanguage;
            Current = o;
            SyncScreen();
            Loc.Language = o.Language;
            QualitySettings.vSyncCount = o.VSync ? 1 : 0;
            Preview(o);
        }

        static int Volume(string key, int fallback) =>
            Mathf.Clamp(PlayerPrefs.GetInt(Prefix + key, fallback), 0, VolumeMax);

        /// <summary>
        /// The player remembers the window by itself, and it can change outside the game's options (Alt+Enter),
        /// so the screen is read back instead of trusted from the saved values.
        /// </summary>
        public static void SyncScreen()
        {
            var o = Current;
            if (!Screen.fullScreen) o.Mode = DisplayMode.Windowed;
            else if (o.Mode == DisplayMode.Windowed) o.Mode = DisplayMode.Fullscreen;
            o.Width = Screen.width;
            o.Height = Screen.height;
            Current = o;
        }

        /// <summary>Lets the volumes be heard while they are still being chosen.</summary>
        public static void Preview(in GameOptions options)
        {
            s_live = options;
            AudioListener.volume = options.Master / (float)VolumeMax;
        }

        public static void Apply(in GameOptions options)
        {
            var before = Current;
            Current = options;
            Preview(options);
            Loc.Language = options.Language;
            QualitySettings.vSyncCount = options.VSync ? 1 : 0;
            if (ScreenAdjustable && !options.SameScreen(before))
                Screen.SetResolution(options.Width, options.Height, ScreenMode(options.Mode));

            PlayerPrefs.SetInt(Prefix + "sfx", options.Sfx ? 1 : 0);
            PlayerPrefs.SetInt(Prefix + "vsync", options.VSync ? 1 : 0);
            PlayerPrefs.SetInt(Prefix + "master", options.Master);
            PlayerPrefs.SetInt(Prefix + "effects", options.Effects);
            PlayerPrefs.SetInt(Prefix + "music", options.Music);
            PlayerPrefs.SetString(Prefix + "language", options.Language);
            PlayerPrefs.SetInt(Prefix + "mode", (int)options.Mode);
            PlayerPrefs.Save();
        }

        // Exclusive full screen only exists on Windows.
        static FullScreenMode ScreenMode(DisplayMode mode) => mode switch
        {
            DisplayMode.Windowed => FullScreenMode.Windowed,
            DisplayMode.Fullscreen when Application.platform == RuntimePlatform.WindowsPlayer =>
                FullScreenMode.ExclusiveFullScreen,
            _ => FullScreenMode.FullScreenWindow,
        };

        /// <summary>Largest first; <paramref name="current"/> is always listed, even when the monitor does not report it.</summary>
        public static List<Vector2Int> Resolutions(Vector2Int current)
        {
            var list = new List<Vector2Int> { current };
            foreach (var r in Screen.resolutions)
            {
                var size = new Vector2Int(r.width, r.height);
                if (r.width >= MinWidth && r.height >= MinHeight && !list.Contains(size)) list.Add(size);
            }
            list.Sort((a, b) => a.x != b.x ? b.x.CompareTo(a.x) : b.y.CompareTo(a.y));
            return list;
        }
    }
}
