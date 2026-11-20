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
            { "part2.csv", new[] { "participant_id", "block", "trial_index", "clip_id", "cond_a", "cond_b", "pair_type", "different", "preference", "rt_q1_s", "rt_q2_s", "view_a_s", "view_b_s", "loops_a", "loops_b", "max_dev_a_deg", "max_dev_b_deg", "align_s", "start_dev_deg", "restarts_a", "restarts_b", "reminder_off_a", "reminder_off_b", "paw_theta_deg", "skipped" } },
            { "eyerange.csv", new[] { "participant_id", "sweep", "direction_deg", "repeat", "speed_deg_s", "onset_dev_deg", "head_onset_ecc_deg", "censored", "stopped_early", "max_head_rot_deg", "skipped" } },
            { "eyerange_trace.csv", new[] { "participant_id", "sweep", "direction_deg", "repeat", "t_s", "dot_ecc_deg", "head_yaw_deg", "head_pitch_deg", "head_rot_deg", "head_rot_along_deg" } },
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
            { "eyerange.csv", StudySchema.EyeRange },
            { "eyerange_trace.csv", StudySchema.EyeRangeTrace },
            { "ssq.csv", StudySchema.Ssq },
            { "headpose.csv", StudySchema.HeadPose },
            { "playback.csv", StudySchema.Playback },
        };

        [Test]
        public void SchemaMatchesDataContractV6()
        {
            Assert.AreEqual(6, StudySchema.ContractVersion);
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
            Assert.IsTrue(lines.Any(l => l.Contains("数据契约 v6")), "README does not declare data contract v6");
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
                log.Part2.Append("P01", "main", 3, "near_A", "pa", "paw", "real", 1, "-2", 1.25f, "2.5", 12.5f, 10.04f, 1, 0, 2.25, 3.5, 1.84, 3.1, 1, 0, 0, 0, 20.0, 0);
                log.EyeRange.Append("P01", 5, 135.0, 1, 8.0, 2.1, 21.37, 0, 1, 3.04, 0);
                log.EyeRange.Append("P01", 6, 0.0, 0, 8.0, 1.2, "", 1, 0, 2.25, 0);
                log.EyeRange.Append("P01", 7, 90.0, 1, 8.0, "", "", "", "", "", 1);
                log.EyeRangeTrace.Append("P01", 5, 135.0, 1, 0.0139, 0.111, -1.5, 1.25, 0.0, 0.0);
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
                Assert.AreEqual("P01,main,3,near_A,pa,paw,real,1,-2,1.25,2.5,12.5,10.04,1,0,2.25,3.5,1.84,3.1,1,0,0,0,20,0", p2[1]);
                string[] er = File.ReadAllLines(Path.Combine(dir, "eyerange.csv"));
                Assert.AreEqual(string.Join(",", StudySchema.EyeRange), er[0]);
                Assert.AreEqual("P01,5,135,1,8,2.1,21.37,0,1,3.04,0", er[1], "stopped at the head onset");
                Assert.AreEqual("P01,6,0,0,8,1.2,,1,0,2.25,0", er[2], "censored sweep: no onset eccentricity, ran to the maximum");
                Assert.AreEqual("P01,7,90,1,8,,,,,,1", er[3]);
                string[] tr = File.ReadAllLines(Path.Combine(dir, "eyerange_trace.csv"));
                Assert.AreEqual(string.Join(",", StudySchema.EyeRangeTrace), tr[0]);
                Assert.AreEqual("P01,5,135,1,0.0139,0.111,-1.5,1.25,0,0", tr[1]);
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
        public void HeadAlongDirectionProjectsTheHeadOffsetOnTheTarget()
        {
            // 0 = up, 90 = right (clockwise for the viewer), as the gaze-training ring
            Assert.AreEqual(10.0, StudySchema.HeadAlongDirection(10, 0, 90), 1e-9);
            Assert.AreEqual(-10.0, StudySchema.HeadAlongDirection(10, 0, 270), 1e-9);
            Assert.AreEqual(5.0, StudySchema.HeadAlongDirection(0, 5, 0), 1e-9);
            Assert.AreEqual(0.0, StudySchema.HeadAlongDirection(0, 5, 90), 1e-9);
            Assert.AreEqual(0.0, StudySchema.HeadAlongDirection(0, 0, 45), 1e-12);
            // along never exceeds the great-circle deviation; diagonal target, head straight toward it
            double dev = StudySchema.AngleFromReference(6, 6);
            Assert.AreEqual(dev, StudySchema.HeadAlongDirection(6, 6, 45), 0.05);
            Assert.LessOrEqual(Math.Abs(StudySchema.HeadAlongDirection(6, -2, 135)), StudySchema.AngleFromReference(6, -2) + 1e-9);
        }

        [Test]
        public void HeadRotationIsMeasuredFromTheSweepOnset()
        {
            // user 11-20: the rule uses the rotation since the sweep onset, not the deviation from the reference
            Assert.AreEqual(0.0, StudySchema.GreatCircleDeg(3, -2, 3, -2), 1e-6);
            Assert.AreEqual(5.0, StudySchema.GreatCircleDeg(-4, 0, 1, 0), 1e-9, "head left 4 deg off the reference, turns 5 deg right");
            Assert.AreEqual(StudySchema.AngleFromReference(7, 3), StudySchema.GreatCircleDeg(0, 0, 7, 3), 1e-9);
            Assert.AreEqual(5.0, StudySchema.RotationAlongDirection(-4, 0, 1, 0, 90), 1e-6, "toward a dot on the right");
            Assert.AreEqual(-5.0, StudySchema.RotationAlongDirection(-4, 0, 1, 0, 270), 1e-6, "away from a dot on the left");
            Assert.AreEqual(3.0, StudySchema.RotationAlongDirection(2, 1, 2, 4, 0), 1e-6, "3 deg up toward an upper dot");
            Assert.AreEqual(0.0, StudySchema.RotationAlongDirection(2, 1, 2, 4, 90), 1e-6);
            // a residual offset with no rotation gives 0, whatever the deviation from the reference
            Assert.AreEqual(0.0, StudySchema.RotationAlongDirection(4, 2, 4, 2, 45), 1e-12);
            Assert.AreEqual(StudySchema.HeadAlongDirection(6, -2, 135), StudySchema.RotationAlongDirection(0, 0, 6, -2, 135), 1e-12);
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
