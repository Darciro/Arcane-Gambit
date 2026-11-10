using System;
using System.Collections.Generic;
using ArcaneGambit.FX;
using ArcaneGambit.Rules;
using UnityEngine;

namespace ArcaneGambit.Game
{
    /// <summary>
    /// Turns what the rules report (events and log entries) into one-shot FX cues on the board: the units' moves,
    /// captures and blocks, the cards' board effects, the weather entrances. The rules never call effects; cues without
    /// a player simply play nothing. Card movements live in GameManagerFx; what spans several plays (Lightning Storm,
    /// Fireball, crossbow volleys, markers, the match's opening and end) in GameManagerBoardFx.
    /// </summary>
    sealed class MatchFx
    {
        // Mud makes every landing heavier (FX_SPEC 8 E): the Move player's squash follows the intensity.
        const float MudIntensity = 1.25f;

        readonly FxDirector _director;
        readonly GameManager _game;
        Coord? _blocked;

        public MatchFx(FxDirector director, GameManager game)
        {
            _director = director;
            _game = game;
        }

        public bool Locked => _director != null && _director.Locked;

        /// <summary>Squares whose capture or block a rehearsal (lightning, fireball, volley) presents with its own effect.</summary>
        public Func<Coord, bool> PresentedElsewhere = _ => false;

        /// <summary>After the board has applied the event, so new and moved units already have their views.</summary>
        public void OnEvent(MatchEvent e, Match match)
        {
            switch (e.Kind)
            {
                case EventKind.TurnStarted:
                    if (_director != null) _director.NewTurn();
                    break;
                case EventKind.Moved:
                case EventKind.Retreated:
                case EventKind.Intercepted:
                    var unit = match.FindUnit(e.UnitId);
                    Play(unit != null && unit.Jumped ? FxCueId.CavalryJump : FxCueId.Move, e.To,
                        _game.PieceObject(e.UnitId), match.HasCondition(CardId.Mud) ? MudIntensity : 1f);
                    break;
                case EventKind.Recruited:
                    var recruit = match.FindUnit(e.UnitId);
                    if (recruit != null) PlayRecruit(recruit, match);
                    break;
                case EventKind.Blocked:
                    _blocked = PresentedElsewhere(e.To) ? (Coord?)null : e.To;
                    break;
            }
        }

        public void OnLog(LogEntry e, Match match)
        {
            switch (e.Kind)
            {
                case LogKind.CardPlayed:
                    PlayCard(e);
                    break;
                case LogKind.UnitCaptured when !PresentedElsewhere(e.To):
                    // Ranged and arcane captures leave the attacker where it was: then nothing strikes on the square.
                    Play(FxCueId.Capture, e.To, _game.PieceAt(e.To));
                    break;
                case LogKind.Blocked when _blocked != null:
                    PlayBlocked(e, _blocked.Value);
                    _blocked = null;
                    break;
                case LogKind.WeatherCleared:
                    Play(FxCueId.ConditionRemoved);
                    break;
            }
        }

        /// <summary>A new condition's entrance waits for the player to read its notice.</summary>
        public void OnWeatherAcknowledged(CardId card)
        {
            switch (card)
            {
                case CardId.Blizzard: Play(FxCueId.Blizzard); break;
                case CardId.Gale: Play(FxCueId.Gale); break;
                case CardId.Fog: Play(FxCueId.Fog); break;
                case CardId.Mud: Play(FxCueId.Mud); break;
                case CardId.MoonlessNight: Play(FxCueId.MoonlessNight); break;
            }
        }

        public FxJob Play(FxCueId cue, Coord square, GameObject target, float intensity = 1f, string text = null,
            Dictionary<string, GameObject> named = null) =>
            Play(cue, _game.SquareWorld(square), target, intensity, text, named);

        public FxJob Play(FxCueId cue) => Play(cue, Vector3.zero, null, 1f, null, null);

        FxJob Play(FxCueId cue, Vector3 position, GameObject target, float intensity, string text,
            Dictionary<string, GameObject> named)
        {
            if (_director == null) return null;
            return _director.Play(cue, position, intensity,
                new FxContext { Target = target, Board = _game.BoardObject, Named = named, Text = text });
        }

        void PlayRecruit(Unit unit, Match match)
        {
            var counter = _game.ReserveCounter(unit.Side, unit.Type);
            int left = match.Get(unit.Side).Reserve[unit.Type];
            var context = new FxContext
            {
                Target = _game.PieceObject(unit.Id),
                Board = _game.BoardObject,
                Named = counter != null ? new Dictionary<string, GameObject> { [FxLabels.Counter] = counter.gameObject } : null,
                CountFrom = left + 1,
                CountTo = left,
                CountFormat = counter != null ? GameManager.CountFormat("ui.reserveCount") : null,
            };
            if (_director != null) _director.Play(FxCueId.Recruit, _game.SquareWorld(unit.Pos), 1f, context);
        }

        // A shield reaction or worn protection stops the capture. Vigil Seal stamps its own seal; Smoke Screen's effect
        // is the smoke its marker brings.
        public void PlayBlocked(LogEntry e, Coord square)
        {
            if (e.Card == CardId.SmokeScreen && !e.Flag) return;
            if (e.Card == CardId.VigilSeal && !e.Flag)
            {
                Play(FxCueId.VigilSeal, square, _game.PieceAt(square));
                return;
            }
            string text = e.Flag ? Loc.Get("fx.blockedGear") : Loc.Format("fx.blocked", Loc.CardName(e.Card));
            Play(FxCueId.CaptureBlocked, square, _game.PieceAt(square), text: text);
        }

        void PlayCard(LogEntry e)
        {
            switch (e.Card)
            {
                case CardId.DawnPrayer:
                    Play(FxCueId.DawnPrayer);
                    break;
                case CardId.ForcedMarch:
                case CardId.CavalryFeint:
                case CardId.CoordinatedAdvance:
                case CardId.LineSwap:
                    if (e.Flag) Play(FxCueId.QuickMove, e.To, _game.PieceAt(e.To));
                    break;
            }
        }
    }
}
