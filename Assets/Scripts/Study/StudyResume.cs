using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace Pano2StereoVR.Study
{
    /// <summary>A formal session folder of the participant found at check-in, with the flow step to resume from.</summary>
    public sealed class ResumeCandidate
    {
        public string Directory = "";
        public Dictionary<string, object> Session = new Dictionary<string, object>();
        public string Status = "";
        public string StartUtc = "";
        /// <summary>Index in the plan flow of the first step that was not completed (Flow.Count when every step ended).</summary>
        public int StartIndex;
        public string StartStep = "";
        public bool Complete;
    }

    /// <summary>
    /// Continuing an interrupted formal session (user decision 2026-11-23, added after the analysis freeze to handle
    /// program failures). The plan, its trial order and the stimuli are unchanged. A step is completed when events.csv
    /// holds its "step_end"; the session continues at the first step without one, from the start of that step. The
    /// rows the interrupted step (and any later step) had written are moved to a "superseded_*" subfolder of the session
    /// folder, so the files in the folder only hold the steps that are kept. Restarting instead moves the old formal
    /// folders out of the data folder into the backup folder ("replaced"), so they are kept but not analysed.
    /// </summary>
    public static class StudyResume
    {
        public const string ResumeEvent = "resume_from";
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        /// <summary>Folders of the participant under the data folder (the plain ID and ID__suffix) whose session.json has mode "formal".</summary>
        public static List<string> FormalSessionDirs(string dataRoot, string participantId)
        {
            var result = new List<string>();
            if (string.IsNullOrEmpty(dataRoot) || !System.IO.Directory.Exists(dataRoot))
            {
                return result;
            }
            foreach (string d in System.IO.Directory.GetDirectories(dataRoot))
            {
                string name = Path.GetFileName(d);
                if (name != participantId && !name.StartsWith(participantId + "__", StringComparison.Ordinal))
                {
                    continue;
                }
                Dictionary<string, object> s = ReadSession(d);
                if (s != null && MiniJson.GetString(s, "mode", StudySettings.ModeFormal) == StudySettings.ModeFormal)
                {
                    result.Add(d);
                }
            }
            return result;
        }

        /// <summary>session.json of a folder, or null when it is missing or unreadable.</summary>
        public static Dictionary<string, object> ReadSession(string dir)
        {
            string path = Path.Combine(dir, "session.json");
            if (!File.Exists(path))
            {
                return null;
            }
            try
            {
                return MiniJson.AsObject(MiniJson.Parse(File.ReadAllText(path)), "session.json");
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// The latest formal session folder of the participant (by start_utc) with its resume step, or null when there is
        /// none. flowSteps are the plan flow entries as written in events.csv (FlowStep.ToString()).
        /// </summary>
        public static ResumeCandidate Find(string dataRoot, string participantId, IList<string> flowSteps)
        {
            ResumeCandidate best = null;
            foreach (string d in FormalSessionDirs(dataRoot, participantId))
            {
                Dictionary<string, object> s = ReadSession(d);
                string start = MiniJson.GetString(s, "start_utc", "");
                if (best != null && string.CompareOrdinal(start, best.StartUtc) <= 0)
                {
                    continue;
                }
                int idx = FirstIncompleteStep(flowSteps, ReadEvents(Path.Combine(d, "events.csv")));
                string status = MiniJson.GetString(s, "status", "");
                best = new ResumeCandidate
                {
                    Directory = d,
                    Session = s,
                    Status = status,
                    StartUtc = start,
                    StartIndex = idx,
                    StartStep = idx < flowSteps.Count ? flowSteps[idx] : "",
                    Complete = status == "complete" || idx >= flowSteps.Count,
                };
            }
            return best;
        }

        /// <summary>
        /// Index of the first flow step without a "step_end" event. The steps run in flow order, so the events are
        /// matched in order; a "resume_from" event (written when a session is continued) sets the position to its index.
        /// </summary>
        public static int FirstIncompleteStep(IList<string> flowSteps, IEnumerable<KeyValuePair<string, string>> events)
        {
            int idx = 0;
            foreach (KeyValuePair<string, string> e in events)
            {
                if (e.Key == ResumeEvent)
                {
                    if (int.TryParse(e.Value.Split(' ')[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int r)
                        && r >= 0 && r <= flowSteps.Count)
                    {
                        idx = r;
                    }
                }
                else if (e.Key == "step_end" && idx < flowSteps.Count && e.Value == flowSteps[idx])
                {
                    idx++;
                }
            }
            return idx;
        }

        /// <summary>(event, detail) pairs of an events.csv file (empty when the file is missing).</summary>
        public static List<KeyValuePair<string, string>> ReadEvents(string path)
        {
            var list = new List<KeyValuePair<string, string>>();
            List<string[]> rows = ReadCsv(path);
            for (int i = 1; i < rows.Count; i++)
            {
                string[] r = rows[i];
                if (r.Length >= 2)
                {
                    list.Add(new KeyValuePair<string, string>(r[1], r.Length > 2 ? r[2] : ""));
                }
            }
            return list;
        }

        /// <summary>All records of a CSV file written by CsvLog (header first); quoted fields may hold commas, quotes and newlines.</summary>
        public static List<string[]> ReadCsv(string path)
        {
            var rows = new List<string[]>();
            if (!File.Exists(path))
            {
                return rows;
            }
            string text = File.ReadAllText(path, Encoding.UTF8);
            var field = new StringBuilder();
            var row = new List<string>();
            bool quoted = false;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (quoted)
                {
                    if (c == '"')
                    {
                        if (i + 1 < text.Length && text[i + 1] == '"')
                        {
                            field.Append('"');
                            i++;
                        }
                        else
                        {
                            quoted = false;
                        }
                    }
                    else
                    {
                        field.Append(c);
                    }
                }
                else if (c == '"')
                {
                    quoted = true;
                }
                else if (c == ',')
                {
                    row.Add(field.ToString());
                    field.Clear();
                }
                else if (c == '\n' || c == '\r')
                {
                    if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                    {
                        i++;
                    }
                    row.Add(field.ToString());
                    field.Clear();
                    rows.Add(row.ToArray());
                    row.Clear();
                }
                else
                {
                    field.Append(c);
                }
            }
            if (field.Length > 0 || row.Count > 0)
            {
                row.Add(field.ToString());
                rows.Add(row.ToArray());
            }
            return rows;
        }

        /// <summary>Steps that need the headset; a continued session runs the headset fit first when it starts at one of them.</summary>
        public static bool NeedsHeadset(FlowStepKind kind)
        {
            return kind == FlowStepKind.StereoTest || kind == FlowStepKind.EyeRange || kind == FlowStepKind.GazeTraining
                   || kind == FlowStepKind.Part;
        }

        /// <summary>
        /// Move the rows written by flow[startIndex..] out of the session folder into dir/superseded_&lt;utc&gt;/ (same file
        /// names, header kept). Returns the archive folder, or "" when nothing had to be moved.
        /// </summary>
        public static string ArchiveFromStep(string dir, IList<FlowStep> flow, int startIndex, DateTime utcNow)
        {
            var wholeFiles = new HashSet<string>();
            var ssqMeasurements = new HashSet<string>();
            var parts = new HashSet<string>();
            var jsonFiles = new HashSet<string>();
            for (int i = Math.Max(0, startIndex); i < flow.Count; i++)
            {
                FlowStep f = flow[i];
                switch (f.Kind)
                {
                    case FlowStepKind.Ssq:
                    case FlowStepKind.BreakSsq:
                        ssqMeasurements.Add(f.Argument);
                        break;
                    case FlowStepKind.StereoTest:
                        wholeFiles.Add("stereotest.csv");
                        break;
                    case FlowStepKind.EyeRange:
                        wholeFiles.Add("eyerange.csv");
                        wholeFiles.Add("eyerange_trace.csv");
                        break;
                    case FlowStepKind.GazeTraining:
                        wholeFiles.Add("gazetraining.csv");
                        break;
                    case FlowStepKind.Part:
                        parts.Add(f.Argument);
                        if (f.PartNumber == 1)
                        {
                            wholeFiles.Add("part1.csv");
                            wholeFiles.Add("part1_revisions.csv");
                            wholeFiles.Add("part1_replays.csv");
                        }
                        else
                        {
                            wholeFiles.Add("part2.csv");
                        }
                        break;
                    case FlowStepKind.PostQuestionnaire:
                        jsonFiles.Add("postquestionnaire.json");
                        break;
                }
            }
            string archive = Path.Combine(dir, "superseded_" + utcNow.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture));
            bool moved = false;
            foreach (string name in wholeFiles)
            {
                moved |= MoveRows(dir, archive, name, r => true);
            }
            if (ssqMeasurements.Count > 0)
            {
                moved |= MoveRows(dir, archive, "ssq.csv", r => r.Length > 1 && ssqMeasurements.Contains(r[1]));
            }
            if (parts.Count > 0)
            {
                // headpose.csv and playback.csv: column 1 is "part"
                moved |= MoveRows(dir, archive, "headpose.csv", r => r.Length > 1 && parts.Contains(r[1]));
                moved |= MoveRows(dir, archive, "playback.csv", r => r.Length > 1 && parts.Contains(r[1]));
            }
            foreach (string name in jsonFiles)
            {
                string src = Path.Combine(dir, name);
                if (File.Exists(src))
                {
                    System.IO.Directory.CreateDirectory(archive);
                    File.Move(src, Path.Combine(archive, name));
                    moved = true;
                }
            }
            return moved ? archive : "";
        }

        /// <summary>Move the data rows of one CSV file that match into archive/name (appended); the file keeps its header and the other rows.</summary>
        private static bool MoveRows(string dir, string archive, string name, Func<string[], bool> match)
        {
            string path = Path.Combine(dir, name);
            List<string[]> rows = ReadCsv(path);
            if (rows.Count <= 1)
            {
                return false;
            }
            string[] header = rows[0];
            var keep = new List<string[]>();
            var move = new List<string[]>();
            for (int i = 1; i < rows.Count; i++)
            {
                (match(rows[i]) ? move : keep).Add(rows[i]);
            }
            if (move.Count == 0)
            {
                return false;
            }
            System.IO.Directory.CreateDirectory(archive);
            string target = Path.Combine(archive, name);
            var sb = new StringBuilder();
            if (!File.Exists(target))
            {
                sb.Append(CsvLog.Line(header.Cast<object>().ToArray()));
            }
            foreach (string[] r in move)
            {
                sb.Append(CsvLog.Line(r.Cast<object>().ToArray()));
            }
            File.AppendAllText(target, sb.ToString(), Utf8NoBom);
            var live = new StringBuilder(CsvLog.Line(header.Cast<object>().ToArray()));
            foreach (string[] r in keep)
            {
                live.Append(CsvLog.Line(r.Cast<object>().ToArray()));
            }
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, live.ToString(), Utf8NoBom);
            ReplaceFile(tmp, path);
            return true;
        }

        /// <summary>Put tmp in place of path in one step where the file system allows it (File.Replace), else delete and move.</summary>
        public static void ReplaceFile(string tmp, string path)
        {
            if (File.Exists(path))
            {
                try
                {
                    File.Replace(tmp, path, null);
                    return;
                }
                catch (PlatformNotSupportedException)
                {
                }
                catch (NotSupportedException)
                {
                }
                File.Delete(path);
            }
            File.Move(tmp, path);
        }

        public const string ReplacedNoteFile = "REPLACED_README.txt";
        public const string PreviousMovedNoteFile = "PREVIOUS_DATA_MOVED.txt";

        /// <summary>
        /// Move a session folder out of the data folder (restart chosen at check-in): into backupRoot/replaced/ when the
        /// backup folder is on the same volume, otherwise into dataRoot/_replaced/ (folders starting with "_" are skipped by
        /// the analysis). Nothing is copied or deleted: the folder is renamed. A note (REPLACED_README.txt) in the moved
        /// folder says what happened. Returns the new location.
        /// </summary>
        public static string MoveToReplaced(string dir, string backupRoot, string dataRoot, DateTime utcNow, string participantId = "")
        {
            string trimmed = dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string root = Path.Combine(dataRoot, "_replaced");
            if (!string.IsNullOrEmpty(backupRoot))
            {
                string backupReplaced = Path.Combine(backupRoot, "replaced");
                string a = Path.GetPathRoot(Path.GetFullPath(trimmed)) ?? "";
                string b = Path.GetPathRoot(Path.GetFullPath(backupReplaced)) ?? "";
                if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase))
                {
                    root = backupReplaced;
                }
            }
            System.IO.Directory.CreateDirectory(root);
            string stamp = utcNow.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
            string target = Path.Combine(root, Path.GetFileName(trimmed) + "__replaced_" + stamp);
            System.IO.Directory.Move(trimmed, target);
            WriteReplacedNote(target, trimmed, participantId, utcNow, "");
            return target;
        }

        /// <summary>Write (or rewrite) the note in a moved folder; newSessionDir is filled in once the new session folder is known.</summary>
        public static void WriteReplacedNote(string movedDir, string originalDir, string participantId, DateTime utcNow, string newSessionDir)
        {
            string t = utcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
            string text =
                "此文件夹是被替换的正式数据，不进入分析。\n" +
                "This folder holds formal data that were replaced; they are not analysed.\n\n" +
                "participant_id: " + participantId + "\n" +
                "original_folder: " + originalDir + "\n" +
                "moved_utc: " + t + "\n" +
                "reason: 主试在签到页对已有正式数据的编号选择了“重新开始”（Restart chosen at check-in for an ID with formal data）\n" +
                "new_session_folder: " + (string.IsNullOrEmpty(newSessionDir) ? "(written when the new session starts)" : newSessionDir) + "\n";
            File.WriteAllText(Path.Combine(movedDir, ReplacedNoteFile), text, Utf8NoBom);
        }

        /// <summary>Note in the new session folder pointing to where the replaced data went.</summary>
        public static void WritePreviousMovedNote(string newSessionDir, IEnumerable<KeyValuePair<string, string>> moves, DateTime utcNow)
        {
            var sb = new StringBuilder();
            sb.Append("本编号此前的正式数据已在 ").Append(utcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture))
              .Append(" 因选择“重新开始”整体移出数据文件夹，保留在下列位置，不进入分析。\n")
              .Append("Earlier formal data of this ID were moved out of the data folder when Restart was chosen; they are kept here and not analysed.\n\n");
            foreach (KeyValuePair<string, string> m in moves)
            {
                sb.Append(m.Key).Append(" -> ").Append(m.Value).Append('\n');
            }
            File.WriteAllText(Path.Combine(newSessionDir, PreviousMovedNoteFile), sb.ToString(), Utf8NoBom);
        }
    }
}
