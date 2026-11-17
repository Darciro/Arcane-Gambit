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
    /// Board FX that span several plays, driven here from the log: the rehearsals (FX_SPEC 7, Lightning Storm and
    /// Fireball), crossbow volleys, the weather's lasting look, markers rising, and the armies mustering. A unit a
    /// projectile or a bolt captures is held on the board (its view is not faded yet) until the effect reaches the
    /// moment of impact, then released to the usual capture animation.
    /// </summary>
    public sealed partial class GameManager
    {
        const float StrikeImpact = 0.5f, FireballImpact = 0.15f, ShieldLead = 0.2f, ShotDuration = 0.55f;
        const float ThunderMin = 8f, ThunderMax = 14f;
        const int VolleyBolts = 3;
        const float VolleyStagger = 0.08f, MusterRankDelay = 0.08f, MusterFileDelay = 0.015f;
        static readonly FxLook StormLook = new FxLook(0.2f, -0.25f);

        // FX_SPEC 8 E. Combined by FxAmbience: the strongest condition wins on each channel.
        static FxLook? WeatherLook(CardId card) => card switch
        {
            CardId.LightningStorm => StormLook,
            CardId.Blizzard => new FxLook(0.2f, 0f, -30f),
            CardId.Fog => new FxLook(0.3f),
            CardId.MoonlessNight => new FxLook(0.45f, -0.8f),
            _ => null,
        };

        static bool Projectile(CardId card) => card == CardId.Fireball || card == CardId.CrossbowVolley || card == CardId.SiegeOrder;

        sealed class FireballFx
        {
            public Coord Ally, Target;
            public FxJob Cast;
            public Image Runes, Ring;
        }

        RectTransform _board, _boardFxUnder, _boardFxOver;
        Sprite _markRing;
        readonly HashSet<Coord> _claims = new HashSet<Coord>();
        readonly HashSet<Coord> _projectileTargets = new HashSet<Coord>();
        readonly HashSet<Coord> _presented = new HashSet<Coord>();
        readonly Dictionary<Coord, Piece> _dying = new Dictionary<Coord, Piece>();
        FxJob _stormMark;
        CancellationTokenSource _thunder;
        FireballFx _fireball;
        (Coord from, Coord target)? _volley;

        FxAmbience Ambience => _fx != null ? _fx.Ambience : null;

        void ResetBoardFx()
        {
            StopThunder();
            StopStormMark();
            if (_fireball != null) EndFireball(_fireball);
            _fireball = null;
            _volley = null;
            foreach (var piece in _dying.Values) Destroy(piece.Rt.gameObject);
            _dying.Clear();
            _claims.Clear();
            _projectileTargets.Clear();
            _presented.Clear();
            if (Ambience != null) Ambience.ClearAll();
        }

        /// <summary>Before the events of a sync: which captures an effect will present, from the log about to be read.</summary>
        void ClaimBoardCaptures()
        {
            _claims.Clear();
            _projectileTargets.Clear();
            if (FxDirector == null || _awaitingStart) return;
            var log = _match.Log;
            Coord? projectile = _fireball?.Target ?? _volley?.target;
            if (projectile != null) _projectileTargets.Add(projectile.Value);
            for (int i = _logSeen; i < log.Count; i++)
            {
                var e = log[i];
                if (e.Kind == LogKind.LightningStruck && e.Flag) _claims.Add(e.To);
                else if (e.Kind == LogKind.CaptureDeclared && Projectile(e.Card))
                {
                    projectile = e.To;
                    _projectileTargets.Add(e.To);
                }
                else if (e.Kind == LogKind.UnitCaptured && projectile == e.To)
                {
                    _claims.Add(e.To);
                    projectile = null;
                }
            }
        }

        bool DeferCapture(Coord square, Piece piece)
        {
            if (!_claims.Contains(square)) return false;
            _dying[square] = piece;
            return true;
        }

        void FinishCapture(Coord square)
        {
            if (_dying.Remove(square, out var piece) && piece.Rt != null) PlayCapture(piece);
        }

        // After the log: a held unit whose effect never started (no player, an unexpected outcome) goes at once.
        void ReleaseUnpresented()
        {
            var squares = new List<Coord>(_dying.Keys);
            foreach (var square in squares)
                if (!_presented.Contains(square)) FinishCapture(square);
        }

        void QueueBoardFx(LogEntry e)
        {
            if (FxDirector == null) return;
            switch (e.Kind)
            {
                case LogKind.LightningMarked:
                    StormMark(e.To);
                    break;
                case LogKind.LightningStruck:
                    _presented.Add(e.To);
                    RunFx(StormStrikeAsync(e, destroyCancellationToken));
                    break;
                case LogKind.WeatherCleared:
                    if (Ambience != null) Ambience.Clear(e.Card.ToString());
                    if (e.Card != CardId.LightningStorm) break;
                    StopThunder();
                    StopStormMark();
                    break;
                case LogKind.CaptureDeclared when e.Card == CardId.Fireball:
                    FireballCast(e.From, e.To);
                    break;
                case LogKind.CaptureDeclared when e.Card == CardId.CrossbowVolley || e.Card == CardId.SiegeOrder:
                    _volley = (e.From, e.To);
                    break;
                case LogKind.UnitCaptured when _volley != null && e.To == _volley.Value.target:
                case LogKind.Blocked when _volley != null:
                case LogKind.Retreated when _volley != null:
                case LogKind.Intercepted when _volley != null:
                    var volley = _volley.Value;
                    _volley = null;
                    _presented.Add(volley.target);
                    RunFx(VolleyAsync(volley.from, volley.target, e, destroyCancellationToken));
                    break;
                case LogKind.UnitCaptured when _fireball != null && e.To == _fireball.Target:
                case LogKind.Blocked when _fireball != null:
                case LogKind.Retreated when _fireball != null:
                case LogKind.Intercepted when _fireball != null:
                    var fireball = _fireball;
                    _fireball = null;
                    _presented.Add(fireball.Target);
                    RunFx(FireballResolveAsync(fireball, e, destroyCancellationToken));
                    break;
            }
        }

        /// <summary>A condition settles over the board once its notice has been read; 7.1 A for the storm.</summary>
        void OnWeatherBoardFx(CardId card)
        {
            if (FxDirector == null) return;
            if (Ambience != null && WeatherLook(card) is FxLook look) Ambience.Set(card.ToString(), look);
            if (card != CardId.LightningStorm) return;
            PlayBoard(FxCueId.StormArrival, Vector3.zero, null);
            StopThunder();
            _thunder = CancellationTokenSource.CreateLinkedTokenSource(destroyCancellationToken);
            RunFx(ThunderAsync(_thunder.Token));
        }

        async Awaitable ThunderAsync(CancellationToken ct)
        {
            while (true)
            {
                await Awaitable.WaitForSecondsAsync(Random.Range(ThunderMin, ThunderMax), ct);
                if (!_match.HasCondition(CardId.LightningStorm)) return;
                PlayBoard(FxCueId.StormThunder, Vector3.zero, null);
            }
        }

        void StopThunder()
        {
            if (_thunder == null) return;
            _thunder.Cancel();
            _thunder.Dispose();
            _thunder = null;
        }

        /// <summary>7.1 B: the ring digs into the square and flickers, the unit there shivers, until the bolt.</summary>
        void StormMark(Coord square)
        {
            StopStormMark();
            var ring = MarkerRing(square);
            var unit = _match.At(square);
            var named = new Dictionary<string, GameObject>();
            if (ring != null) named[FxLabels.Ring] = ring.gameObject;
            _stormMark = PlayBoard(FxCueId.StormMark, SquareWorld(square), unit != null ? PieceObject(unit.Id) : null,
                named: named, text: Loc.Format("fx.storm.marked", square));
        }

        void StopStormMark()
        {
            if (_stormMark != null && FxDirector != null) FxDirector.Stop(_stormMark);
            _stormMark = null;
        }

        /// <summary>7.1 C: the bolt falls; a struck unit blurs at the impact.</summary>
        async Awaitable StormStrikeAsync(LogEntry e, CancellationToken ct)
        {
            var square = e.To;
            try
            {
                StopStormMark();
                PivotBoardAt(square);
                var target = _dying.TryGetValue(square, out var piece) ? piece.Holder.gameObject : PieceAt(square);
                string text = e.Flag ? Loc.Format("fx.storm.hit", Loc.Get("fx.captured." + e.Unit)) : Loc.Get("fx.storm.miss");
                var job = PlayBoard(FxCueId.StormStrike, SquareWorld(square), target, e.Flag ? 1f : 0.6f, text: text);
                await FxDirector.WaitStartedAsync(job, ct);
                if (!job.IsDone) await Awaitable.WaitForSecondsAsync(StrikeImpact, ct);
            }
            finally
            {
                _presented.Remove(square);
                FinishCapture(square);
            }
        }

        /// <summary>7.2 · 1: runes under the allied unit and a ring on the target, held through the reaction window.</summary>
        void FireballCast(Coord ally, Coord target)
        {
            if (_fireball != null) EndFireball(_fireball);
            var runes = BoardImage(_runeCircle, ally, Square * 1.5f, "Fireball Runes");
            var ring = BoardImage(_markRing, target, Square, "Fireball Ring");
            var named = new Dictionary<string, GameObject>();
            if (runes != null) named[FxLabels.Circle] = runes.gameObject;
            if (ring != null) named[FxLabels.Ring] = ring.gameObject;
            _fireball = new FireballFx
            {
                Ally = ally,
                Target = target,
                Runes = runes,
                Ring = ring,
                Cast = PlayBoard(FxCueId.FireballCast, SquareWorld(ally), PieceAt(ally), named: named),
            };
        }

        void EndFireball(FireballFx fireball)
        {
            if (FxDirector != null) FxDirector.Stop(fireball.Cast);
            if (fireball.Runes != null) Destroy(fireball.Runes.gameObject);
            if (fireball.Ring != null) Destroy(fireball.Ring.gameObject);
        }

        /// <summary>7.2 · 2 and 3: the shot arcs to the target, then explodes on it or on a shield of ink.</summary>
        async Awaitable FireballResolveAsync(FireballFx fireball, LogEntry outcome, CancellationToken ct)
        {
            var anchors = new List<GameObject>();
            RectTransform orb = null;
            Image shield = null;
            try
            {
                if (FxDirector != null) FxDirector.Stop(fireball.Cast);
                orb = CreateOrb(fireball.Ally);
                var shot = PlayBoard(FxCueId.FireballShot, SquareWorld(fireball.Ally), orb.gameObject,
                    named: new Dictionary<string, GameObject> { [FxLabels.Holder] = orb.GetChild(0).gameObject },
                    from: BoardAnchor(fireball.Ally, anchors), to: BoardAnchor(fireball.Target, anchors));

                if (outcome.Kind == LogKind.Blocked)
                {
                    await FxDirector.WaitStartedAsync(shot, ct);
                    if (!shot.IsDone) await Awaitable.WaitForSecondsAsync(ShotDuration - ShieldLead, ct);
                    shield = CreateShield(fireball.Ally, fireball.Target);
                    string text = outcome.Flag ? Loc.Get("fx.blockedGear")
                        : Loc.Format("fx.blocked", Loc.CardName(outcome.Card));
                    var blocked = PlayBoard(FxCueId.FireballBlocked, SquareWorld(fireball.Target), PieceAt(fireball.Target),
                        named: new Dictionary<string, GameObject> { [FxLabels.Shield] = shield.gameObject }, text: text);
                    await FxDirector.WaitAsync(shot, ct);
                    EndFireball(fireball);
                    Destroy(orb.gameObject);
                    await FxDirector.WaitAsync(blocked, ct);
                    return;
                }

                await FxDirector.WaitAsync(shot, ct);
                EndFireball(fireball);
                Destroy(orb.gameObject);
                if (outcome.Kind != LogKind.UnitCaptured) return;
                var target = _dying.TryGetValue(fireball.Target, out var piece) ? piece.Holder.gameObject : PieceAt(fireball.Target);
                var hit = PlayBoard(FxCueId.FireballHit, SquareWorld(fireball.Target), target,
                    text: Loc.Get("fx.captured." + outcome.Unit));
                await FxDirector.WaitStartedAsync(hit, ct);
                if (!hit.IsDone) await Awaitable.WaitForSecondsAsync(FireballImpact, ct);
                FinishCapture(fireball.Target);
                await FxDirector.WaitAsync(hit, ct);
            }
            finally
            {
                EndFireball(fireball);
                if (orb != null) Destroy(orb.gameObject);
                if (shield != null) Destroy(shield.gameObject);
                foreach (var anchor in anchors) Destroy(anchor);
                _presented.Remove(fireball.Target);
                FinishCapture(fireball.Target);
            }
        }

        /// <summary>C · Crossbow Volley (and Siege Order): three bolts fly, then the hit or the block is shown.</summary>
        async Awaitable VolleyAsync(Coord from, Coord target, LogEntry outcome, CancellationToken ct)
        {
            var anchors = new List<GameObject>();
            var bolts = new List<RectTransform>();
            try
            {
                FxJob last = null;
                for (int i = 0; i < VolleyBolts; i++)
                {
                    if (i > 0) await Awaitable.WaitForSecondsAsync(VolleyStagger, ct);
                    var bolt = CreateBolt(from, target);
                    bolts.Add(bolt);
                    last = PlayBoard(FxCueId.CrossbowVolley, SquareWorld(from), bolt.gameObject,
                        named: new Dictionary<string, GameObject> { [FxLabels.Holder] = bolt.GetChild(0).gameObject },
                        from: BoardAnchor(from, anchors), to: BoardAnchor(target, anchors));
                }
                if (last != null) await FxDirector.WaitAsync(last, ct);
                foreach (var bolt in bolts) Destroy(bolt.gameObject);
                bolts.Clear();
                if (outcome.Kind == LogKind.UnitCaptured)
                {
                    var victim = _dying.TryGetValue(target, out var piece) ? piece.Holder.gameObject : null;
                    _matchFx.Play(FxCueId.Capture, target, victim);
                    FinishCapture(target);
                }
                else if (outcome.Kind == LogKind.Blocked) _matchFx.PlayBlocked(outcome, target);
            }
            finally
            {
                foreach (var bolt in bolts) Destroy(bolt.gameObject);
                foreach (var anchor in anchors) Destroy(anchor);
                _presented.Remove(target);
                FinishCapture(target);
            }
        }

        // An ink stroke pointing at the target, until Epic Toon FX gives the volley its bolts.
        RectTransform CreateBolt(Coord from, Coord target)
        {
            var root = Ui.Rect(_boardFxOver, "Bolt", 0f, 0f, 0f, 0f);
            root.anchoredPosition = SquareCenter(from);
            var holder = Ui.Rect(root, "Holder", 0f, 0f, 0f, 0f);
            var stroke = Ui.Image(holder, "Stroke", Ui.Solid, 0f, 0f, 22f, 3f);
            stroke.color = Ui.InkText;
            stroke.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            stroke.rectTransform.anchoredPosition = Vector2.zero;
            var delta = SquareCenter(target) - SquareCenter(from);
            stroke.rectTransform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
            return root;
        }

        // C · Palisade, stakes and ditch rise out of the ground; a smoke screen covers its square. Others just pop.
        void PlayMarkerFx(Marker m, RectTransform rt)
        {
            FxCueId? cue = m.Kind switch
            {
                MarkerKind.Smoke => FxCueId.SmokeScreen,
                MarkerKind.Palisade or MarkerKind.Ditch or MarkerKind.Stakes => FxCueId.Fortify,
                _ => null,
            };
            var holder = rt.Find("Holder");
            if (cue == null || holder == null || FxDirector == null || !FxDirector.HasPlayer(cue.Value) || _awaitingStart)
            {
                StartCoroutine(PopRoutine(rt));
                return;
            }
            var named = new Dictionary<string, GameObject> { [FxLabels.Holder] = holder.gameObject };
            var fill = Find<Image>(rt, "Fill");
            if (fill != null) named[FxLabels.Fill] = fill.gameObject;
            if (cue == FxCueId.Fortify) holder.localScale = Vector3.zero;
            RunFx(MarkerAsync(PlayBoard(cue.Value, SquareWorld(m.Pos), rt.gameObject, named: named), holder,
                destroyCancellationToken));
        }

        // Whatever the player did, the marker ends at full size.
        static async Awaitable MarkerAsync(FxJob job, Transform holder, CancellationToken ct)
        {
            try
            {
                await FxDirector.WaitAsync(job, ct);
            }
            finally
            {
                if (holder != null) holder.localScale = Vector3.one;
            }
        }

        // F · The armies muster when the match starts, rank by rank from each side's back line.
        void PlayMatchStartFx()
        {
            if (FxDirector == null || !FxDirector.HasPlayer(FxCueId.MatchStart)) return;
            foreach (var piece in _pieces.Values)
            {
                var unit = _match.FindUnit(piece.Id);
                if (unit == null) continue;
                int rank = unit.Side == Side.White ? unit.Pos.Y : Match.Size - 1 - unit.Pos.Y;
                piece.Group.alpha = 0f;
                RunFx(MusterAsync(piece, rank * MusterRankDelay + unit.Pos.X * MusterFileDelay, destroyCancellationToken));
            }
        }

        async Awaitable MusterAsync(Piece piece, float delay, CancellationToken ct)
        {
            try
            {
                await Awaitable.WaitForSecondsAsync(delay, ct);
                if (piece.Rt == null) return;
                var job = PlayBoard(FxCueId.MatchStart, piece.Rt.position, piece.Rt.gameObject,
                    named: new Dictionary<string, GameObject> { [FxLabels.Holder] = piece.Holder.gameObject });
                await FxDirector.WaitAsync(job, ct);
            }
            finally
            {
                if (piece.Group != null) piece.Group.alpha = 1f;
            }
        }

        internal GameObject BoardObject => _board.gameObject;

        internal TextMeshProUGUI ReserveCounter(Side side, UnitType type)
        {
            var army = _armies[(int)side];
            int index = System.Array.IndexOf(Match.Recruitable, type);
            // The phone's opponent bar shows no reserve, so its list is empty.
            return army?.ReserveCounts != null && index >= 0 && index < army.ReserveCounts.Length
                ? army.ReserveCounts[index] : null;
        }

        FxJob PlayBoard(FxCueId cue, Vector3 position, GameObject target, float intensity = 1f,
            Dictionary<string, GameObject> named = null, Transform from = null, Transform to = null, string text = null) =>
            FxDirector.Play(cue, position, intensity, new FxContext
            {
                Target = target,
                Board = _board.gameObject,
                Named = named,
                From = from,
                To = to,
                Text = text,
            });

        internal GameObject PieceAt(Coord square)
        {
            var unit = _match.At(square);
            return unit != null ? PieceObject(unit.Id) : null;
        }

        Image MarkerRing(Coord square) =>
            _markers.TryGetValue((square, MarkerKind.Lightning), out var marker) ? Find<Image>(marker, "Ring") : null;

        /// <summary>The board scales around this square: the pivot moves without moving the board.</summary>
        void PivotBoardAt(Coord square)
        {
            var center = SquareCenter(square);
            var pivot = new Vector2(center.x / BoardSize, 1f + center.y / BoardSize);
            var size = _board.rect.size;
            _board.anchoredPosition += Vector2.Scale(pivot - _board.pivot, size);
            _board.pivot = pivot;
        }

        Image BoardImage(Sprite sprite, Coord square, float size, string name)
        {
            if (sprite == null) return null;
            var center = SquareCenter(square);
            var image = Ui.Image(_boardFxUnder, name, sprite, center.x - size * 0.5f, -center.y - size * 0.5f, size, size);
            Ui.Centered(image.rectTransform);
            image.color = Ui.Rubric;
            return image;
        }

        // Until Epic Toon FX is in, the fireball is a small glowing orb so the shot can be followed.
        RectTransform CreateOrb(Coord from)
        {
            const float size = 26f;
            var root = Ui.Rect(_boardFxOver, "Fireball", 0f, 0f, size, size);
            root.pivot = new Vector2(0.5f, 0.5f);
            root.anchoredPosition = SquareCenter(from);
            var holder = Ui.Centered(Ui.Rect(root, "Holder", 0f, 0f, size, size));
            var core = Ui.Image(holder, "Core", Ui.Pill(Ui.RubricLight, Ui.Rubric, Mathf.RoundToInt(size * 0.5f)), 0f, 0f, size, size);
            core.type = Image.Type.Sliced;
            return root;
        }

        Image CreateShield(Coord from, Coord target)
        {
            const float size = 46f;
            var point = Vector2.Lerp(SquareCenter(from), SquareCenter(target), 0.7f);
            var shield = Ui.Image(_boardFxOver, "Shield", Ui.Art("Badge_Shield"), point.x - size * 0.5f, -point.y - size * 0.5f,
                size, size);
            Ui.Centered(shield.rectTransform).localScale = Vector3.zero;
            return shield;
        }

        Transform BoardAnchor(Coord square, List<GameObject> anchors)
        {
            var anchor = Ui.Rect(_boardFxOver, "Anchor", 0f, 0f, 0f, 0f);
            anchor.anchoredPosition = SquareCenter(square);
            anchors.Add(anchor.gameObject);
            return anchor;
        }

        /// <summary>FX sandbox: replays a rehearsal on the chosen square, as the match would.</summary>
        internal void DebugBoardFx(FxCueId cue, Coord square)
        {
            if (FxDirector == null) return;
            var ct = destroyCancellationToken;
            var target = new Coord(Mathf.Clamp(square.X + 2, 0, Match.Size - 1), Mathf.Clamp(square.Y + 2, 0, Match.Size - 1));
            var unit = _match.At(square);
            switch (cue)
            {
                case FxCueId.StormArrival:
                    OnWeatherBoardFx(CardId.LightningStorm);
                    break;
                case FxCueId.StormMark:
                    StormMark(square);
                    break;
                case FxCueId.StormStrike:
                    RunFx(StormStrikeAsync(new LogEntry(_match.Turn, LogKind.LightningStruck, _human, unit?.Type ?? default,
                        to: square, flag: unit != null), ct));
                    break;
                case FxCueId.ConditionRemoved:
                    StopThunder();
                    StopStormMark();
                    if (Ambience != null) Ambience.ClearAll();
                    _matchFx.Play(FxCueId.ConditionRemoved);
                    break;
                case FxCueId.Blizzard:
                case FxCueId.Gale:
                case FxCueId.Fog:
                case FxCueId.Mud:
                case FxCueId.MoonlessNight:
                    var weather = cue switch
                    {
                        FxCueId.Blizzard => CardId.Blizzard,
                        FxCueId.Gale => CardId.Gale,
                        FxCueId.Fog => CardId.Fog,
                        FxCueId.Mud => CardId.Mud,
                        _ => CardId.MoonlessNight,
                    };
                    _matchFx.OnWeatherAcknowledged(weather);
                    OnWeatherBoardFx(weather);
                    break;
                case FxCueId.CrossbowVolley:
                    var struck = _match.At(target);
                    RunFx(VolleyAsync(square, target, new LogEntry(_match.Turn, LogKind.UnitCaptured, _human,
                        struck?.Type ?? default, to: target), ct));
                    break;
                case FxCueId.MatchStart:
                    PlayMatchStartFx();
                    break;
                case FxCueId.FireballHit:
                case FxCueId.FireballBlocked:
                    FireballCast(square, target);
                    var fireball = _fireball;
                    _fireball = null;
                    var victim = _match.At(target);
                    var outcome = cue == FxCueId.FireballHit
                        ? new LogEntry(_match.Turn, LogKind.UnitCaptured, _human, victim?.Type ?? default, to: target)
                        : new LogEntry(_match.Turn, LogKind.Blocked, _human, card: CardId.RaiseShields);
                    RunFx(DelayedResolveAsync(fireball, outcome, ct));
                    break;
            }
        }

        // The sandbox holds the conjuration a moment, standing in for the reaction window.
        async Awaitable DelayedResolveAsync(FireballFx fireball, LogEntry outcome, CancellationToken ct)
        {
            await Awaitable.WaitForSecondsAsync(1.2f, ct);
            await FireballResolveAsync(fireball, outcome, ct);
        }
    }
}
