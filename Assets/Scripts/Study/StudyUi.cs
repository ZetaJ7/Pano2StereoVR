using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Pano2StereoVR.Study
{
    public enum ButtonKind
    {
        Primary,
        Secondary,
    }

    /// <summary>
    /// Runtime-built UI in the <see cref="StudyTheme"/> style. The desktop canvas (Screen Space Overlay) is only
    /// drawn on the PC monitor, not in the headset: a parchment page with one centred ivory card (scrolls when
    /// the content is taller than the card) and a thin experimenter status bar. The headset canvas (World Space,
    /// about 2 m in front of the viewer) holds instructions and rating panels in the dark palette. A small
    /// separate headset hint (no buttons) is used while clips play.
    /// All canvases are children of this GameObject, so destroying it removes the whole UI.
    /// </summary>
    public sealed class StudyUi : MonoBehaviour
    {
        public const float CardWidth = 1640f;
        public const float CardHeight = 960f;
        public const float StatusBarHeight = 44f;
        // RectOffset calls into the engine, so it must not be created in a static or field initializer of a
        // MonoBehaviour (UnityException "set_left is not allowed..."); build it on demand instead.
        private static RectOffset CardPadding() { return new RectOffset(96, 96, 56, 56); }

        public Font Font;
        public Canvas DesktopCanvas { get; private set; }
        public Canvas HmdCanvas { get; private set; }
        public Canvas HintCanvas { get; private set; }
        public RectTransform DesktopContent { get; private set; }
        public RectTransform HmdContent { get; private set; }
        public Text StatusText { get; private set; }
        public Button AbortButton { get; private set; }
        /// <summary>QA only: skip to the next group / the next part (hidden in formal mode).</summary>
        public Button SkipGroupButton { get; private set; }
        public Button SkipPartButton { get; private set; }
        public Button RecenterButton { get; private set; }
        public readonly List<Button> HmdButtons = new List<Button>();

        private readonly Dictionary<KeyCode, Action> _shortcuts = new Dictionary<KeyCode, Action>();
        private RectTransform _desktopPage;
        private ScrollRect _scroll;
        private RectTransform _hmdPanel;
        private Text _hintText;
        private RectTransform _statusBar;

        public bool HintVisible => HintCanvas != null && HintCanvas.gameObject.activeSelf;

        public void Build(Camera cam)
        {
            if (Font == null)
            {
                Font = Font.CreateDynamicFontFromOSFont(StudyTheme.FontNames, 32);
            }
            if (FindObjectOfType<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem");
                es.AddComponent<EventSystem>();
                es.AddComponent<StandaloneInputModule>();
            }
            StudyTheme.Palette L = StudyTheme.Light;

            // ---------------------------------------------------------------- desktop canvas
            DesktopCanvas = NewCanvas("DesktopCanvas", RenderMode.ScreenSpaceOverlay, null, transform);
            var scaler = DesktopCanvas.gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;

            _desktopPage = NewRect("DesktopPage", DesktopCanvas.transform);
            Stretch(_desktopPage);
            _desktopPage.gameObject.AddComponent<Image>().color = L.Page;

            RectTransform border = NewRect("CardBorder", _desktopPage);
            border.anchorMin = border.anchorMax = new Vector2(0.5f, 0.5f);
            border.sizeDelta = new Vector2(CardWidth + 2f, CardHeight + 2f);
            border.anchoredPosition = new Vector2(0f, -StatusBarHeight * 0.5f);
            border.gameObject.AddComponent<Image>().color = L.CardBorder;
            RectTransform card = NewRect("Card", border);
            Stretch(card, 1f);
            card.gameObject.AddComponent<Image>().color = L.Card;

            RectTransform viewport = NewRect("Viewport", card);
            Stretch(viewport);
            viewport.gameObject.AddComponent<RectMask2D>();
            DesktopContent = NewRect("Content", viewport);
            DesktopContent.anchorMin = new Vector2(0f, 1f);
            DesktopContent.anchorMax = new Vector2(1f, 1f);
            DesktopContent.pivot = new Vector2(0.5f, 1f);
            DesktopContent.sizeDelta = Vector2.zero;
            var v = DesktopContent.gameObject.AddComponent<VerticalLayoutGroup>();
            v.padding = CardPadding();
            v.spacing = 18;
            v.childAlignment = TextAnchor.MiddleCenter;
            v.childControlWidth = true;
            v.childControlHeight = true;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;
            // at least the card height, so short pages sit in the middle of the card
            DesktopContent.gameObject.AddComponent<LayoutElement>().minHeight = CardHeight - 2f;
            var fitter = DesktopContent.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            _scroll = card.gameObject.AddComponent<ScrollRect>();
            _scroll.content = DesktopContent;
            _scroll.viewport = viewport;
            _scroll.horizontal = false;
            _scroll.vertical = true;
            _scroll.movementType = ScrollRect.MovementType.Clamped;
            _scroll.inertia = false;
            _scroll.scrollSensitivity = 40f;

            // experimenter status bar (drawn last, on top)
            _statusBar = NewRect("StatusBar", DesktopCanvas.transform);
            _statusBar.anchorMin = new Vector2(0, 1);
            _statusBar.anchorMax = new Vector2(1, 1);
            _statusBar.pivot = new Vector2(0.5f, 1);
            _statusBar.sizeDelta = new Vector2(0, StatusBarHeight);
            _statusBar.anchoredPosition = Vector2.zero;
            _statusBar.gameObject.AddComponent<Image>().color = StudyTheme.StatusBar;
            var barLayout = _statusBar.gameObject.AddComponent<HorizontalLayoutGroup>();
            barLayout.padding = new RectOffset(20, 12, 6, 6);
            barLayout.spacing = 10;
            barLayout.childForceExpandWidth = false;
            barLayout.childControlWidth = true;
            barLayout.childControlHeight = true;
            StatusText = NewText(_statusBar, "", 18, TextAnchor.MiddleLeft, StudyTheme.StatusText);
            StatusText.horizontalOverflow = HorizontalWrapMode.Overflow;
            StatusText.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
            SkipGroupButton = NewButton(_statusBar, "Next group (PgDn)", 17, null, 190, 32, ButtonKind.Secondary, "Btn_SkipGroup", StudyTheme.Dark);
            SkipPartButton = NewButton(_statusBar, "Next part (Shift+PgDn)", 17, null, 230, 32, ButtonKind.Secondary, "Btn_SkipPart", StudyTheme.Dark);
            SkipGroupButton.gameObject.SetActive(false);
            SkipPartButton.gameObject.SetActive(false);
            RecenterButton = NewButton(_statusBar, "Recenter (R)", 17, null, 160, 32, ButtonKind.Secondary, "Btn_Recenter", StudyTheme.Dark);
            AbortButton = NewButton(_statusBar, "Abort (Esc)", 17, null, 140, 32, ButtonKind.Secondary, "Btn_Abort", StudyTheme.Dark);

            // ---------------------------------------------------------------- headset canvas
            HmdCanvas = NewCanvas("HmdCanvas", RenderMode.WorldSpace, cam, transform);
            var rt = (RectTransform)HmdCanvas.transform;
            rt.sizeDelta = new Vector2(1600, 900);
            rt.localScale = Vector3.one * 0.001f;
            HmdCanvas.gameObject.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 3f;
            _hmdPanel = NewRect("HmdPanel", HmdCanvas.transform);
            Stretch(_hmdPanel);
            _hmdPanel.gameObject.AddComponent<Image>().color = StudyTheme.Dark.Card;
            HmdContent = NewVertical(_hmdPanel, "Content", 22, new RectOffset(64, 64, 44, 44));
            HmdContent.GetComponent<VerticalLayoutGroup>().childAlignment = TextAnchor.MiddleCenter;
            ShowHmd(false);

            // ---------------------------------------------------------------- headset hint (no buttons)
            HintCanvas = NewCanvas("HintCanvas", RenderMode.WorldSpace, cam, transform);
            var ht = (RectTransform)HintCanvas.transform;
            ht.sizeDelta = new Vector2(1300, 120);
            ht.localScale = Vector3.one * 0.001f;
            HintCanvas.gameObject.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 3f;
            RectTransform hintPanel = NewRect("HintPanel", HintCanvas.transform);
            Stretch(hintPanel);
            hintPanel.gameObject.AddComponent<Image>().color = StudyTheme.WithAlpha(StudyTheme.Hex("30302e"), 0.82f);
            _hintText = FitLine(NewText(hintPanel, "", 36, TextAnchor.MiddleCenter, StudyTheme.Dark.TextSecondary));
            Stretch((RectTransform)_hintText.transform, 12f);
            HintCanvas.gameObject.SetActive(false);
        }

        // ------------------------------------------------------------------ desktop
        public void ShowDesktopPanel(bool show)
        {
            _desktopPage.gameObject.SetActive(show);
        }

        public void ClearDesktop()
        {
            Clear(DesktopContent);
            DesktopContent.anchoredPosition = Vector2.zero;
            if (_scroll != null)
            {
                _scroll.velocity = Vector2.zero;
            }
            ShowDesktopPanel(true);
        }

        public void SetStatus(string text)
        {
            if (StatusText != null)
            {
                StatusText.text = text;
            }
        }

        public void SetBarLabels(string recenter, string abort, string skipGroup = null, string skipPart = null)
        {
            SetLabel(RecenterButton, recenter);
            SetLabel(AbortButton, abort);
            if (skipGroup != null)
            {
                SetLabel(SkipGroupButton, skipGroup);
            }
            if (skipPart != null)
            {
                SetLabel(SkipPartButton, skipPart);
            }
        }

        /// <summary>Show the QA skip controls (never in formal mode).</summary>
        public void ShowSkipControls(bool show)
        {
            SkipGroupButton.gameObject.SetActive(show);
            SkipPartButton.gameObject.SetActive(show);
        }

        public static void SetLabel(Button b, string label)
        {
            if (b == null)
            {
                return;
            }
            Text t = b.GetComponentInChildren<Text>(true);
            if (t != null)
            {
                t.text = label;
            }
        }

        // ------------------------------------------------------------------ headset
        public void ShowHmd(bool show)
        {
            HmdCanvas.gameObject.SetActive(show);
            if (!show)
            {
                HmdButtons.Clear();
                _shortcuts.Clear();
            }
        }

        public void ClearHmd()
        {
            Clear(HmdContent);
            HmdButtons.Clear();
            _shortcuts.Clear();
            ShowHmd(true);
        }

        public void SetHmdSize(float w, float h)
        {
            ((RectTransform)HmdCanvas.transform).sizeDelta = new Vector2(w, h);
        }

        public void PlaceHmdCanvas(Vector3 headPosition, Vector3 yawForward, float distance, float heightOffset = -0.1f)
        {
            Place(HmdCanvas.transform, headPosition, yawForward, distance, heightOffset);
        }

        public void ShowHint(string text)
        {
            _hintText.text = text;
            HintCanvas.gameObject.SetActive(true);
        }

        public void HideHint()
        {
            if (HintCanvas != null)
            {
                HintCanvas.gameObject.SetActive(false);
            }
        }

        public void PlaceHint(Vector3 headPosition, Vector3 yawForward, float distance, float heightOffset)
        {
            Place(HintCanvas.transform, headPosition, yawForward, distance, heightOffset);
        }

        private static void Place(Transform t, Vector3 headPosition, Vector3 yawForward, float distance, float heightOffset)
        {
            t.position = headPosition + yawForward.normalized * distance + Vector3.up * heightOffset;
            t.rotation = Quaternion.LookRotation(yawForward.normalized, Vector3.up);
        }

        public void AddShortcut(KeyCode key, Action action)
        {
            _shortcuts[key] = action;
        }

        public void ClearShortcuts()
        {
            _shortcuts.Clear();
        }

        private void Update()
        {
            if (_shortcuts.Count == 0)
            {
                return;
            }
            foreach (KeyValuePair<KeyCode, Action> kv in _shortcuts)
            {
                if (Input.GetKeyDown(kv.Key))
                {
                    Action a = kv.Value;
                    a?.Invoke();
                    break;
                }
            }
        }

        public Button HmdButton(Transform parent, string label, Action onClick, float width = 260, float height = 90, int size = 30,
            ButtonKind kind = ButtonKind.Secondary, string name = null)
        {
            Button b = NewButton(parent, label, size, onClick, width, height, kind, name);
            HmdButtons.Add(b);
            return b;
        }

        // ------------------------------------------------------------------ text blocks
        public StudyTheme.Palette PaletteFor(Transform t)
        {
            for (Transform p = t; p != null; p = p.parent)
            {
                if ((HmdCanvas != null && p == HmdCanvas.transform) || (HintCanvas != null && p == HintCanvas.transform)
                    || (_statusBar != null && p == _statusBar))
                {
                    return StudyTheme.Dark;
                }
            }
            return StudyTheme.Light;
        }

        /// <summary>Centred title in the accent colour (no bold).</summary>
        public Text Title(Transform parent, string text, int size = 0)
        {
            StudyTheme.Palette p = PaletteFor(parent);
            Text t = NewText(parent, text, size > 0 ? size : p.TitleSize, TextAnchor.MiddleCenter, p.Title);
            t.lineSpacing = 1.1f;
            return t;
        }

        /// <summary>Prominent notice: a large accent title and short steps or a short sentence below it.</summary>
        public void Notice(Transform parent, string title, string body, TextAnchor bodyAnchor = TextAnchor.UpperCenter)
        {
            StudyTheme.Palette p = PaletteFor(parent);
            Title(parent, title, p.NoticeSize);
            if (!string.IsNullOrEmpty(body))
            {
                Spacer(parent, 6);
                Text b = Body(parent, body, bodyAnchor);
                b.lineSpacing = 1.35f;
            }
        }

        public Text Body(Transform parent, string text, TextAnchor anchor = TextAnchor.UpperLeft, int size = 0)
        {
            StudyTheme.Palette p = PaletteFor(parent);
            Text t = NewText(parent, text, size > 0 ? size : p.BodySize, anchor, p.Text);
            t.lineSpacing = 1.2f;
            return t;
        }

        /// <summary>Body text in a centred block of fixed width (left-aligned lines under a centred title).</summary>
        public Text BodyBlock(Transform parent, string text, float width, int size = 0)
        {
            RectTransform row = NewRow(parent, 0, TextAnchor.UpperCenter);
            Text t = Body(row, text, TextAnchor.UpperLeft, size);
            t.lineSpacing = 1.35f;
            var le = t.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = width;
            le.minWidth = width;
            return t;
        }

        public Text Secondary(Transform parent, string text, TextAnchor anchor = TextAnchor.UpperLeft, int size = 0)
        {
            StudyTheme.Palette p = PaletteFor(parent);
            return FitLine(NewText(parent, text, size > 0 ? size : p.BodySize - 4, anchor, p.TextSecondary));
        }

        /// <summary>Small secondary text (item explanations, hints).</summary>
        public Text Note(Transform parent, string text, TextAnchor anchor = TextAnchor.UpperLeft, int size = 0)
        {
            StudyTheme.Palette p = PaletteFor(parent);
            return FitLine(NewText(parent, text, size > 0 ? size : p.NoteSize, anchor, p.TextMuted));
        }

        /// <summary>
        /// Short texts shrink slightly (down to 80 % of their size) instead of wrapping onto a new line
        /// (user 11-19). Text that would still wrap at 80 % is a paragraph and keeps its size.
        /// </summary>
        private static Text FitLine(Text t)
        {
            var fit = t.gameObject.AddComponent<FitTextToLine>();
            fit.Init(t.fontSize, 0.8f);
            return t;
        }

        public Text Colored(Transform parent, string text, Color color, TextAnchor anchor = TextAnchor.UpperLeft, int size = 0)
        {
            StudyTheme.Palette p = PaletteFor(parent);
            return NewText(parent, text, size > 0 ? size : p.BodySize - 2, anchor, color);
        }

        public void Spacer(Transform parent, float height)
        {
            RectTransform r = NewRect("Spacer", parent);
            var le = r.gameObject.AddComponent<LayoutElement>();
            le.minHeight = height;
            le.preferredHeight = height;
        }

        public Text NewText(Transform parent, string text, int size, TextAnchor anchor, Color color)
        {
            RectTransform r = NewRect("Text", parent);
            var t = r.gameObject.AddComponent<Text>();
            t.font = Font;
            t.fontSize = size;
            t.fontStyle = FontStyle.Normal;
            t.color = color;
            t.alignment = anchor;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.supportRichText = true;
            t.raycastTarget = false;
            t.text = text;
            return t;
        }

        // ------------------------------------------------------------------ controls
        public Button NewButton(Transform parent, string label, int size, Action onClick, float width, float height,
            ButtonKind kind = ButtonKind.Secondary, string name = null, StudyTheme.Palette palette = null)
        {
            StudyTheme.Palette p = palette ?? PaletteFor(parent);
            RectTransform r = NewRect(string.IsNullOrEmpty(name) ? "Button" : name, parent);
            var img = r.gameObject.AddComponent<Image>();
            img.color = Color.white;
            var b = r.gameObject.AddComponent<Button>();
            bool primary = kind == ButtonKind.Primary;
            ColorBlock cb = b.colors;
            cb.normalColor = primary ? p.Primary : p.Secondary;
            cb.highlightedColor = primary ? p.PrimaryHover : p.SecondaryHover;
            cb.selectedColor = cb.normalColor;
            cb.pressedColor = primary ? p.PrimaryPressed : p.SecondaryPressed;
            cb.disabledColor = p.Disabled;
            cb.colorMultiplier = 1f;
            cb.fadeDuration = 0.08f;
            b.colors = cb;
            b.targetGraphic = img;
            var nav = b.navigation;
            nav.mode = Navigation.Mode.None;
            b.navigation = nav;
            var le = r.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = width;
            le.preferredHeight = height;
            le.minHeight = height;
            Text t = FitLine(NewText(r, label, size, TextAnchor.MiddleCenter, primary ? p.OnPrimary : p.OnSecondary));
            Stretch((RectTransform)t.transform, 6f);
            var style = r.gameObject.AddComponent<StudyButtonStyle>();
            style.Button = b;
            style.Label = t;
            style.Background = cb.normalColor;
            style.Hover = cb.highlightedColor;
            style.Pressed = cb.pressedColor;
            style.DisabledBackground = p.Disabled;
            style.SelectedBackground = p.Selected;
            style.LabelColor = primary ? p.OnPrimary : p.OnSecondary;
            style.DisabledLabel = p.OnDisabled;
            style.SelectedLabel = p.OnSelected;
            if (onClick != null)
            {
                b.onClick.AddListener(() => onClick());
            }
            return b;
        }

        public RectTransform NewRow(Transform parent, float spacing = 10, TextAnchor align = TextAnchor.MiddleCenter, float height = -1)
        {
            RectTransform r = NewRect("Row", parent);
            var h = r.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = spacing;
            h.childAlignment = align;
            h.childControlWidth = true;
            h.childControlHeight = true;
            h.childForceExpandWidth = false;
            h.childForceExpandHeight = false;
            if (height > 0)
            {
                var le = r.gameObject.AddComponent<LayoutElement>();
                le.preferredHeight = height;
                le.minHeight = height;
            }
            return r;
        }

        /// <summary>Side-by-side vertical columns of equal width.</summary>
        public RectTransform[] NewColumns(Transform parent, int count, float spacing, float columnSpacing)
        {
            RectTransform row = NewRect("Columns", parent);
            var h = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = spacing;
            h.childAlignment = TextAnchor.UpperLeft;
            h.childControlWidth = true;
            h.childControlHeight = true;
            h.childForceExpandWidth = true;
            h.childForceExpandHeight = false;
            var cols = new RectTransform[count];
            for (int i = 0; i < count; i++)
            {
                RectTransform c = NewRect("Column", row);
                var v = c.gameObject.AddComponent<VerticalLayoutGroup>();
                v.spacing = columnSpacing;
                v.childAlignment = TextAnchor.UpperLeft;
                v.childControlWidth = true;
                v.childControlHeight = true;
                v.childForceExpandWidth = true;
                v.childForceExpandHeight = false;
                c.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
                cols[i] = c;
            }
            return cols;
        }

        public Toggle NewToggle(Transform parent, string label, int size, Action<bool> onChanged, string name = null)
        {
            StudyTheme.Palette p = PaletteFor(parent);
            RectTransform row = NewRow(parent, 16, TextAnchor.MiddleLeft, 52);
            row.name = string.IsNullOrEmpty(name) ? "ToggleRow" : name;
            RectTransform box = NewRect("Box", row);
            var boxImg = box.gameObject.AddComponent<Image>();
            boxImg.color = p.CheckBox;
            var le = box.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = 32;
            le.preferredHeight = 32;
            le.minWidth = 32;
            RectTransform check = NewRect("Check", box);
            Stretch(check, 6);
            var checkImg = check.gameObject.AddComponent<Image>();
            checkImg.color = p.Primary;
            var toggle = box.gameObject.AddComponent<Toggle>();
            toggle.targetGraphic = boxImg;
            toggle.graphic = checkImg;
            ColorBlock cb = toggle.colors;
            cb.normalColor = Color.white;
            cb.highlightedColor = new Color(0.92f, 0.92f, 0.92f, 1f);
            cb.selectedColor = Color.white;
            toggle.colors = cb;
            toggle.isOn = false;
            if (onChanged != null)
            {
                toggle.onValueChanged.AddListener(v => onChanged(v));
            }
            Text t = NewText(row, label, size, TextAnchor.MiddleLeft, p.Text);
            t.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
            return toggle;
        }

        public InputField NewInput(Transform parent, string initial, int size, float width, float height, bool multiline = false, string name = null)
        {
            StudyTheme.Palette p = PaletteFor(parent);
            RectTransform outer = NewRect(string.IsNullOrEmpty(name) ? "Input" : name, parent);
            outer.gameObject.AddComponent<Image>().color = p.CardBorder;
            var le = outer.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = width;
            le.preferredHeight = height;
            le.minHeight = height;
            RectTransform r = NewRect("Field", outer);
            Stretch(r, 1f);
            var img = r.gameObject.AddComponent<Image>();
            img.color = p.InputBackground;
            Text t = NewText(r, "", size, multiline ? TextAnchor.UpperLeft : TextAnchor.MiddleLeft, p.InputText);
            t.supportRichText = false;
            Stretch((RectTransform)t.transform, 10);
            var input = r.gameObject.AddComponent<InputField>();
            input.textComponent = t;
            input.targetGraphic = img;
            input.lineType = multiline ? InputField.LineType.MultiLineNewline : InputField.LineType.SingleLine;
            input.text = initial ?? "";
            return input;
        }

        /// <summary>A labelled row of mutually exclusive choice buttons (GameObject name "ChoiceRow").</summary>
        public sealed class ChoiceRow
        {
            public readonly List<Button> Buttons = new List<Button>();
            public int Selected = -1;
            public Action<int> Changed;

            public void Select(int i)
            {
                Selected = i;
                for (int k = 0; k < Buttons.Count; k++)
                {
                    StudyButtonStyle s = Buttons[k].GetComponent<StudyButtonStyle>();
                    if (s != null)
                    {
                        s.SetSelected(k == i);
                    }
                }
                Changed?.Invoke(i);
            }
        }

        public ChoiceRow NewChoiceRow(Transform parent, string label, string note, IList<string> options, int labelSize, int optionSize,
            float labelWidth, float optionWidth, float height)
        {
            RectTransform row = NewRow(parent, 6, TextAnchor.MiddleLeft, -1);
            row.name = "ChoiceRow";
            if (!string.IsNullOrEmpty(label))
            {
                RectTransform labelBlock = NewRect("Label", row);
                var lv = labelBlock.gameObject.AddComponent<VerticalLayoutGroup>();
                lv.spacing = 0;
                lv.childAlignment = TextAnchor.MiddleLeft;
                lv.childControlWidth = true;
                lv.childControlHeight = true;
                lv.childForceExpandWidth = true;
                lv.childForceExpandHeight = false;
                var le = labelBlock.gameObject.AddComponent<LayoutElement>();
                le.preferredWidth = labelWidth;
                le.minWidth = labelWidth;
                le.minHeight = height;
                StudyTheme.Palette p = PaletteFor(parent);
                NewText(labelBlock, label, labelSize, TextAnchor.MiddleLeft, p.Text);
                if (!string.IsNullOrEmpty(note))
                {
                    FitLine(NewText(labelBlock, note, Math.Max(12, labelSize - 5), TextAnchor.UpperLeft, p.TextMuted));
                }
            }
            var cr = new ChoiceRow();
            for (int i = 0; i < options.Count; i++)
            {
                int idx = i;
                Button b = NewButton(row, options[i], optionSize, () => cr.Select(idx), optionWidth, height, ButtonKind.Secondary, "Choice_" + i);
                cr.Buttons.Add(b);
            }
            return cr;
        }

        public static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = parent != null ? parent.gameObject.layer : 5;
            var r = (RectTransform)go.transform;
            r.SetParent(parent, false);
            return r;
        }

        private RectTransform NewVertical(Transform parent, string name, float spacing, RectOffset padding)
        {
            RectTransform r = NewRect(name, parent);
            Stretch(r);
            var v = r.gameObject.AddComponent<VerticalLayoutGroup>();
            v.spacing = spacing;
            v.padding = padding;
            v.childAlignment = TextAnchor.UpperLeft;
            v.childControlWidth = true;
            v.childControlHeight = true;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;
            return r;
        }

        private static Canvas NewCanvas(string name, RenderMode mode, Camera cam, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = 5;
            go.transform.SetParent(parent, false);
            var c = go.AddComponent<Canvas>();
            c.renderMode = mode;
            if (cam != null)
            {
                c.worldCamera = cam;
            }
            c.sortingOrder = mode == RenderMode.ScreenSpaceOverlay ? 100 : 0;
            go.AddComponent<GraphicRaycaster>();
            return c;
        }

        public static void Stretch(RectTransform r, float inset = 0)
        {
            r.anchorMin = Vector2.zero;
            r.anchorMax = Vector2.one;
            r.offsetMin = new Vector2(inset, inset);
            r.offsetMax = new Vector2(-inset, -inset);
        }

        private static void Clear(Transform t)
        {
            for (int i = t.childCount - 1; i >= 0; i--)
            {
                GameObject child = t.GetChild(i).gameObject;
                child.SetActive(false);
                Destroy(child);
            }
        }
    }
}
