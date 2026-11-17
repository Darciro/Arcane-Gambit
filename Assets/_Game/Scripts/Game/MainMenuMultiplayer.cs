using System.Collections.Generic;
using ArcaneGambit.Rules;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

namespace ArcaneGambit.Game
{
    /// <summary>The private duel screens: War Hall, Join with Code and the waiting room (visual direction v7).</summary>
    public sealed partial class MainMenu
    {
        enum MpScreen { None, Hall, Join, Room }
        enum Style { Primary, Secondary, Danger, Link }

        const float FoundDelay = 0.9f, CopiedTime = 1.8f, NoticeTime = 4f, ShakeTime = 0.4f, HourglassTurn = 2.4f;
        const float DisabledAlpha = 0.45f, DotCycle = 1.2f;
        static readonly Color Well = new Color(0.890f, 0.827f, 0.694f);
        static readonly Color Vellum = new Color(1f, 0.98f, 0.933f, 0.4f);
        static readonly Color FutureChip = new Color(0.839f, 0.761f, 0.608f);
        static readonly Color SoftInk = new Color(0.231f, 0.165f, 0.102f, 0.45f);
        static readonly float[] ShakeKeys = { 0f, -8f, 7f, -5f, 3f, 0f };

        MpScreen _mp;
        NetSession _net;
        RectTransform _mpLayer;
        readonly List<RectTransform> _hourglasses = new List<RectTransform>();
        readonly List<(TextMeshProUGUI text, string prefix)> _dotted = new List<(TextMeshProUGUI, string)>();
        string _hallNotice;

        TMP_InputField _codeInput;
        string _code = "";
        bool _codeError;
        float _shakeStart = -1f, _foundAt = -1f;
        RectTransform _boxRow;
        Image[] _boxFaces, _boxFocus, _boxCaret;
        TextMeshProUGUI[] _boxLetters;
        Button _enterButton;

        bool _confirmLeave;
        float _copiedUntil, _noticeUntil;
        string _roomNotice;
        int _shownCount = -1;

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] static extern void AG_CopyText(string text);
#endif

        void OpenHall()
        {
            if (_mp != MpScreen.None) return;
            _net = NetSession.Instance;
            _net.Changed += OnNetChanged;
            _hallNotice = null;
            if (_net.Reachable != true) _ = _net.CheckAsync();
            Show(MpScreen.Hall);
        }

        void CloseMultiplayer()
        {
            if (_net != null)
            {
                _net.Changed -= OnNetChanged;
                if (_net.State is not (NetState.Offline or NetState.Playing)) _net.Leave();
            }
            Show(MpScreen.None);
            Ui.SetCursor(CursorKind.Default);
        }

        void OnDestroy()
        {
            if (_net != null) _net.Changed -= OnNetChanged;
        }

        void Show(MpScreen screen)
        {
            _mp = screen;
            _confirmLeave = false;
            if (screen == MpScreen.None)
            {
                if (_mpLayer != null) Destroy(_mpLayer.gameObject);
                _mpLayer = null;
                _codeInput = null;
                return;
            }
            if (screen == MpScreen.Join)
            {
                _codeError = false;
                _foundAt = -1f;
            }
            Rebuild();
        }

        void OnNetChanged()
        {
            string notice = _net.TakeNotice();
            var state = _net.State;
            switch (_mp)
            {
                case MpScreen.Hall when state == NetState.Hosting:
                    Show(MpScreen.Room);
                    return;
                case MpScreen.Join:
                    if (state == NetState.Guest && _foundAt < 0f) _foundAt = Time.unscaledTime;
                    if (state == NetState.Failed && !_codeError)
                    {
                        _codeError = true;
                        _shakeStart = Time.unscaledTime;
                    }
                    break;
                case MpScreen.Room when state is NetState.Offline or NetState.Failed:
                    _hallNotice = notice ?? _net.Error;
                    Show(MpScreen.Hall);
                    return;
                case MpScreen.Room when notice != null:
                    _roomNotice = notice;
                    _noticeUntil = Time.unscaledTime + NoticeTime;
                    break;
            }
            if (_mp != MpScreen.None) Rebuild();
        }

        void UpdateMultiplayer()
        {
            if (_mp == MpScreen.None) return;
            float now = Time.unscaledTime;
            var state = _net.State;
            if (!SceneTransition.Busy)
                Ui.SetCursor(state is NetState.Creating or NetState.Joining ? CursorKind.Wait : CursorKind.Default);

            foreach (var glass in _hourglasses)
            {
                float t = now % HourglassTurn / HourglassTurn;
                glass.localRotation = Quaternion.Euler(0f, 0f, t < 0.78f ? 0f : -180f * (t - 0.78f) / 0.22f);
            }
            foreach (var (text, prefix) in _dotted) text.text = prefix + Dots(now);

            if (_mp == MpScreen.Join) UpdateJoin(now);
            if (_mp != MpScreen.Room) return;
            int count = state == NetState.Countdown ? Mathf.CeilToInt(_net.CountdownLeft - NetSession.BattleCrySeconds) : -1;
            bool expired = (_copiedUntil > 0f && now >= _copiedUntil) || (_noticeUntil > 0f && now >= _noticeUntil);
            if (expired)
            {
                if (now >= _copiedUntil) _copiedUntil = 0f;
                if (now >= _noticeUntil) _noticeUntil = 0f;
            }
            if (expired || count != _shownCount) Rebuild();
        }

        void UpdateJoin(float now)
        {
            if (_foundAt >= 0f && now - _foundAt >= FoundDelay)
            {
                _foundAt = -1f;
                Show(MpScreen.Room);
                return;
            }
            if (_boxRow == null) return;
            float shake = 0f;
            if (_shakeStart >= 0f)
            {
                float t = (now - _shakeStart) / ShakeTime;
                if (t >= 1f) _shakeStart = -1f;
                else
                {
                    float k = t * (ShakeKeys.Length - 1);
                    int i = (int)k;
                    shake = Mathf.Lerp(ShakeKeys[i], ShakeKeys[i + 1], k - i);
                }
            }
            _boxRow.anchoredPosition = new Vector2(shake, _boxRow.anchoredPosition.y);
            bool focused = _codeInput != null && _codeInput.isFocused;
            bool blink = now % 1f < 0.5f;
            for (int i = 0; i < _boxCaret.Length; i++)
            {
                bool current = focused && i == _code.Length && Typing;
                _boxFocus[i].enabled = current;
                _boxCaret[i].enabled = current && blink;
            }
        }

        void OnMultiplayerKeys(Keyboard keys, bool enter)
        {
            if (!keys.escapeKey.wasPressedThisFrame) return;
            var state = _net.State;
            switch (_mp)
            {
                case MpScreen.Hall:
                    if (state != NetState.Creating) CloseMultiplayer();
                    break;
                case MpScreen.Join:
                    OnJoinBack();
                    break;
                case MpScreen.Room when state != NetState.Countdown:
                    _confirmLeave = !_confirmLeave;
                    Rebuild();
                    break;
            }
        }

        void Rebuild()
        {
            if (_mpLayer == null) _mpLayer = Ui.Stretch(Ui.Rect(_root, "Multiplayer", 0, 0, 0, 0));
            for (int i = _mpLayer.childCount - 1; i >= 0; i--) Destroy(_mpLayer.GetChild(i).gameObject);
            _hourglasses.Clear();
            _dotted.Clear();
            _boxRow = null;
            _codeInput = null;
            bool mobile = UiProfile.Mobile;
            switch (_mp)
            {
                case MpScreen.Hall when mobile: BuildHallMobile(); break;
                case MpScreen.Join when mobile: BuildJoinMobile(); break;
                case MpScreen.Room when mobile: BuildRoomMobile(); break;
                case MpScreen.Hall: BuildHall(); break;
                case MpScreen.Join: BuildJoin(); break;
                case MpScreen.Room: BuildRoom(); break;
            }
        }

        // ── Salão de Guerra ─────────────────────────────────────────────────────

        void BuildHall()
        {
            const float w = 1000f, rowX = 56f, rowWidth = w - 112f, rowGap = 16f, rowHeight = 106f, offHeight = 131f;
            var state = _net.State;
            bool offline = _net.Reachable == false;
            bool busy = state == NetState.Creating;
            string banner = offline ? Loc.Get("ui.mp.error.offline")
                : state == NetState.Failed ? Loc.Get(_net.Error)
                : _hallNotice != null ? Loc.Get(_hallNotice) : null;
            float shift = banner != null ? 98f : 0f;
            var panel = Modal(w, 714f + shift);

            Eyebrow(panel, Loc.Get("ui.mp.hall.eyebrow"), 44f, w);
            Heading(panel, Loc.Get("ui.mp.hall.title"), 70f, w, 56f);
            Lead(panel, Loc.Get("ui.mp.hall.lead"), 144f, w);
            Ornament(panel, w * 0.5f, 203f);
            if (banner != null) Banner(panel, banner, rowX, 234f, rowWidth, retry: offline || state == NetState.Failed);

            float y = 230f + shift;
            var create = OptionRow(panel, rowX, y, rowWidth, rowHeight, "Icon_Swords", "ui.mp.create.title", "ui.mp.create.text");
            var createButton = Btn(create, Loc.Get(busy ? "ui.mp.create.busy" : "ui.mp.create.button"), Style.Primary,
                rowWidth - 24f - 250f, (rowHeight - 56f) * 0.5f, 250f, 56f, OnCreate, busy ? "Icon_Hourglass" : null);
            if (busy) Busy(createButton);
            else SetEnabled(createButton, !offline);

            y += rowHeight + rowGap;
            var join = OptionRow(panel, rowX, y, rowWidth, rowHeight, "Icon_Key", "ui.mp.join.title", "ui.mp.join.text");
            SetEnabled(Btn(join, Loc.Get("ui.mp.join.button"), Style.Secondary, rowWidth - 24f - 250f,
                (rowHeight - 56f) * 0.5f, 250f, 56f, () => Show(MpScreen.Join)), !busy && !offline);

            y += rowHeight + rowGap;
            var find = OptionRow(panel, rowX, y, rowWidth, offHeight, "Icon_Trophy", "ui.mp.find.title", "ui.mp.find.text",
                off: true);
            var chip = Chip(find, Loc.Get("ui.mp.find.chip"), 0f, 34f, FutureChip, Ui.InkText);
            chip.anchoredPosition = new Vector2(rowWidth - 24f - chip.sizeDelta.x, chip.anchoredPosition.y);
            Ui.Text(find, Loc.Get("ui.mp.find.note"), Ui.ItalicFont, 18f, Ui.InkBody, rowWidth - 24f - 250f, 72f, 250f, 24f,
                TextAlignmentOptions.TopRight);

            y += offHeight + 22f;
            var back = Btn(panel, Loc.Get("ui.mp.hall.back"), Style.Link, (w - 340f) * 0.5f - 24f, y, 340f, 44f,
                CloseMultiplayer, "Icon_Back", 20f);
            SetEnabled(back, !busy);
            Chip(panel, Loc.Get("ui.mp.key.esc"), (w + 340f) * 0.5f - 18f, y + 11f, Color.clear, Ui.InkSoft,
                height: 22f, fontSize: 14f, face: Ui.Boxed(Color.clear, SoftInk, 1, 0, 0));
        }

        void OnCreate()
        {
            _hallNotice = null;
            _ = _net.CreateAsync();
        }

        RectTransform OptionRow(RectTransform panel, float x, float y, float w, float h, string icon, string title,
            string text, bool off = false)
        {
            RectTransform row;
            if (off)
            {
                row = Ui.Rect(panel, "Option", x, y, w, h);
                Ui.Image(row, "Face", Ui.Solid, 0f, 0f, w, h).color = new Color(Ui.Parchment.r, Ui.Parchment.g, Ui.Parchment.b, 0.3f);
                Ui.DashedBox(row, 0.75f, 0.75f, w - 1.5f, h - 1.5f, Ui.Ink, 1.5f, 5f, 4f);
            }
            else row = Ui.Sliced(panel, "Option", Ui.Boxed(Vellum, Ui.Ink), x, y, w, h).rectTransform;

            var medal = Ui.Sliced(row, "Medal", Ui.Pill(Well, Ui.Ink, 33, 1.5f), 24f, (h - 66f) * 0.5f, 66f, 66f);
            var glyph = Ui.Image(medal.transform, "Icon", Ui.Art(icon), 16f, 16f, 34f, 34f);
            glyph.color = Ui.Ink;
            if (off) medal.gameObject.AddComponent<CanvasGroup>().alpha = 0.5f;

            var color = off ? Ui.InkFaded : Ui.InkText;
            Ui.Text(row, Loc.Get(title), Ui.TitleFont, 29f, color, 114f, 20f, w - 114f - 298f, 34f).textWrappingMode =
                TextWrappingModes.NoWrap;
            Ui.Text(row, Loc.Get(text), Ui.BodyFont, 20f, off ? Ui.InkFaded : Ui.Ink, 114f, 58f, w - 114f - 298f, 56f);
            return row;
        }

        void Banner(RectTransform panel, string text, float x, float y, float w, bool retry)
        {
            const float h = 68f;
            var root = Ui.Rect(panel, "Banner", x, y, w, h);
            Ui.Image(root, "Wash", Ui.Solid, 0f, 0f, w, h).color = new Color(Ui.Rubric.r, Ui.Rubric.g, Ui.Rubric.b, 0.08f);
            Ui.Sliced(root, "Line", Ui.Boxed(Color.clear, Ui.Rubric, 2, 0, 0), 0f, 0f, w, h);
            Ui.Image(root, "Icon", Ui.Art("Icon_Alert"), 18f, (h - 26f) * 0.5f, 26f, 26f).color = Ui.Rubric;
            float textWidth = w - 58f - (retry ? 200f : 18f);
            Ui.Text(root, text, Ui.BodyFont, 19f, Ui.Rubric, 58f, 0f, textWidth, h, TextAlignmentOptions.MidlineLeft);
            if (!retry) return;
            Btn(root, Loc.Get("ui.mp.retry"), Style.Danger, w - 14f - 176f, (h - 44f) * 0.5f, 176f, 44f, () =>
            {
                _hallNotice = null;
                _ = _net.CheckAsync();
            }, fontSize: 18f);
        }

        // ── Entrar com Código ───────────────────────────────────────────────────

        bool Connecting => _net.State == NetState.Joining;
        bool Found => _net.State is NetState.Guest or NetState.Countdown;
        bool Typing => !Connecting && !Found;

        void BuildJoin()
        {
            const float w = 840f, h = 518f, box = 80f, boxHeight = 98f, gap = 12f, sep = 16f;
            int n = NetSession.CodeLength;
            var panel = Modal(w, h);
            Eyebrow(panel, Loc.Get("ui.mp.code.eyebrow"), 44f, w);
            Heading(panel, Loc.Get("ui.mp.code.title"), 70f, w, 56f);
            Lead(panel, Loc.Get("ui.mp.code.lead"), 140f, w);

            float x0 = (w - CodeRowWidth(box, gap, sep)) * 0.5f;
            CodeBoxes(panel, x0, 202f, box, boxHeight, gap, sep, 54f);

            var msg = Ui.Rect(panel, "Message", (w - 620f) * 0.5f, 318f, 620f, 58f);
            if (_codeError)
                IconLine(msg, "Icon_Alert", Loc.Get(_net.Error ?? "ui.mp.error.join"), Ui.BodyFont, 19f, Ui.Rubric, 620f);
            else if (Connecting)
                IconLine(msg, "Icon_Hourglass", Loc.Get("ui.mp.code.searching"), Ui.ItalicFont, 18f, Ui.InkBody, 620f, dots: true);
            else if (Found)
                IconLine(msg, "Icon_Check", Loc.Get("ui.mp.code.found"), Ui.BodyFont, 21f, Ui.InkText, 620f);
            else
                Ui.Text(msg, Loc.Format("ui.mp.code.hint", n), Ui.ItalicFont, 18f, Ui.InkBody, 0f, 0f, 620f, 58f,
                    TextAlignmentOptions.Top);

            Ui.Dashed(panel, new Vector2(56f, -398f), new Vector2(w - 56f, -398f), SoftInk, 1f, 1.5f, 3f);
            var back = Btn(panel, Loc.Get(Connecting ? "ui.mp.code.cancel" : "ui.mp.code.back"), Style.Secondary, 56f, 420f,
                Connecting ? 190f : 170f, 56f, OnJoinBack, Connecting ? null : "Icon_Back");
            SetEnabled(back, !Found);
            _enterButton = Btn(panel, Loc.Get(Connecting ? "ui.mp.code.entering" : "ui.mp.code.enter"), Style.Primary,
                w - 56f - 270f, 420f, 270f, 56f, OnJoinEnter, Connecting ? "Icon_Hourglass" : null);
            if (Connecting) Busy(_enterButton);
            else SetEnabled(_enterButton, Typing && _code.Length == n);

            FocusCode();
        }

        void FocusCode()
        {
            if (!Typing) return;
            _codeInput.ActivateInputField();
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(_codeInput.gameObject);
        }

        static float CodeRowWidth(float box, float gap, float sep) =>
            NetSession.CodeLength * box + (NetSession.CodeLength - 1) * gap + sep + gap;

        void CodeBoxes(RectTransform panel, float x0, float y, float box, float boxHeight, float gap, float sep, float font)
        {
            int n = NetSession.CodeLength, half = n / 2;
            float rowWidth = CodeRowWidth(box, gap, sep), caret = box * 0.375f;
            _boxRow = Ui.Rect(panel, "Boxes", x0, y, rowWidth, boxHeight);
            _boxFaces = new Image[n];
            _boxFocus = new Image[n];
            _boxCaret = new Image[n];
            _boxLetters = new TextMeshProUGUI[n];
            float x = 0f;
            for (int i = 0; i < n; i++)
            {
                if (i == half)
                {
                    Ui.Image(_boxRow, "Separator", Ui.Solid, x, (boxHeight - 3f) * 0.5f, sep, 3f).color = SoftInk;
                    x += sep + gap;
                }
                _boxFaces[i] = Ui.Sliced(_boxRow, "Box", Ui.Boxed(Vellum, Ui.Ink), x, 0f, box, boxHeight);
                _boxFocus[i] = Ui.Sliced(_boxRow, "Focus", Ui.Boxed(Color.clear, Ui.Rubric, 2, 3, 2), x, 0f, box, boxHeight);
                _boxCaret[i] = Ui.Image(_boxRow, "Caret", Ui.Solid, x + (box - caret) * 0.5f, boxHeight - boxHeight * 0.235f,
                    caret, 3f);
                _boxCaret[i].color = Ui.Rubric;
                _boxLetters[i] = Ui.Text(_boxRow, "", Ui.TitleFont, font, Ui.InkText, x, 0f, box, boxHeight,
                    TextAlignmentOptions.Center);
                x += box + gap;
            }
            _codeInput = CodeField(panel, x0, y, rowWidth, boxHeight);
            _codeInput.SetTextWithoutNotify(_code);
            PaintBoxes();
        }

        // The field itself is invisible: it only collects keys and pastes, and the boxes draw what it holds.
        TMP_InputField CodeField(RectTransform panel, float x, float y, float w, float h)
        {
            var hit = Ui.Image(panel, "CodeField", Ui.Solid, x, y, w, h, raycast: true);
            hit.color = Color.clear;
            hit.gameObject.SetActive(false);
            var area = Ui.Stretch(Ui.Rect(hit.transform, "TextArea", 0, 0, 0, 0));
            var text = Ui.Text(area, "", Ui.TitleFont, 20f, Color.clear, 0, 0, 0, 0);
            Ui.Stretch(text.rectTransform);
            var input = hit.gameObject.AddComponent<TMP_InputField>();
            input.transition = Selectable.Transition.None;
            input.targetGraphic = hit;
            input.textViewport = area;
            input.textComponent = text;
            input.fontAsset = Ui.TitleFont;
            input.lineType = TMP_InputField.LineType.SingleLine;
            input.characterLimit = NetSession.CodeLength;
            input.keyboardType = TouchScreenKeyboardType.ASCIICapable;
            input.shouldHideMobileInput = true;
            input.onFocusSelectAll = false;
            input.customCaretColor = true;
            input.caretColor = Color.clear;
            input.selectionColor = Color.clear;
            input.onValidateInput = (_, _, c) => char.IsLetterOrDigit(c) && c < 128 ? char.ToUpperInvariant(c) : '\0';
            input.interactable = Typing;
            input.onValueChanged.AddListener(OnCodeTyped);
            input.onSubmit.AddListener(_ => OnJoinEnter());
            hit.gameObject.SetActive(true);
            return input;
        }

        void OnCodeTyped(string raw)
        {
            string clean = NetSession.CleanCode(raw);
            if (clean != raw)
            {
                _codeInput.SetTextWithoutNotify(clean);
                _codeInput.caretPosition = clean.Length;
            }
            _code = clean;
            if (_codeError)
            {
                _codeError = false;
                Rebuild();
                return;
            }
            PaintBoxes();
            SetEnabled(_enterButton, Typing && _code.Length == NetSession.CodeLength);
        }

        void PaintBoxes()
        {
            for (int i = 0; i < _boxFaces.Length; i++)
            {
                bool filled = i < _code.Length;
                _boxLetters[i].text = filled ? _code[i].ToString() : "";
                _boxFaces[i].sprite = _codeError ? Ui.Boxed(Vellum, Ui.Rubric)
                    : Found ? Ui.Boxed(Well, Ui.Ink)
                    : filled ? Ui.Boxed(Ui.Parchment, Ui.Ink) : Ui.Boxed(Vellum, Ui.Ink);
                _boxFaces[i].color = Connecting ? new Color(1f, 1f, 1f, 0.6f) : Color.white;
                _boxLetters[i].alpha = Connecting ? 0.6f : 1f;
            }
        }

        void OnJoinEnter()
        {
            if (!Typing || _code.Length != NetSession.CodeLength) return;
            _codeError = false;
            _ = _net.JoinAsync(_code);
        }

        // Cancelling a search keeps the code, so a typo can be fixed without typing it all again.
        void OnJoinBack()
        {
            if (Connecting) _net.Leave();
            else if (!Found)
            {
                Show(MpScreen.Hall);
                if (_net.State == NetState.Failed) _net.Leave();
            }
        }

        // ── Sala de espera ──────────────────────────────────────────────────────

        void BuildRoom()
        {
            const float w = 1240f, h = 933f, x = 52f, inner = w - 104f;
            bool host = _net.IsHost;
            var state = _net.State;
            bool counting = state == NetState.Countdown;
            bool present = _net.OpponentPresent;
            var panel = Modal(w, h);

            var role = Chip(panel, Loc.Get(host ? "ui.mp.room.host" : "ui.mp.room.guest"), 0f, 38f, Vellum, Ui.InkText);
            role.anchoredPosition = new Vector2(w - 46f - role.sizeDelta.x, role.anchoredPosition.y);
            var lockChip = Chip(panel, Loc.Get("ui.mp.room.private"), 0f, 38f, Vellum, Ui.InkText, "Icon_Lock");
            lockChip.anchoredPosition = new Vector2(role.anchoredPosition.x - 8f - lockChip.sizeDelta.x, role.anchoredPosition.y);

            Eyebrow(panel, Loc.Get("ui.mp.room.eyebrow"), 40f, w);
            Heading(panel, Loc.Get("ui.mp.room.title"), 64f, w, 52f);

            CodeBox(panel, x, 146f, inner, host);

            const float cardWidth = (inner - 120f) * 0.5f, cardY = 340f, cardHeight = 273f;
            Commander(panel, x, cardY, cardWidth, cardHeight, Loc.Get("ui.mp.room.you"), Loc.Get("ui.mp.room.youName"),
                _net.LocalSide, mine: true);
            var vs = Ui.Rect(panel, "Versus", x + cardWidth, cardY, 120f, cardHeight);
            Ui.Image(vs, "Emblem", Ui.Art("Emblem_Versus"), 18f, 81f, 84f, 84f);
            Ui.Text(vs, Loc.Get("ui.mp.room.versus"), Ui.ItalicFont, 20f, Ui.InkSoft, 0f, 167f, 120f, 28f, TextAlignmentOptions.Top);
            float oppX = x + cardWidth + 120f;
            if (present)
                Commander(panel, oppX, cardY, cardWidth, cardHeight, Loc.Get(host ? "ui.mp.room.opponent" : "ui.mp.room.host"),
                    Loc.Get("ui.mp.room.commander"), _net.LocalSide.Other(), mine: false);
            else Vacant(panel, oppX, cardY, cardWidth, cardHeight);

            Settings(panel, x, 633f, inner, host, counting);
            Bar(panel, x, 816f, inner, host, state, present);

            if (_confirmLeave) ConfirmLeave(panel, w, h, host);
        }

        void CodeBox(RectTransform panel, float x, float y, float w, bool host)
        {
            const float tile = 58f, tileHeight = 72f, gap = 8f, sep = 14f;
            var box = Ui.Sliced(panel, "CodeBox", Ui.Boxed(Well, Ui.Ink), x, y, w, 172f).rectTransform;
            var label = Ui.Label(box, Loc.Get(host ? "ui.mp.room.inviteCode" : "ui.mp.room.roomCode"), 24f, 18f, 400f, 22f);
            label.fontSize = 16f;
            string code = _net.Code ?? "";
            float tx = 24f;
            for (int i = 0; i < code.Length; i++)
            {
                if (i == code.Length / 2)
                {
                    Ui.Image(box, "Separator", Ui.Solid, tx, 50f + (tileHeight - 3f) * 0.5f, sep, 3f).color = SoftInk;
                    tx += sep + gap;
                }
                Ui.Sliced(box, "Tile", Ui.Boxed(Ui.Parchment, Ui.Ink), tx, 50f, tile, tileHeight);
                Ui.Text(box, code[i].ToString(), Ui.TitleFont, 42f, Ui.InkText, tx, 50f, tile, tileHeight,
                    TextAlignmentOptions.Center);
                tx += tile + gap;
            }
            if (host)
            {
                bool copied = _copiedUntil > Time.unscaledTime;
                var copy = Btn(box, Loc.Get(copied ? "ui.mp.room.copied" : "ui.mp.room.copy"), Style.Secondary,
                    w - 24f - 230f, 50f, 230f, 72f, OnCopy, copied ? "Icon_Check" : "Icon_Copy");
                if (copied)
                {
                    var face = (Image)copy.targetGraphic;
                    face.sprite = Ui.Solid;
                    face.color = Ui.Ink;
                    foreach (var g in copy.GetComponentsInChildren<Graphic>())
                        if (g != face) g.color = Ui.Parchment;
                }
            }
            Ui.Text(box, Loc.Get(host ? "ui.mp.room.hostNote" : "ui.mp.room.guestNote"), Ui.ItalicFont, 17f, Ui.InkBody,
                24f, 132f, w - 48f, 24f);
        }

        void OnCopy()
        {
            string code = _net.Code;
            if (string.IsNullOrEmpty(code)) return;
#if UNITY_WEBGL && !UNITY_EDITOR
            AG_CopyText(code);
#else
            GUIUtility.systemCopyBuffer = code;
#endif
            _copiedUntil = Time.unscaledTime + CopiedTime;
            Rebuild();
        }

        void Commander(RectTransform panel, float x, float y, float w, float h, string label, string name, Side side, bool mine)
        {
            var card = Ui.Sliced(panel, "Commander", Ui.Boxed(Vellum, mine ? Ui.Rubric : Ui.Ink), x, y, w, h).rectTransform;
            CardHeader(card, label, w);
            Ui.Image(card, "Seal", Ui.UnitArt(UnitType.Sovereign, side), (w - 92f) * 0.5f, 48f, 92f, 92f);
            CardTitle(card, name, Ui.InkText, w);
            Ui.Text(card, Loc.Army(side), Ui.ItalicFont, 19f, Ui.InkBody, 0f, 184f, w, 26f, TextAlignmentOptions.Top);
            var chip = Chip(card, Loc.Get("ui.mp.room.ready"), 0f, 222f, Ui.Ink, Ui.Parchment, "Icon_Check");
            chip.anchoredPosition = new Vector2((w - chip.sizeDelta.x) * 0.5f, chip.anchoredPosition.y);
        }

        void Vacant(RectTransform panel, float x, float y, float w, float h)
        {
            var card = Ui.Rect(panel, "Vacant", x, y, w, h);
            Ui.Image(card, "Face", Ui.Solid, 0f, 0f, w, h).color = new Color(Ui.Parchment.r, Ui.Parchment.g, Ui.Parchment.b, 0.28f);
            Ui.DashedBox(card, 0.75f, 0.75f, w - 1.5f, h - 1.5f, Ui.Ink, 1.5f, 5f, 4f);
            CardHeader(card, Loc.Get("ui.mp.room.opponent"), w);
            Ui.Sliced(card, "Seal", Ui.Pill(Color.clear, SoftInk, 48, 2f), (w - 96f) * 0.5f, 46f, 96f, 96f);
            Ui.Text(card, "?", Ui.ItalicFont, 50f, SoftInk, (w - 96f) * 0.5f, 46f, 96f, 96f, TextAlignmentOptions.Center);
            CardTitle(card, Loc.Get("ui.mp.room.vacant"), Ui.InkFaded, w);
            Ui.Text(card, Loc.Get("ui.mp.room.vacantText"), Ui.ItalicFont, 19f, Ui.InkBody, 0f, 184f, w, 26f,
                TextAlignmentOptions.Top);
            var chip = Chip(card, Loc.Get("ui.mp.room.waitingChip"), 0f, 222f, Color.clear, Ui.InkText, dots: true);
            chip.anchoredPosition = new Vector2((w - chip.sizeDelta.x) * 0.5f, chip.anchoredPosition.y);
        }

        static void CardHeader(RectTransform card, string label, float w)
        {
            var text = Ui.Label(card, label, 0f, 16f, w, 22f, TextAlignmentOptions.Top);
            text.fontSize = 16f;
        }

        static void CardTitle(RectTransform card, string name, Color color, float w) =>
            Ui.Text(card, name, Ui.TitleFont, 30f, color, 0f, 148f, w, 36f, TextAlignmentOptions.Top).textWrappingMode =
                TextWrappingModes.NoWrap;

        void Settings(RectTransform panel, float x, float y, float w, bool host, bool counting)
        {
            const float column = 522f, columnGap = 44f, rowHeight = 52f;
            var box = Ui.Sliced(panel, "Settings", Ui.Boxed(new Color(1f, 0.98f, 0.933f, 0.3f), Ui.Ink, 2, 0, 0), x, y, w, 162f)
                .rectTransform;
            Ui.Text(box, Loc.Get("ui.mp.room.config"), Ui.TitleFont, 24f, Ui.InkText, 24f, 14f, 600f, 30f);
            float left = 24f, right = 24f + column + columnGap;
            SettingRow(box, left, 50f, column, "ui.mp.room.board", Loc.Get("ui.mp.room.boardValue"));
            SettingRow(box, right, 50f, column, "ui.mp.room.mode", Loc.Get("ui.mp.room.modeValue"));
            SettingRow(box, right, 50f + rowHeight, column, "ui.mp.room.first", Loc.Army(Side.White));
            if (!host)
            {
                SettingRow(box, left, 50f + rowHeight, column, "ui.mp.room.turn",
                    Loc.Format("ui.mp.room.turnValue", _net.TurnSeconds) +
                    $" <size=16><font=\"{Ui.ItalicFont.name}\"><color={Ui.ToHex(Ui.InkBody)}>{Loc.Get("ui.mp.room.turnByHost")}</color></font></size>");
                return;
            }
            SettingRow(box, left, 50f + rowHeight, column, "ui.mp.room.turn", "");
            var options = NetSession.TurnOptions;
            const float seg = 74f, segHeight = 38f;
            float sx = left + column - seg * options.Length, sy = 50f + rowHeight + (rowHeight - segHeight) * 0.5f;
            Ui.Sliced(box, "Segments", Ui.Outline, sx, sy, seg * options.Length, segHeight);
            for (int i = 0; i < options.Length; i++)
            {
                int seconds = options[i];
                bool on = seconds == _net.TurnSeconds;
                var face = Ui.Image(box, "Segment", Ui.Solid, sx + seg * i, sy, seg, segHeight, raycast: true);
                face.color = on ? Ui.Ink : Color.clear;
                if (i > 0) Ui.Image(box, "Divider", Ui.Solid, sx + seg * i, sy, 1f, segHeight).color = Ui.Ink;
                Ui.Text(face.transform, Loc.Format("ui.mp.room.turnOption", seconds), Ui.TitleFont, 17f,
                    on ? Ui.Parchment : Ui.InkText, 0f, 0f, seg, segHeight, TextAlignmentOptions.Center);
                var button = Ui.MakeButton(face, () => _net.SetTurnSeconds(seconds));
                button.transition = on ? Selectable.Transition.None : Selectable.Transition.ColorTint;
                button.interactable = !counting;
                if (counting && !on) face.gameObject.AddComponent<CanvasGroup>().alpha = 0.5f;
            }
        }

        static void SettingRow(RectTransform box, float x, float y, float w, string key, string value)
        {
            Ui.Dashed(box, new Vector2(x, -y), new Vector2(x + w, -y), SoftInk, 1f, 1.5f, 3f);
            var label = Ui.Text(box, Loc.Get(key), Ui.TitleFont, 17f, Ui.InkSoft, x, y, 240f, 52f, TextAlignmentOptions.MidlineLeft);
            label.characterSpacing = 10f;
            if (value.Length > 0)
                Ui.Text(box, value, Ui.BodyFont, 20f, Ui.InkText, x + 200f, y, w - 200f, 52f, TextAlignmentOptions.MidlineRight);
        }

        void Bar(RectTransform panel, float x, float y, float w, bool host, NetState state, bool present)
        {
            const float h = 82f;
            bool counting = state == NetState.Countdown;
            var bar = Ui.Sliced(panel, "Bar", Ui.Boxed(Well, Ui.Ink, 2, 0, 0), x, y, w, h).rectTransform;
            var leave = Btn(bar, Loc.Get("ui.mp.room.leave"), Style.Danger, 14f, 13f, 204f, 56f, () =>
            {
                _confirmLeave = true;
                Rebuild();
            }, "Icon_Back");
            SetEnabled(leave, !counting);
            if (host)
                SetEnabled(Btn(bar, Loc.Get("ui.mp.room.start"), Style.Primary, w - 14f - 242f, 13f, 242f, 56f,
                    _net.StartBattle, "Icon_Sword"), present && !counting);

            var status = Ui.Rect(bar, "Status", 232f, 0f, w - 232f - 270f, h);
            float sw = status.sizeDelta.x;
            if (counting)
            {
                float left = _net.CountdownLeft - NetSession.BattleCrySeconds;
                _shownCount = Mathf.CeilToInt(left);
                string text = left > 0f
                    ? Loc.Format("ui.mp.room.countdown", $"<size=44><font=\"{Ui.TitleFont.name}\"><color={Ui.ToHex(Ui.Rubric)}>{Ui.Roman(_shownCount)}</color></font></size>")
                    : $"<font=\"{Ui.TitleFont.name}\"><size=34><color={Ui.ToHex(Ui.Rubric)}>{Loc.Get("ui.mp.room.battleCry")}</color></size></font>";
                Ui.Text(status, text, Ui.BodyFont, 23f, Ui.InkText, 0f, 0f, sw, h, TextAlignmentOptions.Center);
                return;
            }
            _shownCount = -1;
            if (_noticeUntil > Time.unscaledTime && _roomNotice != null)
                IconLine(status, "Icon_Alert", Loc.Get(_roomNotice), Ui.BodyFont, 23f, Ui.Rubric, sw, h);
            else if (!host) IconLine(status, "Icon_Hourglass", Loc.Get("ui.mp.room.waitHost"), Ui.BodyFont, 23f, Ui.InkText, sw, h, dots: true);
            else if (!present) IconLine(status, "Icon_Hourglass", Loc.Get("ui.mp.room.waitOpponent"), Ui.BodyFont, 23f, Ui.InkText, sw, h, dots: true);
            else Ui.Text(status, Loc.Get("ui.mp.room.bothReady"), Ui.BodyFont, 23f, Ui.InkText, 0f, 0f, sw, h,
                TextAlignmentOptions.Center);
        }

        void ConfirmLeave(RectTransform panel, float w, float h, bool host)
        {
            const float dw = 660f, dh = 300f, bw = 230f;
            var shade = Ui.Image(panel, "Shade", Ui.Solid, 0f, 0f, w, h, raycast: true);
            shade.color = new Color(ScrimColor.r, ScrimColor.g, ScrimColor.b, 0.5f);
            var dialog = Ui.Modal(panel, (w - dw) * 0.5f, (h - dh) * 0.5f, dw, dh);
            string role = host ? "host" : "guest";
            Eyebrow(dialog, Loc.Get("ui.mp.confirm.eyebrow"), 34f, dw);
            Heading(dialog, Loc.Get($"ui.mp.confirm.{role}Title"), 62f, dw, 42f);
            Ui.Text(dialog, Loc.Get($"ui.mp.confirm.{role}Text"), Ui.BodyFont, 21f, Ui.InkText, 70f, 124f, dw - 140f, 60f,
                TextAlignmentOptions.Top);
            float bx = (dw - bw * 2f - 14f) * 0.5f;
            Btn(dialog, Loc.Get("ui.mp.confirm.stay"), Style.Secondary, bx, 212f, bw, 56f, () =>
            {
                _confirmLeave = false;
                Rebuild();
            });
            Btn(dialog, Loc.Get($"ui.mp.confirm.{role}Button"), Style.Primary, bx + bw + 14f, 212f, bw, 56f, _net.Leave);
        }

        // ── Building blocks ─────────────────────────────────────────────────────

        RectTransform Modal(float w, float h)
        {
            var scrim = Ui.Stretch(Ui.Image(_mpLayer, "Scrim", Ui.Solid, 0, 0, 0, 0, raycast: true).rectTransform);
            scrim.GetComponent<Image>().color = ScrimColor;
            var design = UiProfile.Design;
            return Ui.Modal(UiProfile.ModalStage(_mpLayer, w, h), (design.x - w) * 0.5f, (design.y - h) * 0.5f, w, h);
        }

        static void Eyebrow(RectTransform panel, string text, float y, float w)
        {
            var eyebrow = Ui.Text(panel, text, Ui.TitleFont, 18f, Ui.Rubric, 0f, y, w, 24f, TextAlignmentOptions.Top);
            eyebrow.characterSpacing = 20f;
        }

        static void Heading(RectTransform panel, string text, float y, float w, float size) =>
            Ui.Text(panel, Ui.RomanInitial(text), Ui.TitleFont, size, Ui.InkText, 0f, y, w, size * 1.25f,
                TextAlignmentOptions.Top).textWrappingMode = TextWrappingModes.NoWrap;

        static void Lead(RectTransform panel, string text, float y, float w) =>
            Ui.Text(panel, text, Ui.ItalicFont, 23f, Ui.Ink, 0f, y, w, 32f, TextAlignmentOptions.Top);

        Button Btn(RectTransform parent, string label, Style style, float x, float y, float w, float h, UnityAction onClick,
            string icon = null, float fontSize = 22f)
        {
            Button button;
            TextMeshProUGUI text;
            if (style == Style.Link)
            {
                var face = Ui.Image(parent, "Link", Ui.Solid, x, y, w, h, raycast: true);
                face.color = Color.clear;
                button = Ui.MakeButton(face, onClick);
                button.transition = Selectable.Transition.None;
                text = Ui.Text(face.transform, label, Ui.TitleFont, fontSize, Ui.Ink, 0f, 0f, w, h, TextAlignmentOptions.Center);
                text.textWrappingMode = TextWrappingModes.NoWrap;
            }
            else
            {
                button = Ui.Button(parent, label, style == Style.Primary, x, y, w, h, onClick, fontSize);
                if (style == Style.Danger) Ui.Danger(button);
                text = button.GetComponentInChildren<TextMeshProUGUI>();
            }
            var colors = button.colors;
            colors.disabledColor = Color.white;
            button.colors = colors;
            if (icon == null) return button;

            const float size = 20f, gap = 10f;
            float left = (w - (size + gap + text.GetPreferredValues(label).x)) * 0.5f;
            var glyph = Ui.Image(button.transform, "Icon", Ui.Art(icon), left, (h - size) * 0.5f, size, size);
            glyph.color = text.color;
            text.rectTransform.anchoredPosition += new Vector2((size + gap) * 0.5f, 0f);
            if (icon == "Icon_Hourglass") _hourglasses.Add(Ui.Centered(glyph.rectTransform));
            return button;
        }

        static void SetEnabled(Button button, bool enabled)
        {
            button.interactable = enabled;
            var group = button.GetComponent<CanvasGroup>();
            if (group == null) group = button.gameObject.AddComponent<CanvasGroup>();
            group.alpha = enabled ? 1f : DisabledAlpha;
        }

        // A busy button keeps full ink: it is working, not unavailable.
        static void Busy(Button button)
        {
            SetEnabled(button, false);
            button.GetComponent<CanvasGroup>().alpha = 1f;
        }

        RectTransform Chip(RectTransform parent, string text, float x, float y, Color fill, Color ink, string icon = null,
            float height = 30f, float fontSize = 16f, bool dots = false, Sprite face = null)
        {
            const float pad = 13f, iconSize = 15f, iconGap = 7f;
            var root = Ui.Rect(parent, "Chip", x, y, 100f, height);
            var frame = Ui.Sliced(root, "Face", face != null ? face : Ui.Pill(fill, Ui.Ink, Mathf.RoundToInt(height * 0.5f), 1f),
                0f, 0f, 100f, height);
            var label = Ui.Text(root, text, Ui.TitleFont, fontSize, ink, 0f, 0f, 100f, height, TextAlignmentOptions.Midline);
            label.characterSpacing = 6f;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            float textWidth = label.GetPreferredValues(text + (dots ? " ···" : "")).x;
            float iconWidth = icon != null ? iconSize + iconGap : 0f;
            float width = pad * 2f + iconWidth + textWidth;
            root.sizeDelta = frame.rectTransform.sizeDelta = new Vector2(width, height);
            label.rectTransform.anchoredPosition = new Vector2(pad + iconWidth, 0f);
            label.rectTransform.sizeDelta = new Vector2(textWidth + 4f, height);
            label.alignment = TextAlignmentOptions.MidlineLeft;
            if (icon != null) Ui.Image(root, "Icon", Ui.Art(icon), pad, (height - iconSize) * 0.5f, iconSize, iconSize).color = ink;
            if (dots) _dotted.Add((label, text + " "));
            return root;
        }

        // Icon and text centered together on one line, as the design's status and message lines are.
        void IconLine(RectTransform parent, string icon, string text, TMP_FontAsset font, float size, Color color, float w,
            float h = 30f, bool dots = false)
        {
            const float iconSize = 24f, gap = 10f;
            var label = Ui.Text(parent, text, font, size, color, 0f, 0f, w, h, TextAlignmentOptions.MidlineLeft);
            label.textWrappingMode = TextWrappingModes.NoWrap;
            float textWidth = Mathf.Min(label.GetPreferredValues(text + (dots ? " ···" : "")).x, w - iconSize - gap);
            float left = (w - iconSize - gap - textWidth) * 0.5f;
            var glyph = Ui.Image(parent, "Icon", Ui.Art(icon), left, (h - iconSize) * 0.5f, iconSize, iconSize);
            glyph.color = color;
            if (icon == "Icon_Hourglass") _hourglasses.Add(Ui.Centered(glyph.rectTransform));
            label.rectTransform.anchoredPosition = new Vector2(left + iconSize + gap, label.rectTransform.anchoredPosition.y);
            label.rectTransform.sizeDelta = new Vector2(textWidth + 8f, h);
            if (dots) _dotted.Add((label, text + " "));
        }

        static string Dots(float now)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < 3; i++)
            {
                float p = ((now - i * 0.2f) % DotCycle + DotCycle) % DotCycle / DotCycle;
                float a = p < 0.25f ? Mathf.Lerp(0.25f, 1f, p / 0.25f) : p < 0.5f ? Mathf.Lerp(1f, 0.25f, (p - 0.25f) / 0.25f) : 0.25f;
                sb.Append($"<alpha=#{Mathf.RoundToInt(a * 255f):X2}>·");
            }
            return sb.ToString();
        }

        // ── Phone layout ────────────────────────────────────────────────────────
        // Not in the mobile-horizontal doc: the three screens become full pages, like its Options board, because the
        // desktop modals would shrink below reading size on a 390-unit-tall area.

        static Vector2 Area => UiProfile.MobileArea;

        RectTransform MobilePage(string eyebrow, string title)
        {
            const float header = 56f;
            var page = Ui.Stretch(Ui.Image(_mpLayer, "Page", Ui.Solid, 0, 0, 0, 0, raycast: true).rectTransform);
            page.GetComponent<Image>().color = Ui.Parchment;
            foreach (var (y, h) in new[] { (header - 1.5f, 1.5f), (header + 2f, 1f) })
            {
                var line = Ui.Line(page, 0f, y, 0f, h).rectTransform;
                line.anchorMax = Vector2.one;
                line.sizeDelta = new Vector2(0f, h);
            }
            var stage = Ui.Rect(page, "Stage", 0f, 0f, Area.x, Area.y);
            stage.anchorMin = stage.anchorMax = stage.pivot = new Vector2(0.5f, 0.5f);
            stage.anchoredPosition = Vector2.zero;
            string small = $"<size=43%><color={Ui.ToHex(Ui.InkSoft)}><cspace=0.2em>  {Loc.Get(eyebrow)}</cspace></color></size>";
            var heading = Ui.Text(stage, Ui.RomanInitial(Loc.Get(title)) + small, Ui.TitleFont, 26f, Ui.InkText, 110f, 0f,
                Area.x - 220f, header, TextAlignmentOptions.Center);
            heading.textWrappingMode = TextWrappingModes.NoWrap;
            heading.enableAutoSizing = true;
            heading.fontSizeMin = 18f;
            heading.fontSizeMax = 26f;
            return stage;
        }

        Button BackButton(RectTransform stage, UnityAction onClick)
        {
            var back = Ui.IconButton(stage, s_side, 6f, onClick);
            Ui.Chevron(back.transform, 22f, 22f, 10f, -1, Ui.Ink);
            return back;
        }

        void BuildHallMobile()
        {
            var state = _net.State;
            bool offline = _net.Reachable == false;
            bool busy = state == NetState.Creating;
            string banner = offline ? Loc.Get("ui.mp.error.offline")
                : state == NetState.Failed ? Loc.Get(_net.Error)
                : _hallNotice != null ? Loc.Get(_hallNotice) : null;
            var stage = MobilePage("ui.mp.hall.eyebrow", "ui.mp.hall.title");
            SetEnabled(BackButton(stage, CloseMultiplayer), !busy);

            float side = s_side, inner = Area.x - side * 2f, y = 70f;
            if (banner != null)
            {
                BannerMobile(stage, banner, side, y, inner, offline || state == NetState.Failed);
                y += 54f;
            }
            else
            {
                Ui.Text(stage, Loc.Get("ui.mp.hall.lead"), Ui.ItalicFont, 15f, Ui.Ink, side, y, inner, 22f,
                    TextAlignmentOptions.Top);
                y += 34f;
            }

            const float gap = 12f, pad = 14f, button = 44f;
            float w = (inner - gap * 2f) / 3f, h = Area.y - 21f - y, buttonY = h - pad - button;
            var create = OptionColumn(stage, side, y, w, h, "Icon_Swords", "ui.mp.create.title", "ui.mp.create.text", false);
            var createButton = Btn(create, Loc.Get(busy ? "ui.mp.create.busy" : "ui.mp.create.button"), Style.Primary, pad,
                buttonY, w - pad * 2f, button, OnCreate, busy ? "Icon_Hourglass" : null, 17f);
            if (busy) Busy(createButton);
            else SetEnabled(createButton, !offline);

            var join = OptionColumn(stage, side + w + gap, y, w, h, "Icon_Key", "ui.mp.join.title", "ui.mp.join.text", false);
            SetEnabled(Btn(join, Loc.Get("ui.mp.join.button"), Style.Secondary, pad, buttonY, w - pad * 2f, button,
                () => Show(MpScreen.Join), fontSize: 17f), !busy && !offline);

            var find = OptionColumn(stage, side + (w + gap) * 2f, y, w, h, "Icon_Trophy", "ui.mp.find.title",
                "ui.mp.find.text", true);
            var chip = Chip(find, Loc.Get("ui.mp.find.chip"), 0f, buttonY + 7f, FutureChip, Ui.InkText, height: 30f,
                fontSize: 14f);
            chip.anchoredPosition = new Vector2((w - chip.sizeDelta.x) * 0.5f, chip.anchoredPosition.y);
            Ui.Text(find, Loc.Get("ui.mp.find.note"), Ui.ItalicFont, 13.5f, Ui.InkBody, pad, buttonY - 22f, w - pad * 2f, 20f,
                TextAlignmentOptions.Top);
        }

        RectTransform OptionColumn(RectTransform parent, float x, float y, float w, float h, string icon, string title,
            string text, bool off)
        {
            const float pad = 14f, medal = 48f, textY = 74f;
            RectTransform column;
            if (off)
            {
                column = Ui.Rect(parent, "Option", x, y, w, h);
                Ui.Image(column, "Face", Ui.Solid, 0f, 0f, w, h).color =
                    new Color(Ui.Parchment.r, Ui.Parchment.g, Ui.Parchment.b, 0.3f);
                Ui.DashedBox(column, 0.75f, 0.75f, w - 1.5f, h - 1.5f, Ui.Ink, 1.5f, 5f, 4f);
            }
            else column = Ui.Sliced(parent, "Option", Ui.Boxed(Vellum, Ui.Ink), x, y, w, h).rectTransform;

            var badge = Ui.Sliced(column, "Medal", Ui.Pill(Well, Ui.Ink, 24, 1.5f), pad, pad, medal, medal);
            Ui.Image(badge.transform, "Icon", Ui.Art(icon), 12f, 12f, 24f, 24f).color = Ui.Ink;
            if (off) badge.gameObject.AddComponent<CanvasGroup>().alpha = 0.5f;
            var color = off ? Ui.InkFaded : Ui.InkText;
            var heading = Ui.Text(column, Loc.Get(title), Ui.TitleFont, 18f, color, pad + medal + 10f, pad,
                w - pad * 2f - medal - 10f, medal, TextAlignmentOptions.MidlineLeft);
            heading.lineSpacing = -12f;
            heading.enableAutoSizing = true;
            heading.fontSizeMin = 14f;
            heading.fontSizeMax = 18f;
            Ui.Text(column, Loc.Get(text), Ui.BodyFont, 14.5f, off ? Ui.InkFaded : Ui.Ink, pad, textY, w - pad * 2f,
                h - textY - pad - 44f - 8f);
            return column;
        }

        void BannerMobile(RectTransform parent, string text, float x, float y, float w, bool retry)
        {
            const float h = 44f, retryWidth = 130f;
            var root = Ui.Rect(parent, "Banner", x, y, w, h);
            Ui.Image(root, "Wash", Ui.Solid, 0f, 0f, w, h).color = new Color(Ui.Rubric.r, Ui.Rubric.g, Ui.Rubric.b, 0.08f);
            Ui.Sliced(root, "Line", Ui.Boxed(Color.clear, Ui.Rubric, 2, 0, 0), 0f, 0f, w, h);
            Ui.Image(root, "Icon", Ui.Art("Icon_Alert"), 12f, (h - 20f) * 0.5f, 20f, 20f).color = Ui.Rubric;
            float textWidth = w - 42f - (retry ? retryWidth + 18f : 12f);
            var label = Ui.Text(root, text, Ui.BodyFont, 14.5f, Ui.Rubric, 42f, 0f, textWidth, h,
                TextAlignmentOptions.MidlineLeft);
            label.enableAutoSizing = true;
            label.fontSizeMin = 11.5f;
            label.fontSizeMax = 14.5f;
            if (!retry) return;
            Btn(root, Loc.Get("ui.mp.retry"), Style.Danger, w - 6f - retryWidth, 4f, retryWidth, h - 8f, () =>
            {
                _hallNotice = null;
                _ = _net.CheckAsync();
            }, fontSize: 15f);
        }

        // The boxes sit high, so the system keyboard that opens below them leaves them in view.
        void BuildJoinMobile()
        {
            const float box = 54f, boxHeight = 64f, gap = 9f, sep = 14f, boxesY = 102f, message = 600f;
            int n = NetSession.CodeLength;
            var stage = MobilePage("ui.mp.code.eyebrow", "ui.mp.code.title");
            SetEnabled(BackButton(stage, OnJoinBack), !Found);
            Ui.Text(stage, Loc.Get("ui.mp.code.lead"), Ui.ItalicFont, 15f, Ui.Ink, 0f, 70f, Area.x, 22f,
                TextAlignmentOptions.Top);
            CodeBoxes(stage, (Area.x - CodeRowWidth(box, gap, sep)) * 0.5f, boxesY, box, boxHeight, gap, sep, 36f);

            var msg = Ui.Rect(stage, "Message", (Area.x - message) * 0.5f, boxesY + boxHeight + 14f, message, 30f);
            if (_codeError)
                IconLine(msg, "Icon_Alert", Loc.Get(_net.Error ?? "ui.mp.error.join"), Ui.BodyFont, 15f, Ui.Rubric, message);
            else if (Connecting)
                IconLine(msg, "Icon_Hourglass", Loc.Get("ui.mp.code.searching"), Ui.ItalicFont, 15f, Ui.InkBody, message,
                    dots: true);
            else if (Found)
                IconLine(msg, "Icon_Check", Loc.Get("ui.mp.code.found"), Ui.BodyFont, 16f, Ui.InkText, message);
            else
                Ui.Text(msg, Loc.Format("ui.mp.code.hint", n), Ui.ItalicFont, 14f, Ui.InkBody, 0f, 0f, message, 30f,
                    TextAlignmentOptions.Top);

            const float buttonWidth = 260f;
            _enterButton = Btn(stage, Loc.Get(Connecting ? "ui.mp.code.entering" : "ui.mp.code.enter"), Style.Primary,
                (Area.x - buttonWidth) * 0.5f, boxesY + boxHeight + 58f, buttonWidth, 48f, OnJoinEnter,
                Connecting ? "Icon_Hourglass" : null, 18f);
            if (Connecting) Busy(_enterButton);
            else SetEnabled(_enterButton, Typing && _code.Length == n);
            FocusCode();
        }

        // The code and the battle settings on the left; the two commanders and what happens next on the right.
        void BuildRoomMobile()
        {
            bool host = _net.IsHost;
            var state = _net.State;
            bool counting = state == NetState.Countdown;
            bool present = _net.OpponentPresent;
            var stage = MobilePage("ui.mp.room.eyebrow", "ui.mp.room.title");

            const float leftWidth = 360f, columnGap = 24f, top = 68f;
            float side = s_side, rightX = side + leftWidth + columnGap, rightWidth = Area.x - side - rightX;
            CodeBoxMobile(stage, side, top, leftWidth, host);
            SettingsMobile(stage, side, top + 124f, leftWidth, Area.y - 21f - top - 124f, host, counting);

            const float versus = 46f, cardHeight = 168f;
            float cardWidth = (rightWidth - versus) * 0.5f, oppX = rightX + cardWidth + versus;
            CommanderMobile(stage, rightX, top, cardWidth, cardHeight, Loc.Get("ui.mp.room.you"),
                Loc.Get("ui.mp.room.youName"), _net.LocalSide, true);
            Ui.Image(stage, "Emblem", Ui.Art("Emblem_Versus"), rightX + cardWidth + 5f, top + 50f, 36f, 36f);
            Ui.Text(stage, Loc.Get("ui.mp.room.versus"), Ui.ItalicFont, 13f, Ui.InkSoft, rightX + cardWidth, top + 90f, versus,
                18f, TextAlignmentOptions.Top);
            if (present)
                CommanderMobile(stage, oppX, top, cardWidth, cardHeight,
                    Loc.Get(host ? "ui.mp.room.opponent" : "ui.mp.room.host"), Loc.Get("ui.mp.room.commander"),
                    _net.LocalSide.Other(), false);
            else VacantMobile(stage, oppX, top, cardWidth, cardHeight);

            StatusMobile(stage, rightX, top + cardHeight + 12f, rightWidth, 52f, host, counting);

            const float button = 48f, leaveWidth = 150f;
            float buttonY = Area.y - 21f - button;
            var leave = Btn(stage, Loc.Get("ui.mp.room.leave"), Style.Danger, rightX, buttonY, leaveWidth, button, () =>
            {
                _confirmLeave = true;
                Rebuild();
            }, "Icon_Back", 16f);
            SetEnabled(leave, !counting);
            if (host)
                SetEnabled(Btn(stage, Loc.Get("ui.mp.room.start"), Style.Primary, rightX + leaveWidth + 10f, buttonY,
                    rightWidth - leaveWidth - 10f, button, _net.StartBattle, "Icon_Sword", 17f), present && !counting);

            if (_confirmLeave) ConfirmLeaveMobile(host);
        }

        void CodeBoxMobile(RectTransform parent, float x, float y, float w, bool host)
        {
            const float pad = 12f, tile = 36f, tileHeight = 44f, gap = 6f, sep = 10f, tilesY = 34f, copy = 44f;
            var box = Ui.Sliced(parent, "CodeBox", Ui.Boxed(Well, Ui.Ink), x, y, w, 114f).rectTransform;
            var label = Ui.Label(box, Loc.Get(host ? "ui.mp.room.inviteCode" : "ui.mp.room.roomCode"), pad, 10f, 180f, 18f);
            label.fontSize = 12.5f;
            label.characterSpacing = 12f;
            var role = Chip(box, Loc.Get(host ? "ui.mp.room.host" : "ui.mp.room.guest"), 0f, 7f, Vellum, Ui.InkText,
                height: 22f, fontSize: 12f);
            role.anchoredPosition = new Vector2(w - pad - role.sizeDelta.x, role.anchoredPosition.y);
            var lockChip = Chip(box, Loc.Get("ui.mp.room.private"), 0f, 7f, Vellum, Ui.InkText, "Icon_Lock", 22f, 12f);
            lockChip.anchoredPosition =
                new Vector2(role.anchoredPosition.x - 6f - lockChip.sizeDelta.x, lockChip.anchoredPosition.y);

            string code = _net.Code ?? "";
            float tx = pad;
            for (int i = 0; i < code.Length; i++)
            {
                if (i == code.Length / 2)
                {
                    Ui.Image(box, "Separator", Ui.Solid, tx, tilesY + (tileHeight - 3f) * 0.5f, sep, 3f).color = SoftInk;
                    tx += sep + gap;
                }
                Ui.Sliced(box, "Tile", Ui.Boxed(Ui.Parchment, Ui.Ink), tx, tilesY, tile, tileHeight);
                Ui.Text(box, code[i].ToString(), Ui.TitleFont, 28f, Ui.InkText, tx, tilesY, tile, tileHeight,
                    TextAlignmentOptions.Center);
                tx += tile + gap;
            }
            bool copied = _copiedUntil > Time.unscaledTime;
            if (host)
            {
                var button = Btn(box, "", Style.Secondary, w - pad - copy, tilesY, copy, tileHeight, OnCopy);
                var glyph = Ui.Image(button.transform, "Icon", Ui.Art(copied ? "Icon_Check" : "Icon_Copy"), 12f,
                    (tileHeight - 20f) * 0.5f, 20f, 20f);
                glyph.color = copied ? Ui.Parchment : Ui.Ink;
                if (copied)
                {
                    var face = (Image)button.targetGraphic;
                    face.sprite = Ui.Solid;
                    face.color = Ui.Ink;
                }
            }
            string note = Loc.Get(host ? "ui.mp.room.hostNote" : "ui.mp.room.guestNote");
            if (copied) note = $"<color={Ui.ToHex(Ui.Rubric)}>{Loc.Get("ui.mp.room.copied")}</color> · " + note;
            Ui.Text(box, note, Ui.ItalicFont, 13.5f, Ui.InkBody, pad, 86f, w - pad * 2f, 20f).textWrappingMode =
                TextWrappingModes.NoWrap;
        }

        void SettingsMobile(RectTransform parent, float x, float y, float w, float h, bool host, bool counting)
        {
            const float pad = 12f, rowY = 34f;
            float row = (h - rowY - 6f) / 4f, inner = w - pad * 2f;
            var box = Ui.Sliced(parent, "Settings", Ui.Boxed(new Color(1f, 0.98f, 0.933f, 0.3f), Ui.Ink, 2, 0, 0), x, y, w, h)
                .rectTransform;
            Ui.Text(box, Loc.Get("ui.mp.room.config"), Ui.TitleFont, 16f, Ui.InkText, pad, 9f, inner, 20f);
            SettingRowMobile(box, pad, rowY, inner, row, "ui.mp.room.board", Loc.Get("ui.mp.room.boardValue"));
            SettingRowMobile(box, pad, rowY + row, inner, row, "ui.mp.room.mode", Loc.Get("ui.mp.room.modeValue"));
            SettingRowMobile(box, pad, rowY + row * 2f, inner, row, "ui.mp.room.first", Loc.Army(Side.White));
            float turnY = rowY + row * 3f;
            if (!host)
            {
                SettingRowMobile(box, pad, turnY, inner, row, "ui.mp.room.turn",
                    Loc.Format("ui.mp.room.turnValue", _net.TurnSeconds));
                return;
            }
            SettingRowMobile(box, pad, turnY, inner, row, "ui.mp.room.turn", "");
            var options = NetSession.TurnOptions;
            const float seg = 52f, segHeight = 30f;
            float sx = pad + inner - seg * options.Length, sy = turnY + (row - segHeight) * 0.5f;
            Ui.Sliced(box, "Segments", Ui.Outline, sx, sy, seg * options.Length, segHeight);
            for (int i = 0; i < options.Length; i++)
            {
                int seconds = options[i];
                bool on = seconds == _net.TurnSeconds;
                var face = Ui.Image(box, "Segment", Ui.Solid, sx + seg * i, sy, seg, segHeight, raycast: true);
                face.color = on ? Ui.Ink : Color.clear;
                if (i > 0) Ui.Image(box, "Divider", Ui.Solid, sx + seg * i, sy, 1f, segHeight).color = Ui.Ink;
                Ui.Text(face.transform, Loc.Format("ui.mp.room.turnOption", seconds), Ui.TitleFont, 14f,
                    on ? Ui.Parchment : Ui.InkText, 0f, 0f, seg, segHeight, TextAlignmentOptions.Center);
                var button = Ui.MakeButton(face, () => _net.SetTurnSeconds(seconds));
                button.transition = on ? Selectable.Transition.None : Selectable.Transition.ColorTint;
                button.interactable = !counting;
                if (counting && !on) face.gameObject.AddComponent<CanvasGroup>().alpha = 0.5f;
            }
        }

        static void SettingRowMobile(RectTransform box, float x, float y, float w, float h, string key, string value)
        {
            Ui.Dashed(box, new Vector2(x, -y), new Vector2(x + w, -y), SoftInk, 1f, 1.5f, 3f);
            var label = Ui.Text(box, Loc.Get(key), Ui.TitleFont, 13f, Ui.InkSoft, x, y, 150f, h,
                TextAlignmentOptions.MidlineLeft);
            label.characterSpacing = 8f;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            if (value.Length > 0)
                Ui.Text(box, value, Ui.BodyFont, 15.5f, Ui.InkText, x + 150f, y, w - 150f, h,
                    TextAlignmentOptions.MidlineRight).textWrappingMode = TextWrappingModes.NoWrap;
        }

        void CommanderMobile(RectTransform parent, float x, float y, float w, float h, string label, string name, Side side,
            bool mine)
        {
            var card = Ui.Sliced(parent, "Commander", Ui.Boxed(Vellum, mine ? Ui.Rubric : Ui.Ink), x, y, w, h).rectTransform;
            CardHeaderMobile(card, label, w);
            Ui.Image(card, "Seal", Ui.UnitArt(UnitType.Sovereign, side), (w - 56f) * 0.5f, 28f, 56f, 56f);
            CardTitleMobile(card, name, Ui.InkText, w);
            Ui.Text(card, Loc.Army(side), Ui.ItalicFont, 13.5f, Ui.InkBody, 4f, 110f, w - 8f, 18f, TextAlignmentOptions.Top)
                .textWrappingMode = TextWrappingModes.NoWrap;
            var chip = Chip(card, Loc.Get("ui.mp.room.ready"), 0f, 134f, Ui.Ink, Ui.Parchment, "Icon_Check", 24f, 12f);
            chip.anchoredPosition = new Vector2((w - chip.sizeDelta.x) * 0.5f, chip.anchoredPosition.y);
        }

        void VacantMobile(RectTransform parent, float x, float y, float w, float h)
        {
            var card = Ui.Rect(parent, "Vacant", x, y, w, h);
            Ui.Image(card, "Face", Ui.Solid, 0f, 0f, w, h).color = new Color(Ui.Parchment.r, Ui.Parchment.g, Ui.Parchment.b, 0.28f);
            Ui.DashedBox(card, 0.75f, 0.75f, w - 1.5f, h - 1.5f, Ui.Ink, 1.5f, 5f, 4f);
            CardHeaderMobile(card, Loc.Get("ui.mp.room.opponent"), w);
            Ui.Sliced(card, "Seal", Ui.Pill(Color.clear, SoftInk, 28, 2f), (w - 56f) * 0.5f, 28f, 56f, 56f);
            Ui.Text(card, "?", Ui.ItalicFont, 30f, SoftInk, (w - 56f) * 0.5f, 28f, 56f, 56f, TextAlignmentOptions.Center);
            CardTitleMobile(card, Loc.Get("ui.mp.room.vacant"), Ui.InkFaded, w);
            Ui.Text(card, Loc.Get("ui.mp.room.vacantText"), Ui.ItalicFont, 13.5f, Ui.InkBody, 4f, 110f, w - 8f, 18f,
                TextAlignmentOptions.Top);
            var chip = Chip(card, Loc.Get("ui.mp.room.waitingChip"), 0f, 134f, Color.clear, Ui.InkText, height: 24f,
                fontSize: 12f, dots: true);
            chip.anchoredPosition = new Vector2((w - chip.sizeDelta.x) * 0.5f, chip.anchoredPosition.y);
        }

        static void CardHeaderMobile(RectTransform card, string label, float w)
        {
            var text = Ui.Label(card, label, 0f, 9f, w, 16f, TextAlignmentOptions.Top);
            text.fontSize = 12f;
            text.characterSpacing = 10f;
        }

        static void CardTitleMobile(RectTransform card, string name, Color color, float w)
        {
            var title = Ui.Text(card, name, Ui.TitleFont, 19f, color, 4f, 86f, w - 8f, 24f, TextAlignmentOptions.Top);
            title.textWrappingMode = TextWrappingModes.NoWrap;
            title.enableAutoSizing = true;
            title.fontSizeMin = 13f;
            title.fontSizeMax = 19f;
        }

        void StatusMobile(RectTransform parent, float x, float y, float w, float h, bool host, bool counting)
        {
            var status = Ui.Sliced(parent, "Status", Ui.Boxed(Well, Ui.Ink, 2, 0, 0), x, y, w, h).rectTransform;
            float sw = w - 16f;
            var line = Ui.Rect(status, "Line", 8f, 0f, sw, h);
            if (counting)
            {
                float left = _net.CountdownLeft - NetSession.BattleCrySeconds;
                _shownCount = Mathf.CeilToInt(left);
                string text = left > 0f
                    ? Loc.Format("ui.mp.room.countdown", $"<size=30><font=\"{Ui.TitleFont.name}\"><color={Ui.ToHex(Ui.Rubric)}>{Ui.Roman(_shownCount)}</color></font></size>")
                    : $"<font=\"{Ui.TitleFont.name}\"><size=24><color={Ui.ToHex(Ui.Rubric)}>{Loc.Get("ui.mp.room.battleCry")}</color></size></font>";
                Ui.Text(line, text, Ui.BodyFont, 16f, Ui.InkText, 0f, 0f, sw, h, TextAlignmentOptions.Center);
                return;
            }
            _shownCount = -1;
            if (_noticeUntil > Time.unscaledTime && _roomNotice != null)
                IconLine(line, "Icon_Alert", Loc.Get(_roomNotice), Ui.BodyFont, 15f, Ui.Rubric, sw, h);
            else if (!host)
                IconLine(line, "Icon_Hourglass", Loc.Get("ui.mp.room.waitHost"), Ui.BodyFont, 15f, Ui.InkText, sw, h, dots: true);
            else if (!_net.OpponentPresent)
                IconLine(line, "Icon_Hourglass", Loc.Get("ui.mp.room.waitOpponent"), Ui.BodyFont, 15f, Ui.InkText, sw, h,
                    dots: true);
            else Ui.Text(line, Loc.Get("ui.mp.room.bothReady"), Ui.BodyFont, 15f, Ui.InkText, 0f, 0f, sw, h,
                TextAlignmentOptions.Center);
        }

        void ConfirmLeaveMobile(bool host)
        {
            const float w = 440f, h = 214f, pad = 24f, gap = 10f;
            var shade = Ui.Stretch(Ui.Image(_mpLayer, "Shade", Ui.Solid, 0, 0, 0, 0, raycast: true).rectTransform);
            shade.GetComponent<Image>().color = new Color(ScrimColor.r, ScrimColor.g, ScrimColor.b, 0.5f);
            var stage = Ui.Rect(_mpLayer, "Stage", 0f, 0f, Area.x, Area.y);
            stage.anchorMin = stage.anchorMax = stage.pivot = new Vector2(0.5f, 0.5f);
            stage.anchoredPosition = Vector2.zero;
            var dialog = Ui.Modal(stage, (Area.x - w) * 0.5f, (Area.y - h) * 0.5f, w, h);
            string role = host ? "host" : "guest";
            var eyebrow = Ui.Text(dialog, Loc.Get("ui.mp.confirm.eyebrow"), Ui.TitleFont, 13f, Ui.Rubric, 0f, 20f, w, 18f,
                TextAlignmentOptions.Top);
            eyebrow.characterSpacing = 18f;
            Ui.Text(dialog, Ui.RomanInitial(Loc.Get($"ui.mp.confirm.{role}Title")), Ui.TitleFont, 26f, Ui.InkText, 0f, 40f, w,
                34f, TextAlignmentOptions.Top).textWrappingMode = TextWrappingModes.NoWrap;
            Ui.Text(dialog, Loc.Get($"ui.mp.confirm.{role}Text"), Ui.BodyFont, 15.5f, Ui.InkText, pad, 82f, w - pad * 2f, 50f,
                TextAlignmentOptions.Top);
            float half = (w - pad * 2f - gap) * 0.5f;
            Btn(dialog, Loc.Get("ui.mp.confirm.stay"), Style.Secondary, pad, 148f, half, 44f, () =>
            {
                _confirmLeave = false;
                Rebuild();
            }, fontSize: 17f);
            Btn(dialog, Loc.Get($"ui.mp.confirm.{role}Button"), Style.Primary, pad + half + gap, 148f, half, 44f, _net.Leave,
                fontSize: 17f);
        }
    }
}
