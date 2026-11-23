using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace Pano2StereoVR.Study.Tests
{
    /// <summary>Continuing an interrupted formal session (StudyResume, user 2026-11-23).</summary>
    public sealed class StudyResumeTests
    {
        private static readonly string[] FlowRaw =
        {
            "checkin", "ssq:S_pre", "hmd_fit", "stereotest", "part:1", "ssq:S_mid1", "break", "ssq:S_mid2", "hmd_fit",
            "eyerange", "gazetraining", "part:2", "ssq:S_post", "post_questionnaire", "end",
        };

        private static List<FlowStep> Flow() => FlowRaw.Select(FlowStep.Parse).ToList();
        private static List<string> Tokens() => Flow().Select(f => f.ToString()).ToList();

        private static KeyValuePair<string, string> E(string ev, string detail) => new KeyValuePair<string, string>(ev, detail);

        private static List<KeyValuePair<string, string>> EventsUpTo(int completedSteps, bool startNext)
        {
            List<string> t = Tokens();
            var ev = new List<KeyValuePair<string, string>> { E("session_start", "x") };
            for (int i = 0; i < completedSteps; i++)
            {
                ev.Add(E("step_start", t[i]));
                ev.Add(E("step_end", t[i]));
            }
            if (startNext)
            {
                ev.Add(E("step_start", t[completedSteps]));
            }
            return ev;
        }

        /// <summary>Run a test body in a fresh temporary folder that is removed afterwards (no SetUp/TearDown needed).</summary>
        private static void InTempDir(Action<string> body)
        {
            string dir = Path.Combine(Path.GetTempPath(), "study_resume_test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                body(dir);
            }
            finally
            {
                if (Directory.Exists(dir))
                {
                    Directory.Delete(dir, true);
                }
            }
        }

        [Test]
        public void InterruptedInPart1ResumesAtPart1()
        {
            // checkin, ssq:S_pre, hmd_fit, stereotest ended; part:1 started but did not end
            int idx = StudyResume.FirstIncompleteStep(Tokens(), EventsUpTo(4, true));
            Assert.AreEqual(4, idx);
            Assert.AreEqual("Part:1", Tokens()[idx]);
        }

        [Test]
        public void CompletedStepIsSkippedAndNextStepStarts()
        {
            // part:1 ended, nothing of ssq:S_mid1 recorded -> continue at ssq:S_mid1
            Assert.AreEqual(5, StudyResume.FirstIncompleteStep(Tokens(), EventsUpTo(5, false)));
        }

        [Test]
        public void RepeatedTokensAreMatchedInOrder()
        {
            // the second hmd_fit (index 8) must not be satisfied by the first one
            List<KeyValuePair<string, string>> ev = EventsUpTo(8, false);
            Assert.AreEqual(8, StudyResume.FirstIncompleteStep(Tokens(), ev));
            ev.Add(E("step_start", "HmdFit"));
            ev.Add(E("step_end", "HmdFit"));
            Assert.AreEqual(9, StudyResume.FirstIncompleteStep(Tokens(), ev));
        }

        [Test]
        public void ResumeEventSetsThePosition()
        {
            // first attempt stopped in part:1, the continued attempt logs resume_from 4 and completes part:1
            List<KeyValuePair<string, string>> ev = EventsUpTo(4, true);
            ev.Add(E("session_resume", "dir"));
            ev.Add(E(StudyResume.ResumeEvent, "4 Part:1"));
            ev.Add(E("resume_hmd_fit_start", ""));
            ev.Add(E("resume_hmd_fit_end", ""));
            ev.Add(E("step_start", "Part:1"));
            ev.Add(E("step_end", "Part:1"));
            Assert.AreEqual(5, StudyResume.FirstIncompleteStep(Tokens(), ev));
        }

        [Test]
        public void EveryStepEndedMeansComplete()
        {
            Assert.AreEqual(FlowRaw.Length, StudyResume.FirstIncompleteStep(Tokens(), EventsUpTo(FlowRaw.Length, false)));
        }

        [Test]
        public void ReadCsvHandlesQuotedFields()
        {
            InTempDir(_dir =>
            {
                string p = Path.Combine(_dir, "x.csv");
                File.WriteAllText(p, "a,b,c\n1,\"x, y\",\"he said \"\"hi\"\"\"\n2,\"line\nbreak\",z\n");
                List<string[]> rows = StudyResume.ReadCsv(p);
                Assert.AreEqual(3, rows.Count);
                Assert.AreEqual("x, y", rows[1][1]);
                Assert.AreEqual("he said \"hi\"", rows[1][2]);
                Assert.AreEqual("line\nbreak", rows[2][1]);
            });
        }

        [Test]
        public void ArchiveFromPart1MovesOnlyPart1AndLaterRows()
        {
            InTempDir(_dir =>
            {
                var log = new StudyDataLogger(_dir, "P99");
                log.StereoTest.Append("P99", 0, "main", "circle", 4, 700, 175, "xr", "circle", 1, 1.2, 0);
                log.Part1.Append(Enumerable.Repeat((object)"1", StudySchema.Part1.Length).ToArray());
                log.Part1.Append(Enumerable.Repeat((object)"2", StudySchema.Part1.Length).ToArray());
                log.WriteSsq("S_pre", Enumerable.Repeat(0, 16).ToList());
                log.WriteSsq("S_mid1", Enumerable.Repeat(1, 16).ToList());
                log.HeadPose.Append("P99", 1, 3, "a", 0.1, 2, 1.0, 0.0, 0.0, 0.0);
                log.Playback.Append("P99", 0, 0, "", "part1/x.mp4", 300, 300, 0, 40, 41);   // stereo test or other non-part rows stay
                log.Playback.Append("P99", 1, 3, "a", "part1/y.mp4", 300, 299, 1, 40, 45);

                string archive = StudyResume.ArchiveFromStep(_dir, Flow(), 4, new DateTime(2026, 11, 23, 7, 0, 0, DateTimeKind.Utc));
                Assert.AreEqual(Path.Combine(_dir, "superseded_20261123_070000"), archive);

                Assert.AreEqual(1, StudyResume.ReadCsv(Path.Combine(_dir, "part1.csv")).Count, "part1.csv keeps only its header");
                Assert.AreEqual(3, StudyResume.ReadCsv(Path.Combine(archive, "part1.csv")).Count, "header + 2 moved rows");
                Assert.AreEqual(2, StudyResume.ReadCsv(Path.Combine(_dir, "stereotest.csv")).Count, "stereo test (before part 1) kept");
                List<string[]> ssq = StudyResume.ReadCsv(Path.Combine(_dir, "ssq.csv"));
                Assert.AreEqual(2, ssq.Count);
                Assert.AreEqual("S_pre", ssq[1][1]);
                Assert.AreEqual("S_mid1", StudyResume.ReadCsv(Path.Combine(archive, "ssq.csv"))[1][1]);
                Assert.AreEqual(1, StudyResume.ReadCsv(Path.Combine(_dir, "headpose.csv")).Count);
                List<string[]> pb = StudyResume.ReadCsv(Path.Combine(_dir, "playback.csv"));
                Assert.AreEqual(2, pb.Count);
                Assert.AreEqual("0", pb[1][1]);
                Assert.AreEqual(string.Join(",", StudySchema.Part1), string.Join(",", StudyResume.ReadCsv(Path.Combine(_dir, "part1.csv"))[0]));

                // the logger appends after the header as before
                var again = new StudyDataLogger(_dir, "P99");
                again.Part1.Append(Enumerable.Repeat((object)"3", StudySchema.Part1.Length).ToArray());
                List<string[]> p1 = StudyResume.ReadCsv(Path.Combine(_dir, "part1.csv"));
                Assert.AreEqual(2, p1.Count);
                Assert.AreEqual("3", p1[1][0]);
            });
        }

        [Test]
        public void ArchiveWithNothingToMoveReturnsEmpty()
        {
            InTempDir(_dir =>
            {
                var log = new StudyDataLogger(_dir, "P99");
                log.WriteSsq("S_pre", Enumerable.Repeat(0, 16).ToList());
                Assert.AreEqual("", StudyResume.ArchiveFromStep(_dir, Flow(), 4, DateTime.UtcNow));
                Assert.AreEqual(2, StudyResume.ReadCsv(Path.Combine(_dir, "ssq.csv")).Count);
            });
        }

        [Test]
        public void FindUsesFormalFoldersOnly()
        {
            InTempDir(_dir =>
            {
                string data = Path.Combine(_dir, "StudyData");
                Directory.CreateDirectory(Path.Combine(data, "P07"));
                Directory.CreateDirectory(Path.Combine(data, "P07__20261123_060000"));
                Directory.CreateDirectory(Path.Combine(data, "P070"));
                File.WriteAllText(Path.Combine(data, "P07", "session.json"), "{\"mode\": \"formal\", \"status\": \"in_progress\", \"start_utc\": \"2026-11-23T05:00:00.000Z\"}");
                File.WriteAllText(Path.Combine(data, "P07__20261123_060000", "session.json"), "{\"mode\": \"qa\", \"status\": \"complete\", \"start_utc\": \"2026-11-23T06:00:00.000Z\"}");
                File.WriteAllText(Path.Combine(data, "P070", "session.json"), "{\"mode\": \"formal\", \"status\": \"complete\", \"start_utc\": \"2026-11-23T07:00:00.000Z\"}");
                var ev = new System.Text.StringBuilder("t_utc,event,detail\n");
                foreach (KeyValuePair<string, string> e in EventsUpTo(4, true))
                {
                    ev.Append("2026-11-23T05:00:00.000Z,").Append(e.Key).Append(',').Append(e.Value).Append('\n');
                }
                File.WriteAllText(Path.Combine(data, "P07", "events.csv"), ev.ToString());

                List<string> dirs = StudyResume.FormalSessionDirs(data, "P07");
                Assert.AreEqual(1, dirs.Count);
                ResumeCandidate c = StudyResume.Find(data, "P07", Tokens());
                Assert.IsNotNull(c);
                Assert.AreEqual(Path.Combine(data, "P07"), c.Directory);
                Assert.AreEqual(4, c.StartIndex);
                Assert.AreEqual("Part:1", c.StartStep);
                Assert.IsFalse(c.Complete);
                Assert.IsNull(StudyResume.Find(data, "P08", Tokens()));
            });
        }

        [Test]
        public void RestartMovesTheFolderToReplaced()
        {
            InTempDir(_dir =>
            {
                string data = Path.Combine(_dir, "StudyData");
                string backup = Path.Combine(_dir, "StudyData_backup");
                string p = Path.Combine(data, "P07");
                Directory.CreateDirectory(Path.Combine(p, "superseded_x"));
                File.WriteAllText(Path.Combine(p, "session.json"), "{\"mode\": \"formal\"}");
                File.WriteAllText(Path.Combine(p, "superseded_x", "part1.csv"), "a\n1\n");
                DateTime t = new DateTime(2026, 11, 23, 8, 0, 0, DateTimeKind.Utc);
                string moved = StudyResume.MoveToReplaced(p, backup, data, t, "P07");
                Assert.AreEqual(Path.Combine(backup, "replaced", "P07__replaced_20261123_080000"), moved);
                Assert.IsFalse(Directory.Exists(p));
                Assert.IsTrue(File.Exists(Path.Combine(moved, "session.json")));
                Assert.IsTrue(File.Exists(Path.Combine(moved, "superseded_x", "part1.csv")));
                string note = File.ReadAllText(Path.Combine(moved, StudyResume.ReplacedNoteFile));
                StringAssert.Contains("P07", note);
                StringAssert.Contains(p, note);

                // once the new session folder exists, both folders say what happened
                string fresh = Path.Combine(data, "P07");
                Directory.CreateDirectory(fresh);
                StudyResume.WritePreviousMovedNote(fresh, new[] { new KeyValuePair<string, string>(p, moved) }, t);
                StudyResume.WriteReplacedNote(moved, p, "P07", t, fresh);
                StringAssert.Contains(moved, File.ReadAllText(Path.Combine(fresh, StudyResume.PreviousMovedNoteFile)));
                StringAssert.Contains("new_session_folder: " + fresh, File.ReadAllText(Path.Combine(moved, StudyResume.ReplacedNoteFile)));
            });
        }

        [Test]
        public void RestartWithoutBackupFolderStaysInsideTheDataFolderUnderAnUnderscoreName()
        {
            InTempDir(_dir =>
            {
                string data = Path.Combine(_dir, "StudyData");
                string p = Path.Combine(data, "P08");
                Directory.CreateDirectory(p);
                File.WriteAllText(Path.Combine(p, "session.json"), "{\"mode\": \"formal\"}");
                string moved = StudyResume.MoveToReplaced(p, "", data, new DateTime(2026, 11, 23, 9, 0, 0, DateTimeKind.Utc), "P08");
                Assert.AreEqual(Path.Combine(data, "_replaced", "P08__replaced_20261123_090000"), moved);
                Assert.IsTrue(File.Exists(Path.Combine(moved, "session.json")));
                Assert.AreEqual(0, StudyResume.FormalSessionDirs(data, "P08").Count, "moved folder no longer counts as this ID's data");
            });
        }

        [Test]
        public void ReplaceFileSwapsContent()
        {
            InTempDir(_dir =>
            {
                string a = Path.Combine(_dir, "a.csv");
                File.WriteAllText(a, "old\n");
                File.WriteAllText(a + ".tmp", "new\n");
                StudyResume.ReplaceFile(a + ".tmp", a);
                Assert.AreEqual("new\n", File.ReadAllText(a));
                Assert.IsFalse(File.Exists(a + ".tmp"));
            });
        }

        [Test]
        public void HeadsetStepsNeedTheFit()
        {
            Assert.IsTrue(StudyResume.NeedsHeadset(FlowStepKind.Part));
            Assert.IsTrue(StudyResume.NeedsHeadset(FlowStepKind.StereoTest));
            Assert.IsFalse(StudyResume.NeedsHeadset(FlowStepKind.Ssq));
            Assert.IsFalse(StudyResume.NeedsHeadset(FlowStepKind.Break));
        }
    }
}
