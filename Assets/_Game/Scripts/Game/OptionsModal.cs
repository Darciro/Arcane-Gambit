using System;
using ArcaneGambit.FX;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace ArcaneGambit.Game
{
    /// <summary>
    /// The Options modal (visual direction v9), shared by the main menu and the match's pause. Changes are a draft
    /// until Apply; volumes are heard and the modal reads in the chosen language right away. The frame rate limit
    /// and accessibility groups are not in yet.
    /// </summary>
    public sealed class OptionsModal
    {
        enum OptionList { None, Mode, Resolution }

        static float StageWidth => UiProfile.Design.x;
        static float StageHeight => UiProfile.Design.y;
        const float OptWidth = 1200f, OptPad = 56f, OptColumn = 516f, OptControl = 300f, OptRow = 70f, OptTallRow = 80f;
        const float OptHeading = 28f, OptGroupGap = 22f, OptSelect = 44f, OptItem = 40f, KeepSeconds = 12f;
        const float DisabledAlpha = 0.45f;
        const int OptVisibleItems = 6;
        static readonly Color Scrim = new Color(0.125f, 0.082f, 0.039f, 0.62f);
        static readonly Color Well = new Color(0.890f, 0.827f, 0.694f);
        static readonly Color SoftInk = new Color(0.231f, 0.165f, 0.102f, 0.45f);
        static readonly Color Field = new Color(1f, 0.98f, 0.933f, 0.6f);
        static readonly Color KnobOff = new Color(0.627f, 0.541f, 0.4f);
        static readonly Color Muted = new Color(0.541f, 0.451f, 0.314f);

        readonly Func<RectTransform> _parent;
        readonly Action _languageChanged, _closed;
        RectTransform _layer;
        GameOptions _draft, _revert;
        OptionList _openList;
        float _keepUntil = -1f;
        int _keepShown;
        TextMeshProUGUI _keepText, _dirtyLabel;
        Button _applyButton;

        bool KeepPending => _keepUntil >= 0f;

        /// <summary>
        /// <paramref name="languageChanged"/> may rebuild the host's whole UI: the modal asks for
        /// <paramref name="parent"/> again afterwards when it has to stay open.
        /// </summary>
        public OptionsModal(Func<RectTransform> parent, Action languageChanged, Action closed)
        {
            _parent = parent;
            _languageChanged = languageChanged;
            _closed = closed;
            GameSettings.SyncScreen();
            _draft = GameSettings.Current;
            NewLayer();
            Rebuild();
        }

        void NewLayer()
        {
            if (_layer != null) UnityEngine.Object.Destroy(_layer.gameObject);
            _layer = Ui.Stretch(Ui.Rect(_parent(), "Options", 0, 0, 0, 0));
        }

        void Close()
        {
            if (KeepPending) return;
            GameSettings.Preview(GameSettings.Current);
            Loc.Language = GameSettings.Current.Language;
            UnityEngine.Object.Destroy(_layer.gameObject);
            _layer = null;
            _closed();
        }

        // A new screen mode has to be confirmed first, so only then does the modal outlive Apply.
        void Apply()
        {
            var before = GameSettings.Current;
            if (KeepPending || _draft.Same(before)) return;
            _openList = OptionList.None;
            GameSettings.Apply(_draft);
            bool screen = !_draft.SameScreen(before);
            if (screen)
            {
                _revert = before;
                _keepUntil = Time.unscaledTime + KeepSeconds;
            }
            else Close();
            if (_draft.Language != before.Language) _languageChanged();
            if (!screen) return;
            NewLayer();
            Rebuild();
        }

        void Restore()
        {
            var defaults = GameSettings.Defaults;
            defaults.Language = _draft.Language;
            if (!GameSettings.ScreenAdjustable)
            {
                defaults.Mode = _draft.Mode;
                defaults.Width = _draft.Width;
                defaults.Height = _draft.Height;
            }
            _draft = defaults;
            OnOptionChanged();
        }

        void OnOptionChanged()
        {
            _openList = OptionList.None;
            GameSettings.Preview(_draft);
            Rebuild();
        }

        void KeepScreen()
        {
            _keepUntil = -1f;
            Close();
        }

        void RevertScreen()
        {
            _keepUntil = -1f;
            var options = GameSettings.Current;
            options.Mode = _revert.Mode;
            options.Width = _revert.Width;
            options.Height = _revert.Height;
            GameSettings.Apply(options);
            _draft = options;
            Rebuild();
        }

        /// <summary>For a host that rebuilt its UI, or whose screen profile changed, while the modal is open.</summary>
        public void Relayout()
        {
            NewLayer();
            Rebuild();
        }

        /// <summary>Call every frame while open; pass no keyboard to hold its keys back.</summary>
        public void Update(Keyboard keys)
        {
            if (KeepPending)
            {
                float left = _keepUntil - Time.unscaledTime;
                if (left <= 0f)
                {
                    RevertScreen();
                    return;
                }
                int seconds = Mathf.CeilToInt(left);
                if (seconds != _keepShown)
                {
                    _keepShown = seconds;
                    _keepText.text = KeepText(seconds);
                }
            }
            if (keys == null) return;

            bool escape = keys.escapeKey.wasPressedThisFrame;
            bool enter = keys.enterKey.wasPressedThisFrame || keys.numpadEnterKey.wasPressedThisFrame;
            if (KeepPending)
            {
                if (escape) RevertScreen();
                else if (enter) KeepScreen();
            }
            else if (_openList != OptionList.None)
            {
                if (!escape) return;
                _openList = OptionList.None;
                Rebuild();
            }
            else if (escape) Close();
            else if (enter) Apply();
        }

        void Rebuild()
        {
            for (int i = _layer.childCount - 1; i >= 0; i--) UnityEngine.Object.Destroy(_layer.GetChild(i).gameObject);
            Loc.Language = _draft.Language;
            if (UiProfile.Mobile)
            {
                RebuildMobile();
                return;
            }

            const float w = OptWidth, top = 146f;
            float left = OptPad, right = OptPad + OptColumn + OptPad;
            float body = OptHeading + OptRow * 2f + OptTallRow + OptGroupGap + OptHeading + OptTallRow;
            float footY = top + body + 22f, buttonY = footY + 20f, h = buttonY + 56f + 32f;

            var scrim = Ui.Stretch(Ui.Image(_layer, "Scrim", Ui.Solid, 0, 0, 0, 0, raycast: true).rectTransform);
            scrim.GetComponent<Image>().color = Scrim;
            var stage = UiProfile.ModalStage(_layer, w, h);
            var panel = Ui.Modal(stage, (StageWidth - w) * 0.5f, (StageHeight - h) * 0.5f, w, h);
            Eyebrow(panel, Loc.Get("ui.options.eyebrow"), 40f, w);
            Ui.Text(panel, Ui.RomanInitial(Loc.Get("ui.options.title")), Ui.TitleFont, 56f, Ui.InkText, 0f, 66f, w, 70f,
                TextAlignmentOptions.Top).textWrappingMode = TextWrappingModes.NoWrap;

            var close = Ui.Sliced(panel, "Close", Ui.Pill(Color.clear, Ui.Ink, 23, 1.5f), w - 30f - 46f, 28f, 46f, 46f);
            Stroke(close.transform, 23f, 23f, 17f, 45f);
            Stroke(close.transform, 23f, 23f, 17f, -45f);
            Ui.MakeButton(close, Close);

            float y = GroupHeading(panel, "ui.options.audio", left, top);
            y = ToggleRow(panel, left, y, "ui.options.sfx", _draft.Sfx, true, () =>
            {
                _draft.Sfx = !_draft.Sfx;
                OnOptionChanged();
            });
            y = SliderRow(panel, left, y, OptRow, "ui.options.master", null, _draft.Master, true, v => _draft.Master = v);
            y = SliderRow(panel, left, y, OptTallRow, "ui.options.effects", _draft.Sfx ? null : "ui.options.effectsOff",
                _draft.Effects, _draft.Sfx, v => _draft.Effects = v);
            SliderRow(panel, left, y, OptRow, "ui.options.music", null, _draft.Music, true, v => _draft.Music = v);

            bool screen = GameSettings.ScreenAdjustable, borderless = _draft.Mode == DisplayMode.Borderless;
            y = GroupHeading(panel, "ui.options.video", right, top);
            float modeY = y;
            y = SelectRow(panel, right, y, OptRow, "ui.options.mode", null, Loc.Get("ui.options.mode." + _draft.Mode),
                screen, OptionList.Mode, true);
            float resolutionY = y;
            y = SelectRow(panel, right, y, OptTallRow, "ui.options.resolution",
                borderless ? "ui.options.resolutionNote" : null, ResolutionLabel(new Vector2Int(_draft.Width, _draft.Height)),
                screen && !borderless, OptionList.Resolution);
            y = ToggleRow(panel, right, y, "ui.options.vsync", _draft.VSync, false, () =>
            {
                _draft.VSync = !_draft.VSync;
                OnOptionChanged();
            });
            y = GroupHeading(panel, "ui.options.language", right, y + OptGroupGap);
            LanguageRow(panel, right, y);

            Ui.Line(panel, OptPad, footY, w - OptPad * 2f, 1.5f);
            string restoreLabel = Loc.Get("ui.options.restore");
            var restore = Ui.Image(panel, "Restore", Ui.Solid, OptPad - 10f, buttonY + 6f, 300f, 44f, raycast: true);
            restore.color = Color.clear;
            Ui.MakeButton(restore, Restore).transition = Selectable.Transition.None;
            var restoreText = Ui.Text(restore.transform, restoreLabel, Ui.TitleFont, 20f, Ui.Ink, 0f, 0f, 300f, 44f,
                TextAlignmentOptions.Center);
            restoreText.textWrappingMode = TextWrappingModes.NoWrap;
            float restoreWidth = restoreText.GetPreferredValues(restoreLabel).x + 20f;
            restore.rectTransform.sizeDelta = restoreText.rectTransform.sizeDelta = new Vector2(restoreWidth, 44f);
            _dirtyLabel = Ui.Text(panel, Loc.Get("ui.options.dirty"), Ui.ItalicFont, 18f, Ui.Rubric,
                OptPad + restoreWidth + 4f, buttonY, 360f, 56f, TextAlignmentOptions.MidlineLeft);
            const float buttonWidth = 170f;
            Ui.Button(panel, Loc.Get("ui.options.cancel"), false, w - OptPad - buttonWidth * 2f - 14f, buttonY,
                buttonWidth, 56f, Close, 22f);
            _applyButton = Ui.Button(panel, Loc.Get("ui.options.apply"), true, w - OptPad - buttonWidth, buttonY,
                buttonWidth, 56f, Apply, 22f);
            var colors = _applyButton.colors;
            colors.disabledColor = Color.white;
            _applyButton.colors = colors;
            _applyButton.gameObject.AddComponent<CanvasGroup>();
            RefreshFooter();

            float listX = right + OptColumn - OptControl;
            if (_openList == OptionList.Mode)
                ModeList(panel, listX, modeY + (OptRow + OptSelect) * 0.5f + 4f);
            else if (_openList == OptionList.Resolution)
                ResolutionList(panel, listX, resolutionY + (OptTallRow + OptSelect) * 0.5f + 4f);
            if (KeepPending) KeepDialog(panel, w, h);
        }

        void RefreshFooter()
        {
            if (UiProfile.Mobile) return;
            bool dirty = !_draft.Same(GameSettings.Current);
            _dirtyLabel.enabled = dirty;
            bool ready = dirty && !KeepPending;
            _applyButton.interactable = ready;
            _applyButton.GetComponent<CanvasGroup>().alpha = ready ? 1f : DisabledAlpha;
        }

        static float GroupHeading(RectTransform panel, string key, float x, float y)
        {
            string text = Loc.Get(key);
            var heading = Ui.Text(panel, text, Ui.TitleFont, 25f, Ui.InkText, x, y, OptColumn, OptHeading,
                TextAlignmentOptions.MidlineLeft);
            heading.textWrappingMode = TextWrappingModes.NoWrap;
            float start = heading.GetPreferredValues(text).x + 12f;
            Ui.Line(panel, x + start, y + 13f, OptColumn - start, 1f, 0.7f);
            Ui.Line(panel, x + start, y + 16f, OptColumn - start, 0.6f, 0.45f);
            return y + OptHeading;
        }

        static void RowLabel(RectTransform panel, float x, float y, float h, string key, string note, bool first,
            bool off = false)
        {
            if (!first) Ui.Dashed(panel, new Vector2(x, -y), new Vector2(x + OptColumn, -y), SoftInk, 1f, 1.5f, 3f);
            float w = OptColumn - OptControl - 16f;
            var color = off ? Muted : Ui.InkText;
            if (note == null)
            {
                Ui.Text(panel, Loc.Get(key), Ui.BodyFont, 20f, color, x, y, w, h, TextAlignmentOptions.MidlineLeft);
                return;
            }
            Ui.Text(panel, Loc.Get(key), Ui.BodyFont, 20f, color, x, y + 7f, w, 26f, TextAlignmentOptions.MidlineLeft);
            Ui.Text(panel, Loc.Get(note), Ui.ItalicFont, 15.5f, Ui.InkSoft, x, y + 34f, w, 40f);
        }

        static float ToggleRow(RectTransform panel, float x, float y, string key, bool on, bool first, UnityAction onClick)
        {
            RowLabel(panel, x, y, OptRow, key, null, first);
            var hit = Ui.Image(panel, "Toggle", Ui.Solid, x + OptColumn - OptControl, y + OptRow * 0.5f - 15f, 200f, 30f,
                raycast: true);
            hit.color = Color.clear;
            Ui.Sliced(hit.transform, "Track", Ui.Pill(on ? Well : Field, Ui.Ink, 15, 1.5f), 0f, 0f, 58f, 30f);
            Ui.Image(hit.transform, "Knob", Ui.Pill(on ? Ui.Rubric : KnobOff, Color.clear, 10, 0f), on ? 32.5f : 4.5f,
                4.5f, 21f, 21f);
            Ui.Text(hit.transform, Loc.Get(on ? "ui.options.on" : "ui.options.off"), Ui.TitleFont, 18f, Ui.InkText,
                70f, 0f, 130f, 30f, TextAlignmentOptions.MidlineLeft);
            Ui.MakeButton(hit, onClick).transition = Selectable.Transition.None;
            return y + OptRow;
        }

        // The slider stays alive while it is dragged: only its number and the footer follow the value.
        float SliderRow(RectTransform panel, float x, float y, float h, string key, string note, int value, bool enabled,
            Action<int> set)
        {
            const float track = OptControl - 52f;
            RowLabel(panel, x, y, h, key, note, false, !enabled);
            float cx = x + OptColumn - OptControl, cy = y + h * 0.5f;
            var number = Ui.Text(panel, value.ToString(), Ui.TitleFont, 20f, enabled ? Ui.InkText : Muted,
                cx + track + 12f, cy - 15f, 40f, 30f, TextAlignmentOptions.MidlineRight);
            BuildSlider(panel, cx, cy - 12f, track, 24f, 16f, value, enabled, number, set);
            return y + h;
        }

        void BuildSlider(Transform parent, float x, float y, float track, float height, float knobSize, int value,
            bool enabled, TextMeshProUGUI number, Action<int> set)
        {
            const float groove = 6f;
            var root = Ui.Image(parent, "Slider", Ui.Solid, x, y, track, height, raycast: true);
            root.color = Color.clear;
            float grooveY = (height - groove) * 0.5f;
            Ui.Sliced(root.transform, "Track", Ui.Pill(Field, Ui.Ink, 3), 0f, grooveY, track, groove);
            var fill = Ui.Sliced(Ui.Rect(root.transform, "Fill Area", 0f, grooveY, track, groove), "Fill",
                Ui.Pill(Ui.Rubric, Ui.Ink, 3), 0f, 0f, 0f, 0f);
            Ui.Stretch(fill.rectTransform);
            var area = Ui.Rect(root.transform, "Handle Area", knobSize * 0.5f, 0f, track - knobSize, height);
            var handle = Ui.Rect(area, "Handle", 0f, 0f, 0f, 0f);
            handle.anchorMin = Vector2.zero;
            handle.anchorMax = new Vector2(0f, 1f);
            handle.pivot = new Vector2(0.5f, 0.5f);
            handle.offsetMin = handle.offsetMax = Vector2.zero;
            var knob = Ui.Sliced(handle, "Knob", Ui.Boxed(Ui.Parchment, Ui.Ink, 2, 0, 0), 0f, 0f, knobSize, knobSize)
                .rectTransform;
            knob.anchorMin = knob.anchorMax = knob.pivot = new Vector2(0.5f, 0.5f);
            knob.anchoredPosition = Vector2.zero;
            knob.localRotation = Quaternion.Euler(0f, 0f, 45f);

            var slider = root.gameObject.AddComponent<Slider>();
            slider.transition = Selectable.Transition.None;
            slider.navigation = new Navigation { mode = Navigation.Mode.None };
            slider.fillRect = fill.rectTransform;
            slider.handleRect = handle;
            slider.wholeNumbers = true;
            slider.minValue = 0f;
            slider.maxValue = GameSettings.VolumeMax / GameSettings.VolumeStep;
            slider.SetValueWithoutNotify(value / GameSettings.VolumeStep);
            slider.interactable = enabled;
            if (!enabled) root.gameObject.AddComponent<CanvasGroup>().alpha = 0.4f;
            slider.onValueChanged.AddListener(v =>
            {
                int volume = Mathf.RoundToInt(v) * GameSettings.VolumeStep;
                set(volume);
                number.text = volume.ToString();
                GameSettings.Preview(_draft);
                RefreshFooter();
            });
        }

        // The "Opções" board of the mobile-horizontal doc: a full page in two columns with Done in its header.
        // A phone has no screen mode to choose, and the reduce-motion switch moves here from the pause menu.
        void RebuildMobile()
        {
            const float header = 56f, gutter = 18f, top = 60f, done = 44f;
            float side = UiProfile.SideInset;
            var page = Ui.Stretch(Ui.Image(_layer, "Page", Ui.Solid, 0, 0, 0, 0, raycast: true).rectTransform);
            page.GetComponent<Image>().color = Ui.Parchment;
            float width = page.rect.width;

            Ui.Line(page, 0f, header - 1.5f, width, 1.5f);
            Ui.Line(page, 0f, header + 2f, width, 1f);
            var back = Ui.IconButton(page, side, (header - 44f) * 0.5f, Close);
            Ui.Chevron(back.transform, 22f, 22f, 10f, -1, Ui.Ink);
            string eyebrow = $"<size=43%><color={Ui.ToHex(Ui.InkSoft)}><cspace=0.2em>  {Loc.Get("ui.options.eyebrow")}</cspace></color></size>";
            Ui.Text(page, Ui.RomanInitial(Loc.Get("ui.options.title")) + eyebrow, Ui.TitleFont, 28f, Ui.InkText, 0f, 0f,
                width, header, TextAlignmentOptions.Center).textWrappingMode = TextWrappingModes.NoWrap;
            string doneLabel = Loc.Get("ui.m.options.done");
            var doneButton = Ui.Button(page, doneLabel, true, 0f, (header - done) * 0.5f, 100f, done, Done, 17f);
            var doneText = doneButton.GetComponentInChildren<TextMeshProUGUI>();
            var doneSize = new Vector2(doneText.GetPreferredValues(doneLabel).x + 32f, done);
            var doneRect = (RectTransform)doneButton.transform;
            doneRect.sizeDelta = doneText.rectTransform.sizeDelta = doneSize;
            doneRect.anchoredPosition = new Vector2(width - side - doneSize.x, doneRect.anchoredPosition.y);

            float column = (width - side * 2f) * 0.5f - gutter, right = width * 0.5f + gutter;
            float y = MobileHeading(page, "ui.options.audio", side, top, column);
            y = MobileToggle(page, side, y, column, "ui.options.sfx", null, _draft.Sfx, true, () =>
            {
                _draft.Sfx = !_draft.Sfx;
                OnOptionChanged();
            });
            y = MobileSlider(page, side, y, column, "ui.options.master", null, _draft.Master, true, v => _draft.Master = v);
            y = MobileSlider(page, side, y, column, "ui.options.effects", _draft.Sfx ? null : "ui.options.effectsOff",
                _draft.Effects, _draft.Sfx, v => _draft.Effects = v);
            MobileSlider(page, side, y, column, "ui.options.music", null, _draft.Music, true, v => _draft.Music = v);

            y = MobileHeading(page, "ui.options.language", right, top, column);
            y = MobileLanguage(page, right, y, column);
            y = MobileHeading(page, "ui.m.options.access", right, y, column);
            y = MobileToggle(page, right, y, column, "ui.m.options.reduce", "ui.m.options.reduceNote",
                FxSettings.ReduceFlashesAndShakes, true, () =>
                {
                    FxSettings.ReduceFlashesAndShakes = !FxSettings.ReduceFlashesAndShakes;
                    Rebuild();
                });

            string restoreLabel = $"<u>{Loc.Get("ui.options.restore")}</u>";
            var restore = Ui.Image(page, "Restore", Ui.Solid, right, y + 6f, column, 44f, raycast: true);
            restore.color = Color.clear;
            var restoreText = Ui.Text(restore.transform, restoreLabel, Ui.ItalicFont, 16f, Ui.Rubric, 0f, 0f, column, 44f,
                TextAlignmentOptions.MidlineLeft);
            restore.rectTransform.sizeDelta = new Vector2(restoreText.GetPreferredValues(restoreLabel).x + 12f, 44f);
            Ui.MakeButton(restore, Restore).transition = Selectable.Transition.None;
        }

        // The page has no Apply: Done keeps what was chosen and the back arrow drops it.
        void Done()
        {
            if (_draft.Same(GameSettings.Current)) Close();
            else Apply();
        }

        static float MobileHeading(RectTransform page, string key, float x, float y, float w)
        {
            const float height = 21f;
            y += 14f;
            string text = Loc.Get(key);
            var heading = Ui.Text(page, text, Ui.TitleFont, 19f, Ui.InkText, x, y, w, height, TextAlignmentOptions.MidlineLeft);
            heading.textWrappingMode = TextWrappingModes.NoWrap;
            float start = heading.GetPreferredValues(text).x + 10f;
            Ui.Line(page, x + start, y + 10f, w - start, 1f, 0.7f);
            Ui.Line(page, x + start, y + 13f, w - start, 0.6f, 0.45f);
            return y + height + 2f;
        }

        static void MobileDivider(RectTransform page, float x, float y, float w) =>
            Ui.Dashed(page, new Vector2(x, -y), new Vector2(x + w, -y), SoftInk, 1f, 1.5f, 3f);

        static float MobileToggle(RectTransform page, float x, float y, float w, string key, string note, bool on,
            bool first, UnityAction onClick)
        {
            const float track = 54f, hitWidth = 60f;
            float h = note == null ? 50f : 58f, labelWidth = w - hitWidth - 14f;
            if (!first) MobileDivider(page, x, y, w);
            if (note == null)
                Ui.Text(page, Loc.Get(key), Ui.BodyFont, 17f, Ui.InkText, x, y, labelWidth, h, TextAlignmentOptions.MidlineLeft);
            else
            {
                Ui.Text(page, Loc.Get(key), Ui.BodyFont, 17f, Ui.InkText, x, y + 8f, labelWidth, 22f,
                    TextAlignmentOptions.MidlineLeft);
                Ui.Text(page, Loc.Get(note), Ui.ItalicFont, 14f, Ui.InkSoft, x, y + 31f, labelWidth, 20f);
            }
            var hit = Ui.Image(page, "Toggle", Ui.Solid, x + w - hitWidth, y + (h - 44f) * 0.5f, hitWidth, 44f, raycast: true);
            hit.color = Color.clear;
            Ui.Sliced(hit.transform, "Track", Ui.Pill(on ? Well : Field, Ui.Ink, 15, 1.5f), hitWidth - track, 7f, track, 30f);
            Ui.Image(hit.transform, "Knob", Ui.Pill(on ? Ui.Rubric : KnobOff, Color.clear, 10, 0f),
                hitWidth - track + (on ? 28.5f : 4.5f), 11.5f, 21f, 21f);
            Ui.MakeButton(hit, onClick).transition = Selectable.Transition.None;
            return y + h;
        }

        float MobileSlider(RectTransform page, float x, float y, float w, string key, string note, int value, bool enabled,
            Action<int> set)
        {
            MobileDivider(page, x, y, w);
            var color = enabled ? Ui.InkText : Muted;
            Ui.Text(page, Loc.Get(key), Ui.BodyFont, 17f, color, x, y + 6f, w - 60f, 22f, TextAlignmentOptions.MidlineLeft);
            var number = Ui.Text(page, value.ToString(), Ui.TitleFont, 16.5f, color, x + w - 60f, y + 6f, 60f, 22f,
                TextAlignmentOptions.MidlineRight);
            float sliderY = y + 30f;
            if (note != null)
            {
                Ui.Text(page, Loc.Get(note), Ui.ItalicFont, 14f, Ui.InkSoft, x, sliderY - 2f, w, 20f);
                sliderY += 16f;
            }
            BuildSlider(page, x, sliderY, w, 30f, 20f, value, enabled, number, set);
            return sliderY + 30f + 6f;
        }

        float MobileLanguage(RectTransform page, float x, float y, float w)
        {
            const float height = 44f;
            Ui.Text(page, Loc.Get("ui.options.languageRow"), Ui.BodyFont, 17f, Ui.InkText, x, y + 6f, w, 22f,
                TextAlignmentOptions.MidlineLeft);
            Ui.Text(page, Loc.Get("ui.options.languageNote"), Ui.ItalicFont, 14f, Ui.InkSoft, x, y + 28f, w, 20f);
            var languages = Loc.Languages;
            float seg = w / languages.Length, sy = y + 54f;
            for (int i = 0; i < languages.Length; i++)
            {
                string language = languages[i];
                bool on = language == _draft.Language;
                var face = Ui.Image(page, "Segment", Ui.Solid, x + seg * i, sy, seg, height, raycast: true);
                face.color = on ? Ui.Ink : Field;
                if (i > 0) Ui.Image(page, "Divider", Ui.Solid, x + seg * i, sy, 1f, height).color = Ui.Ink;
                Ui.Text(face.transform, Loc.Get("ui.options.lang." + language), Ui.TitleFont, 16f,
                    on ? Ui.Parchment : Ui.InkText, 0f, 0f, seg, height, TextAlignmentOptions.Center);
                Ui.MakeButton(face, () =>
                {
                    _draft.Language = language;
                    OnOptionChanged();
                }).transition = Selectable.Transition.None;
            }
            Ui.Sliced(page, "Segments", Ui.Outline, x, sy, w, height);
            return sy + height + 8f;
        }

        float SelectRow(RectTransform panel, float x, float y, float h, string key, string note, string value,
            bool enabled, OptionList list, bool first = false)
        {
            RowLabel(panel, x, y, h, key, note, first, !enabled);
            var face = Ui.Sliced(panel, "Select", Ui.Boxed(Field, Ui.Ink, 2, 0, 0), x + OptColumn - OptControl,
                y + (h - OptSelect) * 0.5f, OptControl, OptSelect);
            Ui.Text(face.transform, value, Ui.BodyFont, 19f, Ui.InkText, 14f, 0f, OptControl - 54f, OptSelect,
                TextAlignmentOptions.MidlineLeft).textWrappingMode = TextWrappingModes.NoWrap;
            Stroke(face.transform, OptControl - 24f, OptSelect * 0.5f, 7f, -45f);
            Stroke(face.transform, OptControl - 20f, OptSelect * 0.5f, 7f, 45f);
            if (!enabled)
            {
                face.gameObject.AddComponent<CanvasGroup>().alpha = DisabledAlpha;
                return y + h;
            }
            Ui.MakeButton(face, () =>
            {
                _openList = _openList == list ? OptionList.None : list;
                Rebuild();
            });
            return y + h;
        }

        void LanguageRow(RectTransform panel, float x, float y)
        {
            const float height = 42f;
            RowLabel(panel, x, y, OptTallRow, "ui.options.languageRow", "ui.options.languageNote", true);
            var languages = Loc.Languages;
            float seg = OptControl / languages.Length, sx = x + OptColumn - OptControl, sy = y + (OptTallRow - height) * 0.5f;
            for (int i = 0; i < languages.Length; i++)
            {
                string language = languages[i];
                bool on = language == _draft.Language;
                var face = Ui.Image(panel, "Segment", Ui.Solid, sx + seg * i, sy, seg, height, raycast: true);
                face.color = on ? Ui.Ink : Field;
                if (i > 0) Ui.Image(panel, "Divider", Ui.Solid, sx + seg * i, sy, 1f, height).color = Ui.Ink;
                Ui.Text(face.transform, Loc.Get("ui.options.lang." + language), Ui.TitleFont, 17f,
                    on ? Ui.Parchment : Ui.InkText, 0f, 0f, seg, height, TextAlignmentOptions.Center);
                var button = Ui.MakeButton(face, () =>
                {
                    _draft.Language = language;
                    OnOptionChanged();
                });
                button.transition = on ? Selectable.Transition.None : Selectable.Transition.ColorTint;
            }
            Ui.Sliced(panel, "Segments", Ui.Outline, sx, sy, OptControl, height);
        }

        void ModeList(RectTransform panel, float x, float y)
        {
            var modes = (DisplayMode[])Enum.GetValues(typeof(DisplayMode));
            var labels = Array.ConvertAll(modes, m => Loc.Get("ui.options.mode." + m));
            OptionsList(panel, x, y, labels, Array.IndexOf(modes, _draft.Mode), i =>
            {
                _draft.Mode = modes[i];
                if (modes[i] != DisplayMode.Borderless) return;
                _draft.Width = GameSettings.Desktop.x;
                _draft.Height = GameSettings.Desktop.y;
            });
        }

        void ResolutionList(RectTransform panel, float x, float y)
        {
            var current = new Vector2Int(_draft.Width, _draft.Height);
            var sizes = GameSettings.Resolutions(current);
            OptionsList(panel, x, y, sizes.ConvertAll(ResolutionLabel).ToArray(), sizes.IndexOf(current), i =>
            {
                _draft.Width = sizes[i].x;
                _draft.Height = sizes[i].y;
            });
        }

        static string ResolutionLabel(Vector2Int size) => Loc.Format("ui.options.resolutionValue", size.x, size.y);

        void OptionsList(RectTransform panel, float x, float y, string[] labels, int selected, Action<int> pick)
        {
            const float border = 2f, far = 4000f;
            var away = Ui.Image(panel, "Away", Ui.Solid, -far, -far, far * 2f, far * 2f, raycast: true);
            away.color = Color.clear;
            Ui.MakeButton(away, () =>
            {
                _openList = OptionList.None;
                Rebuild();
            }).transition = Selectable.Transition.None;

            int visible = Mathf.Min(labels.Length, OptVisibleItems);
            float view = visible * OptItem, total = labels.Length * OptItem, itemWidth = OptControl - border * 2f;
            var box = Ui.Sliced(panel, "List", Ui.Boxed(Ui.Parchment, Ui.Ink, 2, 0, 0), x, y, OptControl,
                view + border * 2f, raycast: true);
            Transform content;
            if (labels.Length > visible)
            {
                var scroll = Ui.Scroll(box.transform, border, border, itemWidth, view);
                scroll.content.sizeDelta = new Vector2(0f, total);
                scroll.content.anchoredPosition = new Vector2(0f,
                    Mathf.Clamp(selected * OptItem - (view - OptItem) * 0.5f, 0f, total - view));
                content = scroll.content;
                itemWidth -= 18f;
            }
            else content = Ui.Rect(box.transform, "Items", border, border, itemWidth, view);

            var ink = Ui.Ink;
            for (int i = 0; i < labels.Length; i++)
            {
                int index = i;
                bool on = i == selected;
                var face = Ui.Image(content, "Item", Ui.Solid, 0f, i * OptItem, itemWidth, OptItem, raycast: true);
                Ui.Text(face.transform, labels[i], Ui.BodyFont, 19f, on ? Ui.Parchment : Ui.InkText, 12f, 0f,
                    itemWidth - 24f, OptItem, TextAlignmentOptions.MidlineLeft).textWrappingMode = TextWrappingModes.NoWrap;
                var button = Ui.MakeButton(face, () =>
                {
                    pick(index);
                    OnOptionChanged();
                });
                // The hover is the button's own tint: an EventTrigger here would swallow the wheel before the scroll.
                var colors = button.colors;
                colors.normalColor = colors.selectedColor = on ? ink : Color.clear;
                colors.highlightedColor = on ? ink : new Color(ink.r, ink.g, ink.b, 0.12f);
                colors.pressedColor = on ? ink : new Color(ink.r, ink.g, ink.b, 0.25f);
                colors.fadeDuration = 0f;
                button.colors = colors;
            }
        }

        void KeepDialog(RectTransform panel, float w, float h)
        {
            const float dw = 640f, dh = 254f, bw = 200f;
            var shade = Ui.Image(panel, "Shade", Ui.Solid, 0f, 0f, w, h, raycast: true);
            shade.color = new Color(Scrim.r, Scrim.g, Scrim.b, 0.5f);
            var dialog = Ui.Modal(panel, (w - dw) * 0.5f, (h - dh) * 0.5f, dw, dh);
            Eyebrow(dialog, Loc.Get("ui.options.video"), 32f, dw);
            Ui.Text(dialog, Loc.Get("ui.options.keep.title"), Ui.TitleFont, 40f, Ui.InkText, 0f, 60f, dw, 46f,
                TextAlignmentOptions.Top).textWrappingMode = TextWrappingModes.NoWrap;
            _keepShown = Mathf.CeilToInt(_keepUntil - Time.unscaledTime);
            _keepText = Ui.Text(dialog, KeepText(_keepShown), Ui.BodyFont, 21f, Ui.InkText, 60f, 116f, dw - 120f, 32f,
                TextAlignmentOptions.Top);
            float bx = (dw - bw * 2f - 14f) * 0.5f;
            Ui.Button(dialog, Loc.Get("ui.options.keep.no"), false, bx, 168f, bw, 56f, RevertScreen, 22f);
            Ui.Button(dialog, Loc.Get("ui.options.keep.yes"), true, bx + bw + 14f, 168f, bw, 56f, KeepScreen, 22f);
        }

        static string KeepText(int seconds) => Loc.Format("ui.options.keep.text",
            $"<font=\"{Ui.TitleFont.name}\"><color={Ui.ToHex(Ui.Rubric)}>{Loc.Format("ui.options.keep.seconds", seconds)}</color></font>");

        static void Eyebrow(RectTransform panel, string text, float y, float w) =>
            Ui.Text(panel, text, Ui.TitleFont, 18f, Ui.Rubric, 0f, y, w, 24f, TextAlignmentOptions.Top).characterSpacing = 20f;

        static void Stroke(Transform parent, float centerX, float centerY, float length, float angle)
        {
            var stroke = Ui.Image(parent, "Stroke", Ui.Solid, centerX - length * 0.5f, centerY - 1f, length, 2f);
            stroke.color = Ui.Ink;
            Ui.Centered(stroke.rectTransform).localRotation = Quaternion.Euler(0f, 0f, angle);
        }
    }
}
