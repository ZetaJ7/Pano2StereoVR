using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace Pano2StereoVR.Study.Tests
{
    public sealed class StudySessionStateTests
    {
        /// <summary>
        /// Engine objects must not be created in static or instance field initialisers (UnityException at
        /// MonoBehaviour construction, seen in the 11-19 editor run with a static RectOffset).
        /// </summary>
        [Test]
        public void NoEngineObjectsInFieldInitialisers()
        {
            string dir = Path.Combine(TestPaths.ProjectRoot(), "Assets", "Scripts", "Study");
            var field = new Regex(@"^ {8}(?:(?:public|private|internal|protected|static|readonly)\s+)+[\w<>\[\],\. ]+?\s+\w+\s*=\s*(?<init>.+)$");
            var engine = new Regex(@"new\s+(RectOffset|Material|Texture2D|RenderTexture|GameObject|Mesh)\b|Shader\.Find|Resources\.Load|ColorUtility\.|Font\.CreateDynamicFontFromOSFont|Camera\.main|GameObject\.");
            foreach (string file in Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories))
            {
                string[] lines = File.ReadAllLines(file);
                for (int i = 0; i < lines.Length; i++)
                {
                    Match m = field.Match(lines[i]);
                    if (m.Success && !lines[i].Contains("=>") && !lines[i].Contains("const "))
                    {
                        Assert.IsFalse(engine.IsMatch(m.Groups["init"].Value),
                            Path.GetFileName(file) + ":" + (i + 1) + " creates an engine object in a field initialiser: " + lines[i].Trim());
                    }
                }
            }
            // colours parsed without ColorUtility (pure C#)
            Assert.AreEqual(27f / 255f, StudyTheme.Hex("1B365D").r, 1e-6);
            Assert.AreEqual(93f / 255f, StudyTheme.Hex("#1B365D").b, 1e-6);
        }

        [Test]
        public void ResetClearsEverySessionField()
        {
            var s = new StudySessionState();
            Assert.IsTrue(s.IsEmpty);
            Assert.AreEqual(0, s.Generation);
            string dir = Path.Combine(Path.GetTempPath(), "p2s_state_" + Guid.NewGuid().ToString("N"));
            try
            {
                s.Language = StudyTexts.English;
                s.Mode = StudySettings.ModeQa;
                s.Plan = new StudyPlan { ParticipantId = "P05" };
                s.PlanPath = "plans/P05.json";
                s.PlanSha256 = "abc";
                s.SessionDir = dir;
                s.SessionDirExisted = true;
                s.Log = new StudyDataLogger(dir, "P05");
                s.Events = new CsvLog(Path.Combine(dir, "events.csv"), new[] { "t_utc", "event", "detail" });
                s.Session["status"] = "complete";
                s.Step = "complete";
                s.TrialLabel = "3/18";
                s.DroppedTotal = 12;
                s.MissingStimuli.Add("x.mp4");
                s.Finished = true;
                s.EndReady = true;
                s.BackupPath = "D:/b";
                s.BackupError = "disk full";
                s.Part2ReminderOff = true;
                s.EyeRangeSweeps.Add(new EyeRangeSweepResult { Sweep = 3, DirectionDeg = 90f, HeadOnsetEccDeg = 20f });
                s.PawThetaDeg = 20f;
                s.PawThetaSource = "measured";
                s.PreviewEye = 1f;
                s.DeferredEvents.Add("t\tlanguage\ten");
                s.GazeAttempts.Add(new GazeAttemptResult { Attempt = 1, MaxDeg = 7f });
                s.Part1Ratings[3] = new RatingRecord();
                s.StereoTrialsDone = 7;
                s.Part2TrialsDone = 20;
                s.Part1TrialsDone = 24;
                s.Skips = 2;
                s.SessionStartRealtime = 10f;
                s.BreakStartRealtime = 600f;
                Assert.IsFalse(s.IsEmpty);

                s.Reset();
                Assert.IsTrue(s.IsEmpty, "state left over after Reset");
                Assert.AreEqual(1, s.Generation);
                Assert.AreEqual("language", s.Step);
                Assert.IsNull(s.Plan);
                Assert.IsNull(s.Log);
                s.Reset();
                Assert.AreEqual(2, s.Generation);
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
        public void EyeRangeChoosesThePawRadiusOrTheDefault()
        {
            var plan = new EyeRangePlan
            {
                Present = true, Enabled = true, HeadThresholdDeg = 3f, MaxEccentricityDeg = 40f, SpeedDegPerSecond = 8f,
                DirectionsDeg = new List<float> { 0f, 90f, 180f, 270f },
            };
            var s = new StudySessionState();
            // 8 sweeps (2 per direction), one censored: values 14, 18, 20, 21, 22, 24, 26, 40 -> median 21.5 -> radius 20
            float[,] sw = { { 0f, 14f }, { 0f, 22f }, { 90f, 18f }, { 90f, 24f }, { 180f, 20f }, { 180f, 26f }, { 270f, 21f }, { 270f, -1f } };
            for (int i = 0; i < sw.GetLength(0); i++)
            {
                bool c = sw[i, 1] < 0f;
                s.EyeRangeSweeps.Add(new EyeRangeSweepResult
                {
                    Sweep = i, DirectionDeg = sw[i, 0], Repeat = i % 2, HeadOnsetEccDeg = c ? float.NaN : sw[i, 1], Censored = c, StoppedEarly = !c,
                    MaxHeadRotDeg = 5f,
                });
            }
            s.EyeRangeSweeps.Add(new EyeRangeSweepResult { Sweep = 99, DirectionDeg = 0f, Skipped = true });
            Dictionary<string, object> block = s.ResolveEyeRange(plan, "measured");
            Assert.AreEqual(20f, s.PawThetaDeg);
            Assert.AreEqual("measured", s.PawThetaSource);
            Assert.AreEqual(21.5, block["a_deg"]);
            Assert.AreEqual("continuous", block["mode"]);
            Assert.AreEqual("head_rot_deg", block["measure"]);
            Assert.AreEqual(8.0, block["speed_deg_s"]);
            Assert.AreEqual(3.0, block["head_threshold_deg"]);
            Assert.AreEqual(1, block["censored"]);
            Assert.AreEqual(7, block["stopped_early"]);
            Assert.AreEqual(true, block["stop_at_onset"]);
            Assert.AreEqual("22°", block["a_shown"], "21.5 shown in whole degrees");
            Assert.AreEqual(false, block["skipped"]);
            var perDir = ((List<object>)block["per_direction"]).Cast<Dictionary<string, object>>().ToList();
            Assert.AreEqual(4, perDir.Count);
            Assert.AreEqual(18.0, perDir[0]["median_head_onset_ecc_deg"], "direction 0: 14 and 22; the skipped sweep is left out");
            Assert.AreEqual(2, perDir[0]["n"]);
            Assert.AreEqual(30.5, perDir[3]["median_head_onset_ecc_deg"], "direction 270: 21 and censored = 40");
            Assert.AreEqual(1, perDir[3]["censored"]);
            Assert.AreEqual("18°", perDir[0]["shown"]);
            Assert.AreEqual("31°", perDir[3]["shown"]);

            // every sweep censored: a = the maximum eccentricity -> the largest radius
            s.EyeRangeSweeps.Clear();
            foreach (float d in plan.DirectionsDeg)
            {
                s.EyeRangeSweeps.Add(new EyeRangeSweepResult { DirectionDeg = d, HeadOnsetEccDeg = float.NaN, Censored = true });
            }
            block = s.ResolveEyeRange(plan, "measured");
            Assert.AreEqual(30f, s.PawThetaDeg);
            Assert.AreEqual("≥40°", block["a_shown"], "all censored");
            Assert.AreEqual("≥40°", ((Dictionary<string, object>)((List<object>)block["per_direction"])[0])["shown"]);

            // skipped or disabled: the default (15 deg, the paper's value), with the per-direction medians still listed
            block = s.ResolveEyeRange(plan, "default_skipped");
            Assert.AreEqual(15f, s.PawThetaDeg);
            Assert.AreEqual("default_skipped", s.PawThetaSource);
            Assert.IsNull(block["a_deg"]);
            Assert.AreEqual(true, block["skipped"]);
            Assert.AreEqual(40.0, (double)((Dictionary<string, object>)((List<object>)block["per_direction"])[0])["median_head_onset_ecc_deg"], 1e-6);
            s.ResolveEyeRange(new EyeRangePlan(), "default_no_block");
            Assert.AreEqual(15f, s.PawThetaDeg);
        }

        [Test]
        public void RecenterAnglesComeFromTheHeadDirection()
        {
            // user 11-20: R maps the head yaw and pitch to content 0 / 0; the angles are taken from the forward vector
            StudyRig.YawPitchOf(new UnityEngine.Vector3(0f, 0f, 1f), out float yaw, out float pitch);
            Assert.AreEqual(0f, yaw, 1e-5f);
            Assert.AreEqual(0f, pitch, 1e-5f);
            StudyRig.YawPitchOf(new UnityEngine.Vector3(1f, 0f, 1f), out yaw, out pitch);
            Assert.AreEqual(45f, yaw, 1e-4f);
            Assert.AreEqual(0f, pitch, 1e-4f);
            float p = 12f * (float)Math.PI / 180f;
            StudyRig.YawPitchOf(new UnityEngine.Vector3(0f, (float)Math.Sin(p), (float)Math.Cos(p)) * 3f, out yaw, out pitch);
            Assert.AreEqual(12f, pitch, 1e-3f, "pitch + = up, independent of the vector length");
            StudyRig.YawPitchOf(new UnityEngine.Vector3(-1f, 0f, 0f), out yaw, out _);
            Assert.AreEqual(-90f, yaw, 1e-4f);
            Assert.AreEqual(2, Enum.GetValues(typeof(RecenterMode)).Length, "R and Shift+R only; no automatic recenter (user 11-20)");
        }

        [Test]
        public void RatingRecordKeepsTheInitialAnswerAndCountsRevisions()
        {
            var r = new RatingRecord();
            Assert.IsFalse(r.Rated);
            Assert.AreEqual("", r.SkipReason);
            Assert.IsFalse(r.StaffSkipped);
            r.SetInitial(4, 3, 5);
            Assert.IsTrue(r.Rated);
            Assert.AreEqual(0, r.Revisions);
            r.ReplaysSinceRating = 1;
            Assert.IsFalse(r.Changed);
            r.Revise(5, 3, 5);
            r.Revise(5, 2, 5);
            Assert.AreEqual(2, r.Revisions);
            Assert.AreEqual(0, r.ReplaysSinceRating, "a revision resets the replay-since-rating count");
            Assert.AreEqual(4, r.InitialDepth);
            Assert.AreEqual(3, r.InitialComfort);
            Assert.AreEqual(5, r.Depth);
            Assert.AreEqual(2, r.Comfort);
            Assert.IsTrue(r.Changed);
        }

        [Test]
        public void SelfPacedLimitsFollowTheClipLength()
        {
            var req = new PlayRequest { SelfPaced = true };
            Assert.AreEqual(10f, req.EffectiveMinSeconds(10.0), 1e-6);
            Assert.AreEqual(0f, req.EffectiveMaxSeconds(10.0), 1e-6);
            req.MinViewSeconds = 4f;
            req.MaxViewSeconds = 3f;
            Assert.AreEqual(4f, req.EffectiveMinSeconds(10.0), 1e-6);
            Assert.AreEqual(4f, req.EffectiveMaxSeconds(10.0), 1e-6, "maximum never earlier than the minimum");
            req.MaxViewSeconds = 30f;
            Assert.AreEqual(30f, req.EffectiveMaxSeconds(10.0), 1e-6);
        }
    }
}
