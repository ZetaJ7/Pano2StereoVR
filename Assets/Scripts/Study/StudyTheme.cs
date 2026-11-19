using UnityEngine;

namespace Pano2StereoVR.Study
{
    /// <summary>
    /// One visual theme for every study screen. Desktop screens use a light palette after the kami design
    /// tokens (parchment page, ivory card, near-black text, warm greys, one ink-blue accent). Headset panels
    /// use a dark variant of the same palette (dark surface, warm off-white text, light ink-blue titles).
    /// Hierarchy (user 11-19: bold what should be bold, stress what should be stressed):
    ///   - two weights: titles, questions, item names, primary buttons and selected answers are bold; running
    ///     text and notes are regular;
    ///   - words marked **like this** in the text table are bold in the emphasis colour (ink blue on the
    ///     monitor, amber in the headset), see <see cref="StudyMarkup"/>;
    ///   - rounded shapes (cards, buttons, pills) drawn from <see cref="StudyShapes"/>, a soft shadow under cards.
    /// Answer options always share one neutral style: no option of a questionnaire or scale is highlighted
    /// before the participant picks it.
    /// Font: legacy uGUI Text with an OS dynamic font (Microsoft YaHei, then Microsoft YaHei UI, SimHei,
    /// Arial); no font asset is bundled with the project.
    /// </summary>
    public static class StudyTheme
    {
        public static readonly string[] FontNames = { "Microsoft YaHei", "Microsoft YaHei UI", "SimHei", "Arial" };

        /// <summary>Colours and sizes of one surface (desktop or headset).</summary>
        public sealed class Palette
        {
            public string Name;
            public Color Page;
            public Color Card;
            public Color CardBorder;
            public Color Shadow;
            public Color Text;
            public Color TextSecondary;
            public Color TextMuted;
            public Color Title;
            /// <summary>Inline emphasis in running text (**words** of the text table).</summary>
            public Color Emphasis;
            /// <summary>Fill of a pill in the emphasis colour (practice tag).</summary>
            public Color EmphasisTint;
            public Color Success;
            public Color Primary;
            public Color PrimaryHover;
            public Color PrimaryPressed;
            public Color OnPrimary;
            public Color Secondary;
            public Color SecondaryHover;
            public Color SecondaryPressed;
            public Color OnSecondary;
            /// <summary>Answer options (questionnaire chips, scale rows).</summary>
            public Color Choice;
            public Color ChoiceHover;
            public Color ChoicePressed;
            public Color OnChoice;
            public Color Danger;
            public Color DangerHover;
            public Color DangerPressed;
            public Color Disabled;
            public Color OnDisabled;
            public Color Selected;
            public Color OnSelected;
            public Color InputBackground;
            public Color InputBorder;
            public Color InputText;
            public Color CheckBox;
            public Color Tag;
            public Color OnTag;
            public Color Warning;
            public Color WarningTint;
            public Color Error;
            /// <summary>Background of a list row inside a panel (group summary).</summary>
            public Color RowFill;
            /// <summary>Round badge behind the number of a scale option (drawn over the button fill).</summary>
            public Color ScoreBadge;
            public int TitleSize;
            public int NoticeSize;
            public int BodySize;
            public int NoteSize;
            public int ButtonSize;
            /// <summary>Corner radius of buttons and inputs, and of cards and panels (canvas units).</summary>
            public int Radius;
            public int PanelRadius;
        }

        /// <summary>Desktop (PC monitor): kami light tokens.</summary>
        public static readonly Palette Light = new Palette
        {
            Name = "light",
            Page = Hex("f5f4ed"),            // parchment
            Card = Hex("faf9f5"),            // ivory
            CardBorder = Hex("e8e6dc"),
            Shadow = WithAlpha(Hex("141413"), 0.16f),
            Text = Hex("141413"),
            TextSecondary = Hex("3d3d3a"),
            TextMuted = Hex("504e49"),
            Title = Hex("1B365D"),           // ink blue (single accent)
            Emphasis = Hex("1B365D"),
            EmphasisTint = Hex("E4ECF5"),
            Success = Hex("2f6b45"),
            Primary = Hex("1B365D"),
            PrimaryHover = Hex("2D5A8A"),    // ink light
            PrimaryPressed = Hex("142946"),
            OnPrimary = Color.white,
            Secondary = Hex("e8e6dc"),       // warm sand
            SecondaryHover = Hex("dcd9cc"),
            SecondaryPressed = Hex("cfcbbb"),
            OnSecondary = Hex("141413"),
            Choice = Hex("f0eee6"),          // slightly darker than the card
            ChoiceHover = Hex("e4e1d6"),
            ChoicePressed = Hex("d8d5c8"),
            OnChoice = Hex("3d3d3a"),
            Danger = Hex("9b3426"),
            DangerHover = Hex("b24433"),
            DangerPressed = Hex("7c281d"),
            Disabled = Hex("efede6"),
            OnDisabled = Hex("a8a59b"),
            Selected = Hex("1B365D"),
            OnSelected = Color.white,
            InputBackground = Color.white,
            InputBorder = Hex("d6d3c7"),
            InputText = Hex("141413"),
            CheckBox = Hex("c9c5b7"),
            Tag = Hex("E4ECF5"),             // kami default tag tint
            OnTag = Hex("1B365D"),
            Warning = Hex("8a5a12"),
            WarningTint = Hex("f5ecd7"),
            Error = Hex("9b3426"),
            RowFill = Hex("f0eee6"),
            ScoreBadge = WithAlpha(Hex("141413"), 0.08f),
            TitleSize = 46,
            NoticeSize = 54,
            BodySize = 28,
            NoteSize = 20,
            ButtonSize = 28,
            Radius = 12,
            PanelRadius = 20,
        };

        /// <summary>Headset panels: dark variant (dark surface card, warm off-white text, light ink-blue titles).</summary>
        public static readonly Palette Dark = new Palette
        {
            Name = "dark",
            Page = Hex("141413"),            // deep dark
            Card = WithAlpha(Hex("30302e"), 0.97f), // dark surface
            CardBorder = Hex("4a4945"),
            Shadow = WithAlpha(Color.black, 0.3f),
            Text = Hex("f5f4ed"),
            TextSecondary = Hex("c9c6bb"),
            TextMuted = Hex("a8a59b"),
            Title = Hex("a9c2e3"),           // light ink-blue tint
            Emphasis = Hex("f2c96b"),        // amber: stands out from the off-white text and the blue titles
            EmphasisTint = Hex("5a4a1e"),
            Success = Hex("7fd08a"),
            Primary = Hex("2D5A8A"),
            PrimaryHover = Hex("3a6ea5"),
            PrimaryPressed = Hex("1B365D"),
            OnPrimary = Color.white,
            Secondary = Hex("4b4a45"),
            SecondaryHover = Hex("66645d"),
            SecondaryPressed = Hex("2D5A8A"),
            OnSecondary = Hex("f5f4ed"),
            Choice = Hex("4b4a45"),
            ChoiceHover = Hex("66645d"),
            ChoicePressed = Hex("2D5A8A"),
            OnChoice = Hex("f5f4ed"),
            Danger = Hex("a8483a"),
            DangerHover = Hex("c05a4a"),
            DangerPressed = Hex("8a3a2e"),
            Disabled = Hex("383835"),
            OnDisabled = Hex("7c7a72"),
            Selected = Hex("2D5A8A"),
            OnSelected = Color.white,
            InputBackground = Hex("f5f4ed"),
            InputBorder = Hex("5a5852"),
            InputText = Hex("141413"),
            CheckBox = Hex("5a5852"),
            Tag = Hex("3a4a5e"),
            OnTag = Hex("a9c2e3"),
            Warning = Hex("e0b25c"),
            WarningTint = Hex("5a4a1e"),
            Error = Hex("e08a7a"),
            RowFill = Hex("3a3a37"),
            ScoreBadge = WithAlpha(Color.white, 0.10f),
            // headset canvas: 1 unit = 1 mm at about 2 m, so 40 units span about 1.15 deg (em height)
            TitleSize = 54,
            NoticeSize = 64,
            BodySize = 40,
            NoteSize = 30,
            ButtonSize = 38,
            Radius = 22,
            PanelRadius = 36,
        };

        /// <summary>Experimenter status bar on the PC monitor (dark strip above the light page).</summary>
        public static readonly Color StatusBar = WithAlpha(Hex("141413"), 0.94f);
        public static readonly Color StatusText = Hex("b0aea5");
        public static readonly Color StatusStrong = Hex("f5f4ed");
        public static readonly Color StatusDim = Hex("6b6a64");
        public static readonly Color StatusAlert = Hex("e08a7a");
        public static readonly Color StatusQa = Hex("f2c96b");

        /// <summary>Gaze-training dot colours (no yellow: the centre cross is yellow).</summary>
        public static readonly Color[] GazeDotColors =
        {
            Hex("e5483a"), // red
            Hex("f08a24"), // orange
            Hex("3fb04f"), // green
            Hex("2cc6d0"), // cyan
            Hex("3e6ff0"), // blue
            Hex("9a5ce6"), // purple
            Hex("f27cbf"), // pink
            Hex("f2f2f2"), // white
        };

        public static readonly Color CueYellow = new Color(1f, 0.85f, 0.1f, 1f);
        /// <summary>Orientation cross while the head is within the alignment tolerance.</summary>
        public static readonly Color CueAligned = new Color(0.25f, 0.85f, 0.35f, 1f);
        public static readonly Color RingDim = new Color(0.35f, 0.35f, 0.33f, 1f);
        public static readonly Color RingLit = new Color(0.95f, 0.95f, 0.92f, 1f);

        // Pure C# parsing: these colours are static readonly fields, and ColorUtility is an engine call that must
        // not run during a MonoBehaviour's construction (same rule as RectOffset).
        public static Color Hex(string rgb)
        {
            string h = rgb.TrimStart('#');
            if (h.Length != 6) return new Color(1f, 0f, 1f, 1f);
            int v;
            if (!int.TryParse(h, System.Globalization.NumberStyles.HexNumber,
                    System.Globalization.CultureInfo.InvariantCulture, out v)) return new Color(1f, 0f, 1f, 1f);
            return new Color(((v >> 16) & 0xFF) / 255f, ((v >> 8) & 0xFF) / 255f, (v & 0xFF) / 255f, 1f);
        }

        public static Color WithAlpha(Color c, float a)
        {
            c.a = a;
            return c;
        }

        /// <summary>"#rrggbb" for a rich-text colour tag.</summary>
        public static string RichHex(Color c)
        {
            return StudyMarkup.Hex(c);
        }

        /// <summary>Rich text of one colour, optionally bold.</summary>
        public static string Tint(string text, Color c, bool bold = false)
        {
            string s = "<color=" + StudyMarkup.Hex(c) + ">" + text + "</color>";
            return bold ? "<b>" + s + "</b>" : s;
        }
    }
}
