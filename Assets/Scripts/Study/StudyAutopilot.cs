using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace Pano2StereoVR.Study
{
    /// <summary>
    /// Desktop flow check (off by default). Started only when the player or editor is launched with
    /// <c>-studyAutopilot</c> (implies QA mode); optional <c>-autopilotShots &lt;dir&gt;</c> (default: AutopilotShots
    /// in the project root) and <c>-autopilotLang zh|en</c>. It acts like the experimenter and the participant
    /// through the real UI (buttons are found by name): picks the language, fills check-in, answers the SSQs
    /// (S_pre, S_mid1, S_mid2, S_post) and the post-questionnaire, waits out the break and presses "ready",
    /// presses the headset buttons (stereo test incl. "can't tell", Part 2 Q1/Q2, Part 1 scales), simulates the
    /// head for the orientation alignment (first off the reference, then on it until the dwell completes),
    /// lets the simulated head follow each natural-viewing-range sweep from 20 deg dot eccentricity on (three times
    /// the dot's further travel, never past the dot), so the head passes 3 deg at about 21 deg (the sweep stops there)
    /// and the provisional rule gives a of about 21 and the PAw radius 20, presses R in each headset-fitting step (the forward must be set before
    /// Continue), uses the staff skip (F8, confirmed) once in Part 1 (group 2, clip 2)
    /// and once in Part 2 (first main trial), presses "Next" on the eye-range result
    /// page (like any headset button), advances self-paced clips (same path as the trigger), replays and re-rates one clip once in the first
    /// Part 1 group summary, uses the QA "next group" control once (last Part 1 group), and screenshots every
    /// new screen, the alignment cross, the gaze-training dots, the gray gap and the left/right preview eye of
    /// the first clips of each part. On the end page it opens and cancels
    /// the exit confirmation, presses "Start next participant", checks that the language screen of a fresh
    /// session appears, and quits. Timings come from the plan (use a reduced plan). A step log is written next
    /// to the screenshots.
    /// </summary>
    public sealed class StudyAutopilot : MonoBehaviour
    {
        private const string Flag = "-studyAutopilot";
        private StudyController _ctrl;
        private StudyUi _ui;
        private string _shotDir = "";
        private StreamWriter _log;
        private int _shotIndex;
        private int _answerCount;
        private bool _rerateDone;
        private bool _replayDone;
        private bool _skipDone;
        private bool _staffDone1;
        private bool _staffDone2;
        private string _language = StudyTexts.Chinese;
        private readonly HashSet<string> _seen = new HashSet<string>();
        private readonly Dictionary<string, int> _counts = new Dictionary<string, int>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            string[] args = Environment.GetCommandLineArgs();
            if (!args.Any(a => string.Equals(a, Flag, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }
            if (FindObjectOfType<StudyController>() == null)
            {
                Debug.LogWarning("[Autopilot] no StudyController in the scene; not started");
                return;
            }
            var go = new GameObject("StudyAutopilot");
            DontDestroyOnLoad(go);
            go.AddComponent<StudyAutopilot>();
        }

        private static string ArgValue(string name)
        {
            string[] a = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < a.Length; i++)
            {
                if (string.Equals(a[i], name, StringComparison.OrdinalIgnoreCase))
                {
                    return a[i + 1];
                }
            }
            return null;
        }

        private void Start()
        {
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            _shotDir = Path.GetFullPath(ArgValue("-autopilotShots") ?? Path.Combine(root, "AutopilotShots"));
            _language = ArgValue("-autopilotLang") == StudyTexts.English ? StudyTexts.English : StudyTexts.Chinese;
            Directory.CreateDirectory(_shotDir);
            _log = new StreamWriter(Path.Combine(_shotDir, "autopilot_log.txt"), false) { AutoFlush = true };
            StartCoroutine(Run());
        }

        private void OnDestroy()
        {
            _log?.Dispose();
        }

        private void Log(string msg)
        {
            string step = _ctrl != null ? _ctrl.CurrentStep + " " + _ctrl.CurrentTrial : "-";
            string line = DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture) + " [" + step.Trim() + "] " + msg;
            _log?.WriteLine(line);
            Debug.Log("[Autopilot] " + line);
        }

        private IEnumerator Shot(string name)
        {
            yield return new WaitForEndOfFrame();
            string safe = new string(name.Select(c => char.IsLetterOrDigit(c) || c == '_' || c == '-' ? c : '_').ToArray());
            string path = Path.Combine(_shotDir, (++_shotIndex).ToString("000", CultureInfo.InvariantCulture) + "_" + safe + ".png");
            ScreenCapture.CaptureScreenshot(path);
            Log("shot " + Path.GetFileName(path));
            yield return null;
            yield return null;
        }

        private bool DesktopVisible => _ui != null && _ui.DesktopContent != null && _ui.DesktopContent.gameObject.activeInHierarchy;
        private bool HmdVisible => _ui != null && _ui.HmdCanvas != null && _ui.HmdCanvas.gameObject.activeInHierarchy;

        private static string FirstText(Transform root)
        {
            Text t = root.GetComponentsInChildren<Text>(false).FirstOrDefault(x => !string.IsNullOrEmpty(x.text));
            return t != null ? t.text : "";
        }

        private static Button Named(Transform root, string name)
        {
            return root == null ? null : root.GetComponentsInChildren<Button>(false).FirstOrDefault(b => b.name == name);
        }

        private bool Press(Transform root, string name)
        {
            Button b = Named(root, name);
            if (b == null || !b.interactable)
            {
                return false;
            }
            Log("press " + name + " '" + FirstText(b.transform).Replace("\n", " ") + "'");
            b.onClick.Invoke();
            return true;
        }

        private IEnumerator Run()
        {
            yield return null;
            _ctrl = FindObjectOfType<StudyController>();
            Log("autopilot started; shots in " + _shotDir + "; mode " + _ctrl.Mode + "; language " + _language);
            float clipSince = -1f;
            bool grayShotDone = false;
            bool restartDone = false;
            int firstGeneration = _ctrl.SessionGeneration;
            string firstSessionDir = "";
            float timeout = Time.realtimeSinceStartup + 3600f;

            while (Time.realtimeSinceStartup < timeout)
            {
                if (_ui == null)
                {
                    _ui = FindObjectOfType<StudyUi>();
                }
                if (_ctrl.Restarting || _ui == null || _ui.DesktopContent == null)
                {
                    yield return null;
                    continue;
                }
                string step = _ctrl.CurrentStep;

                // end page: exit confirmation (cancelled), then start the next participant once
                if (_ctrl.IsFinished)
                {
                    if (!_ctrl.EndReady)
                    {
                        yield return new WaitForSecondsRealtime(0.2f);
                        continue;
                    }
                    firstSessionDir = _ctrl.SessionDirectory;
                    Log("session finished: " + step + "; data folder " + firstSessionDir);
                    yield return new WaitForSecondsRealtime(0.8f);
                    yield return Shot("end_desktop");
                    Press(_ui.DesktopContent, "Btn_Exit");
                    yield return new WaitForSecondsRealtime(0.4f);
                    yield return Shot("end_exit_confirm");
                    Press(_ui.DesktopContent, "Btn_ExitCancel");
                    yield return new WaitForSecondsRealtime(0.3f);
                    Press(_ui.DesktopContent, "Btn_StartNext");
                    restartDone = true;
                    _ui = null;
                    float wait = Time.realtimeSinceStartup + 10f;
                    while ((_ctrl.Restarting || _ctrl.SessionGeneration == firstGeneration) && Time.realtimeSinceStartup < wait)
                    {
                        yield return null;
                    }
                    continue;
                }
                if (restartDone)
                {
                    yield return new WaitForSecondsRealtime(0.8f);
                    bool fresh = step == "language" && !_ctrl.PlanLoaded && _ctrl.SessionDirectory == "";
                    Log("after restart: step " + step + ", generation " + _ctrl.SessionGeneration + ", fresh state " + fresh);
                    yield return Shot("restart_language");
                    break;
                }

                // passive states: orientation cue, gaze dots, playing clip (both preview eyes), gray gap in Part 2
                if (_ctrl.CueVisible && CountFor("cue|" + step) < 2 && Remember("cue|" + step + "|" + _ctrl.CurrentTrial))
                {
                    yield return new WaitForSecondsRealtime(0.3f);
                    yield return Shot("cue_" + step + "_" + _ctrl.CurrentTrial);
                    Bump("cue|" + step);
                }
                if (_ctrl.GazeDotsVisible && CountFor("gaze") < 2)
                {
                    Bump("gaze");
                    yield return new WaitForSecondsRealtime(CountFor("gaze") == 1 ? 0.2f : 2.5f);
                    yield return Shot("gazetraining_" + CountFor("gaze"));
                }
                if (_ctrl.ClipVisible && !HmdVisible)
                {
                    if (clipSince < 0f)
                    {
                        clipSince = Time.realtimeSinceStartup;
                    }
                    int n = CountFor("clip|" + step);
                    if (n < 3 && Time.realtimeSinceStartup - clipSince > 1.0f)
                    {
                        Bump("clip|" + step);
                        _ctrl.SetPreviewEye(0);
                        yield return null;
                        yield return Shot("clip_" + step + "_" + _ctrl.CurrentTrial + "_eyeL");
                        _ctrl.SetPreviewEye(1);
                        yield return null;
                        yield return Shot("clip_" + step + "_" + _ctrl.CurrentTrial + "_eyeR");
                        _ctrl.SetPreviewEye(0);
                    }
                }
                else
                {
                    if (clipSince > 0f && step.StartsWith("Part:2", StringComparison.Ordinal) && !grayShotDone && !HmdVisible && !_ctrl.CueVisible)
                    {
                        grayShotDone = true;
                        yield return Shot("gray_gap_" + step);
                    }
                    clipSince = -1f;
                }
                if (_ctrl.StereoQuadVisible && HmdVisible && CountFor("rds") < 1)
                {
                    Bump("rds");
                    yield return new WaitForSecondsRealtime(0.4f);
                    _ctrl.SetPreviewEye(0);
                    yield return Shot("stereotest_eyeL");
                    _ctrl.SetPreviewEye(1);
                    yield return Shot("stereotest_eyeR");
                    _ctrl.SetPreviewEye(0);
                }

                // orientation alignment: simulate the head off the reference, then on it until the dwell completes
                if (_ctrl.Aligning)
                {
                    if (CountFor("align|" + step) < 1)
                    {
                        Bump("align|" + step);
                        _ctrl.DesktopLookAtReference(20f);
                        yield return new WaitForSecondsRealtime(0.5f);
                        yield return Shot("align_off_" + step);
                        _ctrl.DesktopLookAtReference(0f);
                        yield return new WaitForSecondsRealtime(0.5f);
                        yield return Shot("align_on_" + step);
                        Log("alignment simulated (off 20 deg, then on the reference)");
                    }
                    else
                    {
                        _ctrl.DesktopLookAtReference(0f);
                    }
                    float until = Time.realtimeSinceStartup + 10f;
                    while (_ctrl.Aligning && Time.realtimeSinceStartup < until)
                    {
                        yield return null;
                    }
                    continue;
                }

                // headset fitting (user 11-20): the experimenter sets the forward with R before Continue
                if (step == "HmdFit" && !_ctrl.ForwardSetInStep && _ctrl.SessionDirectory.Length > 0)
                {
                    yield return new WaitForSecondsRealtime(0.5f);
                    yield return Shot("hmdfit_before_R");
                    Log("set forward (R)");
                    _ctrl.RequestSetForward(false);
                    yield return new WaitForSecondsRealtime(0.4f);
                    yield return Shot("hmdfit_forward_set");
                    continue;
                }

                // natural viewing range: the simulated head follows the sweeping dot from 20 deg on (deterministic a, theta 20)
                if (_ctrl.EyeRangeSweepActive)
                {
                    bool shot = false;
                    while (_ctrl.EyeRangeSweepActive)
                    {
                        float dot = _ctrl.EyeRangeDotEccentricityDeg;
                        _ctrl.DesktopLookAlongEyeRangeSweep(EyeRangeHeadEccentricity(dot));
                        if (!shot && dot > 15f && CountFor("eyerange") < 2)
                        {
                            shot = true;
                            Bump("eyerange");
                            yield return Shot("eyerange_sweep_" + CountFor("eyerange"));
                            continue;
                        }
                        yield return null;
                    }
                    Log("eyerange sweep done (head followed from 20 deg)");
                    continue;
                }
                if (step.StartsWith("Part:2", StringComparison.Ordinal) && Remember("paw_theta"))
                {
                    Log("Part 2 PAw correction radius " + _ctrl.PawThetaDeg.ToString("0.##", CultureInfo.InvariantCulture) + " deg");
                }

                // staff skip (F8, user 11-20): once in Part 1 (group 2, clip 2) and once in Part 2 (first main trial), confirmed
                if (_ctrl.StaffSkipAvailable && _ctrl.ClipVisible
                    && ((!_staffDone1 && step.StartsWith("Part:1", StringComparison.Ordinal) && (_ctrl.CurrentTrial ?? "").StartsWith("group 2/", StringComparison.Ordinal)
                         && (_ctrl.CurrentTrial ?? "").Contains(" clip 2/"))
                        || (!_staffDone2 && step.StartsWith("Part:2", StringComparison.Ordinal) && (_ctrl.CurrentTrial ?? "").Contains(" main "))))
                {
                    bool part1 = step.StartsWith("Part:1", StringComparison.Ordinal);
                    if (part1)
                    {
                        _staffDone1 = true;
                    }
                    else
                    {
                        _staffDone2 = true;
                    }
                    yield return new WaitForSecondsRealtime(0.5f);
                    Log("staff skip (F8): " + _ctrl.CurrentTrial);
                    _ctrl.RequestStaffSkipDialog();
                    yield return new WaitForSecondsRealtime(0.4f);
                    yield return Shot("staff_skip_dialog_" + (part1 ? "part1" : "part2"));
                    _ctrl.AnswerStaffSkip(true);
                    yield return new WaitForSecondsRealtime(0.3f);
                    continue;
                }

                // QA skip control: once, at the first clip of the last Part 1 group
                if (!_skipDone && step.StartsWith("Part:1", StringComparison.Ordinal) && IsLastGroupFirstClip(_ctrl.CurrentTrial)
                    && _ctrl.ClipVisible && _ui.SkipGroupButton != null && _ui.SkipGroupButton.gameObject.activeInHierarchy)
                {
                    _skipDone = true;
                    yield return new WaitForSecondsRealtime(0.5f);
                    Log("qa skip: next group (" + _ctrl.CurrentTrial + ")");
                    _ui.SkipGroupButton.onClick.Invoke();
                    yield return new WaitForSecondsRealtime(0.3f);
                    continue;
                }

                // self-paced clip: advance like the trigger once allowed
                if (_ctrl.AdvanceAvailable)
                {
                    yield return new WaitForSecondsRealtime(0.4f);
                    if (CountFor("hint|" + step) < 1)
                    {
                        Bump("hint|" + step);
                        yield return Shot("advance_hint_" + step);
                    }
                    Log("advance (trigger)");
                    _ctrl.RequestAdvance();
                    yield return new WaitForSecondsRealtime(0.2f);
                    continue;
                }

                // headset panel with buttons: answer like a participant
                if (HmdVisible && _ui.HmdButtons.Count > 0)
                {
                    yield return new WaitForSecondsRealtime(0.5f);
                    if (_ui == null || !HmdVisible || _ui.HmdButtons.Count == 0)
                    {
                        continue;
                    }
                    string sig = "hmd|" + step + "|" + FirstText(_ui.HmdContent);
                    if (Remember(sig))
                    {
                        yield return Shot("hmd_" + step);
                    }
                    List<Button> buttons = _ui.HmdButtons.Where(b => b != null && b.interactable).ToList();
                    Button edit = buttons.FirstOrDefault(b => b.name == "Btn_Edit_1");
                    Button replay = buttons.FirstOrDefault(b => b.name == "Btn_Replay_1");
                    Button pick = null;
                    if (edit != null)
                    {
                        // group summary: replay clip 1 once, then re-rate it once, then continue
                        if (!_replayDone && replay != null)
                        {
                            pick = replay;
                            _replayDone = true;
                        }
                        else if (!_rerateDone)
                        {
                            pick = edit;
                            _rerateDone = true;
                        }
                        else
                        {
                            pick = buttons.FirstOrDefault(b => b.name == "Btn_Continue");
                        }
                    }
                    else if (buttons.Count > 0)
                    {
                        pick = buttons[PickHmd(buttons.Count)];
                    }
                    if (pick != null)
                    {
                        Log("hmd press " + pick.name + " '" + FirstText(pick.transform).Replace("\n", " ") + "'");
                        pick.onClick.Invoke();
                        _answerCount++;
                    }
                    yield return new WaitForSecondsRealtime(0.3f);
                    continue;
                }

                // desktop forms: act like the experimenter / participant at the PC
                if (DesktopVisible)
                {
                    yield return DesktopAct(step);
                }
                yield return new WaitForSecondsRealtime(0.2f);
            }

            Log(restartDone ? "done (restart checked)" : "timeout");
            Log("first session data folder: " + firstSessionDir);
            Log("answers given: " + _answerCount + ", screenshots: " + _shotIndex);
            yield return new WaitForSecondsRealtime(1f);
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private IEnumerator DesktopAct(string step)
        {
            Transform c = _ui.DesktopContent;
            string sig = "desk|" + step + "|" + FirstText(c);
            if (step == "language")
            {
                if (Remember(sig))
                {
                    yield return Shot("desktop_language");
                }
                Press(c, _language == StudyTexts.English ? "Btn_LangEn" : "Btn_LangZh");
                yield return new WaitForSecondsRealtime(0.4f);
                yield break;
            }
            if (step == "checkin")
            {
                if (!_ctrl.PlanLoaded)
                {
                    InputField pid = c.GetComponentsInChildren<InputField>().FirstOrDefault();
                    if (pid != null && string.IsNullOrEmpty(pid.text))
                    {
                        pid.text = ArgValue("--participant") ?? "AP01";
                    }
                    Press(c, "Btn_Load");
                    yield return new WaitForSecondsRealtime(0.5f);
                    if (_ctrl.PlanLoaded)
                    {
                        // plan names carry subfolders (part1/..., part2/...); test clips: tools/make_test_clips.py --out <stimuli dir>
                        Log("plan loaded; " + _ctrl.MissingStimuliCount + " stimulus file(s) missing under " + _ctrl.StimuliDirectory
                            + " (QA mode plays placeholders)");
                    }
                }
                foreach (Toggle t in c.GetComponentsInChildren<Toggle>())
                {
                    t.isOn = true;
                }
                yield return new WaitForSecondsRealtime(0.4f);
                if (Remember(sig))
                {
                    yield return Shot("desktop_checkin");
                }
                if (!Press(c, "Btn_Start") && Remember(sig + "|blocked"))
                {
                    Log("checkin: Start not enabled (plan not loaded, or stimuli missing in formal mode)");
                }
                yield break;
            }

            // choice rows: pick one option per row
            var rows = c.GetComponentsInChildren<Transform>().Where(t => t.name == "ChoiceRow").ToList();
            int r = 0;
            foreach (Transform row in rows)
            {
                List<Button> opts = row.GetComponentsInChildren<Button>().Where(b => b.name.StartsWith("Choice_", StringComparison.Ordinal)).ToList();
                if (opts.Count > 0)
                {
                    opts[(r * 7 + step.Length) % Math.Min(2, opts.Count)].onClick.Invoke();
                }
                r++;
            }
            foreach (InputField f in c.GetComponentsInChildren<InputField>())
            {
                if (string.IsNullOrEmpty(f.text))
                {
                    f.text = "autopilot desktop check";
                }
            }
            yield return new WaitForSecondsRealtime(0.4f);
            if (Remember(sig))
            {
                Log("desktop form: " + rows.Count + " choice rows answered");
                yield return Shot("desktop_" + step);
            }
            if (Press(c, "Btn_Submit") || Press(c, "Btn_Continue"))
            {
                yield return new WaitForSecondsRealtime(0.3f);
            }
            else if (Remember(sig + "|waiting"))
            {
                Log("desktop: submit not yet enabled (timer or answers)");
                yield return Shot("desktop_" + step + "_waiting");
            }
        }

        /// <summary>Simulated head eccentricity for a dot eccentricity: still up to 20 deg, then 3 deg per degree of dot travel, at most the dot.</summary>
        private static float EyeRangeHeadEccentricity(float dotEccDeg)
        {
            return Mathf.Min(dotEccDeg, Mathf.Max(0f, (dotEccDeg - 20f) * 3f));
        }

        /// <summary>Trial label "group k/N clip 1/..." with k == N.</summary>
        private static bool IsLastGroupFirstClip(string trial)
        {
            System.Text.RegularExpressions.Match m = System.Text.RegularExpressions.Regex.Match(trial ?? "", @"^group (\d+)/(\d+) clip 1/");
            return m.Success && m.Groups[1].Value == m.Groups[2].Value;
        }

        private int PickHmd(int count)
        {
            // yes/no: alternate so that both branches of Part 2 occur; scales and shapes: vary
            if (count == 2)
            {
                return _answerCount % 2;
            }
            return (_answerCount * 3 + 1) % count;
        }

        private bool Remember(string key)
        {
            return _seen.Add(key);
        }

        private int CountFor(string key)
        {
            return _counts.TryGetValue(key, out int n) ? n : 0;
        }

        private void Bump(string key)
        {
            _counts[key] = CountFor(key) + 1;
        }
    }
}
