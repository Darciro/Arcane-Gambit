using System.Collections.Generic;
using UnityEngine;

namespace ArcaneGambit.Game
{
    public enum Playlist { Menu, Match, Victory, Defeat }

    public enum Sfx { PageTurn, SealStamp, CardShuffle }

    public sealed class GameAudio : MonoBehaviour
    {
        const string MusicRoot = "Audio/Music/", SfxRoot = "Audio/Sfx/Sfx_";
        const float FadeTime = 2f, CrossfadeTime = 3f, StingerFadeOut = 0.6f;

        enum Mode { Shuffle, LoopOne, Once }

        static GameAudio s_instance;

        readonly AudioSource[] _decks = new AudioSource[2];
        readonly float[] _gain = new float[2];
        readonly Dictionary<Sfx, AudioClip> _sfx = new Dictionary<Sfx, AudioClip>();
        AudioSource _effects;
        readonly List<int> _order = new List<int>();
        readonly System.Random _rng = new System.Random();
        AudioClip[] _tracks = System.Array.Empty<AudioClip>();
        Playlist? _playlist;
        Mode _mode;
        int _deck, _cursor, _last = -1;
        float _fadeIn = FadeTime, _fadeOut = FadeTime;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => s_instance = null;

        static GameAudio Instance
        {
            get
            {
                if (s_instance != null) return s_instance;
                var go = new GameObject("Audio");
                DontDestroyOnLoad(go);
                s_instance = go.AddComponent<GameAudio>();
                return s_instance;
            }
        }

        static Mode ModeOf(Playlist playlist) => playlist switch
        {
            Playlist.Menu => Mode.Shuffle,
            Playlist.Match => Mode.LoopOne,
            _ => Mode.Once,
        };

        void Awake()
        {
            for (int i = 0; i < _decks.Length; i++)
            {
                var deck = gameObject.AddComponent<AudioSource>();
                deck.playOnAwake = false;
                deck.loop = false;
                deck.volume = 0f;
                deck.priority = 0;
                _decks[i] = deck;
            }
            _effects = gameObject.AddComponent<AudioSource>();
            _effects.playOnAwake = false;
        }

        public static void Play(Sfx sfx)
        {
            var audio = Instance;
            float volume = GameSettings.SfxVolume;
            if (volume <= 0f) return;
            if (!audio._sfx.TryGetValue(sfx, out var clip))
            {
                clip = Resources.Load<AudioClip>(SfxRoot + sfx);
                if (clip == null) Debug.LogWarning($"[Arcane Gambit] Missing sound: Resources/{SfxRoot}{sfx}");
                audio._sfx[sfx] = clip;
            }
            if (clip != null) audio._effects.PlayOneShot(clip, volume);
        }

        /// <summary>
        /// Menu shuffles through its tracks, Match loops one random track and picks another on every call,
        /// Victory/Defeat cut the current music and play one track once.
        /// </summary>
        public static void PlayMusic(Playlist playlist, float fadeIn = FadeTime)
        {
            var audio = Instance;
            var mode = ModeOf(playlist);
            if (audio._playlist == playlist && mode != Mode.LoopOne) return;
            if (audio._playlist != playlist)
            {
                audio._tracks = Resources.LoadAll<AudioClip>(MusicRoot + playlist);
                audio._order.Clear();
                audio._cursor = 0;
                audio._last = -1;
            }
            audio._playlist = playlist;
            audio._mode = mode;
            if (mode == Mode.Once) audio.PlayNext(0f, StingerFadeOut);
            else audio.PlayNext(fadeIn, FadeTime);
        }

        void Update()
        {
            var current = _decks[_deck];
            if (_mode == Mode.Shuffle && current.clip != null
                && (!current.isPlaying || current.clip.length - current.time <= CrossfadeTime))
                PlayNext(CrossfadeTime, CrossfadeTime);

            for (int i = 0; i < _decks.Length; i++)
            {
                var deck = _decks[i];
                if (!deck.isPlaying) continue;
                bool active = i == _deck;
                float fade = active ? _fadeIn : _fadeOut;
                _gain[i] = Mathf.MoveTowards(_gain[i], active ? 1f : 0f, Time.unscaledDeltaTime / fade);
                deck.volume = _gain[i] * GameSettings.MusicVolume;
                if (!active && _gain[i] <= 0f) deck.Stop();
            }
        }

        void PlayNext(float fadeIn, float fadeOut)
        {
            if (_tracks.Length == 0) return;
            if (_cursor >= _order.Count) Shuffle();
            _last = _order[_cursor++];
            _fadeIn = fadeIn;
            _fadeOut = fadeOut;
            _deck = 1 - _deck;
            var deck = _decks[_deck];
            deck.clip = _tracks[_last];
            deck.loop = _mode == Mode.LoopOne;
            _gain[_deck] = fadeIn > 0f ? 0f : 1f;
            deck.volume = _gain[_deck] * GameSettings.MusicVolume;
            deck.Play();
        }

        void Shuffle()
        {
            _order.Clear();
            for (int i = 0; i < _tracks.Length; i++) _order.Add(i);
            for (int i = _order.Count - 1; i > 0; i--)
            {
                int j = _rng.Next(i + 1);
                (_order[i], _order[j]) = (_order[j], _order[i]);
            }
            if (_order.Count > 1 && _order[0] == _last) (_order[0], _order[^1]) = (_order[^1], _order[0]);
            _cursor = 0;
        }
    }
}
