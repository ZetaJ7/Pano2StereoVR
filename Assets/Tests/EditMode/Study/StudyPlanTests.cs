using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace Pano2StereoVR.Study.Tests
{
    public sealed class StudyPlanTests
    {
        private const string SamplePlan = @"{
  ""plan_format_version"": 4,
  ""study_id"": ""Pano2Stereo-E5"",
  ""config_version"": ""test"",
  ""config_sha256"": ""abc"",
  ""participant_id"": ""P99"",
  ""seed"": 211295368377419,
  ""generated_utc"": ""2026-11-19T00:00:00+00:00"",
  ""timing"": {""clip_s"": 10.0, ""gap_between_ab_s"": 1.5, ""gray_between_trials_s"": 2.0, ""orientation_cue_s"": 2.0},
  ""flow"": [""checkin"", ""ssq:S_pre"", ""hmd_fit"", ""stereotest"", ""part:1"", ""ssq:S_mid1"", ""break"", ""ssq:S_mid2"", ""hmd_fit"", ""gazetraining"", ""part:2"", ""ssq:S_post"", ""post_questionnaire"", ""end""],
  ""min_break_s"": 120.0,
  ""alignment"": {""tolerance_deg"": 5.0, ""dwell_s"": 1.0},
  ""stereotest"": {""enabled"": true, ""shapes"": [""circle"", ""square""], ""quad_distance_m"": 2.0, ""quad_size_deg"": 20.0, ""dot_px"": 3,
                 ""display_fallback"": {""eye_width_px"": 2160, ""hfov_deg"": 105.0},
                 ""trials"": [{""index"": 0, ""block"": ""practice"", ""shape"": ""circle"", ""disparity_px"": 8, ""sign"": ""crossed""}]},
  ""gaze_training"": {""enabled"": true, ""n_dots"": 4, ""radius_deg"": 15.0, ""dwell_s"": 2.0, ""dot_size_deg"": 1.2,
                    ""max_head_deviation_deg"": 5.0, ""max_attempts"": 2, ""reference_yaw_deg"": 0.0, ""reference_pitch_deg"": 0.0,
                    ""orders"": [[2, 0, 3, 1], [1, 3, 0, 2]]},
  ""parts"": [
    {""part"": 2, ""options"": {""self_paced"": true, ""min_view_s"": null, ""max_view_s"": 0},
     ""trials"": [{""block"": ""main"", ""clip_id"": ""near_A"", ""cond_a"": ""pa"", ""cond_b"": ""paw"", ""reference_yaw_deg"": 15.0, ""reference_pitch_deg"": -5.0,
                   ""cell"": ""virtual_indoor"", ""scene"": ""indoor"", ""index"": 0, ""pair_type"": ""real"", ""file_a"": ""near_A__pa.mp4"", ""file_b"": ""near_A__paw.mp4""}]},
    {""part"": 1, ""options"": {""self_paced"": false, ""min_view_s"": null, ""max_view_s"": 0, ""allow_review_on_rerate"": false}, ""n_groups"": 2,
     ""trials"": [{""block"": ""training"", ""clip_id"": ""t"", ""condition"": ""ours"", ""group_index"": 0, ""group_size"": 0, ""position_in_group"": 0, ""index"": 0, ""file"": ""t__ours.mp4""},
                  {""block"": ""dummy"", ""clip_id"": ""d"", ""condition"": ""ours"", ""group_index"": 1, ""group_size"": 2, ""position_in_group"": 0, ""index"": 1, ""file"": ""d__ours.mp4""},
                  {""block"": ""dummy"", ""clip_id"": ""d"", ""condition"": ""mono"", ""group_index"": 1, ""group_size"": 2, ""position_in_group"": 1, ""index"": 2, ""file"": ""d__mono.mp4""},
                  {""block"": ""main"", ""clip_id"": ""vin_A"", ""condition"": ""mono"", ""cell"": ""virtual_indoor"", ""scene"": ""indoor"", ""repetition"": 0, ""group_index"": 2, ""group_size"": 2, ""position_in_group"": 0, ""index"": 3, ""file"": ""vin_A__mono.mp4""},
                  {""block"": ""main"", ""clip_id"": ""vin_A"", ""condition"": ""ours"", ""cell"": ""virtual_indoor"", ""scene"": ""indoor"", ""repetition"": 0, ""group_index"": 2, ""group_size"": 2, ""position_in_group"": 1, ""index"": 4, ""file"": ""vin_A__ours.mp4""}]}
  ]
}";

        [Test]
        public void SamplePlanParsesAllFields()
        {
            StudyPlan plan = StudyPlan.FromJson(SamplePlan);
            Assert.AreEqual(4, plan.PlanFormatVersion);
            Assert.AreEqual("P99", plan.ParticipantId);
            Assert.AreEqual(211295368377419L, plan.Seed);
            Assert.AreEqual(10f, plan.Timing.ClipSeconds);
            Assert.AreEqual(120f, plan.MinBreakSeconds);
            CollectionAssert.AreEqual(new[] { 1, 2 }, plan.PartOrder);
            Assert.AreEqual(5f, plan.Alignment.ToleranceDeg);
            Assert.AreEqual(1f, plan.Alignment.DwellSeconds);
            StudyTrial p2 = plan.GetPart(2).Trials[0];
            Assert.AreEqual("near_A__paw.mp4", p2.FileB);
            Assert.AreEqual(15f, p2.ReferenceYawDeg);
            Assert.AreEqual(-5f, p2.ReferencePitchDeg);
            Assert.AreEqual("real", p2.PairType);
            Assert.IsTrue(plan.GetPart(2).Options.SelfPaced);
            Assert.AreEqual(0f, plan.GetPart(2).Options.MinViewSeconds, "null min_view_s means one full play");
            Assert.IsFalse(plan.GetPart(1).Options.SelfPaced);
            Assert.IsFalse(plan.GetPart(1).Options.AllowReviewOnRerate);
            Assert.AreEqual(1, plan.StereoTest.Trials.Count);
            Assert.AreEqual(8, plan.StereoTest.Trials[0].DisparityPx);
            Assert.AreEqual(3, plan.StereoTest.DotPx);
            Assert.AreEqual(2160, plan.StereoTest.FallbackEyeWidthPx);
            Assert.AreEqual(105f, plan.StereoTest.FallbackHfovDeg);
            Assert.IsTrue(plan.StereoTest.Trials[0].Crossed);
            Assert.IsTrue(plan.GazeTraining.Enabled);
            Assert.AreEqual(4, plan.GazeTraining.DotCount);
            Assert.AreEqual(5f, plan.GazeTraining.MaxHeadDeviationDeg);
            Assert.AreEqual(2, plan.GazeTraining.MaxAttempts);
            CollectionAssert.AreEqual(new[] { 1, 3, 0, 2 }, plan.GazeTraining.OrderFor(1));
            CollectionAssert.AreEqual(new[] { 2, 0, 3, 1 }, plan.GazeTraining.OrderFor(2), "orders repeat when attempts exceed them");
            Assert.AreEqual(90f, plan.GazeTraining.DotAngleDeg(1));
            CollectionAssert.AreEquivalent(
                new[] { "near_A__pa.mp4", "near_A__paw.mp4", "t__ours.mp4", "d__ours.mp4", "d__mono.mp4", "vin_A__mono.mp4", "vin_A__ours.mp4" },
                plan.AllStimulusFiles().ToList());
        }

        [Test]
        public void Part1GroupsAreContiguousSingleClipBlocks()
        {
            StudyPart part1 = StudyPlan.FromJson(SamplePlan).GetPart(1);
            Assert.AreEqual(1, part1.UngroupedTrials.Count);
            Assert.AreEqual("training", part1.UngroupedTrials[0].Block);
            List<List<StudyTrial>> groups = part1.Groups();
            Assert.AreEqual(2, groups.Count);
            CollectionAssert.AreEqual(new[] { "dummy", "dummy" }, groups[0].Select(t => t.Block).ToArray());
            CollectionAssert.AreEqual(new[] { "mono", "ours" }, groups[1].Select(t => t.Condition).ToArray());
            CollectionAssert.AreEqual(new[] { 0, 1 }, groups[1].Select(t => t.PositionInGroup).ToArray());

            // a group split by another group, a group with two clips, a wrong position: all rejected
            string split = SamplePlan.Replace(@"""group_index"": 1, ""group_size"": 2, ""position_in_group"": 1, ""index"": 2",
                @"""group_index"": 2, ""group_size"": 2, ""position_in_group"": 1, ""index"": 2");
            Assert.Throws<FormatException>(() => StudyPlan.FromJson(split));
            string twoClips = SamplePlan.Replace(@"""clip_id"": ""d"", ""condition"": ""mono""", @"""clip_id"": ""e"", ""condition"": ""mono""");
            Assert.Throws<FormatException>(() => StudyPlan.FromJson(twoClips));
            string badPos = SamplePlan.Replace(@"""position_in_group"": 1, ""index"": 4", @"""position_in_group"": 0, ""index"": 4");
            Assert.Throws<FormatException>(() => StudyPlan.FromJson(badPos));
        }

        [Test]
        public void FlowStepsParseInOrder()
        {
            StudyPlan plan = StudyPlan.FromJson(SamplePlan);
            CollectionAssert.AreEqual(
                new[]
                {
                    FlowStepKind.Checkin, FlowStepKind.Ssq, FlowStepKind.HmdFit, FlowStepKind.StereoTest, FlowStepKind.Part,
                    FlowStepKind.Ssq, FlowStepKind.Break, FlowStepKind.Ssq, FlowStepKind.HmdFit, FlowStepKind.GazeTraining,
                    FlowStepKind.Part, FlowStepKind.Ssq, FlowStepKind.PostQuestionnaire, FlowStepKind.End,
                },
                plan.Flow.Select(s => s.Kind).ToArray());
            Assert.AreEqual("S_pre", plan.Flow[1].Argument);
            Assert.AreEqual(1, plan.Flow[4].PartNumber);
            Assert.AreEqual("S_mid1", plan.Flow[5].Argument);
            Assert.AreEqual("S_mid2", plan.Flow[7].Argument);
            Assert.AreEqual(2, plan.Flow[10].PartNumber);
        }

        [Test]
        public void InvalidPlansAreRejected()
        {
            Assert.Throws<FormatException>(() => FlowStep.Parse("part:3"));
            Assert.Throws<FormatException>(() => FlowStep.Parse("ssq"));
            Assert.Throws<FormatException>(() => FlowStep.Parse("dance"));
            Assert.Throws<FormatException>(() => StudyPlan.FromJson(SamplePlan.Replace("\"plan_format_version\": 4", "\"plan_format_version\": 3")));
            // alignment tolerance must be positive
            Assert.Throws<FormatException>(() => StudyPlan.FromJson(SamplePlan.Replace("\"tolerance_deg\": 5.0", "\"tolerance_deg\": 0")));
            // flow asks for part 1 but the plan only has part 2
            Assert.Throws<FormatException>(() => StudyPlan.FromJson(SamplePlan.Replace("{\"part\": 1,", "{\"part\": 2,")));
            // gaze-training order that is not a permutation
            Assert.Throws<FormatException>(() => StudyPlan.FromJson(SamplePlan.Replace("[1, 3, 0, 2]", "[1, 1, 0, 2]")));
            // flow with gazetraining but no gaze_training block
            int a = SamplePlan.IndexOf("\"gaze_training\"", StringComparison.Ordinal);
            int b = SamplePlan.IndexOf("\"parts\"", StringComparison.Ordinal);
            Assert.Throws<FormatException>(() => StudyPlan.FromJson(SamplePlan.Remove(a, b - a)));
        }

        [Test]
        public void GeneratedPlanFromStudyToolsMatchesTheDesign()
        {
            string path = Path.Combine(TestPaths.ProjectRoot(), "StudyTools", "plans", "P01.json");
            if (!File.Exists(path))
            {
                Assert.Ignore("Generate plans first: python StudyTools/make_trial_plans.py --config StudyTools/study_config.example.json --participants P01-P30 --out StudyTools/plans");
            }
            StudyPlan plan = StudyPlan.Load(path);
            Assert.AreEqual("P01", plan.ParticipantId);
            CollectionAssert.AreEqual(new[] { 1, 2 }, plan.PartOrder);
            CollectionAssert.AreEqual(new[] { "S_pre", "S_mid1", "S_mid2", "S_post" },
                plan.Flow.Where(s => s.Kind == FlowStepKind.Ssq).Select(s => s.Argument).ToArray());
            int brk = plan.Flow.FindIndex(s => s.Kind == FlowStepKind.Break);
            Assert.IsTrue(brk > 0 && plan.Flow[brk - 1].Argument == "S_mid1" && plan.Flow[brk + 1].Argument == "S_mid2");
            Assert.AreEqual(2, plan.Flow.Count(s => s.Kind == FlowStepKind.HmdFit));
            Assert.AreEqual(5f, plan.Alignment.ToleranceDeg);
            Assert.AreEqual(1f, plan.Alignment.DwellSeconds);
            int gaze = plan.Flow.FindIndex(s => s.Kind == FlowStepKind.GazeTraining);
            Assert.IsTrue(gaze >= 0 && plan.Flow[gaze + 1].Kind == FlowStepKind.Part && plan.Flow[gaze + 1].PartNumber == 2,
                "gazetraining must come right before part:2");
            StudyPart part2 = plan.GetPart(2);
            StudyPart part1 = plan.GetPart(1);
            Assert.AreEqual(18, part2.Trials.Count(t => t.IsMain));
            Assert.AreEqual(6, part2.Trials.Count(t => t.IsMain && t.PairType == "same"));
            Assert.IsTrue(part2.Options.SelfPaced);
            Assert.AreEqual(36, part1.Trials.Count(t => t.IsMain));
            Assert.AreEqual(4, part1.Trials.Count(t => t.Block == "dummy"));
            Assert.IsTrue(part1.Options.AllowReviewOnRerate, "replay on the re-rate page (user decision 11-19)");
            Assert.AreEqual(2, part1.UngroupedTrials.Count);
            Assert.IsTrue(part1.UngroupedTrials.All(t => t.Block == "training"));

            // Part 1: one group per clip (the dummy group first), every condition once per group
            string[] conditions = { "mono", "ours", "deepsynthesis", "motionparallax" };
            List<List<StudyTrial>> groups = part1.Groups();
            Assert.AreEqual(10, groups.Count);
            Assert.IsTrue(groups[0].All(t => t.Block == "dummy"));
            Assert.IsTrue(groups.Skip(1).All(g => g.All(t => t.Block == "main")));
            foreach (List<StudyTrial> g in groups)
            {
                Assert.AreEqual(1, g.Select(t => t.ClipId).Distinct().Count());
                CollectionAssert.AreEquivalent(conditions, g.Select(t => t.Condition).ToArray());
            }
            for (int i = 1; i < groups.Count; i++)
            {
                Assert.AreNotEqual(groups[i - 1][0].ClipId, groups[i][0].ClipId, "adjacent groups with the same clip");
            }
            // condition positions are balanced over the 9 main groups (each condition in each position 2 or 3 times)
            foreach (string c in conditions)
            {
                for (int pos = 0; pos < 4; pos++)
                {
                    int n = groups.Skip(1).Count(g => g[pos].Condition == c);
                    Assert.IsTrue(n == 2 || n == 3, c + " at position " + pos + " in " + n + " of 9 groups");
                }
            }

            // Part 2: the same clip never appears in two consecutive main trials
            var main2 = part2.Trials.Where(t => t.IsMain).ToList();
            for (int i = 1; i < main2.Count; i++)
            {
                Assert.AreNotEqual(main2[i - 1].ClipId, main2[i].ClipId, "adjacent repeat in part 2");
            }

            Assert.IsTrue(plan.StereoTest.Enabled);
            Assert.AreEqual(7, plan.StereoTest.Trials.Count);
            Assert.AreEqual("practice", plan.StereoTest.Trials[0].Block);
            Assert.AreEqual(8, plan.StereoTest.Trials[0].DisparityPx);
            CollectionAssert.AreEquivalent(new[] { 4, 4, 2, 2, 1, 1 }, plan.StereoTest.Trials.Skip(1).Select(t => t.DisparityPx).ToArray());
            Assert.AreEqual(3, plan.StereoTest.DotPx);

            Assert.IsTrue(plan.GazeTraining.Enabled);
            Assert.AreEqual(8, plan.GazeTraining.DotCount);
            Assert.AreEqual(15f, plan.GazeTraining.RadiusDeg);
            Assert.AreEqual(plan.GazeTraining.MaxAttempts, plan.GazeTraining.Orders.Count);
        }
    }

    internal static class TestPaths
    {
        public static string ProjectRoot()
        {
            string dir = Directory.GetCurrentDirectory();
            for (int i = 0; i < 6 && dir != null; i++)
            {
                if (Directory.Exists(Path.Combine(dir, "Assets")) && Directory.Exists(Path.Combine(dir, "StudyTools")))
                {
                    return dir;
                }
                dir = Path.GetDirectoryName(dir);
            }
            return Directory.GetCurrentDirectory();
        }
    }
}
