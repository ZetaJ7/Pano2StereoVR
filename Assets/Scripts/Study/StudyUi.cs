using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Pano2StereoVR.Study
{
    public enum ButtonKind
    {
        /// <summary>The one action that moves the session on (bold label on the accent colour).</summary>
        Primary,
        Secondary,
        /// <summary>Irreversible action (abort, exit): bold label on red.</summary>
        Danger,
        /// <summary>Answer option: neutral until selected, then accent fill and bold label.</summary>
        Choice,
    }

    /// <summary>
    /// Runtime-built UI in the <see cref="StudyTheme"/> style. The desktop canvas (Screen Space Overlay) is only
    /// drawn on the PC monitor, not in the headset: a parchment page with one centred ivory card (rounded, soft
    /// shadow; scrolls when the content is taller than the card) and a thin experimenter status bar. The headset
    /// canvas (World Space, about 2 m in front of the viewer) holds instructions and rating panels in the dark
    /// palette. A small separate headset hint (no buttons) is used while clips play.
    /// Texts from the text table may mark words with ** (see <see cref="StudyMarkup"/>); every text created here
    /// shows them bold in the emphasis colour of its surface. Shapes are nine-sliced sprites generated at start
    /// from <see cref="StudyShapes"/> and destroyed with this object.
    /// All canvases are children of this GameObject, so destroying it removes the whole UI.
    /// </summary>
    public sealed class StudyUi : MonoBehaviour
    {
        public const float CardWidth = 1640f;
        public const float CardHeight = 960f;
        public const float StatusBarHeight = 44f;
        /// <summary>The scroll viewport stays this far inside the card's top and bottom edge (rounded corners).</summary>
        private const float CardClip = 6f;
        private const int ShadowBlur = 28;
        private const float ShadowDrop = 10f;
        /// <summary>Texels per canvas unit of the generated shape sprites.</summary>
        private const int ShapeScale = 2;
        // RectOffset calls into the engine, so it must not be created in a static or field initializer of a
        // MonoBehaviour (UnityException "set_left is not allowed..."); build it on demand instead.
        private static RectOffset CardPadding() { return new RectOffset(96, 96, 42, 42); }

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
        private readonly Dictionary<long, Sprite> _sprites = new Dictionary<long, Sprite>();
        private readonly List<UnityEngine.Object> _owned = new List<UnityEngine.Object>();
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
            StudyTheme.Palette D = StudyTheme.Dark;

            // ---------------------------------------------------------------- desktop canvas
            DesktopCanvas = NewCanvas("DesktopCanvas", RenderMode.ScreenSpaceOverlay, null, transform);
            var scaler = DesktopCanvas.gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;

            _desktopPage = NewRect("DesktopPage", DesktopCanvas.transform);
            Stretch(_desktopPage);
            _desktopPage.gameObject.AddComponent<Image>().color = L.Page;

            RectTransform frame = NewCardFrame(_desktopPage, "CardFrame", new Vector2(CardWidth, CardHeight), L, out RectTransform card);
            frame.anchoredPosition = new Vector2(0f, -StatusBarHeight * 0.5f);

            RectTransform viewport = NewRect("Viewport", card);
            Stretch(viewport);
            viewport.offsetMin = new Vector2(0f, CardClip);
            viewport.offsetMax = new Vector2(0f, -CardClip);
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
            // at least the viewport height, so short pages sit in the middle of the card
            DesktopContent.gameObject.AddComponent<LayoutElement>().minHeight = CardHeight - 2f * CardClip;
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
            StatusText = CreateText(_statusBar, "", 18, TextAnchor.MiddleLeft, StudyTheme.StatusText, false);
            StatusText.horizontalOverflow = HorizontalWrapMode.Overflow;
            StatusText.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
            SkipGroupButton = NewButton(_statusBar, "Next group (PgDn)", 17, null, 190, 32, ButtonKind.Secondary, "Btn_SkipGroup", D, 8);
            SkipPartButton = NewButton(_statusBar, "Next part (Shift+PgDn)", 17, null, 230, 32, ButtonKind.Secondary, "Btn_SkipPart", D, 8);
            SkipGroupButton.gameObject.SetActive(false);
            SkipPartButton.gameObject.SetActive(false);
            RecenterButton = NewButton(_statusBar, "Recenter (R)", 17, null, 160, 32, ButtonKind.Secondary, "Btn_Recenter", D, 8);
            AbortButton = NewButton(_statusBar, "Abort (Esc)", 17, null, 140, 32, ButtonKind.Secondary, "Btn_Abort", D, 8);

            // ---------------------------------------------------------------- headset canvas
            HmdCanvas = NewCanvas("HmdCanvas", RenderMode.WorldSpace, cam, transform);
            var rt = (RectTransform)HmdCanvas.transform;
            rt.sizeDelta = new Vector2(1600, 900);
            rt.localScale = Vector3.one * 0.001f;
            HmdCanvas.gameObject.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 3f;
            // rounded dark panel with a 2-unit lighter rim
            _hmdPanel = NewRect("HmdPanel", HmdCanvas.transform);
            Stretch(_hmdPanel);
            Round(_hmdPanel.gameObject.AddComponent<Image>(), D.PanelRadius + 2).color = D.CardBorder;
            RectTransform surface = NewRect("Surface", _hmdPanel);
            Stretch(surface, 2f);
            Round(surface.gameObject.AddComponent<Image>(), D.PanelRadius).color = D.Card;
            HmdContent = NewVertical(surface, "Content", 22, new RectOffset(62, 62, 42, 42));
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
            Round(hintPanel.gameObject.AddComponent<Image>(), 60).color = StudyTheme.WithAlpha(StudyTheme.Hex("30302e"), 0.82f);
            _hintText = FitLine(CreateText(hintPanel, "", 36, TextAnchor.MiddleCenter, D.TextSecondary, false));
            var hintRect = (RectTransform)_hintText.transform;
            Stretch(hintRect, 12f);
            hintRect.offsetMin = new Vector2(56f, 12f); // keep the text off the round ends of the pill
            hintRect.offsetMax = new Vector2(-56f, -12f);
            HintCanvas.gameObject.SetActive(false);
        }

        private void OnDestroy()
        {
            foreach (UnityEngine.Object o in _owned)
            {
                if (o != null)
                {
                    Destroy(o);
                }
            }
            _owned.Clear();
            _sprites.Clear();
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

        /// <summary>Status bar text (rich text; composed by the controller).</summary>
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

        /// <summary>Headset hint strip; ** marks in the text are shown bold in the emphasis colour.</summary>
        public void ShowHint(string text)
        {
            _hintText.text = StudyMarkup.ToRich(text, StudyTheme.Dark.Emphasis);
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

        /// <summary>
        /// One option of a rating scale in the headset: the score in a round badge on the left and its wording
        /// left-aligned beside it, so the five options line up as a list. Every option has the same neutral style.
        /// </summary>
        public Button HmdScaleOption(Transform parent, string score, string label, Action onClick, float width, float height,
            int size, string name)
        {
            Button b = HmdButton(parent, label, onClick, width, height, size, ButtonKind.Choice, name);
            StudyTheme.Palette p = PaletteFor(parent);
            StudyButtonStyle style = b.GetComponent<StudyButtonStyle>();
            float d = Mathf.Round(height * 0.62f);
            float left = Mathf.Round(height * 0.22f);
            RectTransform badge = NewRect("Score", b.transform);
            badge.anchorMin = badge.anchorMax = new Vector2(0f, 0.5f);
            badge.pivot = new Vector2(0f, 0.5f);
            badge.sizeDelta = new Vector2(d, d);
            badge.anchoredPosition = new Vector2(left, 0f);
            Image disc = Round(badge.gameObject.AddComponent<Image>(), Mathf.FloorToInt(d * 0.5f));
            disc.color = p.ScoreBadge;
            disc.raycastTarget = false;
            Text n = CreateText(badge, score, size + 2, TextAnchor.MiddleCenter, style.LabelColor, true);
            Stretch((RectTransform)n.transform);
            style.Accent = n;
            Text t = style.Label;
            t.alignment = TextAnchor.MiddleLeft;
            var tr = (RectTransform)t.transform;
            tr.offsetMin = new Vector2(left + d + Mathf.Round(height * 0.26f), 6f);
            tr.offsetMax = new Vector2(-24f, -6f);
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

        /// <summary>Rich text for a Text under the given object: ** marks become bold in the surface's emphasis colour.</summary>
        public string Rich(string text, Transform context)
        {
            return StudyMarkup.ToRich(text, PaletteFor(context).Emphasis);
        }

        /// <summary>Centred bold title in the accent colour.</summary>
        public Text Title(Transform parent, string text, int size = 0)
        {
            StudyTheme.Palette p = PaletteFor(parent);
            Text t = NewText(parent, text, size > 0 ? size : p.TitleSize, TextAnchor.MiddleCenter, p.Title, true);
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

        /// <summary>
        /// Full-width block in the accent colour with one bold line: the instruction that must be seen first on a
        /// page that has other content (e.g. "remove the headset" above a questionnaire).
        /// </summary>
        public Text Banner(Transform parent, string text)
        {
            StudyTheme.Palette p = PaletteFor(parent);
            RectTransform box = NewRect("Banner", parent);
            Image img = Round(box.gameObject.AddComponent<Image>(), p.Radius + 2);
            img.color = p.Primary;
            img.raycastTarget = false;
            var v = box.gameObject.AddComponent<VerticalLayoutGroup>();
            v.padding = new RectOffset(28, 28, 16, 16);
            v.childAlignment = TextAnchor.MiddleCenter;
            v.childControlWidth = true;
            v.childControlHeight = true;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;
            return FitLine(CreateText(box, StudyMarkup.ToBold(text), p.TitleSize - 10, TextAnchor.MiddleCenter, p.OnPrimary, true));
        }

        /// <summary>Centred row for <see cref="Badge"/> pills.</summary>
        public RectTransform BadgeRow(Transform parent)
        {
            RectTransform row = NewRow(parent, 12, TextAnchor.MiddleCenter);
            row.name = "BadgeRow";
            return row;
        }

        /// <summary>Pill-shaped tag with a bold label (role of the page, QA mode, practice).</summary>
        public Text Badge(Transform row, string text, Color fill, Color textColor, int size = 0)
        {
            StudyTheme.Palette p = PaletteFor(row);
            int s = size > 0 ? size : p.NoteSize;
            int h = Mathf.RoundToInt(s * 1.32f) + Mathf.RoundToInt(s * 0.5f);
            RectTransform pill = NewRect("Badge", row);
            Image img = Round(pill.gameObject.AddComponent<Image>(), h / 2);
            img.color = fill;
            img.raycastTarget = false;
            var hl = pill.gameObject.AddComponent<HorizontalLayoutGroup>();
            int pad = Mathf.RoundToInt(s * 0.9f);
            hl.padding = new RectOffset(pad, pad, 0, 0);
            hl.childAlignment = TextAnchor.MiddleCenter;
            hl.childControlWidth = true;
            hl.childControlHeight = true;
            hl.childForceExpandWidth = false;
            hl.childForceExpandHeight = false;
            var le = pill.gameObject.AddComponent<LayoutElement>();
            le.minHeight = h;
            le.preferredHeight = h;
            Text t = CreateText(pill, StudyMarkup.ToBold(text), s, TextAnchor.MiddleCenter, textColor, true);
            // the pill is exactly as wide as its label: never wrap on a rounding difference
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            return t;
        }

        /// <summary>
        /// Header of a question whose text starts with an emphasised keyword ("**Sense of depth**: how strong ..."):
        /// the keyword as a large title, the question below it, and the prefix (practice, re-rating) as a tag above.
        /// A text without a leading keyword is shown as one title, as before.
        /// </summary>
        public void QuestionHeader(Transform parent, string prefix, string text)
        {
            StudyTheme.Palette p = PaletteFor(parent);
            if (!StudyMarkup.TrySplitLead(text, out string lead, out string rest))
            {
                Title(parent, (prefix ?? "") + text, p.TitleSize - 6);
                return;
            }
            string tag = StudyMarkup.Strip(prefix ?? "").Trim().TrimEnd(':', '：').Trim();
            if (tag.Length > 0)
            {
                Badge(BadgeRow(parent), tag, p.EmphasisTint, p.Emphasis, p.NoteSize + 2);
            }
            Title(parent, lead, p.NoticeSize);
            Body(parent, rest, TextAnchor.MiddleCenter);
        }

        public Text Body(Transform parent, string text, TextAnchor anchor = TextAnchor.UpperLeft, int size = 0)
        {
            StudyTheme.Palette p = PaletteFor(parent);
            Text t = NewText(parent, text, size > 0 ? size : p.BodySize, anchor, p.Text);
            t.lineSpacing = 1.2f;
            return t;
        }

        /// <summary>Bold body text: a question or a field label.</summary>
        public Text Strong(Transform parent, string text, TextAnchor anchor = TextAnchor.UpperLeft, int size = 0)
        {
            StudyTheme.Palette p = PaletteFor(parent);
            Text t = NewText(parent, text, size > 0 ? size : p.BodySize, anchor, p.Text, true);
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

        public Text Colored(Transform parent, string text, Color color, TextAnchor anchor = TextAnchor.UpperLeft, int size = 0,
            bool bold = false)
        {
            StudyTheme.Palette p = PaletteFor(parent);
            return NewText(parent, text, size > 0 ? size : p.BodySize - 2, anchor, color, bold);
        }

        public void Spacer(Transform parent, float height)
        {
            RectTransform r = NewRect("Spacer", parent);
            var le = r.gameObject.AddComponent<LayoutElement>();
            le.minHeight = height;
            le.preferredHeight = height;
        }

        /// <summary>Text with the table's ** emphasis shown bold in the emphasis colour of the surface.</summary>
        public Text NewText(Transform parent, string text, int size, TextAnchor anchor, Color color, bool bold = false)
        {
            return CreateText(parent, Rich(text, parent), size, anchor, color, bold);
        }

        /// <summary>Text shown as given (already rich text, or user data that must not be interpreted).</summary>
        private Text CreateText(Transform parent, string richText, int size, TextAnchor anchor, Color color, bool bold)
        {
            RectTransform r = NewRect("Text", parent);
            var t = r.gameObject.AddComponent<Text>();
            t.font = Font;
            t.fontSize = size;
            t.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
            t.color = color;
            t.alignment = anchor;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.supportRichText = true;
            t.raycastTarget = false;
            t.text = richText ?? "";
            return t;
        }

        // ------------------------------------------------------------------ shapes
        /// <summary>Nine-sliced rounded rectangle with the given corner radius in canvas units (cached).</summary>
        public Sprite RoundedSprite(int radius)
        {
            int r = Mathf.Max(1, radius);
            if (_sprites.TryGetValue(r, out Sprite s) && s != null)
            {
                return s;
            }
            // one transparent texel around the shape, so its edge is anti-aliased by the texture filter
            // (world-space panels have no other edge smoothing)
            int border = r * ShapeScale + 1;
            int size = border * 2 + 2;
            s = MakeSprite(StudyShapes.RoundedBox(size, 1f, r * ShapeScale, 1f, false), size, border, 100f * ShapeScale);
            _sprites[r] = s;
            return s;
        }

        /// <summary>Soft shadow of a rounded rectangle; the image must extend `blur` units beyond the rectangle.</summary>
        private Sprite ShadowSprite(int radius, int blur)
        {
            long key = 1000000L + radius * 1000L + blur;
            if (_sprites.TryGetValue(key, out Sprite s) && s != null)
            {
                return s;
            }
            int border = 2 * blur + radius;
            int size = border * 2 + 2;
            s = MakeSprite(StudyShapes.RoundedBox(size, blur, radius, 2f * blur, true), size, border, 100f);
            _sprites[key] = s;
            return s;
        }

        private Sprite TickSprite()
        {
            const long key = 2000000L;
            if (_sprites.TryGetValue(key, out Sprite s) && s != null)
            {
                return s;
            }
            const int size = 48;
            s = MakeSprite(StudyShapes.CheckMark(size), size, 0, 100f);
            _sprites[key] = s;
            return s;
        }

        private Sprite MakeSprite(byte[] coverage, int size, int border, float pixelsPerUnit)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                name = "StudyUiShape",
            };
            var px = new Color32[coverage.Length];
            for (int i = 0; i < px.Length; i++)
            {
                px[i] = new Color32(255, 255, 255, coverage[i]);
            }
            tex.SetPixels32(px);
            tex.Apply(false, false);
            Sprite s = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), pixelsPerUnit, 0,
                SpriteMeshType.FullRect, new Vector4(border, border, border, border));
            s.name = "StudyUiShape";
            _owned.Add(tex);
            _owned.Add(s);
            return s;
        }

        /// <summary>Give an image rounded corners (radius in canvas units, at most half the shorter side).</summary>
        public Image Round(Image img, int radius)
        {
            img.sprite = RoundedSprite(radius);
            img.type = Image.Type.Sliced;
            return img;
        }

        /// <summary>Rounded background for a row or block (the object must not have a graphic yet).</summary>
        public Image Fill(RectTransform r, Color color, int radius = -1)
        {
            Image img = Round(r.gameObject.AddComponent<Image>(), radius >= 0 ? radius : PaletteFor(r).Radius);
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        /// <summary>
        /// Centred rounded card with a hairline border and a soft shadow. The frame is the returned object (move,
        /// show or destroy it); content goes into <paramref name="card"/>.
        /// </summary>
        public RectTransform NewCardFrame(Transform parent, string name, Vector2 size, StudyTheme.Palette p, out RectTransform card)
        {
            RectTransform frame = NewRect(name, parent);
            frame.anchorMin = frame.anchorMax = new Vector2(0.5f, 0.5f);
            frame.sizeDelta = size + new Vector2(2f, 2f);
            RectTransform shadow = NewRect("Shadow", frame);
            Stretch(shadow);
            shadow.offsetMin = new Vector2(-ShadowBlur, -ShadowBlur - ShadowDrop);
            shadow.offsetMax = new Vector2(ShadowBlur, ShadowBlur - ShadowDrop);
            var si = shadow.gameObject.AddComponent<Image>();
            si.sprite = ShadowSprite(p.PanelRadius + 1, ShadowBlur);
            si.type = Image.Type.Sliced;
            si.color = p.Shadow;
            si.raycastTarget = false;
            RectTransform border = NewRect("Border", frame);
            Stretch(border);
            Round(border.gameObject.AddComponent<Image>(), p.PanelRadius + 1).color = p.CardBorder;
            card = NewRect("Card", frame);
            Stretch(card, 1f);
            Round(card.gameObject.AddComponent<Image>(), p.PanelRadius).color = p.Card;
            return frame;
        }

        // ------------------------------------------------------------------ controls
        public Button NewButton(Transform parent, string label, int size, Action onClick, float width, float height,
            ButtonKind kind = ButtonKind.Secondary, string name = null, StudyTheme.Palette palette = null, int radius = -1)
        {
            StudyTheme.Palette p = palette ?? PaletteFor(parent);
            Color normal, hover, pressed, on;
            bool bold = false;
            switch (kind)
            {
                case ButtonKind.Primary:
                    normal = p.Primary; hover = p.PrimaryHover; pressed = p.PrimaryPressed; on = p.OnPrimary; bold = true;
                    break;
                case ButtonKind.Danger:
                    normal = p.Danger; hover = p.DangerHover; pressed = p.DangerPressed; on = Color.white; bold = true;
                    break;
                case ButtonKind.Choice:
                    normal = p.Choice; hover = p.ChoiceHover; pressed = p.ChoicePressed; on = p.OnChoice;
                    break;
                default:
                    normal = p.Secondary; hover = p.SecondaryHover; pressed = p.SecondaryPressed; on = p.OnSecondary;
                    break;
            }
            RectTransform r = NewRect(string.IsNullOrEmpty(name) ? "Button" : name, parent);
            var img = r.gameObject.AddComponent<Image>();
            img.color = Color.white;
            Round(img, Mathf.Min(radius >= 0 ? radius : p.Radius, Mathf.FloorToInt(height * 0.5f)));
            var b = r.gameObject.AddComponent<Button>();
            ColorBlock cb = b.colors;
            cb.normalColor = normal;
            cb.highlightedColor = hover;
            cb.selectedColor = normal;
            cb.pressedColor = pressed;
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
            // button labels: ** marks are bold only, the colour follows the button state
            Text t = FitLine(CreateText(r, StudyMarkup.ToBold(label), size, TextAnchor.MiddleCenter, on, bold));
            Stretch((RectTransform)t.transform, 6f);
            var style = r.gameObject.AddComponent<StudyButtonStyle>();
            style.Button = b;
            style.Label = t;
            style.Background = normal;
            style.Hover = hover;
            style.Pressed = pressed;
            style.DisabledBackground = p.Disabled;
            style.SelectedBackground = p.Selected;
            style.LabelColor = on;
            style.DisabledLabel = p.OnDisabled;
            style.SelectedLabel = p.OnSelected;
            style.Bold = bold;
            style.BoldWhenSelected = true;
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

        /// <summary>Check box (rounded; accent fill with a white tick when on) and its label.</summary>
        public Toggle NewToggle(Transform parent, string label, int size, Action<bool> onChanged, string name = null)
        {
            StudyTheme.Palette p = PaletteFor(parent);
            RectTransform row = NewRow(parent, 16, TextAnchor.MiddleLeft, 52);
            row.name = string.IsNullOrEmpty(name) ? "ToggleRow" : name;
            RectTransform box = NewRect("Box", row);
            Image boxImg = Round(box.gameObject.AddComponent<Image>(), 8);
            boxImg.color = p.CheckBox;
            var le = box.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = 32;
            le.preferredHeight = 32;
            le.minWidth = 32;
            RectTransform face = NewRect("Face", box);
            Stretch(face, 2f);
            Image faceImg = Round(face.gameObject.AddComponent<Image>(), 6);
            faceImg.color = p.InputBackground;
            faceImg.raycastTarget = false;
            RectTransform on = NewRect("On", box);
            Stretch(on);
            Image onImg = Round(on.gameObject.AddComponent<Image>(), 8);
            onImg.color = p.Primary;
            onImg.raycastTarget = false;
            RectTransform tick = NewRect("Tick", on);
            Stretch(tick, 3f);
            var tickImg = tick.gameObject.AddComponent<Image>();
            tickImg.sprite = TickSprite();
            tickImg.color = p.OnPrimary;
            tickImg.raycastTarget = false;
            on.gameObject.SetActive(false);
            var toggle = box.gameObject.AddComponent<Toggle>();
            toggle.targetGraphic = boxImg;
            ColorBlock cb = toggle.colors;
            cb.normalColor = Color.white;
            cb.highlightedColor = new Color(0.86f, 0.86f, 0.86f, 1f);
            cb.selectedColor = Color.white;
            toggle.colors = cb;
            toggle.isOn = false;
            // the "on" face is shown and hidden here (Toggle.graphic would fade only one graphic, not the tick on it)
            toggle.onValueChanged.AddListener(value =>
            {
                on.gameObject.SetActive(value);
                onChanged?.Invoke(value);
            });
            Text t = NewText(row, label, size, TextAnchor.MiddleLeft, p.Text);
            t.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
            return toggle;
        }

        public InputField NewInput(Transform parent, string initial, int size, float width, float height, bool multiline = false, string name = null)
        {
            StudyTheme.Palette p = PaletteFor(parent);
            int radius = Mathf.Min(p.Radius - 2, Mathf.FloorToInt(height * 0.5f));
            RectTransform outer = NewRect(string.IsNullOrEmpty(name) ? "Input" : name, parent);
            Round(outer.gameObject.AddComponent<Image>(), radius).color = p.InputBorder;
            var le = outer.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = width;
            le.preferredHeight = height;
            le.minHeight = height;
            RectTransform r = NewRect("Field", outer);
            Stretch(r, 1f);
            Image img = Round(r.gameObject.AddComponent<Image>(), Mathf.Max(1, radius - 1));
            img.color = p.InputBackground;
            // typed text is user data: no rich text, no emphasis marks
            Text t = CreateText(r, "", size, multiline ? TextAnchor.UpperLeft : TextAnchor.MiddleLeft, p.InputText, false);
            t.supportRichText = false;
            var tr = (RectTransform)t.transform;
            Stretch(tr);
            tr.offsetMin = new Vector2(14f, 8f);
            tr.offsetMax = new Vector2(-14f, -8f);
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

        /// <summary>
        /// Item name in bold with its explanation in small muted text below, then the options. All options look
        /// the same until one is picked (accent fill, bold label).
        /// </summary>
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
                NewText(labelBlock, label, labelSize, TextAnchor.MiddleLeft, p.Text, true);
                if (!string.IsNullOrEmpty(note))
                {
                    FitLine(NewText(labelBlock, note, Math.Max(12, labelSize - 5), TextAnchor.UpperLeft, p.TextMuted));
                }
            }
            var cr = new ChoiceRow();
            for (int i = 0; i < options.Count; i++)
            {
                int idx = i;
                Button b = NewButton(row, options[i], optionSize, () => cr.Select(idx), optionWidth, height, ButtonKind.Choice, "Choice_" + i,
                    null, 10);
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
