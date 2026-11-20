using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace Pano2StereoVR.Study
{
    /// <summary>Column order of every CSV file, matching StudyTools/README.md (data contract v6).</summary>
    public static class StudySchema
    {
        public const int ContractVersion = 6;

        public static readonly string[] SsqItems =
            { "GD", "FA", "HE", "ES", "DF", "IS", "SW", "NA", "CO", "FH", "BV", "DO", "DC", "VE", "SA", "BU" };

        public static readonly string[] StereoTest =
        {
            "participant_id", "trial_index", "block", "shape", "disparity_px", "disparity_arcsec", "arcsec_per_px",
            "px_source", "response", "correct", "rt_s", "skipped",
        };

        public static readonly string[] Part1 =
        {
            "participant_id", "block", "trial_index", "group_index", "position_in_group", "clip_id", "condition", "repetition",
            "depth_quality", "visual_comfort", "artifact_annoyance", "rt_depth_s", "rt_comfort_s", "rt_artifact_s",
            "initial_depth", "initial_comfort", "initial_artifact", "revisions", "view_s", "loops", "replays", "skipped", "skip_reason",
        };

        public static readonly string[] Part1Revisions =
        {
            "participant_id", "group_index", "trial_index", "clip_id", "condition", "event", "revision", "reviewed",
            "depth_quality", "visual_comfort", "artifact_annoyance", "t_utc",
        };

        public static readonly string[] Part1Replays =
        {
            "participant_id", "group_index", "position_in_group", "trial_index", "clip_id", "condition", "replay",
            "t_start_utc", "view_s", "loops", "completed",
        };

        public static readonly string[] Part2 =
        {
            "participant_id", "block", "trial_index", "clip_id", "cond_a", "cond_b", "pair_type",
            "different", "preference", "rt_q1_s", "rt_q2_s", "view_a_s", "view_b_s", "loops_a", "loops_b",
            "max_dev_a_deg", "max_dev_b_deg", "align_s", "start_dev_deg", "restarts_a", "restarts_b", "reminder_off_a", "reminder_off_b",
            "paw_theta_deg", "skipped", "skip_reason",
        };

        /// <summary>
        /// Natural viewing range measurement (contract v6, continuous sweeps since 11-20): one row per sweep.
        /// head_onset_ecc_deg is the dot eccentricity when the head rotation since the sweep onset first exceeded the
        /// threshold (empty and censored = 1 when it never did); stopped_early = 1 when the sweep ended right there
        /// (user 11-20); onset_dev_deg is the head deviation from the reference at the sweep onset.
        /// </summary>
        public static readonly string[] EyeRange =
        {
            "participant_id", "sweep", "direction_deg", "repeat", "speed_deg_s", "onset_dev_deg", "head_onset_ecc_deg", "censored",
            "stopped_early", "max_head_rot_deg", "skipped",
        };

        /// <summary>Natural viewing range: every displayed frame of every sweep (head yaw and pitch relative to the reference).</summary>
        public static readonly string[] EyeRangeTrace =
        {
            "participant_id", "sweep", "direction_deg", "repeat", "t_s", "dot_ecc_deg", "head_yaw_deg", "head_pitch_deg", "head_rot_deg",
            "head_rot_along_deg",
        };

        public static readonly string[] GazeTraining =
        {
            "participant_id", "attempt", "position", "dot_id", "dot_angle_deg", "dwell_s", "max_dev_deg",
            "attempt_max_dev_deg", "threshold_deg", "attempt_passed",
        };

        public static readonly string[] Ssq = new[] { "participant_id", "measurement" }.Concat(SsqItems).ToArray();

        public static readonly string[] HeadPose =
            { "participant_id", "part", "trial_index", "segment", "t_s", "video_frame", "yaw_deg", "pitch_deg", "roll_deg" };

        public static readonly string[] Playback =
        {
            "participant_id", "part", "trial_index", "segment", "file", "frames_expected", "frames_shown",
            "frames_dropped", "dt_mean_ms", "dt_p99_ms",
        };

        /// <summary>Great-circle angle (deg) between the head direction (yaw, pitch relative to a reference) and the reference.</summary>
        public static double AngleFromReference(double yawDeg, double pitchDeg)
        {
            double y = yawDeg * Math.PI / 180.0;
            double p = pitchDeg * Math.PI / 180.0;
            double c = Math.Cos(p) * Math.Cos(y);
            return Math.Acos(Math.Max(-1.0, Math.Min(1.0, c))) * 180.0 / Math.PI;
        }

        /// <summary>
        /// Head rotation along a target direction (deg): the head offset from the reference, as an azimuthal-equidistant
        /// vector (length = great-circle angle, azimuth clockwise from the top as seen by the viewer), projected on the
        /// direction directionDeg (0 = up, 90 = right). Positive = toward the target, negative = away from it.
        /// </summary>
        public static double HeadAlongDirection(double yawDeg, double pitchDeg, double directionDeg)
        {
            return RotationAlongDirection(0.0, 0.0, yawDeg, pitchDeg, directionDeg);
        }

        /// <summary>Unit direction (x right, y up, z forward) for a yaw and pitch relative to the reference.</summary>
        private static void Direction(double yawDeg, double pitchDeg, out double x, out double y, out double z)
        {
            double yr = yawDeg * Math.PI / 180.0;
            double pr = pitchDeg * Math.PI / 180.0;
            x = Math.Cos(pr) * Math.Sin(yr);
            y = Math.Sin(pr);
            z = Math.Cos(pr) * Math.Cos(yr);
        }

        /// <summary>Great-circle angle (deg) between two head directions given as yaw, pitch relative to the reference.</summary>
        public static double GreatCircleDeg(double yaw0Deg, double pitch0Deg, double yaw1Deg, double pitch1Deg)
        {
            Direction(yaw0Deg, pitch0Deg, out double x0, out double y0, out double z0);
            Direction(yaw1Deg, pitch1Deg, out double x1, out double y1, out double z1);
            double c = x0 * x1 + y0 * y1 + z0 * z1;
            double cx = y0 * z1 - z0 * y1, cy = z0 * x1 - x0 * z1, cz = x0 * y1 - y0 * x1;
            // atan2 of |cross| and dot stays accurate for small angles (acos does not)
            return Math.Atan2(Math.Sqrt(cx * cx + cy * cy + cz * cz), c) * 180.0 / Math.PI;
        }

        /// <summary>
        /// Head rotation from direction 0 to direction 1 (yaw, pitch relative to the reference), as an
        /// azimuthal-equidistant vector around direction 0 (length = great-circle angle, azimuth clockwise from the top
        /// as seen by the viewer), projected on directionDeg (0 = up, 90 = right). Positive = toward the target.
        /// </summary>
        public static double RotationAlongDirection(double yaw0Deg, double pitch0Deg, double yaw1Deg, double pitch1Deg, double directionDeg)
        {
            double rot = GreatCircleDeg(yaw0Deg, pitch0Deg, yaw1Deg, pitch1Deg);
            if (rot < 1e-9)
            {
                return 0.0;
            }
            Direction(yaw0Deg, pitch0Deg, out double ox, out double oy, out double oz);
            Direction(yaw1Deg, pitch1Deg, out double tx, out double ty, out double tz);
            // local frame at direction 0: right = up x o (horizontal), up' = o x right
            double rx = oz, ry = 0.0, rz = -ox;
            double rn = Math.Sqrt(rx * rx + rz * rz);
            if (rn < 1e-12)
            {
                rx = 1.0;
                rz = 0.0;
                rn = 1.0;
            }
            rx /= rn;
            rz /= rn;
            double ux = oy * rz - oz * ry, uy = oz * rx - ox * rz, uz = ox * ry - oy * rx;
            double right = tx * rx + ty * ry + tz * rz;
            double up = tx * ux + ty * uy + tz * uz;
            double azimuth = Math.Atan2(right, up);
            return rot * Math.Cos(azimuth - directionDeg * Math.PI / 180.0);
        }
    }

    /// <summary>Append-only CSV file (UTF-8 without BOM, invariant culture). The header is written on creation.</summary>
    public sealed class CsvLog
    {
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        public string FilePath { get; }
        public IReadOnlyList<string> Header { get; }

        public CsvLog(string path, IReadOnlyList<string> header)
        {
            FilePath = path;
            Header = header;
            if (!File.Exists(path))
            {
                File.WriteAllText(path, Line(header.Cast<object>().ToArray()), Utf8NoBom);
            }
        }

        public void Append(params object[] values)
        {
            if (values.Length != Header.Count)
            {
                throw new ArgumentException(
                    "row has " + values.Length + " values but " + Path.GetFileName(FilePath) + " has " + Header.Count + " columns");
            }
            File.AppendAllText(FilePath, Line(values), Utf8NoBom);
        }

        public void AppendRows(IEnumerable<object[]> rows)
        {
            var sb = new StringBuilder();
            foreach (object[] r in rows)
            {
                if (r.Length != Header.Count)
                {
                    throw new ArgumentException("row width mismatch in " + Path.GetFileName(FilePath));
                }
                sb.Append(Line(r));
            }
            if (sb.Length > 0)
            {
                File.AppendAllText(FilePath, sb.ToString(), Utf8NoBom);
            }
        }

        public static string Line(object[] values)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < values.Length; i++)
            {
                if (i > 0)
                {
                    sb.Append(',');
                }
                sb.Append(Escape(Format(values[i])));
            }
            sb.Append('\n');
            return sb.ToString();
        }

        public static string Format(object v)
        {
            switch (v)
            {
                case null: return "";
                case string s: return s;
                case bool b: return b ? "1" : "0";
                case float f: return float.IsNaN(f) ? "" : f.ToString("0.######", CultureInfo.InvariantCulture);
                case double d: return double.IsNaN(d) ? "" : d.ToString("0.######", CultureInfo.InvariantCulture);
                case IFormattable fm: return fm.ToString(null, CultureInfo.InvariantCulture);
                default: return v.ToString();
            }
        }

        private static string Escape(string s)
        {
            if (s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0)
            {
                return s;
            }
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        }
    }

    /// <summary>Writes one participant folder. Every trial is written to disk as soon as it ends.</summary>
    public sealed class StudyDataLogger
    {
        public string Directory { get; }
        public string ParticipantId { get; }
        public CsvLog StereoTest { get; }
        public CsvLog Part1 { get; }
        public CsvLog Part1Revisions { get; }
        public CsvLog Part1Replays { get; }
        public CsvLog Part2 { get; }
        public CsvLog GazeTraining { get; }
        public CsvLog EyeRange { get; }
        public CsvLog EyeRangeTrace { get; }
        public CsvLog Ssq { get; }
        public CsvLog HeadPose { get; }
        public CsvLog Playback { get; }

        public StudyDataLogger(string directory, string participantId)
        {
            Directory = directory;
            ParticipantId = participantId;
            System.IO.Directory.CreateDirectory(directory);
            StereoTest = new CsvLog(Path.Combine(directory, "stereotest.csv"), StudySchema.StereoTest);
            Part1 = new CsvLog(Path.Combine(directory, "part1.csv"), StudySchema.Part1);
            Part1Revisions = new CsvLog(Path.Combine(directory, "part1_revisions.csv"), StudySchema.Part1Revisions);
            Part1Replays = new CsvLog(Path.Combine(directory, "part1_replays.csv"), StudySchema.Part1Replays);
            Part2 = new CsvLog(Path.Combine(directory, "part2.csv"), StudySchema.Part2);
            GazeTraining = new CsvLog(Path.Combine(directory, "gazetraining.csv"), StudySchema.GazeTraining);
            EyeRange = new CsvLog(Path.Combine(directory, "eyerange.csv"), StudySchema.EyeRange);
            EyeRangeTrace = new CsvLog(Path.Combine(directory, "eyerange_trace.csv"), StudySchema.EyeRangeTrace);
            Ssq = new CsvLog(Path.Combine(directory, "ssq.csv"), StudySchema.Ssq);
            HeadPose = new CsvLog(Path.Combine(directory, "headpose.csv"), StudySchema.HeadPose);
            Playback = new CsvLog(Path.Combine(directory, "playback.csv"), StudySchema.Playback);
        }

        /// <summary>
        /// Folder for a new session. If the participant already has a folder, a time-stamped sibling is used
        /// so that earlier data are never overwritten.
        /// </summary>
        public static string NewSessionDirectory(string dataRoot, string participantId, DateTime utcNow, out bool existed)
        {
            string dir = Path.Combine(dataRoot, participantId);
            existed = System.IO.Directory.Exists(dir) && System.IO.Directory.EnumerateFileSystemEntries(dir).Any();
            if (existed)
            {
                dir = Path.Combine(dataRoot, participantId + "__" + utcNow.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture));
            }
            return dir;
        }

        public void WriteJson(string fileName, IDictionary<string, object> content)
        {
            string path = Path.Combine(Directory, fileName);
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, MiniJson.Serialize(content), new UTF8Encoding(false));
            if (File.Exists(path))
            {
                File.Delete(path);
            }
            File.Move(tmp, path);
        }

        public void WriteSsq(string measurement, IReadOnlyList<int> scores)
        {
            if (scores.Count != StudySchema.SsqItems.Length)
            {
                throw new ArgumentException("SSQ needs " + StudySchema.SsqItems.Length + " scores");
            }
            var row = new object[2 + scores.Count];
            row[0] = ParticipantId;
            row[1] = measurement;
            for (int i = 0; i < scores.Count; i++)
            {
                row[2 + i] = scores[i];
            }
            Ssq.Append(row);
        }

        /// <summary>Copy the participant folder to the backup root. Returns the backup path.</summary>
        public string Backup(string backupRoot, DateTime utcNow)
        {
            if (string.IsNullOrEmpty(backupRoot))
            {
                return "";
            }
            string target = Path.Combine(backupRoot,
                Path.GetFileName(Directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
                + "__backup_" + utcNow.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture));
            System.IO.Directory.CreateDirectory(target);
            foreach (string f in System.IO.Directory.GetFiles(Directory))
            {
                File.Copy(f, Path.Combine(target, Path.GetFileName(f)), true);
            }
            return target;
        }

        /// <summary>SHA-256 of a file's bytes (lower-case hex), or "" when the file cannot be read.</summary>
        public static string FileSha256(string path)
        {
            try
            {
                using (var sha = System.Security.Cryptography.SHA256.Create())
                using (FileStream fs = File.OpenRead(path))
                {
                    byte[] h = sha.ComputeHash(fs);
                    return string.Concat(h.Select(b => b.ToString("x2", CultureInfo.InvariantCulture)));
                }
            }
            catch (Exception)
            {
                return "";
            }
        }

        /// <summary>
        /// Full path of a stimulus named relative to the stimuli folder. Plans use forward slashes for the subfolders
        /// (e.g. "part1/practice/train_indoor__ours.mp4", user 11-20); they are turned into the platform separator.
        /// </summary>
        public static string StimulusFullPath(string stimuliDir, string file)
        {
            if (string.IsNullOrEmpty(file))
            {
                return "";
            }
            string rel = file.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
            return Path.GetFullPath(Path.Combine(stimuliDir, rel));
        }

        /// <summary>
        /// Name of a stimulus relative to the stimuli folder with forward slashes (as in the plan), for the logs;
        /// the file name alone when the path is outside the folder.
        /// </summary>
        public static string RelativeStimulusName(string stimuliDir, string fullPath)
        {
            if (string.IsNullOrEmpty(fullPath))
            {
                return "";
            }
            string root = string.IsNullOrEmpty(stimuliDir) ? "" : Path.GetFullPath(stimuliDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            string full = Path.GetFullPath(fullPath);
            if (root.Length > 1 && full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                return full.Substring(root.Length).Replace('\\', '/');
            }
            return Path.GetFileName(full);
        }

        /// <summary>Stimulus files of a plan that do not exist under the stimuli folder (names as in the plan).</summary>
        public static List<string> MissingStimuli(string stimuliDir, IEnumerable<string> files)
        {
            return files.Where(f => !File.Exists(StimulusFullPath(stimuliDir, f))).OrderBy(x => x, StringComparer.Ordinal).ToList();
        }

        /// <summary>
        /// Hash of stimulus names (relative to the stimuli folder, with their subfolders) and sizes; contents are not
        /// read (4K files are large).
        /// </summary>
        public static string StimulusManifestSha256(string stimuliDir, IEnumerable<string> files, out int missing)
        {
            missing = 0;
            var sb = new StringBuilder();
            foreach (string f in files.OrderBy(x => x, StringComparer.Ordinal))
            {
                string p = StimulusFullPath(stimuliDir, f);
                long size = -1;
                if (File.Exists(p))
                {
                    size = new FileInfo(p).Length;
                }
                else
                {
                    missing++;
                }
                sb.Append(f).Append('\t').Append(size.ToString(CultureInfo.InvariantCulture)).Append('\n');
            }
            using (var sha = System.Security.Cryptography.SHA256.Create())
            {
                byte[] h = sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()));
                return string.Concat(h.Select(b => b.ToString("x2", CultureInfo.InvariantCulture)));
            }
        }
    }
}
