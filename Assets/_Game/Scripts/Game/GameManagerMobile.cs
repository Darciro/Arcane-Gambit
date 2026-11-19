using System.Collections.Generic;
using ArcaneGambit.Rules;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ArcaneGambit.Game
{
    /// <summary>
    /// The match on a landscape phone ("mobile-horizontal" design doc, 844 x 390): the board takes the whole height
    /// on the left and everything that is touched sits in a column on the right. Only built when
    /// <see cref="UiProfile.Mobile"/> is set; the desktop HUD in GameManager.cs is untouched by it.
    /// </summary>
    public sealed partial class GameManager
    {
        enum InfoTab { None, Conditions, History, Discard }

        const float MColumn = 350f, MGutter = 7f, MSheetPad = 16f, MSheetInner = 341f, MBand = 36f, MHandTop = 265f;
        const float MHandHeight = 104f, MFanRadius = 560f, MFanStep = 6f, MFanSpread = 24f, MFanLift = 18f;
        const float MBottom = 21f, MBoardView = 685f, MToastWidth = 380f, ViewerCardScale = 0.95f;

        static float s_side;

        static bool MobileInsetChanged => UiProfile.Mobile && !Mathf.Approximately(UiProfile.SideInset, s_side);

        int _zoom = -1;
        InfoTab _infoTab;
        RectTransform _column, _zoomLayer, _infoConditions, _infoDiscard;
        CanvasGroup _actionSheet, _infoGroup, _infoConditionsGroup, _infoHistoryGroup, _infoDiscardGroup;
        float _boardWidth;
        TextMeshProUGUI _handTitle, _handHint, _handPrompt;
        GameObject _handCancel;
        static readonly InfoTab[] InfoTabs = { InfoTab.Conditions, InfoTab.History, InfoTab.Discard };
        readonly Image[] _infoTabFaces = new Image[InfoTabs.Length];
        readonly TextMeshProUGUI[] _infoTabLabels = new TextMeshProUGUI[InfoTabs.Length];

        bool ModalUp => _paused || _netLost || _awaitingStart || _match.Phase == Phase.GameOver
            || _weatherNotices.Count > 0
            || (_match.Phase == Phase.Reaction && _match.Pending.Defender.Side == _human)
            || (_match.Phase == Phase.Foresight && _match.Current == _human);

        static void ApplyMobileProfile()
        {
            s_side = UiProfile.SideInset;
            HandScale = 0.46f;
            ActionCardScale = 0.42f;
            PlayCardScale = 0.8f;
        }

        // The design crops the board art to its squares and labels, and lets it run from the top edge to the bottom.
        void FitBoardMobile(Vector2 canvas)
        {
            const float viewHeight = BoardSize - BoardMargin;
            float scale = Mathf.Min(canvas.y / viewHeight, (canvas.x - s_side * 2f - MColumn - MGutter) / MBoardView);
            float top = (canvas.y - viewHeight * scale) * 0.5f;
            _boardRig.localScale = new Vector3(scale, scale, 1f);
            _boardRig.anchoredPosition = new Vector2(s_side - canvas.x * 0.5f, BoardMargin * scale - top);
            _boardWidth = MBoardView * scale;
            if (_playSlot != null) _playSlot.anchoredPosition = new Vector2(s_side + _boardWidth * 0.5f, 0f);
            if (_toasts != null) PlaceToastsMobile();
        }

        // Toasts stand over the top of the board, the one place they cover nothing that is touched.
        void PlaceToastsMobile()
        {
            _toasts.localScale = Vector3.one;
            _toasts.anchorMin = _toasts.anchorMax = new Vector2(0f, 1f);
            _toasts.anchoredPosition = new Vector2(s_side + Mathf.Max(_boardWidth, MToastWidth) * 0.5f, -10f);
        }

        void ResultTextMobile(RectTransform toast, string title, string text, List<string> chips, float x)
        {
            float width = MToastWidth - x - 14f;
            var dismiss = Ui.Text(toast, Loc.Get("ui.m.toast.dismiss"), Ui.ItalicFont, 12f, Ui.InkBody, MToastWidth - 164f,
                10f, 150f, 16f, TextAlignmentOptions.TopRight);
            dismiss.textWrappingMode = TextWrappingModes.NoWrap;
            var eyebrow = Ui.Label(toast, Loc.Get("ui.toast.result"), x, 12f, width - 120f, 16f);
            eyebrow.fontSize = 12f;
            eyebrow.characterSpacing = 12f;
            eyebrow.textWrappingMode = TextWrappingModes.NoWrap;
            AutoSize(Ui.Text(toast, Ui.Initial(title, 120), Ui.TitleFont, 22f, Ui.InkText, x, 28f, width, 28f), 15f)
                .textWrappingMode = TextWrappingModes.NoWrap;
            AutoSize(Ui.Text(toast, text, Ui.BodyFont, 14f, Ui.InkText, x, 58f, width, 38f), 11.5f);
            AutoSize(Ui.Text(toast, string.Join(" · ", chips), Ui.ItalicFont, 13f, Ui.InkBody, x, 98f, width, 32f), 11f);
        }

        static TextMeshProUGUI AutoSize(TextMeshProUGUI text, float min)
        {
            text.enableAutoSizing = true;
            text.fontSizeMin = min;
            text.fontSizeMax = text.fontSize;
            return text;
        }

        void OnApplicationPause(bool paused)
        {
            if (!UiProfile.Mobile || Application.isEditor || !paused || _match == null || _paused) return;
            if (_awaitingStart || _match.Phase == Phase.GameOver) return;
            SetPaused(true);
            Refresh();
        }

        static void Show(CanvasGroup group, bool on)
        {
            group.alpha = on ? 1f : 0f;
            group.blocksRaycasts = group.interactable = on;
        }

        void BuildMobileHud()
        {
            _logLatest = null;
            _historyOpenPanel = null;
            _deck = _deckCaption = _discard = _discardLast = null;

            // Played cards are shown over the middle of the board; FitBoardMobile keeps this in place.
            _playSlot = Ui.Rect(_stage, "Play Slot", 0f, 0f, 0f, 0f);
            _playSlot.anchorMin = _playSlot.anchorMax = new Vector2(0f, 0.5f);
            _playSlot.pivot = new Vector2(0.5f, 0.5f);

            var column = _column = Ui.Rect(_stage, "Column", 0f, 0f, MColumn, UiProfile.MobileArea.y);
            column.anchorMin = column.anchorMax = column.pivot = new Vector2(1f, 0.5f);
            column.anchoredPosition = new Vector2(-s_side, 0f);

            Ui.Line(column, 0f, 46.5f, MColumn, 1.5f);
            Ui.Line(column, 0f, 49.5f, MColumn, 1f, 0.8f);
            var pause = Ui.IconButton(column, MColumn - 44f, 1f, OnPauseTapped);
            foreach (float x in new[] { 16.5f, 24.5f })
                Ui.Image(pause.transform, "Bar", Ui.Solid, x, 15f, 3f, 14f).color = Ui.Ink;

            _conditions = Ui.Rect(column, "Conditions", 0f, 54f, MColumn, 32f);

            const float endWidth = 132f;
            Ui.Line(column, 0f, 91f, MColumn, 1f, 0.35f);
            _phases = Ui.Text(column, "", Ui.TitleFont, 16f, Ui.Rubric, 0f, 96f, 160f, 20f);
            _phases.textWrappingMode = TextWrappingModes.NoWrap;
            _turn = Ui.Text(column, "", Ui.ItalicFont, 13f, Ui.InkBody, 0f, 115f, 164f, 18f);
            _turn.textWrappingMode = TextWrappingModes.NoWrap;
            var history = Ui.IconButton(column, MColumn - endWidth - 8f - 44f, 92f, () => OpenInfo(InfoTab.History));
            Ui.Image(history.transform, "Icon", Ui.Art("Icon_Hourglass"), 12f, 12f, 20f, 20f).color = Ui.Ink;
            _endTurn = Ui.Button(column, Loc.Get("ui.endTurn"), false, MColumn - endWidth, 92f, endWidth, 44f, OnEndTurn, 16f);

            var head = Ui.Rect(column, "Hand Header", 0f, 245f, MColumn, 16f);
            _rightColumn = head.gameObject.AddComponent<CanvasGroup>();
            _handTitle = Ui.Label(head, "", 0f, 0f, 220f, 16f, TextAlignmentOptions.MidlineLeft);
            _handTitle.fontSize = 12.5f;
            _handTitle.characterSpacing = 12f;
            _handTitle.textWrappingMode = TextWrappingModes.NoWrap;
            _handHint = Ui.Text(head, "", Ui.ItalicFont, 13f, Ui.InkBody, 200f, 0f, 150f, 16f,
                TextAlignmentOptions.MidlineRight);
            _handPrompt = Ui.Text(head, "", Ui.ItalicFont, 13f, Ui.Rubric, 0f, 0f, MColumn, 16f,
                TextAlignmentOptions.MidlineLeft);
            _handPrompt.textWrappingMode = TextWrappingModes.NoWrap;
            _handPrompt.overflowMode = TextOverflowModes.Ellipsis;
            _handPrompt.enableAutoSizing = true;
            _handPrompt.fontSizeMin = 10.5f;
            _handPrompt.fontSizeMax = 13f;
            var cancel = Ui.Image(head, "Cancel", Ui.Solid, MColumn - 76f, -12f, 76f, 30f, raycast: true);
            cancel.color = Color.clear;
            Localized(Ui.Text(cancel.transform, "", Ui.ItalicFont, 14f, Ui.Rubric, 0f, 0f, 76f, 40f,
                TextAlignmentOptions.MidlineRight), () => $"<u>{Loc.Get("ui.m.cancel")}</u>");
            Ui.MakeButton(cancel, CancelMode).transition = Selectable.Transition.None;
            _handCancel = cancel.gameObject;

            _hand = Ui.Rect(column, "Hand", 0f, MHandTop, MColumn, MHandHeight);
            _handGroup = _hand.gameObject.AddComponent<CanvasGroup>();
            // There is no deck on screen: drawn and discarded cards cross the right edge instead.
            _deckPile = _discardPile = Ui.Rect(column, "Deck Anchor", MColumn + s_side + 60f,
                MHandTop + MHandHeight * 0.5f, 0f, 0f);

            BuildActionSheet();
            BuildInfoSheet();
            _zoomLayer = Ui.Stretch(Ui.Rect(_stage, "Zoom", 0f, 0f, 0f, 0f));
        }

        void CancelMode()
        {
            ClearMode();
            Refresh();
        }

        void OnPauseTapped()
        {
            if (_awaitingStart || _match.Phase == Phase.GameOver) return;
            SetPaused(true);
            Refresh();
        }

        // A sheet covers the column from the right edge, over the safe-area margin too; its content keeps the
        // column's own 390-unit height, so both line up on a taller screen.
        static RectTransform MobileSheet(Transform parent, string name, out RectTransform content)
        {
            float width = MSheetPad + MSheetInner + s_side;
            var sheet = Ui.Image(parent, name, Ui.Solid, 0f, 0f, 0f, 0f, raycast: true).rectTransform;
            sheet.GetComponent<Image>().color = Ui.Parchment;
            sheet.anchorMin = Vector2.right;
            sheet.anchorMax = Vector2.one;
            sheet.pivot = new Vector2(1f, 0.5f);
            sheet.offsetMin = new Vector2(-width, 0f);
            sheet.offsetMax = Vector2.zero;
            SheetRule(sheet, -10f, 6f, new Color(ScrimColor.r, ScrimColor.g, ScrimColor.b, 0.1f));
            SheetRule(sheet, -4f, 4f, new Color(ScrimColor.r, ScrimColor.g, ScrimColor.b, 0.22f));
            SheetRule(sheet, 0f, 2f, Ui.Ink);
            SheetRule(sheet, 3.5f, 1f, Ui.Ink);

            content = Ui.Rect(sheet, "Content", 0f, 0f, MSheetInner, UiProfile.MobileArea.y);
            content.anchorMin = content.anchorMax = content.pivot = new Vector2(0f, 0.5f);
            content.anchoredPosition = new Vector2(MSheetPad, 0f);
            return sheet;
        }

        static void SheetRule(RectTransform sheet, float x, float width, Color color)
        {
            var line = Ui.Image(sheet, "Rule", Ui.Solid, 0f, 0f, 0f, 0f).rectTransform;
            line.GetComponent<Image>().color = color;
            line.anchorMin = Vector2.zero;
            line.anchorMax = Vector2.up;
            line.offsetMin = new Vector2(x, 0f);
            line.offsetMax = new Vector2(x + width, 0f);
        }

        static Transform SheetBand(RectTransform content)
        {
            var band = Ui.Image(content, "Band", Ui.Solid, -MSheetPad + 5f, 0f, MSheetPad - 5f + MSheetInner + s_side, MBand);
            band.color = Ui.Rubric;
            return band.transform;
        }

        static TextMeshProUGUI BandLabel(Transform band, string text)
        {
            var label = Ui.Label(band, text, MSheetPad - 5f, 0f, 190f, MBand, TextAlignmentOptions.MidlineLeft);
            label.fontSize = 14f;
            label.characterSpacing = 14f;
            label.color = Ui.Cream;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            return label;
        }

        static TextMeshProUGUI BandNote(Transform band, string text) =>
            Ui.Text(band, text, Ui.ItalicFont, 15f, Ui.Cream, MSheetPad - 5f + 150f, 0f, MSheetInner - 150f, MBand,
                TextAlignmentOptions.MidlineRight);

        void BuildActionSheet()
        {
            const float cardWidth = 104f, cardHeight = 146f, top = MBand + 14f, cancelWidth = 110f, buttonsY = 254f;
            var sheet = MobileSheet(_stage, "Action Sheet", out var content);
            _actionSheet = sheet.gameObject.AddComponent<CanvasGroup>();
            var band = SheetBand(content);
            Localized(BandLabel(band, ""), () => Loc.Get("ui.action.header"));
            _actionStep = BandNote(band, "");
            _actionSlot = Ui.Rect(content, "Subject", 0f, top, cardWidth, cardHeight);
            _actionCells = Ui.Rect(content, "Cells", cardWidth + 12f, top, MSheetInner - cardWidth - 12f, cardHeight);
            _actionHint = Ui.Text(content, "", Ui.ItalicFont, 14.5f, Ui.InkBody, 0f, top + cardHeight + 12f, MSheetInner,
                38f, TextAlignmentOptions.Top);
            _actionPrimary = Ui.Button(content, Loc.Get("ui.action.playCard"), true, 0f, buttonsY,
                MSheetInner - 10f - cancelWidth, 46f, OnActionPrimary, 18f);
            _actionCancel = Ui.Button(content, Loc.Get("ui.action.cancel"), false, MSheetInner - cancelWidth, buttonsY,
                cancelWidth, 46f, CancelMode, 18f);
            Localized(_actionCancel.GetComponentInChildren<TextMeshProUGUI>(), () => Loc.Get("ui.action.cancel"));
            Show(_actionSheet, false);
        }

        // The sheets stay active and are hidden by their groups: TMP only measures text in an active hierarchy.
        void BuildInfoSheet()
        {
            const float tabsY = 10f, tabHeight = 44f, tabsWidth = MSheetInner - 10f - 44f, bodyY = 64f;
            var root = Ui.Stretch(Ui.Rect(_stage, "Info", 0f, 0f, 0f, 0f));
            _infoGroup = root.gameObject.AddComponent<CanvasGroup>();
            var scrim = Ui.Stretch(Ui.Image(root, "Scrim", Ui.Solid, 0f, 0f, 0f, 0f, raycast: true).rectTransform);
            scrim.GetComponent<Image>().color = new Color(ScrimColor.r, ScrimColor.g, ScrimColor.b, 0.45f);
            Ui.MakeButton(scrim.GetComponent<Image>(), CloseInfo).transition = Selectable.Transition.None;
            MobileSheet(root, "Info Sheet", out var content);

            // The design has two tabs; the discard pile, which has no panel of its own on a phone, is the third.
            float tabWidth = tabsWidth / InfoTabs.Length;
            for (int i = 0; i < InfoTabs.Length; i++)
            {
                var tab = InfoTabs[i];
                var face = Ui.Image(content, "Tab", Ui.Solid, tabWidth * i, tabsY, tabWidth, tabHeight, raycast: true);
                _infoTabFaces[i] = face;
                var label = _infoTabLabels[i] = Ui.Text(face.transform, "", Ui.TitleFont, 14.5f, Ui.InkText, 4f, 0f,
                    tabWidth - 8f, tabHeight, TextAlignmentOptions.Center);
                label.textWrappingMode = TextWrappingModes.NoWrap;
                AutoSize(label, 11f);
                Ui.MakeButton(face, () => OpenInfo(tab)).transition = Selectable.Transition.None;
                if (i > 0) Ui.Line(content, tabWidth * i, tabsY, 1f, tabHeight);
            }
            Ui.Sliced(content, "Tabs", Ui.Outline, 0f, tabsY, tabsWidth, tabHeight);
            var close = Ui.IconButton(content, MSheetInner - 44f, tabsY, CloseInfo);
            Cross(close.transform, 14f, 14f, 16f, Ui.Ink, 2f);

            float bodyHeight = UiProfile.MobileArea.y - bodyY - MBottom;
            var conditions = Ui.Scroll(content, 0f, bodyY, MSheetInner, bodyHeight);
            _infoConditions = conditions.content;
            _infoConditionsGroup = conditions.gameObject.AddComponent<CanvasGroup>();
            var log = Ui.Scroll(content, 0f, bodyY, MSheetInner, bodyHeight);
            _logEntries = log.content;
            _infoHistoryGroup = log.gameObject.AddComponent<CanvasGroup>();
            var discard = Ui.Scroll(content, 0f, bodyY, MSheetInner, bodyHeight);
            _infoDiscard = discard.content;
            _infoDiscardGroup = discard.gameObject.AddComponent<CanvasGroup>();
            RefreshInfo();
        }

        void OpenInfo(InfoTab tab)
        {
            _infoTab = tab;
            RefreshInfo();
        }

        void CloseInfo() => OpenInfo(InfoTab.None);

        void RefreshInfo()
        {
            Show(_infoGroup, _infoTab != InfoTab.None);
            Show(_infoConditionsGroup, _infoTab == InfoTab.Conditions);
            Show(_infoHistoryGroup, _infoTab == InfoTab.History);
            Show(_infoDiscardGroup, _infoTab == InfoTab.Discard);
            int count = _match != null ? _match.Conditions.Count : 0;
            _infoTabLabels[0].text = Loc.Format("ui.m.conditions.tab", count);
            _infoTabLabels[1].text = Loc.Get("ui.history");
            _infoTabLabels[2].text = Loc.Get("ui.discard.title");
            if (_infoTab == InfoTab.Discard) RefreshDiscardMobile();
            for (int i = 0; i < InfoTabs.Length; i++)
            {
                bool on = InfoTabs[i] == _infoTab;
                _infoTabFaces[i].color = on ? Ui.Ink : new Color(1f, 0.98f, 0.93f, 0.45f);
                _infoTabLabels[i].color = on ? Ui.Parchment : Ui.InkText;
            }
        }

        // Both piles in one list, yours first, most recent first, as the desktop modal shows them in two tabs.
        void RefreshDiscardMobile()
        {
            for (int i = _infoDiscard.childCount - 1; i >= 0; i--) Destroy(_infoDiscard.GetChild(i).gameObject);
            const float card = 0.62f, rowGap = 8f;
            float width = _infoDiscard.rect.width, cardWidth = Ui.MiniCardWidth * card, textX = cardWidth + 12f;
            float y = 0f;
            foreach (var side in new[] { _human, Opponent })
            {
                var history = DiscardHistory(side);
                var heading = Ui.Label(_infoDiscard, Loc.Format(side == _human ? "ui.discard.yours" : "ui.discard.opponent",
                    history.Count), 0f, y + 4f, width, 18f);
                heading.fontSize = 13f;
                heading.characterSpacing = 14f;
                heading.color = side == _human ? Ui.Rubric : Ui.InkSoft;
                y += 26f;
                if (history.Count == 0)
                {
                    Ui.Text(_infoDiscard, Loc.Get("ui.discard.empty"), Ui.ItalicFont, 14f, Ui.InkBody, 0f, y, width, 20f);
                    y += 30f;
                    continue;
                }
                foreach (var (id, turn, note) in history)
                {
                    var mini = Ui.MiniCard(_infoDiscard, CardCatalog.Get(id));
                    mini.localScale = new Vector3(card, card, 1f);
                    mini.anchoredPosition = new Vector2(0f, -y);
                    Ui.Text(_infoDiscard, Loc.CardName(id), Ui.TitleFont, 16f, Ui.InkText, textX, y, width - textX, 20f)
                        .textWrappingMode = TextWrappingModes.NoWrap;
                    var when = Ui.Text(_infoDiscard, Loc.Format("ui.discard.turn", Ui.Roman(turn)), Ui.TitleFont, 12.5f,
                        Ui.Rubric, textX, y + 20f, width - textX, 16f);
                    when.characterSpacing = 8f;
                    float noteHeight = 0f;
                    if (!string.IsNullOrEmpty(note))
                    {
                        var body = Ui.Text(_infoDiscard, note, Ui.BodyFont, 14f, Ui.InkText, textX, y + 37f, width - textX, 0f);
                        noteHeight = body.GetPreferredValues(note, width - textX, 0f).y;
                        body.rectTransform.sizeDelta = new Vector2(width - textX, noteHeight);
                    }
                    float rowHeight = Mathf.Max(Ui.MiniCardHeight * card, 37f + noteHeight);
                    y += rowHeight + rowGap;
                    Ui.Line(_infoDiscard, 0f, y - rowGap * 0.5f, width, 1f, 0.25f);
                }
                y += 6f;
            }
            _infoDiscard.sizeDelta = new Vector2(0f, y);
        }

        bool CloseMobileSheets()
        {
            if (!UiProfile.Mobile || (_zoom < 0 && _infoTab == InfoTab.None)) return false;
            _zoom = -1;
            _infoTab = InfoTab.None;
            return true;
        }

        void RefreshPhaseMobile()
        {
            bool reaction = _match.Phase == Phase.Reaction;
            _phases.text = Loc.Get(_awaitingStart ? "ui.phase.preparation"
                : reaction ? "ui.phase.confrontation" : "ui.phase.maneuvers");
            _turn.text = Loc.Format("ui.m.turnPhase", Ui.Roman(_match.Turn), _awaitingStart ? 1 : reaction ? 3 : 2, 4);
        }

        ArmyPanel BuildArmyMobile(Side side, bool human)
        {
            const float moraleWidth = 86f;
            var root = human ? Ui.Rect(_column, $"Army {side}", 0f, 138f, MColumn, 103f)
                : Ui.Rect(_column, $"Army {side}", 0f, 3f, MColumn - 52f, 42f);
            var ui = new ArmyPanel { Root = root, Group = root.gameObject.AddComponent<CanvasGroup>() };
            float emblem = human ? 26f : 30f, textX = emblem + 8f, top = human ? 7f : 6f;
            float moraleX = human ? 148f : root.sizeDelta.x - moraleWidth, textWidth = moraleX - textX - 6f;
            if (human) Ui.Line(root, 0f, 0f, MColumn, 1f, 0.35f);
            Ui.Image(root, "Emblem", Ui.UnitArt(UnitType.Sovereign, side), 0f, top, emblem, emblem);
            var name = Localized(Ui.Text(root, "", Ui.TitleFont, human ? 15.5f : 16f, Ui.InkText, textX, top - 3f, textWidth,
                20f), () => Loc.Army(side));
            name.textWrappingMode = TextWrappingModes.NoWrap;
            name.enableAutoSizing = true;
            name.fontSizeMin = 12f;
            name.fontSizeMax = name.fontSize;
            var sub = Ui.Text(root, "", Ui.BodyFont, 12.5f, Ui.InkBody, textX, top + 16f, textWidth, 16f);
            sub.textWrappingMode = TextWrappingModes.NoWrap;

            var label = Localized(Ui.Label(root, "", moraleX, top - 2f, 56f, 16f), () => Loc.Get("ui.morale"));
            label.fontSize = 12.5f;
            label.characterSpacing = 12f;
            ui.Morale = Ui.Text(root, "", Ui.BodyFont, 12.5f, Ui.InkBody, moraleX + moraleWidth - 40f, top - 2f, 40f, 16f,
                TextAlignmentOptions.TopRight);
            ui.Pips = new Image[Match.MaxMorale];
            float pipStep = (moraleWidth + 2f) / ui.Pips.Length;
            for (int i = 0; i < ui.Pips.Length; i++)
                ui.Pips[i] = Ui.Image(root, "Pip", Ui.Art("Pip_Off"), moraleX + i * pipStep, top + 17f, 9f, 9f);

            if (!human)
            {
                ui.Summary = sub;
                ui.ReserveCounts = new TextMeshProUGUI[0];
                return ui;
            }

            ui.Subtitle = sub;
            ui.OrderY = top;
            ui.OrderStep = 29f;
            ui.OrderRight = MColumn + 1f;
            ui.OrderIcons = new Image[Match.OrdersPerTurn + 2];
            for (int i = 0; i < ui.OrderIcons.Length; i++)
                ui.OrderIcons[i] = Ui.Image(root, "Order", Ui.Art("Order_Off"), 0f, top, 28f, 26f);
            ui.ReserveHint = Ui.Label(root, "", 0f, 45f, MColumn, 16f);
            ui.ReserveHint.fontSize = 12.5f;
            ui.ReserveHint.characterSpacing = 12f;
            BuildReserveMobile(root, ui, side, 61f);
            return ui;
        }

        void BuildReserveMobile(RectTransform root, ArmyPanel ui, Side side, float y)
        {
            const float gap = 5f, height = 42f, icon = 24f, seal = 15f;
            int n = Match.Recruitable.Length;
            float w = (MColumn - gap * (n - 1)) / n;
            ui.ReserveCounts = new TextMeshProUGUI[n];
            ui.RecruitButtons = new Button[n];
            ui.RecruitGroups = new CanvasGroup[n];
            ui.RecruitHighlights = new Image[n];
            for (int i = 0; i < n; i++)
            {
                var type = Match.Recruitable[i];
                var tile = Ui.Sliced(root, "Reserve " + type, Ui.Tile, i * (w + gap), y, w, height, raycast: true);
                Ui.Image(tile.transform, "Icon", Ui.UnitArt(type, side), 4f, (height - icon) * 0.5f, icon, icon);
                ui.ReserveCounts[i] = Ui.Text(tile.transform, "", Ui.BodyFont, 14.5f, Ui.InkText, icon + 5f, 0f,
                    w - icon - seal - 9f, height, TextAlignmentOptions.Midline);
                Ui.Seal(tile.transform, Match.Cost(type), w - seal - 3f, (height - seal) * 0.5f, seal);
                ui.RecruitGroups[i] = tile.gameObject.AddComponent<CanvasGroup>();
                ui.RecruitButtons[i] = Ui.MakeButton(tile, () => OnRecruitClicked(side, type));
                var highlight = Ui.Sliced(tile.transform, "Chosen", Ui.Highlight, -2f, -2f, w + 4f, height + 4f);
                highlight.enabled = false;
                ui.RecruitHighlights[i] = highlight;
            }
        }

        // The column only has room for a row of chips; the full list is a tab of the info sheet.
        void RefreshConditionsMobile()
        {
            for (int i = _conditions.childCount - 1; i >= 0; i--) Destroy(_conditions.GetChild(i).gameObject);
            for (int i = _infoConditions.childCount - 1; i >= 0; i--) Destroy(_infoConditions.GetChild(i).gameObject);
            var active = _match.Conditions;
            float listWidth = _infoConditions.rect.width;
            if (active.Count == 0)
            {
                Ui.Text(_conditions, Loc.Get("ui.conditions.header") + " · " + Loc.Get("ui.conditions.none"), Ui.ItalicFont,
                    13f, Ui.InkBody, 0f, 0f, MColumn, 32f, TextAlignmentOptions.MidlineLeft);
                Ui.Text(_infoConditions, Loc.Get("ui.conditions.empty"), Ui.ItalicFont, 15f, Ui.InkBody, 0f, 12f, listWidth,
                    44f, TextAlignmentOptions.Top);
                _infoConditions.sizeDelta = new Vector2(0f, 60f);
                RefreshInfo();
                return;
            }

            const float gap = 5f, artWidth = 26f, chipPad = 3f + artWidth + 5f + 7f;
            var names = new TextMeshProUGUI[active.Count];
            var widths = new float[active.Count];
            float natural = gap * (active.Count - 1);
            for (int i = 0; i < active.Count; i++)
            {
                var card = active[i];
                var chip = Ui.Sliced(_conditions, "Condition " + card,
                    Ui.Pill(new Color(1f, 0.98f, 0.93f, 0.35f), Ui.Ink, 3), 0f, 0f, 0f, 32f, raycast: true);
                Ui.MakeButton(chip, () => OpenInfo(InfoTab.Conditions));
                ConditionArt(chip.transform, card, 3f, 5f, artWidth, 22f);
                names[i] = Ui.Text(chip.transform, Loc.CardName(card), Ui.TitleFont, 13.5f, Ui.InkText, 3f + artWidth + 5f, 0f,
                    0f, 32f, TextAlignmentOptions.MidlineLeft);
                names[i].textWrappingMode = TextWrappingModes.NoWrap;
                names[i].overflowMode = TextOverflowModes.Ellipsis;
                widths[i] = chipPad + names[i].GetPreferredValues(names[i].text).x;
                natural += widths[i];
            }
            // Chips share what is left of the row, or give it up evenly when their names do not fit.
            float spare = (MColumn - natural) / active.Count, x = 0f;
            for (int i = 0; i < active.Count; i++)
            {
                float w = widths[i] + spare;
                var chip = (RectTransform)names[i].transform.parent;
                chip.anchoredPosition = new Vector2(x, 0f);
                chip.sizeDelta = new Vector2(w, 32f);
                names[i].rectTransform.sizeDelta = new Vector2(w - chipPad, 32f);
                x += w + gap;
            }

            const float rowHeight = 62f, rowGap = 7f, textX = 86f;
            float y = 0f;
            foreach (var card in active)
            {
                var row = Ui.Sliced(_infoConditions, "Condition " + card, Ui.Tile, 0f, y, listWidth, rowHeight);
                ConditionArt(row.transform, card, 6f, 6f, 70f, 50f);
                Ui.Text(row.transform, Loc.CardName(card), Ui.TitleFont, 17f, Ui.InkText, textX, 5f, listWidth - textX - 8f,
                    22f).textWrappingMode = TextWrappingModes.NoWrap;
                string detail = Loc.Get("ui.conditions." + card);
                if (card == CardId.LightningStorm)
                    foreach (var m in _match.Markers)
                        if (m.Kind == MarkerKind.Lightning) detail = Loc.Format("ui.conditions.lightning", m.Pos);
                Ui.Text(row.transform, detail, Ui.BodyFont, 15f, Ui.InkText, textX, 25f, listWidth - textX - 8f, 34f);
                y += rowHeight + rowGap;
            }
            Ui.Text(_infoConditions, Loc.Get("ui.m.conditions.note"), Ui.ItalicFont, 14f, Ui.InkBody, 0f, y + 3f, listWidth,
                40f);
            _infoConditions.sizeDelta = new Vector2(0f, y + 46f);
            RefreshInfo();
        }

        // Whole cards fanned on an arc at the foot of the column, running off the bottom edge as on the PC. A tap
        // enlarges a card instead of selecting it, and the enlarged card is where it is played from.
        void RefreshHandMobile()
        {
            for (int i = _hand.childCount - 1; i >= 0; i--) Destroy(_hand.GetChild(i).gameObject);
            _handCards.Clear();
            _hoveredCard = -1;
            var hand = _awaitingStart || _match.Phase == Phase.GameOver ? null : _match.Get(_human).Hand;
            if (hand == null || _zoom >= hand.Count || ModalUp) _zoom = -1;
            if (hand != null)
            {
                float w = Ui.CardWidth * HandScale, h = Ui.CardHeight * HandScale;
                // A longer hand closes the fan so its outer cards still end at the column's edges.
                float step = hand.Count > 1 ? Mathf.Min(MFanStep, MFanSpread / (hand.Count - 1)) : 0f;
                float middle = (hand.Count - 1) * 0.5f;
                for (int i = 0; i < hand.Count; i++)
                {
                    var info = CardCatalog.Get(hand[i]);
                    var slot = Ui.Image(_hand, "Slot", Ui.Solid, 0f, 0f, w, h, raycast: true);
                    slot.color = Color.clear;
                    var fan = slot.rectTransform;
                    fan.pivot = new Vector2(0.5f, 1f - MFanRadius / h);
                    fan.anchoredPosition = new Vector2(MColumn * 0.5f, -MFanRadius);
                    fan.localRotation = Quaternion.Euler(0f, 0f, -step * (i - middle));

                    var holder = Ui.Rect(slot.transform, "Holder", 0f, 0f, w, h);
                    holder.pivot = new Vector2(0.5f, 0f);
                    holder.anchoredPosition = new Vector2(w * 0.5f, -h);
                    var card = Ui.Card(holder, info, HandScale);
                    card.pivot = new Vector2(0.5f, 0f);
                    bool selected = _mode == Mode.Card && _cardIndex == i;
                    bool off = HumanOwnsTurn && !_match.CanPlay(info.Id);
                    card.anchoredPosition = new Vector2(w * 0.5f, -h + (selected ? MFanLift : 0f));
                    if (off)
                        foreach (var graphic in card.GetComponentsInChildren<Graphic>()) graphic.color *= OffTint;

                    var shadow = Ui.Sliced(card, "Shadow", Ui.Art("Card_Shadow"), -10f, -12f, w + 20f, h + 20f);
                    shadow.pixelsPerUnitMultiplier = 4f;
                    shadow.color = new Color(1f, 1f, 1f, 0.55f);
                    shadow.transform.SetAsFirstSibling();
                    if (selected)
                        Ui.Sliced(card, "Chosen", Ui.Pill(Color.clear, Ui.Rubric, 6, 2.75f), -5.5f, -5.5f, w + 11f,
                            h + 11f);
                    if (i >= hand.Count - _heldDraws) card.gameObject.AddComponent<CanvasGroup>().alpha = 0f;
                    _handCards.Add(new HandCard
                    {
                        Slot = slot.rectTransform,
                        Holder = holder,
                        Card = card,
                        Shadow = shadow,
                        Selected = selected,
                        Off = off,
                        Graphics = new Graphic[0],
                        Colors = new Color[0],
                    });

                    int index = i;
                    var button = slot.gameObject.AddComponent<Button>();
                    button.transition = Selectable.Transition.None;
                    button.onClick.AddListener(() => OnHandCardTapped(index));
                }
            }
            RefreshZoom();
        }

        void OnHandCardTapped(int index)
        {
            if (_mode == Mode.Card && _cardIndex == index)
            {
                CancelMode();
                return;
            }
            ClearMode();
            GameAudio.Play(Sfx.CardShuffle);
            _zoom = index;
            Refresh();
        }

        void CloseZoom()
        {
            _zoom = -1;
            RefreshZoom();
        }

        void UseZoomed()
        {
            int index = _zoom;
            _zoom = -1;
            if (HumanTurn) OnCardClicked(index);
            else Refresh();
        }

        // The card on the left and what can be done with it on the right, as both the enlarged hand card and the
        // foresight choice use it. The caller fills the column from the top.
        static RectTransform CardViewer(RectTransform layer, CardInfo info, bool arrows, System.Action<int> step,
            out RectTransform column)
        {
            const float cardX = 162f, cardY = 30f;
            var area = UiProfile.MobileArea;
            var stage = Ui.Rect(layer, "Viewer", 0f, 0f, area.x, area.y);
            stage.anchorMin = stage.anchorMax = stage.pivot = new Vector2(0.5f, 0.5f);
            stage.anchoredPosition = Vector2.zero;
            float w = Ui.CardWidth * ViewerCardScale, h = Ui.CardHeight * ViewerCardScale;
            Ui.Sliced(stage, "Shadow", Ui.Art("Card_Shadow"), cardX - 24f, cardY + 2f, w + 48f, h + 44f);
            var card = Ui.Card(stage, info, ViewerCardScale);
            card.anchoredPosition = new Vector2(cardX, -cardY);
            if (arrows)
                foreach (int direction in new[] { -1, 1 })
                {
                    int dir = direction;
                    var button = Ui.IconButton(stage, dir < 0 ? cardX - 54f : cardX + w + 10f, 173f, () => step(dir),
                        light: true);
                    Ui.Chevron(button.transform, 22f, 22f, 10f, dir, Ui.Cream);
                }
            column = Ui.Rect(stage, "Column", 470f, 72f, 300f, 260f);
            return card;
        }

        static TextMeshProUGUI ViewerLabel(RectTransform column, string text, float y)
        {
            var label = Ui.Label(column, text, 0f, y, column.sizeDelta.x, 18f);
            label.fontSize = 14f;
            label.characterSpacing = 16f;
            label.color = Ui.Parchment;
            return label;
        }

        static float ViewerNote(RectTransform column, string text, float y)
        {
            float width = column.sizeDelta.x;
            var note = Ui.Text(column, text, Ui.ItalicFont, 16f, Ui.Cream, 0f, y, width, 0f);
            float height = note.GetPreferredValues(text, width, 0f).y;
            note.rectTransform.sizeDelta = new Vector2(width, height);
            return y + height;
        }

        void RefreshZoom()
        {
            for (int i = _zoomLayer.childCount - 1; i >= 0; i--) Destroy(_zoomLayer.GetChild(i).gameObject);
            if (_zoom < 0) return;
            var hand = _match.Get(_human).Hand;
            var info = CardCatalog.Get(hand[_zoom]);
            int count = hand.Count;

            var scrim = Ui.Image(_zoomLayer, "Scrim", Ui.Solid, 0f, 0f, 0f, 0f, raycast: true);
            Ui.Stretch(scrim.rectTransform);
            scrim.color = new Color(ScrimColor.r, ScrimColor.g, ScrimColor.b, 0.74f);
            Ui.MakeButton(scrim, CloseZoom).transition = Selectable.Transition.None;
            CardViewer(_zoomLayer, info, count > 1, dir =>
            {
                _zoom = (_zoom + dir + count) % count;
                RefreshZoom();
            }, out var column);

            bool usable = HumanTurn && _match.CanPlay(info.Id);
            string note = info.IsReaction ? Loc.Get("ui.hint.reaction")
                : !HumanOwnsTurn ? Loc.Get("ui.m.zoom.wait")
                : CardHint(info);
            ViewerLabel(column, Loc.Format("ui.m.zoom.count", _zoom + 1, count), 0f);
            float y = ViewerNote(column, note, 28f) + 16f;
            var use = Ui.Button(column, Loc.Get("ui.m.zoom.use"), true, 0f, y, column.sizeDelta.x, 46f, UseZoomed, 18f);
            if (!usable) Disabled(use);
            var back = Ui.Button(column, Loc.Get("ui.m.zoom.back"), false, 0f, y + 56f, column.sizeDelta.x, 46f, CloseZoom, 18f);
            ((Image)back.targetGraphic).sprite = Ui.Boxed(new Color(Ui.Cream.r, Ui.Cream.g, Ui.Cream.b, 0f), Ui.Cream, 2, 0, 0);
            back.GetComponentInChildren<TextMeshProUGUI>().color = Ui.Cream;
        }

        // The action panel becomes a sheet that only comes in when a card is ready to be confirmed; until then
        // the line above the hand says what the board is waiting for.
        void RefreshActionMobile(string hint, bool ready, bool cancellable)
        {
            Show(_actionSheet, ready);
            bool live = !_awaitingStart && _match.Phase != Phase.GameOver;
            int count = live ? _match.Get(_human).Hand.Count : 0;
            bool idle = live && HumanOwnsTurn && _mode == Mode.Idle;
            _handTitle.enabled = _handHint.enabled = idle;
            _handPrompt.enabled = live && !idle;
            _handCancel.SetActive(live && cancellable && !ready);
            _handTitle.text = Loc.Format("ui.m.hand", count, Loc.Get(count == 1 ? "ui.card" : "ui.cards"));
            _handHint.text = count > 0 ? Loc.Get("ui.m.handHint") : "";
            _handPrompt.text = hint;
            _handPrompt.color = cancellable ? Ui.Rubric : Ui.InkBody;
            _handPrompt.rectTransform.sizeDelta = new Vector2(cancellable ? MColumn - 80f : MColumn, 16f);
        }

        void BuildCellsMobile(List<(string label, string value, string sub, bool marked)> cells)
        {
            const float labelWidth = 60f, textX = 66f;
            float w = _actionCells.rect.width, row = _actionCells.rect.height / 3f;
            Ui.Line(_actionCells, 0f, 0f, w, 1f, 0.35f);
            for (int i = 0; i < cells.Count; i++)
            {
                var (label, value, sub, marked) = cells[i];
                float y = row * i;
                var title = Ui.Label(_actionCells, label, 0f, y + 8f, labelWidth, 16f);
                title.characterSpacing = 8f;
                title.textWrappingMode = TextWrappingModes.NoWrap;
                title.enableAutoSizing = true;
                title.fontSizeMin = 9f;
                title.fontSizeMax = 12.5f;
                var top = Ui.Text(_actionCells, value, Ui.BodyFont, 16f, marked ? Ui.Rubric : Ui.InkText, textX, y + 5f,
                    w - textX, 20f);
                var bottom = Ui.Text(_actionCells, sub, Ui.BodyFont, 13.5f, Ui.InkBody, textX, y + 25f, w - textX, 18f);
                top.textWrappingMode = bottom.textWrappingMode = TextWrappingModes.NoWrap;
                top.overflowMode = bottom.overflowMode = TextOverflowModes.Ellipsis;
                Ui.Line(_actionCells, 0f, y + row - 1f, w, 1f, 0.35f);
            }
        }

        RectTransform MobileModal(float w, float h, float top = -1f)
        {
            var area = UiProfile.MobileArea;
            return Ui.Modal(MobileStage(), (area.x - w) * 0.5f, top < 0f ? (area.y - h) * 0.5f : top, w, h);
        }

        static TextMeshProUGUI MobileEyebrow(RectTransform panel, string text, float x, float y, float w)
        {
            var label = Ui.Label(panel, text, x, y, w, 18f, TextAlignmentOptions.Top);
            label.fontSize = 13f;
            label.characterSpacing = 18f;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            return label;
        }

        // The reaction dock stands beside the board, so nothing has to step aside for the attack to be seen:
        // there is no peek button, and only the chosen reaction spells out what it does.
        void BuildConfrontMobile()
        {
            var pc = _match.Pending;
            var defender = _match.Get(pc.Defender.Side);
            var applicable = _match.ReactionOptions();
            var cards = new List<CardId>();
            foreach (var id in defender.Hand)
                if (CardCatalog.Get(id).IsReaction && !cards.Contains(id)) cards.Add(id);
            if (_reaction == null || !applicable.Contains(_reaction.Value))
                _reaction = applicable.Count > 0 ? applicable[0] : (CardId?)null;

            FocusBoard(pc);
            MobileSheet(_confront, "Dock", out var panel);
            var band = SheetBand(panel);
            BandLabel(band, Loc.Get("ui.reaction.header"));
            BandNote(band, Loc.Get("ui.morale") + " " + Loc.Format("ui.moraleValue", defender.Morale, Match.MaxMorale));

            Ui.Text(panel, Loc.Get("ui.reaction.title"), Ui.TitleFont, 23f, Ui.InkText, 0f, MBand + 6f, 240f, 28f)
                .textWrappingMode = TextWrappingModes.NoWrap;
            Ui.Text(panel, Loc.Get("ui.m.reaction.limit"), Ui.ItalicFont, 14f, Ui.InkBody, 200f, MBand + 12f,
                MSheetInner - 200f, 20f, TextAlignmentOptions.TopRight);

            const float duelY = MBand + 42f, duelWidth = 152f;
            Duelist(panel, pc.Attacker, 0f, duelY, duelWidth, Loc.Get("ui.reaction.attacker"), false);
            Duelist(panel, pc.Defender, MSheetInner - duelWidth, duelY, duelWidth, Loc.Get("ui.reaction.yourUnit"), true);
            Arrow(panel, duelWidth + 4f, duelY + 14f, MSheetInner - duelWidth * 2f - 10f);

            const float buttonHeight = 46f, passWidth = 120f, optionGap = 5f;
            float buttonsY = UiProfile.MobileArea.y - MBottom - buttonHeight;
            if (_reaction is CardId selected)
            {
                var play = Ui.Button(panel, Loc.Format("ui.reaction.play", Loc.CardName(selected), _match.ReactionCost(selected)),
                    true, 0f, buttonsY, MSheetInner - passWidth - 10f, buttonHeight, () => OnReaction(selected), 17f);
                var label = play.GetComponentInChildren<TextMeshProUGUI>();
                label.margin = new Vector4(10f, 0f, 10f, 0f);
                label.enableAutoSizing = true;
                label.fontSizeMin = 12f;
                label.fontSizeMax = 17f;
                Ui.Button(panel, Loc.Get("ui.reaction.pass"), false, MSheetInner - passWidth, buttonsY, passWidth,
                    buttonHeight, () => OnReaction(null), 17f);
            }
            else
                Ui.Button(panel, Loc.Get("ui.reaction.pass"), false, 0f, buttonsY, MSheetInner, buttonHeight,
                    () => OnReaction(null), 17f);

            float top = duelY + 40f, areaHeight = buttonsY - 10f - top;
            var scroll = Ui.Scroll(panel, 0f, top, MSheetInner + 18f, areaHeight);
            var area = scroll.content;
            float y = 0f;
            foreach (var id in cards)
                y += ReactionOptionMobile(area, id, 0f, y, MSheetInner, applicable.Contains(id), pc) + optionGap;
            area.sizeDelta = new Vector2(0f, y - optionGap);
        }

        static void Duelist(RectTransform panel, Unit unit, float x, float y, float width, string caption, bool target)
        {
            const float token = 26f, textX = token + 6f;
            Ui.Image(panel, "Token", Ui.UnitArt(unit.Type, unit.Side), x, y + 2f, token, token);
            var name = Ui.Text(panel, Loc.UnitDot(unit), Ui.TitleFont, 14.5f, target ? Ui.Rubric : Ui.InkText, x + textX, y,
                width - textX, 17f);
            name.textWrappingMode = TextWrappingModes.NoWrap;
            name.enableAutoSizing = true;
            name.fontSizeMin = 10f;
            name.fontSizeMax = 14.5f;
            Ui.Text(panel, caption, Ui.BodyFont, 12.5f, Ui.InkBody, x + textX, y + 16f, width - textX, 16f);
        }

        float ReactionOptionMobile(RectTransform parent, CardId id, float x, float y, float w, bool ok, PendingCapture pc)
        {
            const float badge = 28f, seal = 22f, pad = 8f, textX = pad + badge + 10f;
            bool chosen = ok && _reaction == id;
            var rubric = Ui.Rubric;
            float textWidth = w - textX - 10f - seal - pad, height = 44f;
            var frame = chosen ? Ui.Pill(new Color(rubric.r, rubric.g, rubric.b, 0.07f), rubric, 4, 2f)
                : Ui.Pill(Color.clear, new Color(Ui.Ink.r, Ui.Ink.g, Ui.Ink.b, 0.25f), 4, 2f);
            var box = Ui.Sliced(parent, "Option " + id, frame, x, y, w, height, raycast: ok);

            var title = Ui.Text(box.transform, Loc.CardName(id), Ui.TitleFont, 16f, Ui.InkText, textX, 5f, textWidth, 19f);
            title.textWrappingMode = TextWrappingModes.NoWrap;
            title.enableAutoSizing = true;
            title.fontSizeMin = 12f;
            title.fontSizeMax = 16f;
            var status = Ui.Text(box.transform, ReactionStatus(id, ok, pc), Ui.TitleFont, 12f, ok ? rubric : Ui.InkSoft,
                textX, 24f, textWidth, 15f);
            status.characterSpacing = 6f;
            status.textWrappingMode = TextWrappingModes.NoWrap;
            status.enableAutoSizing = true;
            status.fontSizeMin = 9.5f;
            status.fontSizeMax = 12f;
            if (chosen)
            {
                string outcome = ReactionOutcome(id, ok, pc);
                var body = Ui.Text(box.transform, outcome, Ui.BodyFont, 14f, Ui.InkText, textX, 41f, textWidth, 0f);
                float bodyHeight = body.GetPreferredValues(outcome, textWidth, 0f).y;
                body.rectTransform.sizeDelta = new Vector2(textWidth, bodyHeight);
                height = 41f + bodyHeight + 7f;
                box.rectTransform.sizeDelta = new Vector2(w, height);
            }

            var kind = Ui.Image(box.transform, "Kind", Ui.Art("Kind_Badge"), pad, (height - badge) * 0.5f, badge, badge);
            Ui.Image(kind.transform, "Icon", Ui.KindIcon(CardCatalog.Get(id).Kind), 5f, 5f, 18f, 18f).color = Ui.Ink;
            Ui.Seal(box.transform, _match.ReactionCost(id), w - pad - seal, (height - seal) * 0.5f, seal);
            if (!ok)
            {
                box.gameObject.AddComponent<CanvasGroup>().alpha = 0.55f;
                return height;
            }
            var button = box.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(() =>
            {
                _reaction = id;
                RefreshOverlay();
            });
            return height;
        }

        void BuildPauseMobile()
        {
            const float w = 420f, h = 308f, pad = 24f, button = 44f, gap = 8f, buttonsY = 110f;
            var panel = MobileModal(w, h, 36f);
            MobileEyebrow(panel, Loc.Format("ui.pause.eyebrow", Ui.Roman(_match.Turn)), 0f, 22f, w);
            Ui.Text(panel, Ui.Initial(Loc.Get("ui.pause.title"), 118), Ui.TitleFont, 34f, Ui.InkText, 0f, 42f, w, 44f,
                TextAlignmentOptions.Top);
            float center = w * 0.5f;
            Ui.Line(panel, center - 80f, 95f, 66f, 1.2f, 0.8f);
            Ui.Line(panel, center + 14f, 95f, 66f, 1.2f, 0.8f);
            Ui.Diamond(panel, center, 95.6f, 7f, Ui.Rubric);

            float full = w - pad * 2f, half = (full - gap) * 0.5f, step = button + gap;
            Ui.Button(panel, Loc.Get("ui.pause.resume"), true, pad, buttonsY, full, button, () =>
            {
                SetPaused(false);
                Refresh();
            }, 17f);
            Disabled(Ui.Button(panel, Loc.Get("ui.pause.rules"), false, pad, buttonsY + step, half, button, () => { }, 17f));
            Ui.Button(panel, Loc.Get("ui.pause.settings"), false, pad + half + gap, buttonsY + step, half, button, OpenOptions,
                17f);
            Ui.Danger(Ui.Button(panel, Loc.Get("ui.pause.abandon"), false, pad, buttonsY + step * 2f, full, button, () =>
            {
                _confirmAbandon = true;
                RefreshOverlay();
            }, 17f));
            Ui.Text(panel, Loc.Get("ui.m.pause.hint"), Ui.ItalicFont, 14f, Ui.InkBody, pad, buttonsY + step * 3f + 2f, full,
                20f, TextAlignmentOptions.Top);
        }

        void BuildConfirmMobile()
        {
            const float w = 420f, h = 212f, pad = 24f, gap = 8f, half = (w - pad * 2f - gap) * 0.5f;
            var panel = MobileModal(w, h, 84f);
            MobileEyebrow(panel, Loc.Get("ui.confirm.eyebrow"), 0f, 22f, w).color = Ui.Rubric;
            Ui.Text(panel, Ui.Initial(Loc.Get("ui.confirm.title"), 118), Ui.TitleFont, 28f, Ui.InkText, 0f, 42f, w, 38f,
                TextAlignmentOptions.Top).textWrappingMode = TextWrappingModes.NoWrap;
            Ui.Text(panel, Loc.Get("ui.confirm.text"), Ui.BodyFont, 16.5f, Ui.InkText, pad, 86f, w - pad * 2f, 48f,
                TextAlignmentOptions.Top);
            Ui.Button(panel, Loc.Get("ui.confirm.stay"), false, pad, 146f, half, 44f, () =>
            {
                _confirmAbandon = false;
                RefreshOverlay();
            }, 17f);
            Ui.Button(panel, Loc.Get("ui.confirm.abandon"), true, pad + half + gap, 146f, half, 44f, LoadMenu, 17f);
        }

        // Not in the design doc: the desktop sheet would shrink below reading size, so the same content is laid out
        // again for the phone, the army on the left and the three reminders on the right.
        void BuildIntroMobile()
        {
            const float w = 700f, h = 318f, left = 330f, tipX = 362f, tipWidth = w - tipX - 24f, tipHeight = 86f;
            var panel = MobileModal(w, h);
            MobileEyebrow(panel, Loc.Get(ModeKey("ui.intro.eyebrow")), 0f, 24f, left).fontSize = 12f;
            Ui.Image(panel, "Emblem", Ui.UnitArt(UnitType.Sovereign, _human), (left - 60f) * 0.5f, 48f, 60f, 60f);
            MobileEyebrow(panel, Loc.Get("ui.intro.header"), 0f, 116f, left);
            Ui.Text(panel, Ui.Initial(Loc.Army(_human), 118), Ui.TitleFont, 30f, Ui.InkText, 0f, 132f, left, 40f,
                TextAlignmentOptions.Top).textWrappingMode = TextWrappingModes.NoWrap;
            Ui.Text(panel, Loc.Format(ModeKey(_match.Current == _human ? "ui.intro.youFirst" : "ui.intro.aiFirst"),
                Loc.Army(Opponent)), Ui.ItalicFont, 15f, Ui.Ink, 12f, 180f, left - 24f, 44f, TextAlignmentOptions.Top);
            const float buttonWidth = 250f;
            Ui.Button(panel, Loc.Get("ui.intro.button"), true, (left - buttonWidth) * 0.5f, 240f, buttonWidth, 48f,
                OnStartMatch, 20f);

            Ui.Line(panel, left + 12f, 30f, 1f, h - 60f, 0.4f);
            var tips = new[]
            {
                (Loc.Get("ui.intro.goalTitle"), Loc.Get("ui.intro.goalText")),
                (Loc.Get("ui.intro.moraleTitle"), Loc.Format("ui.intro.moraleText", Match.MoralePerTurn, Match.MaxMorale)),
                (Loc.Format("ui.intro.ordersTitle", Match.OrdersPerTurn), Loc.Get("ui.intro.ordersText")),
            };
            for (int i = 0; i < tips.Length; i++)
            {
                float y = 30f + tipHeight * i;
                if (i > 0) Ui.Line(panel, tipX, y - 6f, tipWidth, 1f, 0.3f);
                if (i == 0) Ui.Image(panel, "Icon", Ui.UnitArt(UnitType.Sovereign, Opponent), tipX, y + 4f, 32f, 32f);
                else if (i == 1)
                    for (int k = 0; k < 3; k++) Ui.Image(panel, "Pip", Ui.Art("Pip_On"), tipX + k * 11f, y + 14f, 10f, 10f);
                else Ui.Image(panel, "Icon", Ui.Art("Order_On"), tipX, y + 5f, 32f, 30f);
                var (title, text) = tips[i];
                Ui.Text(panel, title, Ui.TitleFont, 16f, Ui.InkText, tipX + 42f, y, tipWidth - 42f, 22f);
                Ui.Text(panel, text, Ui.BodyFont, 14f, Ui.Ink, tipX + 42f, y + 22f, tipWidth - 42f, 54f);
            }
        }

        // Also outside the design doc, for the same reason: the options are read one at a time, enlarged, with the
        // viewer of the hand's cards.
        void BuildForesightMobile()
        {
            _foresightCards.Clear();
            var options = _match.ForesightOptions;
            var source = _match.ForesightCard;
            int n = options.Count;
            _foresightPick = Mathf.Clamp(_foresightPick, 0, n - 1);
            var shade = Ui.Stretch(Ui.Image(_overlayNative, "Shade", Ui.Solid, 0f, 0f, 0f, 0f).rectTransform);
            shade.GetComponent<Image>().color = new Color(ScrimColor.r, ScrimColor.g, ScrimColor.b, 0.4f);
            var card = CardViewer(_overlayNative, CardCatalog.Get(options[_foresightPick]), n > 1, dir =>
            {
                _foresightPick = (_foresightPick + dir + n) % n;
                RefreshOverlay();
            }, out var column);
            _foresightCards.Add(card);

            float width = column.sizeDelta.x;
            column.anchoredPosition = new Vector2(column.anchoredPosition.x, -40f);
            ViewerLabel(column, Loc.Format("ui.foresight.eyebrow", Loc.CardName(source),
                Loc.KindName(CardCatalog.Get(source).Kind)), 0f).textWrappingMode = TextWrappingModes.NoWrap;
            Ui.Text(column, Loc.Get("ui.foresight.title"), Ui.TitleFont, 28f, Ui.Parchment, 0f, 22f, width, 36f);
            string lead = source == CardId.OracleEcho ? Loc.Get("ui.foresight.leadEcho")
                : n == 1 ? Loc.Get("ui.foresight.leadOne") : Loc.Format("ui.foresight.lead", n);
            if (n > 1 && source != CardId.OracleEcho)
                lead += " " + (n == 2 ? Loc.Get("ui.foresight.restOne") : Loc.Format("ui.foresight.rest", n - 1));
            float y = ViewerNote(column, lead, 66f) + 12f;
            ViewerLabel(column, Loc.Format("ui.m.zoom.count", _foresightPick + 1, n), y);
            string action = source switch
            {
                CardId.ScoutsAhead => "ui.foresight.keep",
                CardId.OracleEcho => "ui.foresight.recover",
                _ => "ui.foresight.buy",
            };
            var pick = Ui.Button(column, Loc.Format(action, Loc.CardName(options[_foresightPick])), true, 0f, y + 28f, width,
                48f, OnForesight, 18f);
            var label = pick.GetComponentInChildren<TextMeshProUGUI>();
            label.margin = new Vector4(10f, 0f, 10f, 0f);
            label.enableAutoSizing = true;
            label.fontSizeMin = 12f;
            label.fontSizeMax = 18f;
        }

        RectTransform MobileStage()
        {
            var area = UiProfile.MobileArea;
            var stage = Ui.Rect(_overlayNative, "Stage", 0f, 0f, area.x, area.y);
            stage.anchorMin = stage.anchorMax = stage.pivot = new Vector2(0.5f, 0.5f);
            stage.anchoredPosition = Vector2.zero;
            return stage;
        }

        // A panel whose height follows its text: it is shrunk as a whole when it would not fit the area.
        RectTransform FittedModal(float w, float h)
        {
            var area = UiProfile.MobileArea;
            var stage = MobileStage();
            float fit = Mathf.Min(1f, (area.y - 16f) / h);
            stage.localScale = new Vector3(fit, fit, 1f);
            return Ui.Modal(stage, (area.x - w) * 0.5f, (area.y - h) * 0.5f, w, h);
        }

        void BuildNoticeMobile(string eyebrow, string title, string text, string button, UnityEngine.Events.UnityAction onClick)
        {
            const float w = 420f, h = 212f, pad = 24f, buttonWidth = 220f;
            var panel = MobileModal(w, h, 84f);
            MobileEyebrow(panel, eyebrow, 0f, 22f, w).color = Ui.Rubric;
            AutoSize(Ui.Text(panel, Ui.Initial(title, 118), Ui.TitleFont, 26f, Ui.InkText, pad, 42f, w - pad * 2f, 36f,
                TextAlignmentOptions.Top), 18f).textWrappingMode = TextWrappingModes.NoWrap;
            Ui.Text(panel, text, Ui.BodyFont, 16.5f, Ui.InkText, pad, 86f, w - pad * 2f, 48f, TextAlignmentOptions.Top);
            Ui.Button(panel, button, true, (w - buttonWidth) * 0.5f, 146f, buttonWidth, 44f, onClick, 17f);
        }

        // Not in the design doc: the end of the match laid out for the phone, the verdict on the left and the
        // numbers and what to do next on the right.
        void BuildGameOverMobile()
        {
            var winner = _match.Winner ?? Side.White;
            var loser = winner.Other();
            bool won = winner == _human;
            var (fall, captures, saves, played) = GameOverStats();

            const float w = 720f, h = 330f, left = 330f, rightX = 356f, rightWidth = w - rightX - 24f, token = 60f;
            var panel = MobileModal(w, h);
            MobileEyebrow(panel, Loc.Get("ui.gameOver.header"), 0f, 22f, left);
            Ui.Image(panel, "Sovereign", Ui.UnitArt(UnitType.Sovereign, loser), (left - token) * 0.5f, 48f, token, token);
            Cross(panel, (left - token) * 0.5f, 48f, token, Ui.Rubric, 3f);
            _gameOverTitle = Ui.Text(panel, Loc.Get(won ? "ui.gameOver.victory" : "ui.gameOver.defeat"), Ui.FrakturFont, 64f,
                won ? Ui.Rubric : Ui.Ink, 0f, 112f, left, 84f, TextAlignmentOptions.Top);
            if (fall != null)
                Ui.Text(panel, won
                        ? Loc.Format("ui.gameOver.won", Loc.SideName(loser), fall.To, Ui.Roman(fall.Turn))
                        : Loc.Format("ui.gameOver.lost", fall.To, Ui.Roman(fall.Turn)),
                    Ui.ItalicFont, 15f, Ui.Ink, 24f, 204f, left - 48f, 44f, TextAlignmentOptions.Top);
            Ui.Line(panel, left + 12f, 30f, 1f, h - 60f, 0.4f);

            var stats = new[]
            {
                (Ui.Roman(_match.Turn), Loc.Get("ui.gameOver.turns")),
                (captures.ToString(), Loc.Get("ui.gameOver.captures")),
                (saves.ToString(), Loc.Get("ui.gameOver.saves")),
                (played.ToString(), Loc.Get("ui.gameOver.cards")),
            };
            const float statsY = 30f, statHeight = 66f;
            float statWidth = rightWidth * 0.5f;
            Ui.Line(panel, rightX, statsY, rightWidth, 1f, 0.4f);
            Ui.Line(panel, rightX, statsY + statHeight, rightWidth, 1f, 0.4f);
            Ui.Line(panel, rightX, statsY + statHeight * 2f, rightWidth, 1f, 0.4f);
            Ui.Line(panel, rightX + statWidth, statsY, 1f, statHeight * 2f, 0.4f);
            for (int i = 0; i < stats.Length; i++)
            {
                float x = rightX + statWidth * (i % 2), y = statsY + statHeight * (i / 2);
                var (value, caption) = stats[i];
                Ui.Text(panel, value, Ui.TitleFont, 26f, Ui.InkText, x, y + 8f, statWidth, 30f, TextAlignmentOptions.Top);
                Ui.Text(panel, caption, Ui.BodyFont, 14f, Ui.Ink, x + 6f, y + 40f, statWidth - 12f, 20f,
                    TextAlignmentOptions.Top);
            }

            const float buttonsY = 184f, button = 46f, gap = 8f;
            float half = (rightWidth - gap) * 0.5f;
            if (_net != null)
            {
                Ui.Button(panel, Loc.Get("ui.gameOver.menu"), true, rightX, buttonsY, rightWidth, button, LoadMenu, 18f);
                Ui.Button(panel, Loc.Get("ui.gameOver.review"), false, rightX, buttonsY + button + gap, rightWidth, button,
                    Review, 18f);
                return;
            }
            Ui.Button(panel, Loc.Get("ui.gameOver.button"), true, rightX, buttonsY, rightWidth, button, NewMatch, 18f);
            Ui.Button(panel, Loc.Get("ui.gameOver.review"), false, rightX, buttonsY + button + gap, half, button, Review, 17f);
            Ui.Button(panel, Loc.Get("ui.gameOver.menu"), false, rightX + half + gap, buttonsY + button + gap, half, button,
                LoadMenu, 17f);
        }

        // Also outside the design doc: the card on the left and the announcement beside it, the panel as tall as
        // the text needs.
        void BuildConditionMobile(CardId card, bool notice)
        {
            const float w = 720f, pad = 22f, cardScale = 0.72f, gap = 10f;
            float cardW = Ui.CardWidth * cardScale, cardH = Ui.CardHeight * cardScale;
            float columnX = pad + cardW + 22f, columnWidth = w - columnX - pad;

            var column = Ui.Rect(_overlayNative, "Column", 0f, 0f, columnWidth, 0f);
            float y = 0f;
            TextMeshProUGUI Wrapped(string text, TMP_FontAsset font, float size, Color color, float x, float width)
            {
                var t = Ui.Text(column, text, font, size, color, x, y, width, 0f);
                t.rectTransform.sizeDelta = new Vector2(width, t.GetPreferredValues(text, width, 0f).y);
                return t;
            }

            var eyebrow = Ui.Label(column, Loc.Get(notice ? "ui.weather.eyebrow" : "ui.weather.eyebrowActive"), 0f, y,
                columnWidth, 18f);
            eyebrow.fontSize = 13f;
            eyebrow.characterSpacing = 16f;
            eyebrow.color = Ui.Rubric;
            y += 18f + gap;
            y += Wrapped(Ui.Initial(Loc.Get("ui.weather.title." + card), 122), Ui.TitleFont, 28f, Ui.InkText, 0f, columnWidth)
                .rectTransform.sizeDelta.y + gap;
            y += Wrapped(ConditionLead(card, notice), Ui.ItalicFont, 15f, Ui.Ink, 0f, columnWidth).rectTransform.sizeDelta.y
                + gap;

            const float boxPadX = 14f, boxPadY = 10f;
            var box = Ui.Sliced(column, "Effect", Ui.Framed, 0f, y, columnWidth, 0f);
            float boxY = y;
            y += boxPadY;
            float effect = Wrapped(Loc.CardText(card), Ui.BodyFont, 16.5f, Ui.InkText, boxPadX, columnWidth - boxPadX * 2f)
                .rectTransform.sizeDelta.y;
            box.rectTransform.sizeDelta = new Vector2(columnWidth, effect + boxPadY * 2f);
            y = boxY + effect + boxPadY * 2f + gap;

            string facts = string.Join(" · ", Loc.Get("ui.weather.both"), Loc.Get("ui.weather.lasting"),
                Loc.Format("ui.weather.listed", Loc.Get("ui.conditions.header")));
            y += Wrapped(facts, Ui.ItalicFont, 13.5f, Ui.InkBody, 0f, columnWidth).rectTransform.sizeDelta.y + gap + 4f;

            const float buttonHeight = 44f;
            var button = (RectTransform)FitButton(column, Loc.Get(notice ? "ui.weather.button" : "ui.weather.close"), true,
                buttonHeight, 17f, 24f, notice ? DismissWeather : CloseCondition).transform;
            button.anchoredPosition = new Vector2(0f, -y);
            float noteX = button.sizeDelta.x + 14f;
            Ui.Text(column, Loc.Format("ui.weather.remove", Loc.CardName(CardId.DawnPrayer)), Ui.ItalicFont, 13.5f,
                Ui.InkBody, noteX, y, columnWidth - noteX, buttonHeight, TextAlignmentOptions.MidlineLeft);
            y += buttonHeight;

            float h = pad * 2f + Mathf.Max(cardH, y);
            var panel = FittedModal(w, h);
            Ui.Card(panel, CardCatalog.Get(card), cardScale).anchoredPosition = new Vector2(pad, -(h - cardH) * 0.5f);
            column.SetParent(panel, false);
            column.anchoredPosition = new Vector2(columnX, -(h - y) * 0.5f);
        }
    }
}
