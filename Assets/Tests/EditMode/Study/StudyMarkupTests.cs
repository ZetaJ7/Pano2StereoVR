using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace Pano2StereoVR.Study.Tests
{
    /// <summary>
    /// Emphasis marks of the text table (visual pass, 11-19) and the generated UI shapes.
    /// </summary>
    public sealed class StudyMarkupTests
    {
        private static void LoadTable()
        {
            string path = Path.Combine(TestPaths.ProjectRoot(), "Assets", "Scripts", "Study", "Resources", "StudyTexts.json");
            StudyTexts.Clear();
            StudyTexts.LoadJson(File.ReadAllText(path));
        }

        [Test]
        public void EmphasisBecomesBoldAndColoured()
        {
            Assert.AreEqual("只<b><color=#f2c96b>转动眼睛</color></b>看", StudyMarkup.ToRich("只**转动眼睛**看", "#f2c96b"));
            Assert.AreEqual("a <b>b</b> c <b>d</b>", StudyMarkup.ToBold("a **b** c **d**"));
            Assert.AreEqual("<b><color=#1b365d>x</color></b>", StudyMarkup.ToRich("**x**", new Color(27f / 255f, 54f / 255f, 93f / 255f, 1f)));
            Assert.AreEqual("plain", StudyMarkup.ToRich("plain", "#ffffff"));
            Assert.AreEqual("", StudyMarkup.ToRich(null, "#ffffff"));
            // rich text that is already there is kept
            Assert.AreEqual("<size=44><b>+3</b></size>\nx", StudyMarkup.ToBold("<size=44><b>+3</b></size>\nx"));
        }

        [Test]
        public void MarkerWithoutPartnerIsShownAsTyped()
        {
            Assert.AreEqual("a ** b", StudyMarkup.ToBold("a ** b"));
            Assert.AreEqual("<b>a</b> ** b", StudyMarkup.ToBold("**a** ** b"));
            Assert.AreEqual(-1, StudyMarkup.CountSpans("a ** b"));
            Assert.AreEqual(-1, StudyMarkup.CountSpans("****"));
            Assert.AreEqual(2, StudyMarkup.CountSpans("**a** and **b**"));
            Assert.AreEqual(0, StudyMarkup.CountSpans("no marks"));
        }

        [Test]
        public void StripAndEmAreInverse()
        {
            Assert.AreEqual("前一段", StudyMarkup.Strip(StudyMarkup.Em("前一段")));
            Assert.AreEqual("", StudyMarkup.Em(""));
            Assert.AreEqual("按扳机键继续", StudyMarkup.Strip("按**扳机键**继续"));
        }

        [Test]
        public void LeadingKeywordSplitsIntoHeadlineAndQuestion()
        {
            Assert.IsTrue(StudyMarkup.TrySplitLead("**立体感**：画面的立体感有多强？", out string lead, out string rest));
            Assert.AreEqual("立体感", lead);
            Assert.AreEqual("画面的立体感有多强？", rest);
            Assert.IsTrue(StudyMarkup.TrySplitLead("**Sense of depth**: how strong was it?", out lead, out rest));
            Assert.AreEqual("Sense of depth", lead);
            Assert.AreEqual("How strong was it?", rest);
            Assert.IsFalse(StudyMarkup.TrySplitLead("立体感：画面的立体感有多强？", out lead, out rest));
            Assert.IsFalse(StudyMarkup.TrySplitLead("**立体感**", out lead, out rest));
            Assert.IsFalse(StudyMarkup.TrySplitLead("请看**黄色十字**", out lead, out rest));
        }

        [Test]
        public void ColourHexIsLowerCaseRgb()
        {
            Assert.AreEqual("#1b365d", StudyMarkup.Hex(StudyTheme.Hex("1B365D")));
            Assert.AreEqual("#ffffff", StudyMarkup.Hex(new Color(1f, 1f, 1f, 0.2f)));
            Assert.AreEqual("#f2c96b", StudyTheme.RichHex(StudyTheme.Dark.Emphasis));
            Assert.AreEqual("<b><color=#9b3426>x</color></b>", StudyTheme.Tint("x", StudyTheme.Light.Error, true));
        }

        [Test]
        public void EveryTableEntryHasBalancedEmphasisInBothLanguages()
        {
            LoadTable();
            int marked = 0;
            foreach (string k in StudyTexts.Keys)
            {
                int zh = StudyMarkup.CountSpans(StudyTexts.Zh(k));
                int en = StudyMarkup.CountSpans(StudyTexts.En(k));
                Assert.GreaterOrEqual(zh, 0, "unbalanced or empty ** in the Chinese text of " + k);
                Assert.GreaterOrEqual(en, 0, "unbalanced or empty ** in the English text of " + k);
                Assert.AreEqual(zh > 0, en > 0, "emphasis in only one language for " + k);
                if (zh > 0)
                {
                    marked++;
                }
            }
            Assert.Greater(marked, 10, "the text table has lost its emphasis marks");
        }

        [Test]
        public void ScaleQuestionsStartWithTheirKeyword()
        {
            LoadTable();
            foreach (string k in new[] { "scale_depth", "scale_comfort", "scale_artifact" })
            {
                Assert.IsTrue(StudyMarkup.TrySplitLead(StudyTexts.Zh(k), out string leadZh, out string restZh), k + " zh");
                Assert.IsTrue(StudyMarkup.TrySplitLead(StudyTexts.En(k), out string leadEn, out string restEn), k + " en");
                Assert.AreEqual(StudyTexts.Zh(k + "_short"), leadZh, k + ": the headline is the short name used in the summary");
                Assert.IsNotEmpty(restZh);
                Assert.IsNotEmpty(leadEn);
                Assert.IsNotEmpty(restEn);
            }
        }

        [Test]
        public void ButtonAndTitleTextsCarryNoColourEmphasis()
        {
            // labels of answer options must stay neutral: no emphasis marks in option texts
            LoadTable();
            string[] prefixes = { "ssq_level_", "depth_", "comfort_", "artifact_", "pref_", "shape_" };
            foreach (string k in StudyTexts.Keys)
            {
                foreach (string p in prefixes)
                {
                    if (k.StartsWith(p))
                    {
                        Assert.AreEqual(0, StudyMarkup.CountSpans(StudyTexts.Zh(k)), "answer option " + k + " is emphasised");
                        Assert.AreEqual(0, StudyMarkup.CountSpans(StudyTexts.En(k)), "answer option " + k + " is emphasised");
                    }
                }
            }
        }

        [Test]
        public void RoundedBoxIsOpaqueInsideAndClearAtTheCorners()
        {
            const int size = 52; // radius 12 at 2 texels per unit: border 25, 2 texels of centre
            byte[] a = StudyShapes.RoundedBox(size, 1f, 24f, 1f, false);
            Assert.AreEqual(size * size, a.Length);
            Assert.AreEqual(255, a[(size / 2) * size + size / 2], "centre");
            Assert.AreEqual(0, a[0], "corner texel");
            Assert.AreEqual(0, a[size - 1], "corner texel");
            Assert.AreEqual(0, a[(size / 2) * size], "one clear texel at the straight edge");
            Assert.AreEqual(255, a[(size / 2) * size + 2], "opaque just inside the straight edge");
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    Assert.AreEqual(a[y * size + x], a[y * size + (size - 1 - x)], "left-right symmetry");
                    Assert.AreEqual(a[y * size + x], a[(size - 1 - y) * size + x], "top-bottom symmetry");
                }
            }
        }

        [Test]
        public void ShadowFadesFromTheBoxEdgeToTheTextureEdge()
        {
            const int blur = 28;
            const int radius = 21;
            int border = 2 * blur + radius;
            int size = border * 2 + 2;
            byte[] a = StudyShapes.RoundedBox(size, blur, radius, 2f * blur, true);
            int mid = size / 2;
            Assert.AreEqual(255, a[mid * size + mid], "full under the card");
            Assert.LessOrEqual(a[mid * size], 2, "gone at the texture edge");
            int atEdge = a[mid * size + blur];
            Assert.That(atEdge, Is.InRange(110, 145), "about half at the edge of the card");
            for (int x = 1; x <= mid; x++)
            {
                Assert.GreaterOrEqual(a[mid * size + x], a[mid * size + x - 1], "monotonic towards the card");
            }
        }

        [Test]
        public void TickHasInkAndClearCorners()
        {
            const int size = 48;
            byte[] a = StudyShapes.CheckMark(size);
            int ink = 0;
            foreach (byte b in a)
            {
                if (b > 128)
                {
                    ink++;
                }
            }
            Assert.That(ink, Is.InRange(size * size / 20, size * size / 3), "tick coverage");
            Assert.AreEqual(0, a[0]);
            Assert.AreEqual(0, a[size * size - 1]);
        }

        [Test]
        public void EmphasisColoursDifferFromTheTextTheySitIn()
        {
            foreach (StudyTheme.Palette p in new[] { StudyTheme.Light, StudyTheme.Dark })
            {
                Assert.AreNotEqual(StudyMarkup.Hex(p.Text), StudyMarkup.Hex(p.Emphasis), p.Name + " emphasis equals the text colour");
                Assert.Greater(p.Radius, 0);
                Assert.Greater(p.PanelRadius, p.Radius);
            }
        }
    }
}
