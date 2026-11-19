using System;
using System.Text;
using UnityEngine;

namespace Pano2StereoVR.Study
{
    /// <summary>
    /// Emphasis markup of the text table (user 11-19: bold what should be bold, stress what should be stressed).
    /// Words between two <c>**</c> markers are the ones a reader must not miss ("**head still**"); the UI shows
    /// them bold, and in running text also in the emphasis colour of the surface (ink blue on the PC monitor,
    /// amber in the headset). Button labels get bold only, so that the label colour can still follow the
    /// button state. A marker without a partner is shown as typed.
    /// A text that starts with an emphasised span ("**Sense of depth**: how strong ...") can be split into a
    /// headline and the rest (<see cref="TrySplitLead"/>); the rating scale uses this.
    /// Pure C#: no engine calls, so it runs in the EditMode tests outside the editor.
    /// </summary>
    public static class StudyMarkup
    {
        public const string Marker = "**";

        /// <summary>Wrap a text in emphasis markers.</summary>
        public static string Em(string text)
        {
            return string.IsNullOrEmpty(text) ? "" : Marker + text + Marker;
        }

        /// <summary>uGUI rich text: emphasised spans become bold, and coloured when a colour is given.</summary>
        public static string ToRich(string text, Color? emphasis)
        {
            return ToRich(text, emphasis.HasValue ? Hex(emphasis.Value) : null);
        }

        /// <summary>uGUI rich text; colorHex is "#rrggbb", or null for bold only.</summary>
        public static string ToRich(string text, string colorHex)
        {
            if (string.IsNullOrEmpty(text) || text.IndexOf(Marker, StringComparison.Ordinal) < 0)
            {
                return text ?? "";
            }
            string open = string.IsNullOrEmpty(colorHex) ? "<b>" : "<b><color=" + colorHex + ">";
            string close = string.IsNullOrEmpty(colorHex) ? "</b>" : "</color></b>";
            return Replace(text, open, close);
        }

        /// <summary>uGUI rich text with emphasised spans bold only (button labels: the colour follows the state).</summary>
        public static string ToBold(string text)
        {
            return ToRich(text, (string)null);
        }

        /// <summary>The text without emphasis markers (logs, comparisons).</summary>
        public static string Strip(string text)
        {
            if (string.IsNullOrEmpty(text) || text.IndexOf(Marker, StringComparison.Ordinal) < 0)
            {
                return text ?? "";
            }
            return Replace(text, "", "");
        }

        /// <summary>Number of emphasised spans; -1 when a marker has no partner or a span is empty.</summary>
        public static int CountSpans(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return 0;
            }
            int n = 0;
            int i = 0;
            while (true)
            {
                int a = text.IndexOf(Marker, i, StringComparison.Ordinal);
                if (a < 0)
                {
                    return n;
                }
                int b = text.IndexOf(Marker, a + Marker.Length, StringComparison.Ordinal);
                if (b < 0 || b == a + Marker.Length)
                {
                    return -1;
                }
                n++;
                i = b + Marker.Length;
            }
        }

        /// <summary>
        /// Split "**lead**: rest" into the lead and the rest (separating colon and spaces removed, first letter of
        /// the rest capitalised). False when the text does not start with an emphasised span or nothing follows it.
        /// </summary>
        public static bool TrySplitLead(string text, out string lead, out string rest)
        {
            lead = "";
            rest = "";
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }
            string s = text.TrimStart();
            if (!s.StartsWith(Marker, StringComparison.Ordinal))
            {
                return false;
            }
            int b = s.IndexOf(Marker, Marker.Length, StringComparison.Ordinal);
            if (b <= Marker.Length)
            {
                return false;
            }
            string head = s.Substring(Marker.Length, b - Marker.Length).Trim();
            string tail = s.Substring(b + Marker.Length).TrimStart(' ', '　', ':', '：').TrimEnd();
            if (head.Length == 0 || tail.Length == 0)
            {
                return false;
            }
            lead = head;
            rest = char.IsLower(tail[0]) ? char.ToUpperInvariant(tail[0]) + tail.Substring(1) : tail;
            return true;
        }

        /// <summary>"#rrggbb" of a colour (alpha ignored), without ColorUtility.</summary>
        public static string Hex(Color c)
        {
            return "#" + Channel(c.r) + Channel(c.g) + Channel(c.b);
        }

        private static string Channel(float v)
        {
            int i = (int)Math.Round(Math.Max(0.0, Math.Min(1.0, v)) * 255.0);
            return i.ToString("x2", System.Globalization.CultureInfo.InvariantCulture);
        }

        private static string Replace(string text, string open, string close)
        {
            var sb = new StringBuilder(text.Length + 32);
            int i = 0;
            while (i < text.Length)
            {
                int a = text.IndexOf(Marker, i, StringComparison.Ordinal);
                int b = a < 0 ? -1 : text.IndexOf(Marker, a + Marker.Length, StringComparison.Ordinal);
                if (a < 0 || b < 0)
                {
                    sb.Append(text, i, text.Length - i); // no (further) complete span: keep as typed
                    break;
                }
                sb.Append(text, i, a - i);
                sb.Append(open);
                sb.Append(text, a + Marker.Length, b - a - Marker.Length);
                sb.Append(close);
                i = b + Marker.Length;
            }
            return sb.ToString();
        }
    }
}
