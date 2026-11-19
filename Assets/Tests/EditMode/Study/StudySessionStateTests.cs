using System;
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
        public void RatingRecordKeepsTheInitialAnswerAndCountsRevisions()
        {
            var r = new RatingRecord();
            Assert.IsFalse(r.Rated);
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
