using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace Pano2StereoVR.Study
{
    /// <summary>
    /// Per-participant trial plan written by StudyTools/make_trial_plans.py (plan format 4).
    /// Format 3 (2026-11-19): stereo-test disparities in display pixels, a gaze-training block, per-part
    /// viewing options (self-paced viewing, replay on re-rate) and Part 1 grouped by clip
    /// (group_index, group_size, position_in_group; training trials have group_index 0).
    /// Format 4 (2026-11-19, second walkthrough): flow token "break" (rest, then the participant starts),
    /// and an "alignment" block (head within tolerance_deg of the reference for dwell_s starts playback).
    /// Format 5 (2026-11-20, user decisions 11-20): flow token "eyerange" (natural viewing range measurement,
    /// before the gaze training) with an "eye_range" block, and Part 2 PAw file names with the placeholder
    /// <see cref="PawThetaPlaceholder"/>, resolved at run time to the correction radius chosen for the participant.
    /// </summary>
    public sealed class StudyPlan
    {
        public const int SupportedFormatVersion = 5;

        /// <summary>Placeholder for the pose-aware correction radius (deg) in Part 2 file names, e.g. near_A__paw_t{paw_theta}.mp4.</summary>
        public const string PawThetaPlaceholder = "{paw_theta}";

        public int PlanFormatVersion;
        public string StudyId = "";
        public string ConfigVersion = "";
        public string ConfigSha256 = "";
        public string ParticipantId = "";
        public long Seed;
        public string GeneratedUtc = "";
        public StudyTiming Timing = new StudyTiming();
        public List<FlowStep> Flow = new List<FlowStep>();
        public List<string> FlowRaw = new List<string>();
        public float MinBreakSeconds = 120f;
        public StereoTestPlan StereoTest = new StereoTestPlan();
        public GazeTrainingPlan GazeTraining = new GazeTrainingPlan();
        public AlignmentPlan Alignment = new AlignmentPlan();
        public EyeRangePlan EyeRange = new EyeRangePlan();
        public List<StudyPart> Parts = new List<StudyPart>();

        public StudyPart GetPart(int number)
        {
            return Parts.FirstOrDefault(p => p.Number == number);
        }

        public List<int> PartOrder => Flow.Where(s => s.Kind == FlowStepKind.Part).Select(s => s.PartNumber).ToList();

        /// <summary>
        /// Every stimulus file the plan may play. A PAw name with <see cref="PawThetaPlaceholder"/> is listed once per
        /// correction-radius variant, so the check-in missing-stimuli check covers every variant.
        /// </summary>
        public IEnumerable<string> AllStimulusFiles()
        {
            var seen = new HashSet<string>();
            foreach (StudyPart part in Parts)
            {
                foreach (StudyTrial t in part.Trials)
                {
                    foreach (string f in new[] { t.File, t.FileA, t.FileB })
                    {
                        if (string.IsNullOrEmpty(f))
                        {
                            continue;
                        }
                        foreach (string name in ExpandPawTheta(f))
                        {
                            if (seen.Add(name))
                            {
                                yield return name;
                            }
                        }
                    }
                }
            }
        }

        /// <summary>A file name with the placeholder expanded over every variant; other names unchanged.</summary>
        public IEnumerable<string> ExpandPawTheta(string file)
        {
            if (string.IsNullOrEmpty(file) || !file.Contains(PawThetaPlaceholder))
            {
                return new[] { file };
            }
            return EyeRange.PawThetaVariantsDeg.Select(v => ResolvePawTheta(file, v));
        }

        /// <summary>Replace the placeholder with the correction radius (deg), e.g. near_A__paw_t20.mp4.</summary>
        public static string ResolvePawTheta(string file, float thetaDeg)
        {
            return string.IsNullOrEmpty(file) ? file : file.Replace(PawThetaPlaceholder, FormatTheta(thetaDeg));
        }

        /// <summary>Radius as written in file names: 20 -> "20", 12.5 -> "12.5" (as make_trial_plans.format_theta).</summary>
        public static string FormatTheta(float thetaDeg)
        {
            return thetaDeg.ToString("0.###", CultureInfo.InvariantCulture);
        }

        public static StudyPlan Load(string path)
        {
            return FromJson(File.ReadAllText(path));
        }

        public static StudyPlan FromJson(string json)
        {
            Dictionary<string, object> root = MiniJson.AsObject(MiniJson.Parse(json), "plan");
            var plan = new StudyPlan
            {
                PlanFormatVersion = (int)MiniJson.GetLong(root, "plan_format_version"),
                StudyId = MiniJson.GetString(root, "study_id"),
                ConfigVersion = MiniJson.GetString(root, "config_version"),
                ConfigSha256 = MiniJson.GetString(root, "config_sha256"),
                ParticipantId = MiniJson.GetString(root, "participant_id"),
                Seed = MiniJson.GetLong(root, "seed"),
                GeneratedUtc = MiniJson.GetString(root, "generated_utc"),
                MinBreakSeconds = (float)MiniJson.GetNumber(root, "min_break_s", 120.0),
            };
            if (plan.PlanFormatVersion != SupportedFormatVersion)
            {
                throw new FormatException(
                    "plan_format_version " + plan.PlanFormatVersion + " is not supported (expected "
                    + SupportedFormatVersion + "); regenerate the plan with make_trial_plans.py");
            }
            if (string.IsNullOrEmpty(plan.ParticipantId))
            {
                throw new FormatException("plan has no participant_id");
            }

            if (root.TryGetValue("timing", out object timingObj) && timingObj is Dictionary<string, object> timing)
            {
                plan.Timing.ClipSeconds = (float)MiniJson.GetNumber(timing, "clip_s", 10.0);
                plan.Timing.GapBetweenAbSeconds = (float)MiniJson.GetNumber(timing, "gap_between_ab_s", 1.5);
                plan.Timing.GrayBetweenTrialsSeconds = (float)MiniJson.GetNumber(timing, "gray_between_trials_s", 2.0);
                plan.Timing.OrientationCueSeconds = (float)MiniJson.GetNumber(timing, "orientation_cue_s", 2.0);
            }

            foreach (object step in MiniJson.GetList(root, "flow"))
            {
                string raw = Convert.ToString(step, CultureInfo.InvariantCulture);
                plan.FlowRaw.Add(raw);
                plan.Flow.Add(FlowStep.Parse(raw));
            }
            if (plan.Flow.Count == 0)
            {
                throw new FormatException("plan has an empty flow");
            }

            if (root.TryGetValue("stereotest", out object stObj) && stObj is Dictionary<string, object> st)
            {
                plan.StereoTest.Enabled = MiniJson.GetBool(st, "enabled");
                plan.StereoTest.Shapes = MiniJson.GetList(st, "shapes").Select(o => Convert.ToString(o, CultureInfo.InvariantCulture)).ToList();
                plan.StereoTest.QuadDistanceM = (float)MiniJson.GetNumber(st, "quad_distance_m", 2.0);
                plan.StereoTest.QuadSizeDeg = (float)MiniJson.GetNumber(st, "quad_size_deg", 20.0);
                plan.StereoTest.DotPx = Math.Max(1, (int)MiniJson.GetLong(st, "dot_px", 3));
                if (st.TryGetValue("display_fallback", out object dfObj) && dfObj is Dictionary<string, object> df)
                {
                    plan.StereoTest.FallbackEyeWidthPx = (int)MiniJson.GetLong(df, "eye_width_px", plan.StereoTest.FallbackEyeWidthPx);
                    plan.StereoTest.FallbackHfovDeg = (float)MiniJson.GetNumber(df, "hfov_deg", plan.StereoTest.FallbackHfovDeg);
                }
                foreach (object o in MiniJson.GetList(st, "trials"))
                {
                    Dictionary<string, object> t = MiniJson.AsObject(o, "stereotest trial");
                    plan.StereoTest.Trials.Add(new StereoTrial
                    {
                        Index = (int)MiniJson.GetLong(t, "index"),
                        Block = MiniJson.GetString(t, "block", "main"),
                        Shape = MiniJson.GetString(t, "shape"),
                        DisparityPx = (int)MiniJson.GetLong(t, "disparity_px"),
                        Sign = MiniJson.GetString(t, "sign", "crossed"),
                    });
                }
            }

            if (root.TryGetValue("alignment", out object alObj) && alObj is Dictionary<string, object> al)
            {
                plan.Alignment.ToleranceDeg = (float)MiniJson.GetNumber(al, "tolerance_deg", plan.Alignment.ToleranceDeg);
                plan.Alignment.DwellSeconds = (float)MiniJson.GetNumber(al, "dwell_s", plan.Alignment.DwellSeconds);
                if (plan.Alignment.ToleranceDeg <= 0f || plan.Alignment.DwellSeconds < 0f)
                {
                    throw new FormatException("alignment.tolerance_deg must be positive and dwell_s not negative");
                }
            }

            if (root.TryGetValue("eye_range", out object erObj) && erObj is Dictionary<string, object> er)
            {
                plan.EyeRange = EyeRangePlan.FromJson(er);
            }

            if (root.TryGetValue("gaze_training", out object gtObj) && gtObj is Dictionary<string, object> gt)
            {
                GazeTrainingPlan g = plan.GazeTraining;
                g.Enabled = MiniJson.GetBool(gt, "enabled", true);
                g.DotCount = (int)MiniJson.GetLong(gt, "n_dots", 8);
                g.RadiusDeg = (float)MiniJson.GetNumber(gt, "radius_deg", 15.0);
                g.DwellSeconds = (float)MiniJson.GetNumber(gt, "dwell_s", 2.0);
                g.DotSizeDeg = (float)MiniJson.GetNumber(gt, "dot_size_deg", 1.2);
                g.MaxHeadDeviationDeg = (float)MiniJson.GetNumber(gt, "max_head_deviation_deg", 5.0);
                g.MaxAttempts = Math.Max(1, (int)MiniJson.GetLong(gt, "max_attempts", 3));
                g.ReferenceYawDeg = (float)MiniJson.GetNumber(gt, "reference_yaw_deg");
                g.ReferencePitchDeg = (float)MiniJson.GetNumber(gt, "reference_pitch_deg");
                foreach (object o in MiniJson.GetList(gt, "orders"))
                {
                    if (o is List<object> order)
                    {
                        g.Orders.Add(order.Select(x => Convert.ToInt32(x, CultureInfo.InvariantCulture)).ToList());
                    }
                }
                if (g.DotCount < 1)
                {
                    throw new FormatException("gaze_training.n_dots must be at least 1");
                }
                foreach (List<int> order in g.Orders)
                {
                    if (order.Count != g.DotCount || order.Distinct().Count() != g.DotCount || order.Any(k => k < 0 || k >= g.DotCount))
                    {
                        throw new FormatException("gaze_training.orders must be permutations of 0.." + (g.DotCount - 1));
                    }
                }
            }

            foreach (object o in MiniJson.GetList(root, "parts"))
            {
                Dictionary<string, object> p = MiniJson.AsObject(o, "part");
                var part = new StudyPart { Number = (int)MiniJson.GetLong(p, "part") };
                if (p.TryGetValue("options", out object optObj) && optObj is Dictionary<string, object> opt)
                {
                    part.Options.SelfPaced = MiniJson.GetBool(opt, "self_paced");
                    part.Options.MinViewSeconds = (float)MiniJson.GetNumber(opt, "min_view_s", 0.0);
                    part.Options.MaxViewSeconds = (float)MiniJson.GetNumber(opt, "max_view_s", 0.0);
                    part.Options.AllowReviewOnRerate = MiniJson.GetBool(opt, "allow_review_on_rerate");
                }
                foreach (object to in MiniJson.GetList(p, "trials"))
                {
                    Dictionary<string, object> t = MiniJson.AsObject(to, "trial");
                    part.Trials.Add(new StudyTrial
                    {
                        Index = (int)MiniJson.GetLong(t, "index"),
                        Block = MiniJson.GetString(t, "block", "main"),
                        ClipId = MiniJson.GetString(t, "clip_id"),
                        Condition = MiniJson.GetString(t, "condition"),
                        Cell = MiniJson.GetString(t, "cell"),
                        Scene = MiniJson.GetString(t, "scene"),
                        Repetition = (int)MiniJson.GetLong(t, "repetition"),
                        File = MiniJson.GetString(t, "file"),
                        CondA = MiniJson.GetString(t, "cond_a"),
                        CondB = MiniJson.GetString(t, "cond_b"),
                        PairType = MiniJson.GetString(t, "pair_type"),
                        FileA = MiniJson.GetString(t, "file_a"),
                        FileB = MiniJson.GetString(t, "file_b"),
                        ReferenceYawDeg = (float)MiniJson.GetNumber(t, "reference_yaw_deg"),
                        ReferencePitchDeg = (float)MiniJson.GetNumber(t, "reference_pitch_deg"),
                        GroupIndex = (int)MiniJson.GetLong(t, "group_index"),
                        GroupSize = (int)MiniJson.GetLong(t, "group_size"),
                        PositionInGroup = (int)MiniJson.GetLong(t, "position_in_group"),
                    });
                }
                part.ValidateGroups();
                plan.Parts.Add(part);
            }

            foreach (int n in plan.PartOrder)
            {
                if (plan.GetPart(n) == null)
                {
                    throw new FormatException("flow refers to part " + n + " but the plan has no such part");
                }
            }
            if (plan.Flow.Any(s => s.Kind == FlowStepKind.StereoTest) && plan.StereoTest.Enabled && plan.StereoTest.Trials.Count == 0)
            {
                throw new FormatException("stereo test is enabled but has no trials");
            }
            if (plan.Flow.Any(s => s.Kind == FlowStepKind.GazeTraining) && !root.ContainsKey("gaze_training"))
            {
                throw new FormatException("flow has gazetraining but the plan has no gaze_training block");
            }
            int eyeStep = plan.Flow.FindIndex(s => s.Kind == FlowStepKind.EyeRange);
            if (eyeStep >= 0 && !plan.EyeRange.Present)
            {
                throw new FormatException("flow has eyerange but the plan has no eye_range block");
            }
            int part2Step = plan.Flow.FindIndex(s => s.Kind == FlowStepKind.Part && s.PartNumber == 2);
            if (eyeStep >= 0 && part2Step >= 0 && eyeStep > part2Step)
            {
                throw new FormatException("eyerange must come before part:2 (it chooses the PAw correction radius)");
            }
            bool placeholder = plan.Parts.SelectMany(p => p.Trials)
                .Any(t => new[] { t.File, t.FileA, t.FileB }.Any(f => !string.IsNullOrEmpty(f) && f.Contains(PawThetaPlaceholder)));
            if (placeholder && !plan.EyeRange.Present)
            {
                throw new FormatException("stimulus names use " + PawThetaPlaceholder + " but the plan has no eye_range block");
            }
            return plan;
        }
    }

    /// <summary>One outward sweep of the natural viewing range measurement.</summary>
    public sealed class EyeRangeSweep
    {
        public int Index;
        /// <summary>Direction of the sweep around the reference, degrees clockwise from the top as seen by the viewer.</summary>
        public float DirectionDeg;
        /// <summary>Repetition of this direction (0-based).</summary>
        public int Repeat;
    }

    /// <summary>
    /// Natural viewing range measurement (user decisions 11-20), before the gaze training. Continuous outward sweeps
    /// (option A, user 11-20; natural viewing is a continuous search): the head is aligned to the central cross, then
    /// a dot starts at the cross and moves outward along one direction at a constant angular speed up to the maximum
    /// eccentricity, and the participant follows it naturally with the gaze. The head rotation since the sweep onset
    /// is recorded on every frame; the dot eccentricity at which it first exceeds the threshold is the head onset
    /// eccentricity of that sweep. a (the eye-only range) is the median over the sweeps and chooses the pose-aware
    /// (PAw) correction radius used in Part 2. Rule, threshold, speed and range are provisional (to be fixed after the
    /// pilot); the raw frames are kept in eyerange_trace.csv. The discrete mode of the first 11-20 version is removed.
    /// </summary>
    public sealed class EyeRangePlan
    {
        public const string ModeContinuous = "continuous";
        public const string RuleMedianOnsetEcc = "median_onset_ecc";
        /// <summary>Measure (user 11-20): great-circle head rotation since the sweep onset.</summary>
        public const string MeasureHeadRot = "head_rot_deg";

        /// <summary>The plan has an eye_range block.</summary>
        public bool Present;
        public bool Enabled;
        public string Mode = ModeContinuous;
        public string Rule = RuleMedianOnsetEcc;
        public string Measure = MeasureHeadRot;
        public List<float> DirectionsDeg = new List<float>();
        public int Repeats = 2;
        public float SpeedDegPerSecond = 8f;
        public float MaxEccentricityDeg = 40f;
        public float HeadThresholdDeg = 3f;
        /// <summary>End a sweep as soon as the head rotation exceeds the threshold (user 11-20); censored sweeps run to the maximum.</summary>
        public bool StopAtOnset = true;
        public float DotSizeDeg = 1.5f;
        public float AlignDwellSeconds = 0.5f;
        public float GapSeconds = 0.3f;
        public float ReferenceYawDeg;
        public float ReferencePitchDeg;
        public List<float> PawThetaVariantsDeg = new List<float> { 10f, 15f, 20f, 25f, 30f };
        /// <summary>Paper default; used when the step is disabled, not in the flow or skipped.</summary>
        public float DefaultPawThetaDeg = 15f;
        public List<EyeRangeSweep> Sweeps = new List<EyeRangeSweep>();

        /// <summary>Duration of one sweep from the cross to the maximum eccentricity (s).</summary>
        public float SweepSeconds => SpeedDegPerSecond > 0f ? MaxEccentricityDeg / SpeedDegPerSecond : 0f;

        public static EyeRangePlan FromJson(Dictionary<string, object> e)
        {
            var p = new EyeRangePlan
            {
                Present = true,
                Enabled = MiniJson.GetBool(e, "enabled", true),
                Mode = MiniJson.GetString(e, "mode", ModeContinuous),
                Rule = MiniJson.GetString(e, "rule", RuleMedianOnsetEcc),
                Measure = MiniJson.GetString(e, "measure", MeasureHeadRot),
                Repeats = (int)MiniJson.GetLong(e, "repeats", 2),
                SpeedDegPerSecond = (float)MiniJson.GetNumber(e, "speed_deg_s", 8.0),
                MaxEccentricityDeg = (float)MiniJson.GetNumber(e, "max_eccentricity_deg", 40.0),
                HeadThresholdDeg = (float)MiniJson.GetNumber(e, "head_threshold_deg", 3.0),
                StopAtOnset = MiniJson.GetBool(e, "stop_at_onset", true),
                DotSizeDeg = (float)MiniJson.GetNumber(e, "dot_size_deg", 1.5),
                AlignDwellSeconds = (float)MiniJson.GetNumber(e, "align_dwell_s", 0.5),
                GapSeconds = (float)MiniJson.GetNumber(e, "gap_s", 0.3),
                ReferenceYawDeg = (float)MiniJson.GetNumber(e, "reference_yaw_deg"),
                ReferencePitchDeg = (float)MiniJson.GetNumber(e, "reference_pitch_deg"),
                DefaultPawThetaDeg = (float)MiniJson.GetNumber(e, "default_paw_theta_deg", 15.0),
            };
            p.DirectionsDeg = Floats(MiniJson.GetList(e, "directions_deg"));
            if (e.ContainsKey("paw_theta_variants_deg"))
            {
                p.PawThetaVariantsDeg = Floats(MiniJson.GetList(e, "paw_theta_variants_deg"));
            }
            foreach (object o in MiniJson.GetList(e, "sweeps"))
            {
                Dictionary<string, object> t = MiniJson.AsObject(o, "eye_range sweep");
                p.Sweeps.Add(new EyeRangeSweep
                {
                    Index = (int)MiniJson.GetLong(t, "index"),
                    DirectionDeg = (float)MiniJson.GetNumber(t, "direction_deg"),
                    Repeat = (int)MiniJson.GetLong(t, "repeat"),
                });
            }
            if (p.Mode != ModeContinuous)
            {
                throw new FormatException("eye_range.mode '" + p.Mode + "' is not supported (only " + ModeContinuous
                    + "; the discrete mode was removed on 11-20); regenerate the plan with make_trial_plans.py");
            }
            if (p.Rule != RuleMedianOnsetEcc || p.Measure != MeasureHeadRot)
            {
                throw new FormatException("eye_range: rule must be " + RuleMedianOnsetEcc + " and measure " + MeasureHeadRot);
            }
            if (p.PawThetaVariantsDeg.Count == 0 || p.PawThetaVariantsDeg.Any(v => v <= 0f) || !p.PawThetaVariantsDeg.Contains(p.DefaultPawThetaDeg))
            {
                throw new FormatException("eye_range: paw_theta_variants_deg must be positive and contain default_paw_theta_deg");
            }
            if (p.SpeedDegPerSecond <= 0f || p.MaxEccentricityDeg <= 0f || p.HeadThresholdDeg < 0f || p.Repeats < 1)
            {
                throw new FormatException("eye_range: speed_deg_s and max_eccentricity_deg must be positive, repeats at least 1, head_threshold_deg not negative");
            }
            if (p.Enabled && p.Sweeps.Count == 0)
            {
                throw new FormatException("eye_range is enabled but has no sweeps");
            }
            if (p.Sweeps.Any(t => !p.DirectionsDeg.Contains(t.DirectionDeg)))
            {
                throw new FormatException("eye_range: a sweep direction is not in directions_deg");
            }
            return p;
        }

        private static List<float> Floats(List<object> list)
        {
            return list.Select(x => Convert.ToSingle(x, CultureInfo.InvariantCulture)).ToList();
        }

        /// <summary>Median of the values (NaN when empty).</summary>
        public static double Median(IEnumerable<double> values)
        {
            List<double> v = values.Where(x => !double.IsNaN(x)).OrderBy(x => x).ToList();
            if (v.Count == 0)
            {
                return double.NaN;
            }
            int m = v.Count / 2;
            return v.Count % 2 == 1 ? v[m] : 0.5 * (v[m - 1] + v[m]);
        }

        /// <summary>
        /// Head onset eccentricity of one sweep: the dot eccentricity of the first frame whose head rotation since
        /// the sweep onset exceeds the threshold. censored = true (and NaN) when it never does.
        /// </summary>
        public static float OnsetEccentricity(IList<float> dotEccDeg, IList<float> headRotDeg, float thresholdDeg, out bool censored)
        {
            int n = Math.Min(dotEccDeg.Count, headRotDeg.Count);
            for (int i = 0; i < n; i++)
            {
                if (headRotDeg[i] > thresholdDeg)
                {
                    censored = false;
                    return dotEccDeg[i];
                }
            }
            censored = true;
            return float.NaN;
        }

        /// <summary>
        /// Provisional rule "median_onset_ecc" (user 11-20): a is the median head onset eccentricity over the
        /// sweeps; a censored sweep (the head never passed the threshold) counts as the maximum eccentricity.
        /// </summary>
        public static float NaturalRangeDeg(IEnumerable<KeyValuePair<float, bool>> onsetAndCensored, float maxEccentricityDeg)
        {
            double m = Median(onsetAndCensored.Select(kv => kv.Value ? (double)maxEccentricityDeg : kv.Key));
            return double.IsNaN(m) ? float.NaN : (float)m;
        }

        /// <summary>The variant nearest to a (clamped to the variant range: a = 40 gives 30); ties go to the smaller radius.</summary>
        public static float NearestVariant(IEnumerable<float> variants, float aDeg)
        {
            float best = float.NaN;
            foreach (float v in variants.OrderBy(x => x))
            {
                if (float.IsNaN(best) || Math.Abs(v - aDeg) < Math.Abs(best - aDeg) - 1e-4f)
                {
                    best = v;
                }
            }
            return best;
        }
    }

    public sealed class StudyTiming
    {
        public float ClipSeconds = 10f;
        public float GapBetweenAbSeconds = 1.5f;
        public float GrayBetweenTrialsSeconds = 2f;
        public float OrientationCueSeconds = 2f;
    }

    public sealed class StereoTestPlan
    {
        public bool Enabled;
        public List<string> Shapes = new List<string>();
        public float QuadDistanceM = 2f;
        public float QuadSizeDeg = 20f;
        public int DotPx = 3;
        /// <summary>Used when no XR eye texture is available (pending: check against the HMD specification).</summary>
        public int FallbackEyeWidthPx = 2160;
        public float FallbackHfovDeg = 105f;
        public List<StereoTrial> Trials = new List<StereoTrial>();
    }

    public sealed class StereoTrial
    {
        public int Index;
        public string Block = "main";
        public string Shape = "";
        public int DisparityPx;
        public string Sign = "crossed";

        public bool Crossed => !string.Equals(Sign, "uncrossed", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Gaze training before Part 2: yellow cross at the reference direction and a ring of coloured dots;
    /// the participant fixates each highlighted dot without turning the head. The deviation threshold and
    /// the number of attempts come from the config (pending user decision).
    /// </summary>
    public sealed class GazeTrainingPlan
    {
        public bool Enabled;
        public int DotCount = 8;
        public float RadiusDeg = 15f;
        public float DwellSeconds = 2f;
        public float DotSizeDeg = 1.2f;
        public float MaxHeadDeviationDeg = 5f;
        public int MaxAttempts = 3;
        public float ReferenceYawDeg;
        public float ReferencePitchDeg;
        public List<List<int>> Orders = new List<List<int>>();

        /// <summary>Dot order for an attempt (0-based); identity order when the plan has none.</summary>
        public List<int> OrderFor(int attempt)
        {
            if (Orders.Count == 0)
            {
                return Enumerable.Range(0, DotCount).ToList();
            }
            return Orders[attempt % Orders.Count];
        }

        /// <summary>Angle of dot k on the ring, degrees clockwise from the top as seen by the viewer.</summary>
        public float DotAngleDeg(int k)
        {
            return 360f * k / DotCount;
        }
    }

    /// <summary>
    /// Orientation alignment before Part 2 trials and the gaze training: playback starts automatically once
    /// the head has stayed within ToleranceDeg of the reference direction for DwellSeconds.
    /// </summary>
    public sealed class AlignmentPlan
    {
        public float ToleranceDeg = 5f;
        public float DwellSeconds = 1f;
    }

    /// <summary>Viewing options of one part (from the config; several defaults are pending user decision).</summary>
    public sealed class StudyPartOptions
    {
        /// <summary>Clips loop after one full play until the participant advances.</summary>
        public bool SelfPaced;
        /// <summary>Earliest advance in seconds; 0 or less = one full play of the clip.</summary>
        public float MinViewSeconds;
        /// <summary>Automatic advance in seconds; 0 or less = no limit.</summary>
        public float MaxViewSeconds;
        /// <summary>Part 1: the group summary offers a replay of each clip.</summary>
        public bool AllowReviewOnRerate;
    }

    public sealed class StudyPart
    {
        public int Number;
        public StudyPartOptions Options = new StudyPartOptions();
        public List<StudyTrial> Trials = new List<StudyTrial>();

        /// <summary>Trials outside groups (group_index 0), in plan order.</summary>
        public List<StudyTrial> UngroupedTrials => Trials.Where(t => t.GroupIndex <= 0).ToList();

        /// <summary>Consecutive groups (group_index &gt; 0) in plan order.</summary>
        public List<List<StudyTrial>> Groups()
        {
            var groups = new List<List<StudyTrial>>();
            foreach (StudyTrial t in Trials)
            {
                if (t.GroupIndex <= 0)
                {
                    continue;
                }
                if (groups.Count == 0 || groups[groups.Count - 1][0].GroupIndex != t.GroupIndex)
                {
                    groups.Add(new List<StudyTrial>());
                }
                groups[groups.Count - 1].Add(t);
            }
            return groups;
        }

        /// <summary>Groups must be contiguous, hold one clip each and list positions 0..size-1 in order.</summary>
        public void ValidateGroups()
        {
            List<List<StudyTrial>> groups = Groups();
            if (groups.Select(g => g[0].GroupIndex).Distinct().Count() != groups.Count)
            {
                throw new FormatException("part " + Number + ": a group is split into several runs");
            }
            bool seenGroup = false;
            foreach (StudyTrial t in Trials)
            {
                if (t.GroupIndex > 0)
                {
                    seenGroup = true;
                }
                else if (seenGroup)
                {
                    throw new FormatException("part " + Number + ": ungrouped trial after the first group");
                }
            }
            foreach (List<StudyTrial> g in groups)
            {
                if (g.Select(t => t.ClipId + "#" + t.Repetition).Distinct().Count() != 1)
                {
                    throw new FormatException("part " + Number + ", group " + g[0].GroupIndex + ": more than one clip");
                }
                for (int i = 0; i < g.Count; i++)
                {
                    if (g[i].PositionInGroup != i || (g[i].GroupSize > 0 && g[i].GroupSize != g.Count))
                    {
                        throw new FormatException("part " + Number + ", group " + g[0].GroupIndex + ": positions or size do not match");
                    }
                }
            }
        }
    }

    public sealed class StudyTrial
    {
        public int Index;
        public string Block = "main";
        public string ClipId = "";
        public string Condition = "";
        public string Cell = "";
        public string Scene = "";
        public int Repetition;
        public string File = "";
        public string CondA = "";
        public string CondB = "";
        public string PairType = "";
        public string FileA = "";
        public string FileB = "";
        public float ReferenceYawDeg;
        public float ReferencePitchDeg;
        public int GroupIndex;
        public int GroupSize;
        public int PositionInGroup;

        public bool IsMain => Block == "main";
    }

    public enum FlowStepKind
    {
        Checkin,
        Ssq,
        HmdFit,
        StereoTest,
        EyeRange,
        GazeTraining,
        Part,
        BreakSsq,
        Break,
        PostQuestionnaire,
        End,
    }

    /// <summary>One entry of the plan flow, e.g. "ssq:S_pre", "part:1", "ssq:S_mid1", "break", "eyerange", "gazetraining", "part:2".</summary>
    public readonly struct FlowStep
    {
        public readonly FlowStepKind Kind;
        public readonly string Argument;

        public FlowStep(FlowStepKind kind, string argument)
        {
            Kind = kind;
            Argument = argument ?? "";
        }

        public int PartNumber => Kind == FlowStepKind.Part ? int.Parse(Argument, CultureInfo.InvariantCulture) : 0;

        public static FlowStep Parse(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                throw new FormatException("empty flow step");
            }
            string name = raw.Trim();
            string arg = "";
            int colon = name.IndexOf(':');
            if (colon >= 0)
            {
                arg = name.Substring(colon + 1).Trim();
                name = name.Substring(0, colon).Trim();
            }
            switch (name)
            {
                case "checkin": return new FlowStep(FlowStepKind.Checkin, arg);
                case "ssq": return new FlowStep(FlowStepKind.Ssq, RequireArg(raw, arg));
                case "hmd_fit": return new FlowStep(FlowStepKind.HmdFit, arg);
                case "stereotest": return new FlowStep(FlowStepKind.StereoTest, arg);
                case "eyerange": return new FlowStep(FlowStepKind.EyeRange, arg);
                case "gazetraining": return new FlowStep(FlowStepKind.GazeTraining, arg);
                case "part":
                    if (!int.TryParse(RequireArg(raw, arg), NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) || (n != 1 && n != 2))
                    {
                        throw new FormatException("flow step '" + raw + "' must be part:1 or part:2");
                    }
                    return new FlowStep(FlowStepKind.Part, arg);
                case "break_ssq": return new FlowStep(FlowStepKind.BreakSsq, RequireArg(raw, arg));
                case "break": return new FlowStep(FlowStepKind.Break, arg);
                case "post_questionnaire": return new FlowStep(FlowStepKind.PostQuestionnaire, arg);
                case "end": return new FlowStep(FlowStepKind.End, arg);
                default: throw new FormatException("unknown flow step '" + raw + "'");
            }
        }

        private static string RequireArg(string raw, string arg)
        {
            if (string.IsNullOrEmpty(arg))
            {
                throw new FormatException("flow step '" + raw + "' needs an argument");
            }
            return arg;
        }

        public override string ToString()
        {
            return string.IsNullOrEmpty(Argument) ? Kind.ToString() : Kind + ":" + Argument;
        }
    }
}
