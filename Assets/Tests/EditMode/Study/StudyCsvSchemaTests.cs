using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace Pano2StereoVR.Study.Tests
{
    public sealed class StudyCsvSchemaTests
    {
        private static readonly Dictionary<string, string[]> Expected = new Dictionary<string, string[]>
        {
            { "stereotest.csv", new[] { "participant_id", "trial_index", "block", "shape", "disparity_px", "disparity_arcsec", "arcsec_per_px", "px_source", "response", "correct", "rt_s", "skipped" } },
            { "part1.csv", new[] { "participant_id", "block", "trial_index", "group_index", "position_in_group", "clip_id", "condition", "repetition", "depth_quality", "visual_comfort", "artifact_annoyance", "rt_depth_s", "rt_comfort_s", "rt_artifact_s", "initial_depth", "initial_comfort", "initial_artifact", "revisions", "view_s", "loops", "replays", "skipped" } },
            { "part1_replays.csv", new[] { "participant_id", "group_index", "position_in_group", "trial_index", "clip_id", "condition", "replay", "t_start_utc", "view_s", "loops", "completed" } },
            { "part1_revisions.csv", new[] { "participant_id", "group_index", "trial_index", "clip_id", "condition", "event", "revision", "reviewed", "depth_quality", "visual_comfort", "artifact_annoyance", "t_utc" } },
            { "part2.csv", new[] { "participant_id", "block", "trial_index", "clip_id", "cond_a", "cond_b", "pair_type", "different", "preference", "rt_q1_s", "rt_q2_s", "view_a_s", "view_b_s", "loops_a", "loops_b", "max_dev_a_deg", "max_dev_b_deg", "align_s", "start_dev_deg", "restarts_a", "restarts_b", "reminder_off_a", "reminder_off_b", "skipped" } },
            { "gazetraining.csv", new[] { "participant_id", "attempt", "position", "dot_id", "dot_angle_deg", "dwell_s", "max_dev_deg", "attempt_max_dev_deg", "threshold_deg", "attempt_passed" } },
            { "ssq.csv", new[] { "participant_id", "measurement", "GD", "FA", "HE", "ES", "DF", "IS", "SW", "NA", "CO", "FH", "BV", "DO", "DC", "VE", "SA", "BU" } },
            { "headpose.csv", new[] { "participant_id", "part", "trial_index", "segment", "t_s", "video_frame", "yaw_deg", "pitch_deg", "roll_deg" } },
            { "playback.csv", new[] { "participant_id", "part", "trial_index", "segment", "file", "frames_expected", "frames_shown", "frames_dropped", "dt_mean_ms", "dt_p99_ms" } },
        };

        private static readonly Dictionary<string, string[]> Schema = new Dictionary<string, string[]>
        {
            { "stereotest.csv", StudySchema.StereoTest },
            { "part1.csv", StudySchema.Part1 },
            { "part1_revisions.csv", StudySchema.Part1Revisions },
            { "part1_replays.csv", StudySchema.Part1Replays },
            { "part2.csv", StudySchema.Part2 },
            { "gazetraining.csv", StudySchema.GazeTraining },
            { "ssq.csv", StudySchema.Ssq },
            { "headpose.csv", StudySchema.HeadPose },
            { "playback.csv", StudySchema.Playback },
        };

        [Test]
        public void SchemaMatchesDataContractV4()
        {
            Assert.AreEqual(5, StudySchema.ContractVersion);
            foreach (KeyValuePair<string, string[]> kv in Expected)
            {
                CollectionAssert.AreEqual(kv.Value, Schema[kv.Key], kv.Key);
            }
        }

        [Test]
        public void SchemaMatchesStudyToolsReadme()
        {
            string readme = Path.Combine(TestPaths.ProjectRoot(), "StudyTools", "README.md");
            if (!File.Exists(readme))
            {
                Assert.Ignore("StudyTools/README.md not found");
            }
            string[] lines = File.ReadAllLines(readme);
            Assert.IsTrue(lines.Any(l => l.Contains("数据契约 v5")), "README does not declare data contract v5");
            foreach (KeyValuePair<string, string[]> kv in Schema)
            {
                string row = lines.FirstOrDefault(l => l.StartsWith("| `" + kv.Key + "` |", StringComparison.Ordinal));
                Assert.IsNotNull(row, kv.Key + " missing from README table");
                Match m = Regex.Match(row, @"\|\s*`[^`]+`\s*\|\s*`([^`]+)`");
                Assert.IsTrue(m.Success, "no field list for " + kv.Key);
                string[] fields = m.Groups[1].Value.Split(',').Select(s => s.Trim()).ToArray();
                CollectionAssert.AreEqual(fields, kv.Value, kv.Key + " differs from README");
            }
        }

        [Test]
        public void LoggerWritesHeaderAndInvariantRows()
        {
            string dir = Path.Combine(Path.GetTempPath(), "p2s_study_test_" + Guid.NewGuid().ToString("N"));
            try
            {
                var log = new StudyDataLogger(dir, "P01");
                log.Part2.Append("P01", "main", 3, "near_A", "pa", "paw", "real", 1, "-2", 1.25f, "2.5", 12.5f, 10.04f, 1, 0, 2.25, 3.5, 1.84, 3.1, 1, 0, 0, 0, 0);
                log.Part1.Append("P01", "main", 7, 4, 1, "vin_A", "ours", 0, 4, 5, 3, 1.5f, 2f, 2.5f, 3, 5, 3, 1, 10.2f, 1, 2, 0);
                log.Part1.Append("P01", "main", 8, 4, 2, "vin_A", "mono", 0, "", "", "", "", "", "", "", "", "", 0, "", 0, 0, 1);
                log.Part1Replays.Append("P01", 4, 1, 7, "vin_A", "ours", 1, "2026-11-19T05:00:00.000Z", 4.5f, 0, 0);
                log.Part1Revisions.Append("P01", 4, 7, "vin_A", "ours", "revision", 1, 0, 4, 5, 3, "2026-11-19T05:00:00.000Z");
                log.GazeTraining.Append("P01", 1, 0, 5, 225.0, 2f, 1.25, 3.5, 5f, 1);
                log.StereoTest.Append("P01", 2, "main", "cross", 2, 497.8, 248.9, "config_fallback", "cant_tell", 0, 3.25f, 0);
                log.WriteSsq("S_pre", Enumerable.Range(0, 16).Select(i => i % 4).ToList());
                log.WriteJson("session.json", new Dictionary<string, object> { { "participant_id", "P01" }, { "status", "complete" }, { "seed", 211295368377419L } });
                string[] p2 = File.ReadAllLines(Path.Combine(dir, "part2.csv"));
                Assert.AreEqual(string.Join(",", StudySchema.Part2), p2[0]);
                Assert.AreEqual("P01,main,3,near_A,pa,paw,real,1,-2,1.25,2.5,12.5,10.04,1,0,2.25,3.5,1.84,3.1,1,0,0,0,0", p2[1]);
                Assert.AreEqual("P01,main,7,4,1,vin_A,ours,0,4,5,3,1.5,2,2.5,3,5,3,1,10.2,1,2,0", File.ReadAllLines(Path.Combine(dir, "part1.csv"))[1]);
                Assert.AreEqual("P01,main,8,4,2,vin_A,mono,0,,,,,,,,,,0,,0,0,1", File.ReadAllLines(Path.Combine(dir, "part1.csv"))[2]);
                Assert.AreEqual("P01,4,1,7,vin_A,ours,1,2026-11-19T05:00:00.000Z,4.5,0,0", File.ReadAllLines(Path.Combine(dir, "part1_replays.csv"))[1]);
                Assert.AreEqual("P01,4,7,vin_A,ours,revision,1,0,4,5,3,2026-11-19T05:00:00.000Z", File.ReadAllLines(Path.Combine(dir, "part1_revisions.csv"))[1]);
                Assert.AreEqual("P01,1,0,5,225,2,1.25,3.5,5,1", File.ReadAllLines(Path.Combine(dir, "gazetraining.csv"))[1]);
                Assert.AreEqual("P01,2,main,cross,2,497.8,248.9,config_fallback,cant_tell,0,3.25,0", File.ReadAllLines(Path.Combine(dir, "stereotest.csv"))[1]);
                string[] ssq = File.ReadAllLines(Path.Combine(dir, "ssq.csv"));
                Assert.AreEqual("P01,S_pre,0,1,2,3,0,1,2,3,0,1,2,3,0,1,2,3", ssq[1]);
                Dictionary<string, object> session = MiniJson.AsObject(MiniJson.Parse(File.ReadAllText(Path.Combine(dir, "session.json"))), "session");
                Assert.AreEqual("complete", session["status"]);
                Assert.AreEqual(211295368377419L, session["seed"]);
                Assert.Throws<ArgumentException>(() => log.Part1.Append("too", "few"));
                foreach (string f in Expected.Keys)
                {
                    Assert.IsTrue(File.Exists(Path.Combine(dir, f)), f + " not created");
                }
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
        public void AngleFromReferenceIsTheGreatCircleAngle()
        {
            Assert.AreEqual(0.0, StudySchema.AngleFromReference(0, 0), 1e-9);
            Assert.AreEqual(15.0, StudySchema.AngleFromReference(15, 0), 1e-9);
            Assert.AreEqual(15.0, StudySchema.AngleFromReference(0, -15), 1e-9);
            // cos(c) = cos(10) cos(10) for yaw 10, pitch 10
            Assert.AreEqual(Math.Acos(Math.Cos(10 * Math.PI / 180) * Math.Cos(10 * Math.PI / 180)) * 180 / Math.PI,
                StudySchema.AngleFromReference(10, 10), 1e-9);
        }

        [Test]
        public void ExistingParticipantFolderIsNeverReused()
        {
            string root = Path.Combine(Path.GetTempPath(), "p2s_study_root_" + Guid.NewGuid().ToString("N"));
            try
            {
                string first = StudyDataLogger.NewSessionDirectory(root, "P02", new DateTime(2026, 11, 15, 8, 0, 0, DateTimeKind.Utc), out bool existed);
                Assert.IsFalse(existed);
                Assert.AreEqual(Path.Combine(root, "P02"), first);
                Directory.CreateDirectory(first);
                File.WriteAllText(Path.Combine(first, "session.json"), "{}");
                string second = StudyDataLogger.NewSessionDirectory(root, "P02", new DateTime(2026, 11, 15, 8, 0, 0, DateTimeKind.Utc), out existed);
                Assert.IsTrue(existed);
                Assert.AreEqual(Path.Combine(root, "P02__20261115_080000"), second);
                CollectionAssert.AreEqual(new[] { "x.mp4" }, StudyDataLogger.MissingStimuli(first, new[] { "session.json", "x.mp4" }));
                Assert.AreEqual(64, StudyDataLogger.FileSha256(Path.Combine(first, "session.json")).Length);
                Assert.AreEqual("", StudyDataLogger.FileSha256(Path.Combine(first, "nope.json")));
            }
            finally
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, true);
                }
            }
        }

        [Test]
        public void SettingsResolveCommandLineOverrides()
        {
            string root = Path.GetTempPath();
            StudySettings s = StudySettings.Resolve(root, true, new[] { "Unity.exe", "--data-dir", "D:/x/data", "--participant=P07" });
            Assert.AreEqual(Path.GetFullPath("D:/x/data"), s.DataDir);
            Assert.AreEqual("P07", s.ParticipantId);
            Assert.AreEqual(Path.GetFullPath(Path.Combine(root, "StudyTools", "plans")), s.PlansDir);
            Assert.AreEqual(StudySettings.ModeFormal, s.Mode);
            Assert.IsFalse(s.IsQa);
        }

        [Test]
        public void SessionModeDefaultsToFormalAndAcceptsQa()
        {
            string root = Path.GetTempPath();
            StudySettings a = StudySettings.Resolve(root, true, new[] { "Unity.exe", "--qa", "--participant", "P03" });
            Assert.AreEqual(StudySettings.ModeQa, a.Mode);
            Assert.AreEqual("P03", a.ParticipantId);
            StudySettings b = StudySettings.Resolve(root, true, new[] { "Unity.exe", "--mode", "qa" });
            Assert.IsTrue(b.IsQa);
            StudySettings c = StudySettings.Resolve(root, true, new[] { "Unity.exe", "-studyAutopilot", "-autopilotShots", "D:/shots" });
            Assert.IsTrue(c.IsQa);
            StudySettings d = StudySettings.Resolve(root, true, new[] { "Unity.exe", "--mode=demo" });
            Assert.AreEqual(StudySettings.ModeFormal, d.Mode);
            Assert.IsNotEmpty(d.Warning);
        }
    }
}
