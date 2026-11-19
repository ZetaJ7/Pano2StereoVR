using UnityEngine;

namespace Pano2StereoVR.Study
{
    /// <summary>
    /// One visual theme for every study screen. Desktop screens use a light palette after the kami
    /// design tokens (parchment page, ivory card, near-black text, warm greys, a single ink-blue accent
    /// for titles and primary buttons, no bold, no decorative lines). Headset panels use a dark variant
    /// of the same palette (dark surface, warm off-white text, light ink-blue titles).
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
            public Color Text;
            public Color TextSecondary;
            public Color TextMuted;
            public Color Title;
            public Color Primary;
            public Color PrimaryHover;
            public Color PrimaryPressed;
            public Color OnPrimary;
            public Color Secondary;
            public Color SecondaryHover;
            public Color SecondaryPressed;
            public Color OnSecondary;
            public Color Disabled;
            public Color OnDisabled;
            public Color Selected;
            public Color OnSelected;
            public Color InputBackground;
            public Color InputText;
            public Color CheckBox;
            public Color Warning;
            public Color Error;
            public int TitleSize;
            public int NoticeSize;
            public int BodySize;
            public int NoteSize;
            public int ButtonSize;
        }

        /// <summary>Desktop (PC monitor): kami light tokens.</summary>
        public static readonly Palette Light = new Palette
        {
            Name = "light",
            Page = Hex("f5f4ed"),            // parchment
            Card = Hex("faf9f5"),            // ivory
            CardBorder = Hex("e8e6dc"),
            Text = Hex("141413"),
            TextSecondary = Hex("3d3d3a"),
            TextMuted = Hex("504e49"),
            Title = Hex("1B365D"),           // ink blue (single accent)
            Primary = Hex("1B365D"),
            PrimaryHover = Hex("2D5A8A"),    // ink light
            PrimaryPressed = Hex("142946"),
            OnPrimary = Color.white,
            Secondary = Hex("e8e6dc"),       // warm sand
            SecondaryHover = Hex("dcd9cc"),
            SecondaryPressed = Hex("cfcbbb"),
            OnSecondary = Hex("141413"),
            Disabled = Hex("efede6"),
            OnDisabled = Hex("a8a59b"),
            Selected = Hex("1B365D"),
            OnSelected = Color.white,
            InputBackground = Color.white,
            InputText = Hex("141413"),
            CheckBox = Hex("d6d3c7"),
            Warning = Hex("8a5a12"),
            Error = Hex("9b3426"),
            TitleSize = 46,
            NoticeSize = 54,
            BodySize = 28,
            NoteSize = 20,
            ButtonSize = 28,
        };

        /// <summary>Headset panels: dark variant (dark surface card, warm off-white text, light ink-blue titles).</summary>
        public static readonly Palette Dark = new Palette
        {
            Name = "dark",
            Page = Hex("141413"),            // deep dark
            Card = WithAlpha(Hex("30302e"), 0.97f), // dark surface
            CardBorder = Hex("3d3d3a"),
            Text = Hex("f5f4ed"),
            TextSecondary = Hex("c9c6bb"),
            TextMuted = Hex("a8a59b"),
            Title = Hex("a9c2e3"),           // light ink-blue tint
            Primary = Hex("2D5A8A"),
            PrimaryHover = Hex("3a6ea5"),
            PrimaryPressed = Hex("1B365D"),
            OnPrimary = Color.white,
            Secondary = Hex("45443f"),
            SecondaryHover = Hex("5a5852"),
            SecondaryPressed = Hex("2D5A8A"),
            OnSecondary = Hex("f5f4ed"),
            Disabled = Hex("383835"),
            OnDisabled = Hex("7c7a72"),
            Selected = Hex("2D5A8A"),
            OnSelected = Color.white,
            InputBackground = Hex("f5f4ed"),
            InputText = Hex("141413"),
            CheckBox = Hex("5a5852"),
            Warning = Hex("e0b25c"),
            Error = Hex("e08a7a"),
            // headset canvas: 1 unit = 1 mm at about 2 m, so 40 units span about 1.15 deg (em height)
            TitleSize = 54,
            NoticeSize = 64,
            BodySize = 40,
            NoteSize = 30,
            ButtonSize = 38,
        };

        /// <summary>Experimenter status bar on the PC monitor (dark strip above the light page).</summary>
        public static readonly Color StatusBar = WithAlpha(Hex("141413"), 0.94f);
        public static readonly Color StatusText = Hex("b0aea5");
        public static readonly Color StatusAlert = Hex("e08a7a");

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

        public static string RichHex(Color c)
        {
            return "#" + ColorUtility.ToHtmlStringRGB(c);
        }
    }
}
