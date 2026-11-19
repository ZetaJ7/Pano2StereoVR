using System;
using System.Collections.Generic;
using System.IO;

namespace Pano2StereoVR.Study
{
    /// <summary>
    /// Folder and mode settings. Resolution order: defaults, then study_settings.json next to the executable
    /// (project root in the Editor), then command-line arguments (--plans-dir, --stimuli-dir, --data-dir,
    /// --backup-dir, --texts, --participant, --mode formal|qa, --qa; -studyAutopilot implies qa).
    /// Relative paths are resolved against the root.
    /// Session mode: "formal" (default; a session cannot start while stimuli are missing) or "qa"
    /// (placeholders allowed; session.json is marked mode = qa so the data stay apart from study data).
    /// </summary>
    public sealed class StudySettings
    {
        public const string SettingsFileName = "study_settings.json";
        public const string ModeFormal = "formal";
        public const string ModeQa = "qa";

        public string Root = "";
        public string PlansDir = "";
        public string StimuliDir = "";
        public string DataDir = "";
        public string BackupDir = "";
        public string TextsOverride = "";
        public string ParticipantId = "";
        public string Mode = ModeFormal;
        public string Source = "defaults";
        public string Warning = "";

        public bool IsQa => Mode == ModeQa;

        public static StudySettings Resolve(string root, bool isEditor, IList<string> args)
        {
            var s = new StudySettings
            {
                Root = root,
                PlansDir = isEditor ? Path.Combine("StudyTools", "plans") : "plans",
                StimuliDir = "Stimuli",
                DataDir = "StudyData",
                BackupDir = "StudyData_backup",
                TextsOverride = "study_texts.json",
            };

            string settingsPath = Path.Combine(root, SettingsFileName);
            if (File.Exists(settingsPath))
            {
                Dictionary<string, object> d = MiniJson.AsObject(MiniJson.Parse(File.ReadAllText(settingsPath)), SettingsFileName);
                s.PlansDir = MiniJson.GetString(d, "plans_dir", s.PlansDir);
                s.StimuliDir = MiniJson.GetString(d, "stimuli_dir", s.StimuliDir);
                s.DataDir = MiniJson.GetString(d, "data_dir", s.DataDir);
                s.BackupDir = MiniJson.GetString(d, "backup_dir", s.BackupDir);
                s.TextsOverride = MiniJson.GetString(d, "texts", s.TextsOverride);
                s.ParticipantId = MiniJson.GetString(d, "participant_id", s.ParticipantId);
                s.SetMode(MiniJson.GetString(d, "mode", s.Mode));
                s.Source = settingsPath;
            }

            if (args != null)
            {
                for (int i = 0; i < args.Count; i++)
                {
                    string a = args[i];
                    if (string.Equals(a, "--qa", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(a, "-studyAutopilot", StringComparison.OrdinalIgnoreCase))
                    {
                        s.Mode = ModeQa;
                        s.Source = s.Source == "defaults" ? "command line" : s.Source + " + command line";
                        continue;
                    }
                    string value = null;
                    string key = a;
                    int eq = a.IndexOf('=');
                    if (eq > 0)
                    {
                        key = a.Substring(0, eq);
                        value = a.Substring(eq + 1);
                    }
                    else if (i + 1 < args.Count)
                    {
                        value = args[i + 1];
                    }
                    bool consumed = true;
                    switch (key)
                    {
                        case "--plans-dir": s.PlansDir = value; break;
                        case "--stimuli-dir": s.StimuliDir = value; break;
                        case "--data-dir": s.DataDir = value; break;
                        case "--backup-dir": s.BackupDir = value; break;
                        case "--texts": s.TextsOverride = value; break;
                        case "--participant": s.ParticipantId = value; break;
                        case "--mode": s.SetMode(value); break;
                        default: consumed = false; break;
                    }
                    if (consumed)
                    {
                        s.Source = s.Source == "defaults" ? "command line" : s.Source + " + command line";
                        if (eq < 0)
                        {
                            i++;
                        }
                    }
                }
            }

            s.PlansDir = Absolute(root, s.PlansDir);
            s.StimuliDir = Absolute(root, s.StimuliDir);
            s.DataDir = Absolute(root, s.DataDir);
            s.BackupDir = Absolute(root, s.BackupDir);
            s.TextsOverride = Absolute(root, s.TextsOverride);
            return s;
        }

        private void SetMode(string value)
        {
            string v = (value ?? "").Trim().ToLowerInvariant();
            if (v == ModeFormal || v == ModeQa)
            {
                Mode = v;
            }
            else if (v.Length > 0)
            {
                Warning = "unknown mode '" + value + "', using " + Mode;
            }
        }

        private static string Absolute(string root, string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return path;
            }
            return Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(root, path));
        }

        public string PlanPath(string participantId)
        {
            return Path.Combine(PlansDir, participantId + ".json");
        }
    }
}
