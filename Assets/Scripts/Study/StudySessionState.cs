using System;
using System.Collections.Generic;
using System.Linq;

namespace Pano2StereoVR.Study
{
    /// <summary>Three Part 1 ratings of one clip, with the first answer kept for the revision record.</summary>
    public sealed class RatingRecord
    {
        public StudyTrial Trial;
        public int Depth;
        public int Comfort;
        public int Artifact;
        public int InitialDepth;
        public int InitialComfort;
        public int InitialArtifact;
        public float RtDepth;
        public float RtComfort;
        public float RtArtifact;
        public int Revisions;
        public float ViewSeconds;
        public int Loops;
        /// <summary>Replays from the group summary, in total and since the last rating.</summary>
        public int Replays;
        public int ReplaysSinceRating;
        /// <summary>QA skip: the trial (or its group) was abandoned; excluded from the analysis.</summary>
        public bool Skipped;

        public bool Rated => Depth > 0 && Comfort > 0 && Artifact > 0;

        public void SetInitial(int depth, int comfort, int artifact)
        {
            Depth = InitialDepth = depth;
            Comfort = InitialComfort = comfort;
            Artifact = InitialArtifact = artifact;
            Revisions = 0;
            ReplaysSinceRating = 0;
        }

        public void Revise(int depth, int comfort, int artifact)
        {
            Depth = depth;
            Comfort = comfort;
            Artifact = artifact;
            Revisions++;
            ReplaysSinceRating = 0;
        }

        public bool Changed => Depth != InitialDepth || Comfort != InitialComfort || Artifact != InitialArtifact;
    }

    /// <summary>One attempt of the gaze training.</summary>
    public sealed class GazeAttemptResult
    {
        public int Attempt;
        public List<int> Order = new List<int>();
        public List<float> DotMaxDeg = new List<float>();
        public float MaxDeg;
        public bool Passed;
    }

    /// <summary>Summary of one sweep of the natural viewing range measurement; angles in degrees.</summary>
    public sealed class EyeRangeSweepResult
    {
        public int Sweep;
        public float DirectionDeg;
        public int Repeat;
        public float SpeedDegPerSecond;
        /// <summary>Head deviation from the reference at the sweep onset (residual of the alignment).</summary>
        public float OnsetDevDeg;
        /// <summary>Dot eccentricity when the head rotation since the onset first exceeded the threshold; NaN when censored.</summary>
        public float HeadOnsetEccDeg = float.NaN;
        /// <summary>The head rotation never exceeded the threshold before the maximum eccentricity.</summary>
        public bool Censored;
        /// <summary>The sweep ended at the head onset (stop_at_onset, user 11-20) instead of at the maximum eccentricity.</summary>
        public bool StoppedEarly;
        public float MaxHeadRotDeg;
        public bool Skipped;
    }

    /// <summary>
    /// Everything that belongs to one participant session. "Start next participant" calls <see cref="Reset"/>
    /// so that nothing from the previous session (plan, logger, results, timers, language) carries over.
    /// </summary>
    public sealed class StudySessionState
    {
        public int Generation { get; private set; }

        public string Language = StudyTexts.Chinese;
        public string Mode = StudySettings.ModeFormal;
        public StudyPlan Plan;
        public string PlanPath = "";
        public string PlanSha256 = "";
        public string SessionDir = "";
        public bool SessionDirExisted;
        public StudyDataLogger Log;
        public CsvLog Events;
        public readonly Dictionary<string, object> Session = new Dictionary<string, object>();
        public string Step = "language";
        public string TrialLabel = "";
        public int DroppedTotal;
        public readonly List<string> MissingStimuli = new List<string>();
        public bool Finished;
        public bool EndReady;
        public string BackupPath = "";
        public string BackupError = "";
        public float PreviewEye;
        public readonly List<string> DeferredEvents = new List<string>();
        public readonly List<GazeAttemptResult> GazeAttempts = new List<GazeAttemptResult>();
        public readonly Dictionary<int, RatingRecord> Part1Ratings = new Dictionary<int, RatingRecord>();
        public int StereoTrialsDone;
        public int Part2TrialsDone;
        public int Part1TrialsDone;
        public int Skips;
        public bool Part2ReminderOff;
        public readonly List<EyeRangeSweepResult> EyeRangeSweeps = new List<EyeRangeSweepResult>();
        /// <summary>PAw correction radius (deg) used in Part 2; below 0 until it is chosen.</summary>
        public float PawThetaDeg = -1f;
        /// <summary>"measured", or why the default was used (default_disabled, default_skipped, default_not_in_flow).</summary>
        public string PawThetaSource = "";
        public float SessionStartRealtime = -1f;
        public float BreakStartRealtime = -1f;

        public StudySessionState()
        {
            Reset();
            Generation = 0;
        }

        /// <summary>Clear all per-session state; the generation counter counts sessions in this app run.</summary>
        public void Reset()
        {
            Generation++;
            Language = StudyTexts.Chinese;
            Mode = StudySettings.ModeFormal;
            Plan = null;
            PlanPath = "";
            PlanSha256 = "";
            SessionDir = "";
            SessionDirExisted = false;
            Log = null;
            Events = null;
            Session.Clear();
            Step = "language";
            TrialLabel = "";
            DroppedTotal = 0;
            MissingStimuli.Clear();
            Finished = false;
            EndReady = false;
            BackupPath = "";
            BackupError = "";
            PreviewEye = 0f;
            DeferredEvents.Clear();
            GazeAttempts.Clear();
            Part1Ratings.Clear();
            StereoTrialsDone = 0;
            Part2TrialsDone = 0;
            Part1TrialsDone = 0;
            Skips = 0;
            Part2ReminderOff = false;
            EyeRangeSweeps.Clear();
            PawThetaDeg = -1f;
            PawThetaSource = "";
            SessionStartRealtime = -1f;
            BreakStartRealtime = -1f;
        }

        /// <summary>True when no session data are held (used by the reset test).</summary>
        public bool IsEmpty =>
            Plan == null && Log == null && Events == null && Session.Count == 0 && PlanPath.Length == 0
            && SessionDir.Length == 0 && MissingStimuli.Count == 0 && !Finished && !EndReady && DeferredEvents.Count == 0
            && GazeAttempts.Count == 0 && Part1Ratings.Count == 0 && StereoTrialsDone == 0 && Part2TrialsDone == 0
            && Part1TrialsDone == 0 && Skips == 0 && !Part2ReminderOff && DroppedTotal == 0 && SessionStartRealtime < 0f && BreakStartRealtime < 0f
            && EyeRangeSweeps.Count == 0 && PawThetaDeg < 0f && PawThetaSource.Length == 0
            && Step == "language" && TrialLabel.Length == 0 && BackupPath.Length == 0 && BackupError.Length == 0
            && Language == StudyTexts.Chinese && Mode == StudySettings.ModeFormal;

        /// <summary>Gaze-training summary for session.json.</summary>
        public Dictionary<string, object> GazeSummary()
        {
            return new Dictionary<string, object>
            {
                { "attempts", GazeAttempts.Count },
                { "passed", GazeAttempts.Count > 0 && GazeAttempts[GazeAttempts.Count - 1].Passed },
                { "max_dev_deg_per_attempt", GazeAttempts.Select(a => (object)(double)a.MaxDeg).ToList() },
            };
        }

        /// <summary>
        /// Choose the Part 2 PAw correction radius. source "measured": the provisional rule of the plan (median head
        /// onset eccentricity over the answered sweeps, censored sweeps counted as the maximum eccentricity, user
        /// 11-20) gives a, then the nearest variant; any other source keeps the plan default (15 deg, the paper's
        /// value). Sets <see cref="PawThetaDeg"/> and <see cref="PawThetaSource"/> and returns the "eye_range" block of
        /// session.json (the per-direction medians are listed in every case).
        /// </summary>
        public Dictionary<string, object> ResolveEyeRange(EyeRangePlan plan, string source)
        {
            bool measured = source == "measured";
            List<EyeRangeSweepResult> answered = EyeRangeSweeps.Where(a => !a.Skipped).ToList();
            Func<EyeRangeSweepResult, double> value = r => r.Censored ? plan.MaxEccentricityDeg : r.HeadOnsetEccDeg;
            float aDeg = measured
                ? EyeRangePlan.NaturalRangeDeg(answered.Select(r => new KeyValuePair<float, bool>(r.HeadOnsetEccDeg, r.Censored)), plan.MaxEccentricityDeg)
                : float.NaN;
            PawThetaDeg = measured && !float.IsNaN(aDeg) ? EyeRangePlan.NearestVariant(plan.PawThetaVariantsDeg, aDeg) : plan.DefaultPawThetaDeg;
            PawThetaSource = measured && !float.IsNaN(aDeg) ? source : (measured ? "default_no_data" : source);
            var perDir = new List<object>();
            foreach (float d in plan.DirectionsDeg.Distinct().OrderBy(x => x))
            {
                List<EyeRangeSweepResult> rs = answered.Where(r => r.DirectionDeg == d).ToList();
                double m = EyeRangePlan.Median(rs.Select(value));
                perDir.Add(new Dictionary<string, object>
                {
                    { "direction_deg", (double)d },
                    { "median_head_onset_ecc_deg", double.IsNaN(m) ? null : (object)Math.Round(m, 3) },
                    { "shown", EyeRangeShapes.ShownDegrees(m, plan.MaxEccentricityDeg) },
                    { "n", rs.Count },
                    { "censored", rs.Count(r => r.Censored) },
                });
            }
            return new Dictionary<string, object>
            {
                { "mode", plan.Mode },
                { "a_deg", float.IsNaN(aDeg) ? null : (object)Math.Round(aDeg, 3) },
                { "a_shown", EyeRangeShapes.ShownDegrees(aDeg, plan.MaxEccentricityDeg) },
                { "rule", plan.Rule },
                { "measure", plan.Measure },
                { "speed_deg_s", (double)plan.SpeedDegPerSecond },
                { "max_eccentricity_deg", (double)plan.MaxEccentricityDeg },
                { "stop_at_onset", plan.StopAtOnset },
                { "head_threshold_deg", (double)plan.HeadThresholdDeg },
                { "rule_provisional", true },
                { "per_direction", perDir },
                { "censored", answered.Count(r => r.Censored) },
                { "stopped_early", answered.Count(r => r.StoppedEarly) },
                { "chosen_paw_theta_deg", (double)PawThetaDeg },
                { "paw_theta_source", PawThetaSource },
                { "skipped", PawThetaSource != "measured" },
                { "enabled", plan.Enabled },
                { "default_paw_theta_deg", (double)plan.DefaultPawThetaDeg },
                { "paw_theta_variants_deg", plan.PawThetaVariantsDeg.Select(v => (object)(double)v).ToList() },
                { "n_sweeps", plan.Sweeps.Count },
                { "n_answered", answered.Count },
            };
        }
    }
}
