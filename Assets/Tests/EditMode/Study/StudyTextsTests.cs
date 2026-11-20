using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace Pano2StereoVR.Study.Tests
{
    public sealed class StudyTextsTests
    {
        private static readonly string[] AllowedEmpty = { "dummy_label" };

        private static void LoadTable()
        {
            string path = Path.Combine(TestPaths.ProjectRoot(), "Assets", "Scripts", "Study", "Resources", "StudyTexts.json");
            StudyTexts.Clear();
            StudyTexts.LoadJson(File.ReadAllText(path));
        }

        /// <summary>Keys passed as literals to the text helpers in the session code.</summary>
        private static IEnumerable<string> KeysUsedInCode()
        {
            string dir = Path.Combine(TestPaths.ProjectRoot(), "Assets", "Scripts", "Study");
            var keys = new HashSet<string>();
            var patterns = new[]
            {
                new Regex(@"\bT\(""([A-Za-z0-9_]+)""\)"),
                new Regex(@"\bTF\(""([A-Za-z0-9_]+)"""),
                new Regex(@"\bShowHmdPanel\(""([A-Za-z0-9_]+)"",\s*""([A-Za-z0-9_]+)"",\s*""([A-Za-z0-9_]+)"""),
                new Regex(@"\bShowHmdPanelText\([^;]*?,\s*""([A-Za-z0-9_]+)"",\s*\(\)"),
                new Regex(@"\bShowHmdNotice\(""([A-Za-z0-9_]+)"""),
                new Regex(@"\bShowHmdNotice\(""[A-Za-z0-9_]+"",\s*""([A-Za-z0-9_]+)"""),
                new Regex(@"\bAskTwo\(""([A-Za-z0-9_]+)"",\s*""([A-Za-z0-9_]+)"",\s*""([A-Za-z0-9_]+)"""),
                new Regex(@"\bAskYesNo\(""([A-Za-z0-9_]+)"""),
                new Regex(@"\bAskScale\([^,]+,\s*""([A-Za-z0-9_]+)"""),
                new Regex(@"StudyTexts\.(?:Zh|En|Get)\(""([A-Za-z0-9_]+)""\)"),
            };
            foreach (string file in Directory.GetFiles(dir, "StudyController*.cs"))
            {
                string src = File.ReadAllText(file);
                foreach (Regex re in patterns)
                {
                    foreach (Match m in re.Matches(src))
                    {
                        for (int g = 1; g < m.Groups.Count; g++)
                        {
                            keys.Add(m.Groups[g].Value);
                        }
                    }
                }
            }
            return keys;
        }

        [Test]
        public void EveryKeyUsedByTheSessionExists()
        {
            LoadTable();
            List<string> used = KeysUsedInCode().ToList();
            Assert.Greater(used.Count, 60, "key scan found too few keys; check the patterns");
            string[] dynamicKeys = StudySchema.SsqItems.Select(i => "ssq_" + i)
                .Concat(Enumerable.Range(0, 4).Select(i => "ssq_level_" + i))
                .Concat(new[] { "depth", "comfort", "artifact" }.SelectMany(p => Enumerable.Range(1, 5).Select(n => p + "_" + n)))
                .Concat(new[] { "pref_m3", "pref_m2", "pref_m1", "pref_0", "pref_p1", "pref_p2", "pref_p3" })
                .Concat(new[] { "circle", "square", "triangle", "cross" }.Select(s => "shape_" + s))
                .Concat(new[] { "advance_hint", "replay_hint", "segment_first", "segment_second", "remove_notice", "break_notice" })
                .ToArray();
            foreach (string k in used.Concat(dynamicKeys))
            {
                Assert.IsTrue(StudyTexts.Has(k), "missing text key " + k);
            }
        }

        [Test]
        public void EveryEntryHasBothLanguagesAndMatchingPlaceholders()
        {
            LoadTable();
            foreach (string k in StudyTexts.Keys)
            {
                if (AllowedEmpty.Contains(k))
                {
                    continue;
                }
                string zh = StudyTexts.Zh(k);
                string en = StudyTexts.En(k);
                Assert.IsNotEmpty(zh, "no Chinese text for " + k);
                Assert.IsNotEmpty(en, "no English text for " + k);
                var pz = Regex.Matches(zh, @"\{\d+\}").Cast<Match>().Select(m => m.Value).OrderBy(x => x).ToList();
                var pe = Regex.Matches(en, @"\{\d+\}").Cast<Match>().Select(m => m.Value).OrderBy(x => x).ToList();
                CollectionAssert.AreEqual(pz, pe, "placeholders differ for " + k);
            }
        }

        [Test]
        public void TextIsSingleLanguageForTheSelectedLanguage()
        {
            LoadTable();
            try
            {
                CheckSingleLanguage();
            }
            finally
            {
                StudyTexts.Language = StudyTexts.Chinese;
            }
        }

        private static void CheckSingleLanguage()
        {
            StudyTexts.Language = StudyTexts.Chinese;
            Assert.AreEqual("非常强", StudyTexts.Get("depth_5"));
            Assert.AreEqual("立体感", StudyTexts.Get("scale_depth_short"));
            Assert.AreEqual("前一段好得多", StudyTexts.Get("pref_m3"));
            Assert.AreEqual("后一段好得多", StudyTexts.Get("pref_p3"));
            Assert.AreEqual("晕动症状问卷（SSQ）", StudyTexts.Get("ssq_title"));
            Assert.AreEqual("看东西不清楚", StudyTexts.Get("ssq_BV"));
            Assert.AreEqual("感觉自己或周围在旋转", StudyTexts.Note("ssq_VE"));
            Assert.AreEqual("", StudyTexts.Note("ssq_BU"));
            Assert.AreEqual("第 2 / 11 组", StudyTexts.Format("group_title", 2, 11));
            StudyTexts.Language = StudyTexts.English;
            Assert.AreEqual("Very strong", StudyTexts.Get("depth_5"));
            Assert.AreEqual("First much better", StudyTexts.Get("pref_m3"));
            Assert.AreEqual("None", StudyTexts.Get("ssq_level_0"));
            Assert.AreEqual("Group 2 of 11", StudyTexts.Format("group_title", 2, 11));
            Assert.AreEqual("feeling that you or your surroundings are spinning", StudyTexts.Note("ssq_VE"));
            StudyTexts.Language = "fr";
            Assert.AreEqual(StudyTexts.Chinese, StudyTexts.Language);
            foreach (string k in StudyTexts.Keys)
            {
                Assert.IsFalse(StudyTexts.Get(k, StudyTexts.Chinese).Contains(" / ") && StudyTexts.Get(k, StudyTexts.Chinese).Contains(StudyTexts.En(k))
                               && StudyTexts.En(k).Length > 0 && StudyTexts.Zh(k) != StudyTexts.En(k),
                    "bilingual inline text left in " + k);
            }
        }

        [Test]
        public void EndAndCheckinTextsFollowTheWalkthroughDecisions()
        {
            LoadTable();
            StudyTexts.Language = StudyTexts.Chinese;
            Assert.AreEqual("纸质知情同意书与基本信息表已签署", StudyTexts.Get("checkin_paper_forms"));
            Assert.AreEqual("实验完成，谢谢参与", StudyTexts.Get("end_title"));
            Assert.AreEqual("请在登记本记录学号并签字，离开前带齐随身物品。", StudyTexts.Get("end_body"));
            Assert.AreEqual("请摘下头显休息，并在电脑屏幕上填写问卷", StudyTexts.Get("break_notice"));
            Assert.AreEqual("看不清", StudyTexts.Get("stereo_cant_tell"));
            Assert.AreEqual("Thank you for participation", StudyTexts.En("end_title")); // user 11-19
            foreach (string k in StudyTexts.Keys.Where(k => k.StartsWith("end_") || k == "aborted_title"))
            {
                Assert.IsFalse(StudyTexts.Zh(k).Contains("保存"), k + " mentions saving");
                Assert.IsFalse(StudyTexts.En(k).ToLowerInvariant().Contains("saved"), k + " mentions saving");
            }
        }

        [Test]
        public void EyeRangeTextsDoNotAskToKeepTheHeadStill()
        {
            // user 11-20: the measurement comes before the gaze practice; its texts ask for natural viewing only
            LoadTable();
            // the result page (after the measurement) may mention the head; the instructions before it may not
            string[] keys = { "eyerange_title", "eyerange_intro", "eyerange_cue" };
            foreach (string k in keys)
            {
                Assert.IsTrue(StudyTexts.Has(k), "missing " + k);
                string zh = StudyTexts.Zh(k);
                string en = StudyTexts.En(k).ToLowerInvariant();
                foreach (string w in new[] { "头部", "不动", "只转动", "转头" })
                {
                    Assert.IsFalse(zh.Contains(w), k + " (zh) mentions " + w);
                }
                foreach (string w in new[] { "head", "still", "only your eyes", "eyes only" })
                {
                    Assert.IsFalse(en.Contains(w), k + " (en) mentions " + w);
                }
            }
            StudyTexts.Language = StudyTexts.Chinese;
            Assert.AreEqual("自然注视范围测量", StudyTexts.Get("eyerange_title"));
            StringAssert.Contains("自然地用目光跟随圆点", StudyTexts.Get("eyerange_intro"));
            StringAssert.Contains("16", StudyTexts.Format("eyerange_intro", 16));
            Assert.AreEqual("不转头时，你的眼睛大约能看到 **23°** 的范围", StudyTexts.Format("eyerange_result_line", "23°"));
            Assert.AreEqual("下一步", StudyTexts.Get("next"));
            StudyTexts.Language = StudyTexts.English;
            Assert.AreEqual("Next", StudyTexts.Get("next"));
            StringAssert.Contains("≥40°", StudyTexts.Format("eyerange_result_line", "≥40°"));
            StudyTexts.Language = StudyTexts.Chinese;
        }
    }
}
