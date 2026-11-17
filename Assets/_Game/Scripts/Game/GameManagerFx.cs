using System;
using System.Collections.Generic;
using System.Threading;
using ArcaneGambit.FX;
using ArcaneGambit.Rules;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ArcaneGambit.Game
{
    /// <summary>
    /// Card FX (FX_SPEC 6). Refresh rebuilds the hand and the action panel from scratch, so a card in motion is a ghost:
    /// a copy on its own layer above the HUD that Refresh never touches, animated by the cue's MMF_Player and then
    /// destroyed. Hand cards themselves only get the short, local feedbacks (hover, invalid play).
    /// </summary>
    public sealed partial class GameManager
    {
        const float GhostDrop = 320f, DrawStagger = 0.12f, InvalidTextLift = 120f;

        sealed class Ghost
        {
            public RectTransform Root;
            public Dictionary<string, GameObject> Named;
        }

        RectTransform _cardFxLayer, _deckPile, _playSlot;
        Sprite _runeCircle;
        readonly List<LogEntry> _playedCards = new List<LogEntry>();
        readonly List<RectTransform> _foresightCards = new List<RectTransform>();
        int _drawn, _heldDraws, _moraleGain;
        bool _foresightFxPlayed, _gameOverFxPlayed;
        TextMeshProUGUI _gameOverTitle;
        readonly int?[] _shownMorale = new int?[2];
        int? _shownDeck, _shownDiscard;

        void BuildCardFxLayer(RectTransform hudRoot)
        {
            // Its own canvas on FX_UI: above the HUD, below modals, and without a raycaster so it never takes a click.
            _cardFxLayer = Ui.Stretch(Ui.Rect(hudRoot, "Card FX", 0, 0, 0, 0));
            var canvas = _cardFxLayer.gameObject.AddComponent<Canvas>();
            canvas.overrideSorting = true;
            canvas.sortingLayerName = FxLayers.HudFx;
            _runeCircle = Resources.Load<Sprite>("RuneCircle");
        }

        void ResetCardFx()
        {
            for (int i = _cardFxLayer.childCount - 1; i >= 0; i--) Destroy(_cardFxLayer.GetChild(i).gameObject);
            _playedCards.Clear();
            _drawn = _heldDraws = _moraleGain = 0;
            _foresightFxPlayed = _gameOverFxPlayed = false;
            _shownMorale[0] = _shownMorale[1] = null;
            _shownDeck = _shownDiscard = null;
        }

        void QueueCardFx(LogEntry e)
        {
            switch (e.Kind)
            {
                case LogKind.CardPlayed when !CardCatalog.Get(e.Card).IsWeather:
                case LogKind.Reacted:
                    _playedCards.Add(e);
                    break;
                case LogKind.Preparation when e.Side == _human:
                    if (e.Flag) _drawn++;
                    _moraleGain += e.Amount;
                    break;
                case LogKind.CardDrawn when e.Side == _human:
                case LogKind.ForesightDrawn when e.Side == _human:
                    _drawn++;
                    break;
            }
        }

        // After Refresh, so draws land on the rebuilt hand and counters start from what was on screen before.
        void FlushCardFx()
        {
            if (FxDirector != null)
            {
                float delay = 0f;
                foreach (var e in _playedCards)
                {
                    RunFx(PlayedCardAsync(e, delay, destroyCancellationToken));
                    delay += DrawStagger;
                }
                // The turn is announced first: cards drawn under the banner stay hidden until it leaves.
                if (_turnBanner != null) _heldDraws += _drawn;
                else PlayDraws(_drawn);
                if (_moraleGain > 0) PlayMoraleGain(_moraleGain);
            }
            _playedCards.Clear();
            _drawn = _moraleGain = 0;
            if (_match.Phase != Phase.Foresight) _foresightFxPlayed = false;
            for (int s = 0; s < 2; s++) _shownMorale[s] = _match.Get((Side)s).Morale;
            var p = _match.Get(_human);
            _shownDeck = p.Deck.Count;
            _shownDiscard = p.Discard.Count;
        }

        void PlayDraws(int count)
        {
            int hand = _handCards.Count;
            for (int i = 0; i < count; i++)
            {
                int index = hand - count + i;
                if (index >= 0) RunFx(DrawAsync(index, i * DrawStagger, destroyCancellationToken));
            }
        }

        // Call before the Refresh that reveals the hand; the draws then play on the rebuilt cards.
        int TakeHeldDraws()
        {
            int held = _heldDraws;
            _heldDraws = 0;
            return held;
        }

        // Entry point for fire-and-forget animations: nothing may escape into Unity's synchronization context.
        static async void RunFx(Awaitable animation)
        {
            try
            {
                await animation;
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        void PlayHandFx(FxCueId cue, int index, string text = null)
        {
            if (FxDirector == null || index < 0 || index >= _handCards.Count) return;
            var hand = _handCards[index];
            var named = new Dictionary<string, GameObject> { [FxLabels.Holder] = hand.Holder.gameObject };
            var seal = Find<Image>(hand.Card, "Seal");
            if (seal != null) named[FxLabels.Seal] = seal.gameObject;
            var position = Center(hand.Card) + Vector3.up * InvalidTextLift * hand.Card.lossyScale.y;
            FxDirector.Play(cue, position, 1f, new FxContext { Named = named, Text = text });
        }

        void PlayInvalidFx(int index)
        {
            var info = CardCatalog.Get(_match.Get(_human).Hand[index]);
            bool poor = _match.Get(_human).Morale < info.MinCost;
            PlayHandFx(FxCueId.CardInvalid, index, Loc.Get(poor ? "fx.invalid.morale" : "fx.invalid.other"));
        }

        async Awaitable PlayedCardAsync(LogEntry e, float delay, CancellationToken ct)
        {
            if (delay > 0f) await Awaitable.WaitForSecondsAsync(delay, ct);
            var info = CardCatalog.Get(e.Card);
            bool mine = e.Side == _human;
            // Where a played card is shown: the action panel's slot, or the middle of the board on a phone.
            var slot = Center(_playSlot);
            bool drop = e.Kind == LogKind.Reacted && !mine;
            var ghost = CreateGhost(info, PlayCardScale,
                drop ? slot + Vector3.up * GhostDrop * _playSlot.lossyScale.y : slot, faceDown: false,
                arcane: info.Kind == CardKind.Arcane);
            var anchors = new List<GameObject>();
            try
            {
                if (drop)
                    await PlayGhost(FxCueId.ReactionReveal, ghost, ct, from: Anchor(ghost.Root.position, anchors),
                        to: Anchor(slot, anchors), text: Loc.Get("fx.reaction"));

                var morale = _armies[(int)e.Side].Morale;
                int before = _shownMorale[(int)e.Side] ?? _match.Get(e.Side).Morale;
                int after = _match.Get(e.Side).Morale;
                await PlayGhost(info.Kind == CardKind.Arcane ? FxCueId.ArcaneCast : FxCueId.CardPlay, ghost, ct,
                    counter: before != after ? morale : null, countFrom: before, countTo: after,
                    format: CountFormat("ui.moraleValue", Match.MaxMorale));

                var equipped = info.Kind == CardKind.Equipment && e.Flag ? _match.At(e.To) : null;
                if (equipped != null && _pieces.TryGetValue(equipped.Id, out var piece))
                {
                    var badge = e.Card == CardId.WarHammer ? piece.Hammer : e.Card == CardId.CampaignHorseshoes ? null : piece.Shield;
                    ghost.Named[FxLabels.Token] = piece.Holder.gameObject;
                    // The badge appears when the card lands on the token, not before.
                    if (badge != null)
                    {
                        ghost.Named[FxLabels.Badge] = badge.gameObject;
                        badge.enabled = false;
                    }
                    try
                    {
                        await PlayGhost(FxCueId.Equip, ghost, ct, from: Anchor(slot, anchors),
                            to: Anchor(Center(piece.Rt), anchors));
                    }
                    finally
                    {
                        if (badge != null) badge.enabled = true;
                    }
                    return;
                }

                var pile = mine ? _discardPile : _armies[(int)e.Side].Root;
                int discarded = _match.Get(_human).Discard.Count;
                await PlayGhost(FxCueId.CardDiscard, ghost, ct, from: Anchor(slot, anchors), to: Anchor(Center(pile), anchors),
                    counter: mine ? _discard : null, countFrom: _shownDiscard ?? discarded, countTo: discarded, format: "0");
            }
            finally
            {
                Destroy(ghost.Root.gameObject);
                foreach (var anchor in anchors) Destroy(anchor);
            }
        }

        async Awaitable DrawAsync(int index, float delay, CancellationToken ct)
        {
            var hand = _handCards[index];
            var group = hand.Card.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            var anchors = new List<GameObject>();
            Ghost ghost = null;
            try
            {
                if (delay > 0f) await Awaitable.WaitForSecondsAsync(delay, ct);
                if (hand.Card == null) return;
                var p = _match.Get(_human);
                var info = CardCatalog.Get(p.Hand[Mathf.Min(index, p.Hand.Count - 1)]);
                ghost = CreateGhost(info, HandScale, Center(_deckPile), faceDown: true, arcane: false);
                await PlayGhost(FxCueId.CardDraw, ghost, ct, from: Anchor(Center(_deckPile), anchors),
                    to: Anchor(Center(hand.Card), anchors), counter: _deck, countFrom: p.Deck.Count + 1,
                    countTo: p.Deck.Count, format: "0");
            }
            finally
            {
                if (ghost != null) Destroy(ghost.Root.gameObject);
                foreach (var anchor in anchors) Destroy(anchor);
                if (group != null) group.alpha = 1f;
            }
        }

        async Awaitable PlayGhost(FxCueId cue, Ghost ghost, CancellationToken ct, Transform from = null, Transform to = null,
            TextMeshProUGUI counter = null, float countFrom = 0f, float countTo = 0f, string format = null,
            string text = null)
        {
            var named = new Dictionary<string, GameObject>(ghost.Named);
            bool counting = counter != null && format != null;
            if (counting)
            {
                named[FxLabels.Counter] = counter.gameObject;
                // Refresh already wrote the new value: show the old one until the count starts.
                counter.text = countFrom.ToString(format);
            }
            await FxDirector.PlayAsync(cue, ghost.Root.position, 1f, ct, new FxContext
            {
                Target = ghost.Root.gameObject,
                Named = named,
                From = from,
                To = to,
                CountFrom = countFrom,
                CountTo = countTo,
                CountFormat = counting ? format : null,
                Text = text,
            });
            // Whatever the player did with it, the counter ends on the true value.
            if (counting && counter != null) counter.text = countTo.ToString(format);
        }

        Ghost CreateGhost(CardInfo info, float scale, Vector3 position, bool faceDown, bool arcane)
        {
            float w = Ui.CardWidth * scale, h = Ui.CardHeight * scale;
            var root = Ui.Rect(_cardFxLayer, "Ghost " + info.Id, 0f, 0f, w, h);
            root.pivot = new Vector2(0.5f, 0.5f);
            root.position = position;
            root.gameObject.AddComponent<CanvasGroup>();
            var holder = Ui.Centered(Ui.Rect(root, "Holder", 0f, 0f, w, h));
            var named = new Dictionary<string, GameObject> { [FxLabels.Holder] = holder.gameObject };

            if (arcane && _runeCircle != null)
            {
                float d = w * 1.8f;
                var circle = Ui.Image(holder, "Circle", _runeCircle, (w - d) * 0.5f, (h - d) * 0.5f, d, d);
                Ui.Centered(circle.rectTransform);
                circle.color = Ui.Rubric;
                circle.type = Image.Type.Filled;
                circle.fillMethod = Image.FillMethod.Radial360;
                circle.fillAmount = 0f;
                named[FxLabels.Circle] = circle.gameObject;
            }
            var outline = Ui.Image(holder, "Outline", Ui.Highlight, -6f, -6f, w + 12f, h + 12f);
            outline.type = Image.Type.Sliced;
            outline.color = new Color(Ui.Rubric.r, Ui.Rubric.g, Ui.Rubric.b, 0f);
            named[FxLabels.Outline] = outline.gameObject;

            var card = Ui.Card(holder, info, scale);
            var seal = Find<Image>(card, "Seal");
            if (seal != null)
            {
                Ui.Centered(seal.rectTransform);
                named[FxLabels.Seal] = seal.gameObject;
            }
            var frame = Find<Image>(card, "Frame");
            if (frame != null) named[FxLabels.Frame] = frame.gameObject;

            var back = Ui.Image(holder, "Back", Ui.Art("CardBack"), 0f, 0f, w, h);
            back.gameObject.SetActive(faceDown);
            named[FxLabels.Back] = back.gameObject;
            return new Ghost { Root = root, Named = named };
        }

        Transform Anchor(Vector3 position, List<GameObject> anchors)
        {
            var anchor = Ui.Rect(_cardFxLayer, "Anchor", 0f, 0f, 0f, 0f);
            anchor.position = position;
            anchors.Add(anchor.gameObject);
            return anchor;
        }

        /// <summary>FX sandbox: plays a card cue as the match would, with sample cards.</summary>
        internal void DebugCardFx(FxCueId cue, Coord square)
        {
            var ct = destroyCancellationToken;
            LogEntry Played(CardId card, Side side, LogKind kind = LogKind.CardPlayed, bool flag = false) =>
                new LogEntry(_match.Turn, kind, side, to: square, card: card, flag: flag);
            switch (cue)
            {
                case FxCueId.CardHover:
                case FxCueId.CardInvalid:
                    PlayHandFx(cue, 0, cue == FxCueId.CardInvalid ? Loc.Get("fx.invalid.morale") : null);
                    break;
                case FxCueId.CardDraw:
                    if (_handCards.Count > 0) RunFx(DrawAsync(_handCards.Count - 1, 0f, ct));
                    break;
                case FxCueId.ReactionReveal:
                    RunFx(PlayedCardAsync(Played(CardId.RaiseShields, _human.Other(), LogKind.Reacted), 0f, ct));
                    break;
                case FxCueId.ArcaneCast:
                    RunFx(PlayedCardAsync(Played(CardId.Fireball, _human), 0f, ct));
                    break;
                case FxCueId.Equip:
                    RunFx(PlayedCardAsync(Played(CardId.ReinforcedShield, _human, flag: _match.At(square) != null), 0f, ct));
                    break;
                default:
                    RunFx(PlayedCardAsync(Played(CardId.CrossbowVolley, _human), 0f, ct));
                    break;
            }
        }

        static Vector3 Center(RectTransform rt) => rt.TransformPoint(rt.rect.center);

        static T Find<T>(Transform root, string name) where T : Component
        {
            foreach (var component in root.GetComponentsInChildren<T>(true))
                if (component.name == name) return component;
            return null;
        }

        // B · Morale gained at the start of the turn: the counter climbs, the text springs, "+I" floats up.
        void PlayMoraleGain(int amount)
        {
            var morale = _armies[(int)_human].Morale;
            int after = _match.Get(_human).Morale;
            int before = _shownMorale[(int)_human] ?? after - amount;
            string format = CountFormat("ui.moraleValue", Match.MaxMorale);
            morale.text = before.ToString(format);
            FxDirector.Play(FxCueId.MoraleGain, Center(morale.rectTransform), 1f, new FxContext
            {
                Target = morale.gameObject,
                Named = new Dictionary<string, GameObject> { [FxLabels.Counter] = morale.gameObject },
                CountFrom = before,
                CountTo = after,
                CountFormat = format,
                Text = "+" + Ui.Roman(amount),
            });
        }

        // D · Seer's Omen turns the cards over; Oracle's Echo brings them out of the discard pile. Once per choice:
        // the modal is rebuilt whenever the selection moves.
        void PlayForesightFx()
        {
            if (_foresightFxPlayed || FxDirector == null) return;
            _foresightFxPlayed = true;
            var cue = _match.ForesightCard == CardId.OracleEcho ? FxCueId.OracleEcho : FxCueId.SeersOmen;
            if (!FxDirector.HasPlayer(cue)) return;
            for (int i = 0; i < _foresightCards.Count; i++)
                RunFx(ForesightCardAsync(cue, _foresightCards[i], i * DrawStagger, destroyCancellationToken));
        }

        async Awaitable ForesightCardAsync(FxCueId cue, RectTransform card, float delay, CancellationToken ct)
        {
            var group = card.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            var anchors = new List<GameObject>();
            try
            {
                if (delay > 0f) await Awaitable.WaitForSecondsAsync(delay, ct);
                if (card == null) return;
                var job = FxDirector.Play(cue, Center(card), 1f, new FxContext
                {
                    Target = card.gameObject,
                    Named = new Dictionary<string, GameObject> { [FxLabels.Holder] = card.gameObject },
                    From = Anchor(Center(_discardPile), anchors),
                    To = Anchor(Center(card), anchors),
                });
                await FxDirector.WaitAsync(job, ct);
            }
            finally
            {
                if (group != null) group.alpha = 1f;
                foreach (var anchor in anchors) Destroy(anchor);
            }
        }

        // F · The turn banner slides in and its title is written out.
        void PlayTurnBannerFx(RectTransform toast, TextMeshProUGUI title)
        {
            if (FxDirector == null) return;
            FxDirector.Play(FxCueId.NewTurn, Center(toast), 1f, new FxContext
            {
                Target = toast.gameObject,
                Named = new Dictionary<string, GameObject> { [FxLabels.Title] = title.gameObject },
            });
        }

        // F · Victory or defeat, once per match. Defeat also drains the board's colour until the next match.
        void PlayGameOverFx()
        {
            if (_gameOverFxPlayed || FxDirector == null || _gameOverTitle == null) return;
            _gameOverFxPlayed = true;
            bool won = _match.Winner == _human;
            if (!won && Ambience != null) Ambience.Set("Defeat", new FxLook(0.4f, 0f, -60f));
            FxDirector.Play(won ? FxCueId.Victory : FxCueId.Defeat, Center(_gameOverTitle.rectTransform), 1f, new FxContext
            {
                Named = new Dictionary<string, GameObject> { [FxLabels.Title] = _gameOverTitle.gameObject },
            });
        }

        /// <summary>A C# numeric format that renders the localized counter text, so Feel's count-up keeps its wording.</summary>
        internal static string CountFormat(string key, params object[] rest)
        {
            var args = new object[rest.Length + 1];
            args[0] = "\u0001";
            rest.CopyTo(args, 1);
            var parts = string.Format(Loc.Get(key), args).Split('\u0001');
            string Quote(string s) => s.Length == 0 ? "" : "'" + s.Replace("'", "\\'") + "'";
            return parts.Length == 2 ? Quote(parts[0]) + "0" + Quote(parts[1]) : "0";
        }
    }
}
