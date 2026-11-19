using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR;

namespace Pano2StereoVR.Study
{
    /// <summary>
    /// Flow steps of the study session (SSQ, break, headset fitting, stereo test, gaze training, Parts 1 and 2).
    /// Every wait loop also ends on a QA skip request (<see cref="Interrupted"/>); skipped trials are written
    /// with skipped = 1.
    /// </summary>
    public sealed partial class StudyController
    {
        // ------------------------------------------------------------------ SSQ and break
        /// <summary>SSQ on the PC. noticeKey: prominent "remove the headset" notice (desktop and headset), or null.</summary>
        private IEnumerator SsqStep(string measurement, float minBreakSeconds, string noticeKey)
        {
            ShowGray();
            bool isBreak = minBreakSeconds > 0;
            if (noticeKey != null)
            {
                ShowHmdNotice(noticeKey, null);
            }
            else
            {
                ShowHmdNotice("ssq_hmd_title", "ssq_hmd");
            }
            bool submitted = false;
            bool proceed = false;
            float t0 = Time.realtimeSinceStartup;
            if (isBreak)
            {
                _s.BreakStartRealtime = t0;
                LogEvent("break_start", measurement);
            }

            _ui.ClearDesktop();
            Transform c = _ui.DesktopContent;
            Text timer = null;
            if (noticeKey != null)
            {
                _ui.Notice(c, T(noticeKey), null);
                _ui.Spacer(c, 8);
            }
            if (isBreak)
            {
                timer = _ui.Colored(c, "", StudyTheme.Light.TextSecondary, TextAnchor.MiddleCenter, 28);
                _ui.Spacer(c, 8);
            }
            _ui.Title(c, T("ssq_title"));
            _ui.Secondary(c, T("ssq_instruction"), TextAnchor.MiddleCenter);
            _ui.Spacer(c, 6);
            string[] levels = { T("ssq_level_0"), T("ssq_level_1"), T("ssq_level_2"), T("ssq_level_3") };
            var rows = new List<StudyUi.ChoiceRow>();
            RectTransform[] cols = _ui.NewColumns(c, 2, 60, 12);
            int half = (StudySchema.SsqItems.Length + 1) / 2;
            for (int i = 0; i < StudySchema.SsqItems.Length; i++)
            {
                string key = "ssq_" + StudySchema.SsqItems[i];
                rows.Add(_ui.NewChoiceRow(cols[i < half ? 0 : 1], T(key), StudyTexts.Note(key), levels, 24, 21, 248, 98, 52));
            }
            _ui.Spacer(c, 10);
            RectTransform submitRow = _ui.NewRow(c, 0, TextAnchor.MiddleCenter, 72);
            Button submit = _ui.NewButton(submitRow, T(isBreak ? "break_continue" : "submit"), 30, () =>
            {
                if (!submitted)
                {
                    submitted = true;
                    _s.Log.WriteSsq(measurement, rows.Select(r => r.Selected).ToList());
                    LogEvent("ssq", measurement);
                }
                proceed = true;
            }, 380, 68, ButtonKind.Primary, "Btn_Submit");

            while (!proceed && !Interrupted)
            {
                bool complete = rows.All(r => r.Selected >= 0);
                float left = minBreakSeconds - (Time.realtimeSinceStartup - t0);
                if (timer != null)
                {
                    timer.text = left > 0 ? TF("break_timer", Mathf.CeilToInt(left)) : T("break_timer_done");
                }
                submit.interactable = complete && left <= 0;
                yield return null;
            }
            if (isBreak)
            {
                LogEvent("break_end", measurement + " " + CsvLog.Format(Time.realtimeSinceStartup - t0) + " s");
            }
            _ui.ShowDesktopPanel(false);
        }

        /// <summary>Rest of at least minBreakSeconds; then the participant chooses when to continue.</summary>
        private IEnumerator BreakStep(float minBreakSeconds)
        {
            ShowGray();
            ShowHmdNotice("break_hmd_title", null);
            bool ready = false;
            float t0 = Time.realtimeSinceStartup;
            _s.BreakStartRealtime = t0;
            LogEvent("break_start", "min " + CsvLog.Format(minBreakSeconds) + " s");
            _ui.ClearDesktop();
            Transform c = _ui.DesktopContent;
            _ui.Title(c, T("break_title"), StudyTheme.Light.NoticeSize);
            _ui.Spacer(c, 8);
            _ui.BodyBlock(c, TF("break_body", Mathf.RoundToInt(minBreakSeconds)), 900, 30);
            _ui.Spacer(c, 16);
            Text timer = _ui.Colored(c, "", StudyTheme.Light.TextSecondary, TextAnchor.MiddleCenter, 30);
            _ui.Spacer(c, 24);
            RectTransform row = _ui.NewRow(c, 0, TextAnchor.MiddleCenter, 76);
            Button go = _ui.NewButton(row, T("break_ready"), 30, () => ready = true, 420, 72, ButtonKind.Primary, "Btn_Continue");
            while (!ready && !Interrupted)
            {
                float left = minBreakSeconds - (Time.realtimeSinceStartup - t0);
                timer.text = left > 0 ? TF("break_timer", Mathf.CeilToInt(left)) : T("break_timer_done");
                go.interactable = left <= 0;
                yield return null;
            }
            float breakS = Time.realtimeSinceStartup - t0;
            _s.Session["break_s"] = Round(breakS, 1);
            WriteSession();
            LogEvent("break_end", CsvLog.Format(breakS) + " s" + (ready ? "" : " (skipped)"));
            _ui.ShowDesktopPanel(false);
        }

        // ------------------------------------------------------------------ headset fitting
        private IEnumerator HmdFitStep()
        {
            ShowGray();
            bool done = false;
            _ui.ClearDesktop();
            Transform c = _ui.DesktopContent;
            _ui.Title(c, T("hmd_fit_title"), StudyTheme.Light.NoticeSize);
            _ui.Spacer(c, 10);
            _ui.BodyBlock(c, T("hmd_fit_steps"), 1080, 30);
            _ui.Spacer(c, 30);
            RectTransform row = _ui.NewRow(c, 0, TextAnchor.MiddleCenter, 72);
            _ui.NewButton(row, T("continue") + "  (Space)", 30, () => done = true, 380, 68, ButtonKind.Primary, "Btn_Continue");
            yield return ShowHmdPanel("hmd_fit_hmd_title", "hmd_fit_hmd", "continue", () => done = true, () => done);
            Recenter();
            _ui.ShowDesktopPanel(false);
        }

        // ------------------------------------------------------------------ stereo test
        /// <summary>
        /// Angular size of one rendered eye pixel at the view centre: from the XR left-eye projection matrix and
        /// the eye texture width when XR is active, otherwise from the plan's display fallback (pending check
        /// against the HMD specification).
        /// </summary>
        private double StereoPixelArcsec(StereoTestPlan st, out string source, out int widthPx, out double hfovDeg)
        {
            source = "config_fallback";
            widthPx = st.FallbackEyeWidthPx;
            hfovDeg = st.FallbackHfovDeg;
            if (_rig.XrActive && XRSettings.eyeTextureWidth > 0 && _rig.Camera != null)
            {
                Matrix4x4 m = _rig.Camera.GetStereoProjectionMatrix(Camera.StereoscopicEye.Left);
                float m00 = m[0, 0];
                float m02 = m[0, 2];
                if (m00 > 0f && !float.IsNaN(m00) && !float.IsNaN(m02))
                {
                    double tanR = (1.0 + m02) / m00;
                    double tanL = (1.0 - m02) / m00;
                    if (tanR > 0 && tanL > 0)
                    {
                        source = "xr_eye_texture";
                        widthPx = XRSettings.eyeTextureWidth;
                        hfovDeg = (Math.Atan(tanL) + Math.Atan(tanR)) * 180.0 / Math.PI;
                        return RandomDotStereogram.PixelArcsecAtCentre(tanL, tanR, widthPx);
                    }
                }
            }
            return RandomDotStereogram.PixelArcsecFromFov(hfovDeg, Math.Max(1, widthPx));
        }

        private IEnumerator StereoTestStep()
        {
            StereoTestPlan st = _s.Plan.StereoTest;
            if (!st.Enabled || st.Trials.Count == 0)
            {
                LogEvent("stereotest_skipped", "disabled in plan");
                yield break;
            }
            ShowGray();
            bool go = false;
            yield return ShowHmdPanel("stereo_title", "stereo_intro", "start", () => go = true, () => go);
            Recenter();

            double arcsecPerPx = StereoPixelArcsec(st, out string pxSource, out int eyeWidth, out double hfov);
            int size = RandomDotStereogram.TexturePxForOnePixelTexels(st.QuadSizeDeg, arcsecPerPx);
            if (_rdsTex == null || _rdsTex.width != size)
            {
                if (_rdsTex != null)
                {
                    Destroy(_rdsTex);
                }
                _rdsTex = Track(new Texture2D(size, size * 2, TextureFormat.RGBA32, false)
                {
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp,
                    name = "RandomDotStereogram",
                });
            }
            _s.Session["stereotest_arcsec_per_px"] = Round(arcsecPerPx, 2);
            _s.Session["stereotest_px_source"] = pxSource;
            _s.Session["stereotest_eye_width_px"] = eyeWidth;
            _s.Session["stereotest_hfov_deg"] = Round(hfov, 2);
            _s.Session["stereotest_texture_px"] = size;
            _s.Session["stereotest_dot_px"] = st.DotPx;
            WriteSession();
            LogEvent("stereotest_scale", string.Format(CultureInfo.InvariantCulture,
                "{0:0.##} arcsec/px from {1} (eye width {2} px, hfov {3:0.##} deg), texture {4} px, dot {5} px",
                arcsecPerPx, pxSource, eyeWidth, hfov, size, st.DotPx));
            float quadSize = 2f * st.QuadDistanceM * Mathf.Tan(0.5f * st.QuadSizeDeg * Mathf.Deg2Rad);

            for (int i = 0; i < st.Trials.Count; i++)
            {
                StereoTrial trial = st.Trials[i];
                int d = Math.Max(0, trial.DisparityPx);
                if (Interrupted)
                {
                    _s.Log.StereoTest.Append(_s.Plan.ParticipantId, trial.Index, trial.Block, trial.Shape, d,
                        Round(d * arcsecPerPx, 1), Round(arcsecPerPx, 2), pxSource, "", "", "", 1);
                    continue;
                }
                _s.TrialLabel = (i + 1) + "/" + st.Trials.Count + " " + trial.DisparityPx + "px" + (trial.Block == "practice" ? " practice" : "");
                byte[] lum = RandomDotStereogram.GenerateTopBottom(size, trial.Shape, d, trial.Crossed,
                    unchecked((int)(_s.Plan.Seed % int.MaxValue) + trial.Index * 7919), st.DotPx);
                var px = new Color32[lum.Length];
                for (int k = 0; k < lum.Length; k++)
                {
                    px[k] = new Color32(lum[k], lum[k], lum[k], 255);
                }
                _rdsTex.SetPixels32(px);
                _rdsTex.Apply(false, false);
                _quadMat.mainTexture = _rdsTex;

                Vector3 fwd = _rig.YawForward();
                _rdsQuad.transform.position = _rig.HeadPosition + fwd * st.QuadDistanceM + Vector3.up * 0.12f;
                _rdsQuad.transform.rotation = Quaternion.LookRotation(fwd, Vector3.up);
                _rdsQuad.transform.localScale = new Vector3(quadSize, quadSize, 1f);
                _rdsQuad.SetActive(true);

                string response = null;
                float shown = Time.realtimeSinceStartup;
                _ui.ClearHmd();
                _ui.SetHmdSize(1760, 380);
                _ui.PlaceHmdCanvas(_rig.HeadPosition, fwd, PanelDistance, -0.68f);
                _ui.Body(_ui.HmdContent, (trial.Block == "practice" ? T("practice_label") + "  " : "") + T("stereo_question"), TextAnchor.MiddleCenter);
                RectTransform row = _ui.NewRow(_ui.HmdContent, 18, TextAnchor.MiddleCenter, 112);
                for (int s = 0; s < st.Shapes.Count; s++)
                {
                    string shape = st.Shapes[s];
                    _ui.HmdButton(row, T("shape_" + shape), () => response = shape, 270, 104, 38, ButtonKind.Secondary, "Btn_Shape_" + shape);
                    _ui.AddShortcut(KeyCode.Alpha1 + s, () => response = shape);
                }
                _ui.HmdButton(row, T("stereo_cant_tell"), () => response = "cant_tell", 270, 104, 38, ButtonKind.Secondary, "Btn_CantTell");
                _ui.AddShortcut(KeyCode.Alpha1 + st.Shapes.Count, () => response = "cant_tell");
                _ui.AddShortcut(KeyCode.Alpha0, () => response = "cant_tell");
                while (response == null && !Interrupted)
                {
                    yield return null;
                }
                float rt = Time.realtimeSinceStartup - shown;
                _rdsQuad.SetActive(false);
                _ui.ShowHmd(false);
                bool skipped = response == null;
                _s.Log.StereoTest.Append(_s.Plan.ParticipantId, trial.Index, trial.Block, trial.Shape, d,
                    Round(d * arcsecPerPx, 1), Round(arcsecPerPx, 2), pxSource, response ?? "",
                    skipped ? (object)"" : (response == trial.Shape ? 1 : 0), skipped ? (object)"" : rt, skipped ? 1 : 0);
                if (!skipped)
                {
                    _s.StereoTrialsDone++;
                }
                yield return Wait(0.5f);
            }
            _s.TrialLabel = "";
            if (!_skipPart)
            {
                // user 11-19: a closing page before the study content begins
                bool cont = false;
                yield return ShowHmdPanel("stereo_done_title", "stereo_done", "continue", () => cont = true, () => cont);
            }
        }

        // ------------------------------------------------------------------ orientation alignment
        private void SetAlignFeedback(bool inside, float progress)
        {
            _colorMat.SetColor("_Color", inside ? StudyTheme.CueAligned : StudyTheme.CueYellow);
            int lit = Mathf.Clamp(Mathf.FloorToInt(progress * RingSegments + 1e-4f), 0, RingSegments);
            for (int k = 0; k < _ringSegments.Count; k++)
            {
                Material m = k < lit ? _ringLitMat : _ringDimMat;
                if (_ringSegments[k].sharedMaterial != m)
                {
                    _ringSegments[k].sharedMaterial = m;
                }
            }
        }

        /// <summary>
        /// Yellow cross at the reference direction with a dwell ring. Ends when the head has stayed within the
        /// plan's alignment tolerance for its dwell time (no trigger needed), or on a QA skip.
        /// result(alignment time in s, deviation at the start in deg).
        /// </summary>
        private IEnumerator AlignToReference(float refYaw, float refPitch, string cueText, float panelDrop, Action<float, float> result)
        {
            AlignmentPlan a = _s.Plan.Alignment;
            Quaternion refRot = Quaternion.Euler(-refPitch, refYaw, 0f);
            ShowCue(refRot * Vector3.forward);
            _alignRing.SetActive(true);
            SetAlignFeedback(false, 0f);
            _ui.ClearHmd();
            _ui.SetHmdSize(1500, 250);
            _ui.PlaceHmdCanvas(_rig.HeadPosition, Quaternion.Euler(0f, refYaw, 0f) * Vector3.forward, PanelDistance, panelDrop);
            _ui.Body(_ui.HmdContent, cueText, TextAnchor.MiddleCenter);
            Text devText = _ui.Note(_ui.HmdContent, "", TextAnchor.MiddleCenter);
            _aligning = true;
            _alignYaw = refYaw;
            _alignPitch = refPitch;
            float t0 = Time.realtimeSinceStartup;
            float insideSince = -1f;
            float dev = 0f;
            while (!Interrupted)
            {
                float now = Time.realtimeSinceStartup;
                dev = HeadDeviation(refYaw, refPitch);
                bool inside = dev <= a.ToleranceDeg;
                if (!inside)
                {
                    insideSince = -1f;
                }
                else if (insideSince < 0f)
                {
                    insideSince = now;
                }
                float progress = !inside ? 0f : (a.DwellSeconds <= 0f ? 1f : (now - insideSince) / a.DwellSeconds);
                SetAlignFeedback(inside, progress);
                devText.text = TF("align_deviation", dev.ToString("0.0", CultureInfo.InvariantCulture)) + (inside ? "      " + T("align_hold") : "");
                if (inside && progress >= 1f)
                {
                    break;
                }
                yield return null;
            }
            _aligning = false;
            SetAlignFeedback(false, 0f);
            _alignRing.SetActive(false);
            _cueMarker.SetActive(false);
            _ui.ShowHmd(false);
            result(Time.realtimeSinceStartup - t0, dev);
        }

        // ------------------------------------------------------------------ gaze training (before Part 2)
        private void BuildGazeDots(GazeTrainingPlan g)
        {
            if (_gazeRoot != null)
            {
                Destroy(_gazeRoot);
            }
            _gazeDots.Clear();
            _gazeRoot = Track(new GameObject("GazeTraining"));
            for (int k = 0; k < g.DotCount; k++)
            {
                GameObject dot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Destroy(dot.GetComponent<Collider>());
                dot.name = "GazeDot" + k;
                dot.transform.SetParent(_gazeRoot.transform, false);
                dot.transform.localScale = Vector3.one * GazeDotBaseSize;
                Material m = Track(new Material(ColorShader));
                dot.GetComponent<Renderer>().sharedMaterial = m;
                _gazeDots.Add(dot.GetComponent<Renderer>());
            }
            _gazeRoot.SetActive(false);
        }

        private void PlaceGazeDots(GazeTrainingPlan g, Quaternion refRot)
        {
            Vector3 head = _rig.HeadPosition;
            for (int k = 0; k < _gazeDots.Count; k++)
            {
                // ring of radius r around the reference direction, dot 0 at the top, clockwise for the viewer
                Vector3 local = Quaternion.AngleAxis(-g.DotAngleDeg(k), Vector3.forward)
                    * (Quaternion.AngleAxis(-g.RadiusDeg, Vector3.right) * Vector3.forward);
                _gazeDots[k].transform.position = head + refRot * local * PanelDistance;
            }
        }

        private void HighlightGazeDot(int lit)
        {
            Color[] palette = StudyTheme.GazeDotColors;
            for (int k = 0; k < _gazeDots.Count; k++)
            {
                Color c = palette[k % palette.Length];
                bool on = k == lit;
                _gazeDots[k].sharedMaterial.SetColor("_Color", on ? c : Color.Lerp(Color.black, c, 0.3f));
                _gazeDots[k].transform.localScale = Vector3.one * GazeDotBaseSize * (on ? 1.8f : 1f);
            }
        }

        private float GazeDotBaseSize => 2f * PanelDistance * Mathf.Tan(0.5f * (_s.Plan != null ? _s.Plan.GazeTraining.DotSizeDeg : 1.2f) * Mathf.Deg2Rad);

        private void HideGazeDots()
        {
            if (_gazeRoot != null)
            {
                _gazeRoot.SetActive(false);
            }
        }

        private void ShowCue(Vector3 refDir)
        {
            _cueMarker.transform.position = _rig.HeadPosition + refDir * PanelDistance;
            _cueMarker.transform.rotation = Quaternion.LookRotation(refDir, Vector3.up);
            _cueMarker.SetActive(true);
        }

        private IEnumerator GazeTrainingStep()
        {
            GazeTrainingPlan g = _s.Plan.GazeTraining;
            if (!g.Enabled)
            {
                LogEvent("gazetraining_skipped", "disabled in plan");
                yield break;
            }
            ShowGray();
            bool go = false;
            yield return ShowHmdPanel("gaze_title", "gaze_intro", "start", () => go = true, () => go);
            Recenter();
            BuildGazeDots(g);
            Quaternion refRot = Quaternion.Euler(-g.ReferencePitchDeg, g.ReferenceYawDeg, 0f);
            Vector3 refYawFwd = Quaternion.Euler(0f, g.ReferenceYawDeg, 0f) * Vector3.forward;
            float panelDrop = -PanelDistance * Mathf.Tan((g.RadiusDeg + 10f) * Mathf.Deg2Rad);
            string pid = _s.Plan.ParticipantId;

            for (int attempt = 1; attempt <= g.MaxAttempts && !Interrupted; attempt++)
            {
                _s.TrialLabel = "attempt " + attempt + "/" + g.MaxAttempts;
                List<int> order = g.OrderFor(attempt - 1);
                PlaceGazeDots(g, refRot);
                HighlightGazeDot(-1);
                _gazeRoot.SetActive(true);
                _recenterLocked = true; // user 11-19: no manual recenter while the head direction is measured
                float alignS = 0f, startDev = 0f;
                yield return AlignToReference(g.ReferenceYawDeg, g.ReferencePitchDeg, T("gaze_cue"), panelDrop,
                    (t, d) => { alignS = t; startDev = d; });
                if (Interrupted)
                {
                    break;
                }
                LogEvent("gazetraining_aligned", string.Format(CultureInfo.InvariantCulture, "attempt {0}: {1:0.00} s, {2:0.0} deg", attempt, alignS, startDev));
                ShowCue(refRot * Vector3.forward);
                _ui.ShowHint(T("gaze_running"));
                _ui.PlaceHint(_rig.HeadPosition, refYawFwd, PanelDistance, panelDrop);

                var result = new GazeAttemptResult { Attempt = attempt, Order = new List<int>(order) };
                var shownS = new List<float>();
                bool stopped = false;
                foreach (int k in order)
                {
                    HighlightGazeDot(k);
                    float max = 0f;
                    float t0 = Time.realtimeSinceStartup;
                    while (Time.realtimeSinceStartup - t0 < g.DwellSeconds && !Interrupted)
                    {
                        max = Mathf.Max(max, HeadDeviation(g.ReferenceYawDeg, g.ReferencePitchDeg));
                        if (max > g.MaxHeadDeviationDeg)
                        {
                            // user 11-19: stop the attempt the moment the head leaves the tolerance
                            stopped = true;
                            break;
                        }
                        yield return null;
                    }
                    if (Interrupted)
                    {
                        break;
                    }
                    result.DotMaxDeg.Add(max);
                    shownS.Add(stopped ? Time.realtimeSinceStartup - t0 : g.DwellSeconds);
                    if (stopped)
                    {
                        break;
                    }
                }
                HighlightGazeDot(-1);
                HideGazeDots();
                _cueMarker.SetActive(false);
                _ui.HideHint();
                _recenterLocked = false;
                if (Interrupted)
                {
                    break;
                }
                result.MaxDeg = result.DotMaxDeg.Count > 0 ? result.DotMaxDeg.Max() : 0f;
                result.Passed = result.MaxDeg <= g.MaxHeadDeviationDeg;
                _s.GazeAttempts.Add(result);
                // one row per dot that lit up; a stopped attempt ends at the dot where the head left the tolerance,
                // and that row's dwell_s is the time the dot was actually shown
                var rows = new List<object[]>();
                for (int pos = 0; pos < result.DotMaxDeg.Count; pos++)
                {
                    int k = result.Order[pos];
                    rows.Add(new object[]
                    {
                        pid, attempt, pos, k, Round(g.DotAngleDeg(k), 1), Round(shownS[pos], 2), Round(result.DotMaxDeg[pos], 2),
                        Round(result.MaxDeg, 2), g.MaxHeadDeviationDeg, result.Passed ? 1 : 0,
                    });
                }
                _s.Log.GazeTraining.AppendRows(rows);
                LogEvent("gazetraining_attempt", string.Format(CultureInfo.InvariantCulture,
                    "attempt {0}: max {1:0.0} deg (threshold {2:0.0}) {3}{4}", attempt, result.MaxDeg, g.MaxHeadDeviationDeg,
                    result.Passed ? "passed" : "not passed",
                    stopped ? " (stopped at dot " + result.DotMaxDeg.Count + "/" + result.Order.Count + ")" : ""));

                bool next = false;
                if (result.Passed)
                {
                    yield return ShowHmdPanelText(T("gaze_done_title"), T("gaze_done"), "continue", () => next = true, () => next);
                    break;
                }
                if (attempt < g.MaxAttempts)
                {
                    yield return ShowHmdPanelText(T("gaze_retry_title"),
                        TF("gaze_retry", g.MaxHeadDeviationDeg.ToString("0.#", CultureInfo.InvariantCulture)), "start",
                        () => next = true, () => next);
                }
                else
                {
                    LogEvent("gazetraining_not_passed", "all " + g.MaxAttempts + " attempts above the threshold; session continues");
                    yield return ShowHmdPanelText(T("gaze_done_title"), T("gaze_failed"), "continue", () => next = true, () => next);
                }
            }
            HideGazeDots();
            _cueMarker.SetActive(false);
            _ui.HideHint();
            _recenterLocked = false;
            Dictionary<string, object> summary = _s.GazeSummary();
            summary["skipped"] = Interrupted;
            _s.Session["gaze_training"] = summary;
            WriteSession();
            _s.TrialLabel = "";
        }

        // ------------------------------------------------------------------ self-paced viewing
        /// <summary>
        /// Playback request for a part. label: shown under the view during the whole segment (e.g. "first clip");
        /// hintKey: added once advancing is allowed.
        /// </summary>
        private PlayRequest MakeRequest(StudyPartOptions o, bool hintFollowsHead, float hintYaw, Action<float, long> onFrame,
            string label = null, string hintKey = "advance_hint")
        {
            _hintLabel = label ?? "";
            if (!string.IsNullOrEmpty(_hintLabel))
            {
                _hintFollowHead = hintFollowsHead;
                _ui.ShowHint(_hintLabel);
                PlaceHint(hintFollowsHead ? _rig.YawForward() : Quaternion.Euler(0f, hintYaw, 0f) * Vector3.forward);
            }
            return new PlayRequest
            {
                NominalSeconds = _s.Plan.Timing.ClipSeconds,
                SelfPaced = o.SelfPaced,
                MinViewSeconds = o.MinViewSeconds,
                MaxViewSeconds = o.MaxViewSeconds,
                OnTexture = SetSphereTexture,
                OnFrame = onFrame,
                AdvanceAllowed = () => ArmAdvance(hintFollowsHead, hintYaw, hintKey),
                AdvanceRequested = () => _advanceRequested,
                Abort = () => Interrupted,
            };
        }

        private void ArmAdvance(bool followHead, float hintYaw, string hintKey)
        {
            _advanceArmed = true;
            _advanceRequested = false;
            _triggerSeen = _pointer != null ? _pointer.TriggerPresses : 0;
            _hintFollowHead = followHead;
            string text = T(hintKey);
            _ui.ShowHint(string.IsNullOrEmpty(_hintLabel) ? text : _hintLabel + "      " + text);
            PlaceHint(followHead ? _rig.YawForward() : Quaternion.Euler(0f, hintYaw, 0f) * Vector3.forward);
        }

        private void PlaceHint(Vector3 yawForward)
        {
            _ui.PlaceHint(_rig.HeadPosition, yawForward, PanelDistance, -PanelDistance * Mathf.Tan(24f * Mathf.Deg2Rad));
        }

        private void StopAdvance()
        {
            _advanceArmed = false;
            _advanceRequested = false;
            _hintLabel = "";
            _ui?.HideHint();
        }

        // ------------------------------------------------------------------ Part 2: PA vs PAw
        private void WritePart2Row(StudyTrial t, object different, string preference, object rt1, string rt2, PlaybackStats a,
            PlaybackStats b, float maxA, float maxB, object alignS, object startDev, int restartsA, int restartsB,
            bool reminderOffA, bool reminderOffB, bool skipped)
        {
            _s.Log.Part2.Append(_s.Plan.ParticipantId, t.Block, t.Index, t.ClipId, t.CondA, t.CondB, t.PairType,
                different, preference, rt1, rt2,
                a != null ? (object)a.ViewSeconds : "", b != null ? (object)b.ViewSeconds : "",
                a != null ? (object)a.Loops : "", b != null ? (object)b.Loops : "",
                a != null ? (object)Round(maxA, 2) : "", b != null ? (object)Round(maxB, 2) : "", alignS, startDev,
                restartsA, restartsB, a != null ? (object)(reminderOffA ? 1 : 0) : "", b != null ? (object)(reminderOffB ? 1 : 0) : "",
                skipped ? 1 : 0);
        }

        /// <summary>Result of one Part 2 segment: the completed viewing and how often it was restarted.</summary>
        private sealed class Part2Segment
        {
            public PlaybackStats Stats = new PlaybackStats();
            public float MaxDeg;
            public int Restarts;
            public bool ReminderOff; // the completed viewing played with the head-turn check off
        }

        /// <summary>Head-turn restarts of one segment before the participant is asked whether to stop the reminder.</summary>
        private const int HeadRestartsBeforeChoice = 3; // user 11-19

        /// <summary>Head-still tolerance during Part 2 playback: the gaze-practice threshold (one rule for both).</summary>
        private float HeadStillThresholdDeg()
        {
            return _s.Plan.GazeTraining != null ? _s.Plan.GazeTraining.MaxHeadDeviationDeg : 5f;
        }

        /// <summary>
        /// Play one Part 2 segment. User 11-19: the head deviation is checked on every frame; once it exceeds the
        /// head-still tolerance the clip stops at once, a prompt explains why, the head is aligned to the cross again
        /// and the same clip plays from the start. After <see cref="HeadRestartsBeforeChoice"/> restarts of a segment
        /// the next head turn asks the participant to choose: keep trying (restart again, the choice comes back after
        /// the same number of further restarts) or stop the reminder (the segment plays once more from the start and
        /// head turns no longer stop playback for the rest of Part 2; flagged in the data). The stopped viewings are
        /// kept in headpose.csv and playback.csv as "A_stopped" / "B_stopped"; result holds the completed viewing.
        /// </summary>
        private IEnumerator PlayPart2Segment(StudyTrial t, StudyPartOptions o, string seg, string file, int slotIndex,
            StudyClipPlayer.Slot slot, string labelKey, List<object[]> head, Part2Segment result)
        {
            float threshold = HeadStillThresholdDeg();
            StudyClipPlayer.Slot s = slot;
            while (true)
            {
                bool check = !_s.Part2ReminderOff;
                bool headOut = false;
                float max = 0f;
                float outDeg = 0f;
                var stats = new PlaybackStats();
                var rows = new List<object[]>();
                PlayRequest req = MakeRequest(o, false, t.ReferenceYawDeg, (ts, frame) =>
                {
                    rows.Add(HeadRow(2, t.Index, seg, ts, frame, t.ReferenceYawDeg, t.ReferencePitchDeg));
                    float dev = HeadDeviation(t.ReferenceYawDeg, t.ReferencePitchDeg);
                    max = Mathf.Max(max, dev);
                    if (check && dev > threshold && !headOut)
                    {
                        headOut = true;
                        outDeg = dev;
                    }
                }, T(labelKey));
                Func<bool> abort = req.Abort;
                req.Abort = () => headOut || (abort != null && abort());
                yield return _clips.Play(s, req, stats);
                StopAdvance();
                ShowGray();
                if (!headOut || Interrupted)
                {
                    head.AddRange(rows);
                    result.Stats = stats;
                    result.MaxDeg = max;
                    result.ReminderOff = !check;
                    yield break;
                }

                // stopped by a head turn: keep the record of this viewing, explain, re-align, replay from the start
                string stoppedSeg = seg + "_stopped";
                foreach (object[] r in rows)
                {
                    r[3] = stoppedSeg; // segment column of HeadRow
                }
                head.AddRange(rows);
                WritePlayback(2, t.Index, stoppedSeg, stats);
                int done = result.Restarts; // restarts of this segment before this head turn
                result.Restarts++;
                LogEvent("part2_head_restart", string.Format(CultureInfo.InvariantCulture,
                    "trial {0} segment {1}: {2:0.0} deg > {3:0.0} at {4:0.00} s, restart {5}", t.Index, seg, outDeg, threshold,
                    stats.ViewSeconds, result.Restarts));
                s = _clips.Prepare(slotIndex, StimulusPath(file));
                if (done >= HeadRestartsBeforeChoice && done % HeadRestartsBeforeChoice == 0)
                {
                    int choice = -1;
                    yield return AskTwoText(T("part2_head_choice_title"), TF("part2_head_choice", done),
                        "part2_head_choice_off", "part2_head_choice_continue", v => choice = v);
                    if (Interrupted)
                    {
                        yield break;
                    }
                    bool off = choice == 1;
                    LogEvent("part2_reminder_choice", string.Format(CultureInfo.InvariantCulture,
                        "trial {0} segment {1} after {2} restarts: {3}", t.Index, seg, done, off ? "reminder off" : "keep trying"));
                    if (off)
                    {
                        _s.Part2ReminderOff = true;
                        _s.Session["part2_reminder_off"] = new Dictionary<string, object>
                        {
                            { "trial_index", t.Index }, { "segment", seg }, { "restarts_before", done }, { "t_utc", UtcNow() },
                        };
                        WriteSession();
                    }
                }
                else
                {
                    bool next = false;
                    yield return ShowHmdPanel("part2_head_title", "part2_head_moved", "continue", () => next = true, () => next);
                }
                if (Interrupted)
                {
                    yield break;
                }
                yield return _clips.WaitPrepared(s, PrepareTimeoutSeconds);
                yield return AlignToReference(t.ReferenceYawDeg, t.ReferencePitchDeg,
                    (t.IsMain ? "" : T("practice_label") + "  ") + T("part2_cue"), -0.42f, (a, d) => { });
                if (Interrupted)
                {
                    yield break;
                }
            }
        }

        /// <summary>Headset panel with a title, a body and two buttons. result: 1 = first button, 0 = second.</summary>
        private IEnumerator AskTwoText(string title, string body, string firstKey, string secondKey, Action<int> result)
        {
            int answer = -1;
            _ui.ClearHmd();
            _ui.SetHmdSize(1700, 820);
            _ui.PlaceHmdCanvas(_rig.HeadPosition, _rig.YawForward(), PanelDistance);
            _ui.Title(_ui.HmdContent, title);
            _ui.Spacer(_ui.HmdContent, 4);
            _ui.BodyBlock(_ui.HmdContent, body, 1440);
            _ui.Spacer(_ui.HmdContent, 16);
            RectTransform row = _ui.NewRow(_ui.HmdContent, 60, TextAnchor.MiddleCenter, 120);
            _ui.HmdButton(row, T(firstKey), () => answer = 1, 420, 108, 38, ButtonKind.Secondary, "Btn_Yes");
            _ui.HmdButton(row, T(secondKey), () => answer = 0, 420, 108, 38, ButtonKind.Secondary, "Btn_No");
            while (answer < 0 && !Interrupted)
            {
                yield return null;
            }
            _ui.ShowHmd(false);
            result(answer);
        }

        private IEnumerator Part2Step(StudyPart part)
        {
            ShowGray();
            bool go = false;
            yield return ShowHmdPanel("part2_title", "part2_intro", "start", () => go = true, () => go);
            Recenter();
            StudyTiming tm = _s.Plan.Timing;
            List<StudyTrial> trials = part.Trials;
            if (trials.Count == 0)
            {
                yield break;
            }
            StudyClipPlayer.Slot slotA = _clips.Prepare(0, StimulusPath(trials[0].FileA));
            StudyClipPlayer.Slot slotB = _clips.Prepare(1, StimulusPath(trials[0].FileB));

            for (int i = 0; i < trials.Count; i++)
            {
                StudyTrial t = trials[i];
                if (_skipPart)
                {
                    WritePart2Row(t, "", "", "", "", null, null, 0f, 0f, "", "", 0, 0, false, false, true);
                    continue;
                }
                _skipGroup = false;
                _s.TrialLabel = (i + 1) + "/" + trials.Count + " " + t.Block + " " + t.ClipId + " " + t.CondA + "-" + t.CondB;
                var head = new List<object[]>();
                var statsA = new PlaybackStats();
                var statsB = new PlaybackStats();
                float maxA = 0f, maxB = 0f;
                yield return _clips.WaitPrepared(slotA, PrepareTimeoutSeconds);
                yield return _clips.WaitPrepared(slotB, PrepareTimeoutSeconds);

                // orientation alignment: playback starts once the head stays within the tolerance for the dwell time
                ShowGray();
                _recenterLocked = true; // user 11-19: no manual recenter from the alignment to the end of the second clip
                float alignS = 0f, startDev = 0f;
                yield return AlignToReference(t.ReferenceYawDeg, t.ReferencePitchDeg,
                    (t.IsMain ? "" : T("practice_label") + "  ") + T("part2_cue"), -0.42f, (a, d) => { alignS = a; startDev = d; });
                bool playedA = false, playedB = false;
                var segA = new Part2Segment();
                var segB = new Part2Segment();
                if (!Interrupted)
                {
                    yield return PlayPart2Segment(t, part.Options, "A", t.FileA, 0, slotA, "segment_first", head, segA);
                    statsA = segA.Stats;
                    maxA = segA.MaxDeg;
                    playedA = true;
                    yield return Wait(tm.GapBetweenAbSeconds);
                }
                if (!Interrupted)
                {
                    yield return PlayPart2Segment(t, part.Options, "B", t.FileB, 1, slotB, "segment_second", head, segB);
                    statsB = segB.Stats;
                    maxB = segB.MaxDeg;
                    playedB = true;
                }

                _recenterLocked = false;

                // preload the next pair while the participant answers
                if (i + 1 < trials.Count)
                {
                    slotA = _clips.Prepare(0, StimulusPath(trials[i + 1].FileA));
                    slotB = _clips.Prepare(1, StimulusPath(trials[i + 1].FileB));
                }

                int different = -1;
                float rt1 = 0f;
                string preference = "";
                string rt2 = "";
                if (!Interrupted)
                {
                    yield return AskYesNo("part2_q1", v => different = v, v => rt1 = v);
                }
                if (!Interrupted && different == 1)
                {
                    int? pref = null;
                    float r2 = 0f;
                    yield return AskPreference(v => pref = v, v => r2 = v);
                    if (pref.HasValue)
                    {
                        preference = pref.Value.ToString(CultureInfo.InvariantCulture);
                        rt2 = CsvLog.Format(r2);
                    }
                }
                bool skipped = Interrupted;
                WritePart2Row(t, different >= 0 ? (object)different : "", preference, different >= 0 ? (object)rt1 : "", rt2,
                    playedA ? statsA : null, playedB ? statsB : null, maxA, maxB, Round(alignS, 2), Round(startDev, 2),
                    segA.Restarts, segB.Restarts, segA.ReminderOff, segB.ReminderOff, skipped);
                if (playedA)
                {
                    WritePlayback(2, t.Index, "A", statsA);
                }
                if (playedB)
                {
                    WritePlayback(2, t.Index, "B", statsB);
                }
                _s.Log.HeadPose.AppendRows(head);
                if (skipped)
                {
                    _s.Skips++;
                    LogEvent("qa_skip", "part 2 trial " + t.Index + (_skipPart ? " and the rest of part 2" : ""));
                }
                else
                {
                    _s.Part2TrialsDone++;
                }
                yield return Wait(tm.GrayBetweenTrialsSeconds);
            }
            _s.TrialLabel = "";
        }

        // ------------------------------------------------------------------ Part 1: rating, grouped by clip
        private List<StudyTrial> _p1Seq = new List<StudyTrial>();
        private int _p1Next;
        private int _p1PreSlot;
        private StudyClipPlayer.Slot _p1Pre;

        /// <summary>Play the next trial of the Part 1 sequence (preloading the one after it) and record playback.</summary>
        private IEnumerator PlayPart1Next(StudyTrial t, StudyPartOptions o, RatingRecord rec)
        {
            if (_p1Next >= _p1Seq.Count || _p1Seq[_p1Next] != t)
            {
                LogEvent("part1_sequence_mismatch", "trial " + t.Index);
            }
            yield return _clips.WaitPrepared(_p1Pre, PrepareTimeoutSeconds);
            StudyClipPlayer.Slot current = _p1Pre;
            int currentSlot = _p1PreSlot;
            _p1Next++;
            if (_p1Next < _p1Seq.Count)
            {
                _p1PreSlot = 1 - currentSlot;
                _p1Pre = _clips.Prepare(_p1PreSlot, StimulusPath(_p1Seq[_p1Next].File));
            }
            var head = new List<object[]>();
            var stats = new PlaybackStats();
            yield return _clips.Play(current, MakeRequest(o, true, 0f,
                (ts, frame) => head.Add(HeadRow(1, t.Index, "clip", ts, frame, 0f, 0f))), stats);
            StopAdvance();
            ShowGray();
            rec.ViewSeconds = stats.ViewSeconds;
            rec.Loops = stats.Loops;
            WritePlayback(1, t.Index, "clip", stats);
            _s.Log.HeadPose.AppendRows(head);
        }

        /// <summary>Skip a trial of the sequence that will not be shown (keeps the preloading in step).</summary>
        private void SkipPart1Next(StudyTrial t)
        {
            if (_p1Next < _p1Seq.Count && _p1Seq[_p1Next] == t)
            {
                _p1Next++;
                if (_p1Next < _p1Seq.Count)
                {
                    _p1Pre = _clips.Prepare(_p1PreSlot, StimulusPath(_p1Seq[_p1Next].File));
                }
            }
        }

        /// <summary>
        /// Replay a clip from the group summary: same clip and viewing mode; the trigger or Space returns at
        /// any time (a non-looping replay also returns at the clip end). Logged in part1_replays.csv.
        /// </summary>
        private IEnumerator ReplayPart1(RatingRecord rec, StudyPartOptions o)
        {
            StudyTrial t = rec.Trial;
            int slot = 1 - _p1PreSlot;
            StudyClipPlayer.Slot s = _clips.Prepare(slot, StimulusPath(t.File));
            yield return _clips.WaitPrepared(s, PrepareTimeoutSeconds);
            var head = new List<object[]>();
            var stats = new PlaybackStats();
            string startUtc = UtcNow();
            PlayRequest req = MakeRequest(o, true, 0f, (ts, frame) => head.Add(HeadRow(1, t.Index, "replay", ts, frame, 0f, 0f)),
                null, "replay_hint");
            req.EarlyAdvance = true;
            req.MinViewSeconds = 0.05f;
            req.MaxViewSeconds = 0f;
            yield return _clips.Play(s, req, stats);
            StopAdvance();
            ShowGray();
            rec.Replays++;
            rec.ReplaysSinceRating++;
            _s.Log.Part1Replays.Append(_s.Plan.ParticipantId, t.GroupIndex, t.PositionInGroup, t.Index, t.ClipId, t.Condition,
                rec.Replays, startUtc, stats.ViewSeconds, stats.Loops, stats.Loops >= 1 ? 1 : 0);
            WritePlayback(1, t.Index, "replay", stats);
            _s.Log.HeadPose.AppendRows(head);
            LogEvent("part1_replay", (t.GroupIndex > 0 ? "group " + t.GroupIndex + " clip " + (t.PositionInGroup + 1) : "practice")
                + " trial " + t.Index + " " + CsvLog.Format(stats.ViewSeconds) + " s");
        }

        private IEnumerator RatePart1(string prefix, RatingRecord rec, bool initial)
        {
            int depth = 0, comfort = 0, artifact = 0;
            float rtD = 0f, rtC = 0f, rtA = 0f;
            yield return AskScale(prefix, "scale_depth", "depth", initial ? 0 : rec.Depth, v => depth = v, v => rtD = v);
            if (!Interrupted)
            {
                yield return AskScale(prefix, "scale_comfort", "comfort", initial ? 0 : rec.Comfort, v => comfort = v, v => rtC = v);
            }
            if (!Interrupted)
            {
                yield return AskScale(prefix, "scale_artifact", "artifact", initial ? 0 : rec.Artifact, v => artifact = v, v => rtA = v);
            }
            if (Interrupted || depth == 0 || comfort == 0 || artifact == 0)
            {
                yield break;
            }
            if (initial)
            {
                rec.SetInitial(depth, comfort, artifact);
                rec.RtDepth = rtD;
                rec.RtComfort = rtC;
                rec.RtArtifact = rtA;
            }
            else
            {
                rec.Revise(depth, comfort, artifact);
            }
        }

        private void LogRating(RatingRecord rec, string ev, bool reviewed)
        {
            StudyTrial t = rec.Trial;
            _s.Log.Part1Revisions.Append(_s.Plan.ParticipantId, t.GroupIndex, t.Index, t.ClipId, t.Condition, ev, rec.Revisions,
                reviewed ? 1 : 0, rec.Depth, rec.Comfort, rec.Artifact, UtcNow());
        }

        private static object Score(int v)
        {
            return v > 0 ? (object)v : "";
        }

        private void WritePart1Row(RatingRecord rec)
        {
            StudyTrial t = rec.Trial;
            bool rated = rec.Rated;
            _s.Log.Part1.Append(_s.Plan.ParticipantId, t.Block, t.Index, t.GroupIndex, t.PositionInGroup, t.ClipId, t.Condition,
                t.Repetition, Score(rec.Depth), Score(rec.Comfort), Score(rec.Artifact),
                rated ? (object)rec.RtDepth : "", rated ? (object)rec.RtComfort : "", rated ? (object)rec.RtArtifact : "",
                Score(rec.InitialDepth), Score(rec.InitialComfort), Score(rec.InitialArtifact), rec.Revisions,
                rec.ViewSeconds > 0f ? (object)rec.ViewSeconds : "", rec.Loops, rec.Replays, rec.Skipped ? 1 : 0);
            if (!rec.Skipped)
            {
                _s.Part1TrialsDone++;
            }
        }

        private IEnumerator Part1Step(StudyPart part)
        {
            ShowGray();
            bool go = false;
            yield return ShowHmdPanel("part1_title", "part1_intro", "start", () => go = true, () => go);
            Recenter();
            StudyTiming tm = _s.Plan.Timing;
            StudyPartOptions o = part.Options;
            _p1Seq = part.Trials.ToList();
            if (_p1Seq.Count == 0)
            {
                yield break;
            }
            _p1Next = 0;
            _p1PreSlot = 0;
            _p1Pre = _clips.Prepare(0, StimulusPath(_p1Seq[0].File));
            List<StudyTrial> single = part.UngroupedTrials;
            List<List<StudyTrial>> groups = part.Groups();

            // practice trials: rated one at a time ("next group" skips the rest of the practice)
            if (single.Count > 0 && !Interrupted)
            {
                // user 11-19: say explicitly that these clips are practice
                bool practiceGo = false;
                yield return ShowHmdPanelText(T("practice_title"), TF("practice_intro", single.Count), "start",
                    () => practiceGo = true, () => practiceGo);
            }
            var practiceRecs = new List<RatingRecord>();
            for (int i = 0; i < single.Count; i++)
            {
                StudyTrial t = single[i];
                var rec = new RatingRecord { Trial = t };
                practiceRecs.Add(rec);
                if (!Interrupted)
                {
                    _s.TrialLabel = "practice " + (i + 1) + "/" + single.Count + " " + t.ClipId + " " + t.Condition;
                    yield return PlayPart1Next(t, o, rec);
                    string prefix = t.Block == "training" ? T("practice_label") + "  " : "";
                    if (!Interrupted)
                    {
                        yield return RatePart1(prefix, rec, true);
                    }
                }
                else
                {
                    SkipPart1Next(t);
                }
                rec.Skipped = Interrupted;
                if (rec.Rated)
                {
                    LogRating(rec, "initial", false);
                }
                _s.Part1Ratings[t.Index] = rec;
                if (i + 1 < single.Count)
                {
                    yield return Wait(tm.GrayBetweenTrialsSeconds);
                }
            }
            if (practiceRecs.Count > 0)
            {
                // user 11-19: the practice ends with the same summary (replay, re-rate) as the formal groups
                yield return SummaryLoop(practiceRecs, o, T("practice_summary_title"), "practice",
                    "practice", T("practice_label") + "  ");
                bool practiceSkipped = Interrupted;
                foreach (RatingRecord rec in practiceRecs)
                {
                    if (practiceSkipped)
                    {
                        rec.Skipped = true;
                    }
                    WritePart1Row(rec);
                    if (rec.Skipped)
                    {
                        _s.Skips++;
                    }
                }
                if (practiceSkipped)
                {
                    LogEvent("qa_skip", "part 1 practice");
                }
                else
                {
                    LogEvent("part1_practice_done", "revisions " + practiceRecs.Sum(r => r.Revisions)
                        + " replays " + practiceRecs.Sum(r => r.Replays));
                }
                yield return Wait(tm.GrayBetweenTrialsSeconds);
            }
            _skipGroup = false;

            // groups: one clip under every condition, then the summary with replay and re-rating
            for (int gi = 0; gi < groups.Count; gi++)
            {
                List<StudyTrial> g = groups[gi];
                var recs = g.Select(t => new RatingRecord { Trial = t }).ToList();
                if (!_skipPart)
                {
                    _skipGroup = false;
                    bool next = false;
                    _s.TrialLabel = "group " + (gi + 1) + "/" + groups.Count + " " + g[0].ClipId + " (" + g[0].Block + ")";
                    if (gi == 0 && single.Count > 0)
                    {
                        // user 11-19: the first group after the practice announces the main session
                        yield return ShowHmdPanelText(T("formal_title"), TF("formal_intro", groups.Count), "start", () => next = true, () => next);
                    }
                    else
                    {
                        yield return ShowHmdPanelText(TF("group_title", gi + 1, groups.Count), T("group_intro"), "start", () => next = true, () => next);
                    }
                }
                for (int k = 0; k < g.Count; k++)
                {
                    StudyTrial t = g[k];
                    RatingRecord rec = recs[k];
                    if (Interrupted)
                    {
                        SkipPart1Next(t);
                        continue;
                    }
                    _s.TrialLabel = "group " + (gi + 1) + "/" + groups.Count + " clip " + (k + 1) + "/" + g.Count + " " + t.Block + " " + t.ClipId + " " + t.Condition;
                    yield return PlayPart1Next(t, o, rec);
                    if (!Interrupted)
                    {
                        yield return RatePart1("", rec, true);
                    }
                    if (rec.Rated)
                    {
                        LogRating(rec, "initial", false);
                    }
                    _s.Part1Ratings[t.Index] = rec;
                    if (k + 1 < g.Count)
                    {
                        yield return Wait(tm.GrayBetweenTrialsSeconds);
                    }
                }
                yield return SummaryLoop(recs, o, T("group_summary_title"), "group " + (gi + 1) + "/" + groups.Count,
                    "group " + g[0].GroupIndex, "");
                bool skipped = Interrupted;
                foreach (RatingRecord rec in recs)
                {
                    rec.Skipped = skipped;
                    WritePart1Row(rec);
                }
                if (skipped)
                {
                    _s.Skips++;
                    LogEvent("qa_skip", "part 1 group " + g[0].GroupIndex + " " + g[0].ClipId + (_skipPart ? " (rest of part 1)" : ""));
                }
                else
                {
                    LogEvent("part1_group_done", "group " + g[0].GroupIndex + " " + g[0].ClipId + " revisions " + recs.Sum(r => r.Revisions)
                        + " replays " + recs.Sum(r => r.Replays));
                }
                yield return Wait(tm.GrayBetweenTrialsSeconds);
            }
            _s.TrialLabel = "";
        }

        /// <summary>
        /// Summary page repeated until Continue: replay a clip or re-rate it (formal groups and, since 11-19, the
        /// practice). label goes to the status bar, logName to events.csv, ratePrefix before the re-rating title.
        /// </summary>
        private IEnumerator SummaryLoop(List<RatingRecord> recs, StudyPartOptions o, string title, string label,
            string logName, string ratePrefix)
        {
            while (!Interrupted)
            {
                int choice = -2;
                _s.TrialLabel = label + " summary";
                yield return GroupSummary(recs, o.AllowReviewOnRerate, title, v => choice = v);
                if (Interrupted || choice == -1)
                {
                    break;
                }
                if (choice >= 100)
                {
                    yield return ReplayPart1(recs[choice - 100], o);
                    continue;
                }
                RatingRecord rec = recs[choice];
                bool reviewed = rec.ReplaysSinceRating > 0;
                int before = rec.Revisions;
                yield return RatePart1(ratePrefix + TF("group_rerate_prefix", choice + 1), rec, false);
                if (rec.Revisions > before)
                {
                    LogRating(rec, "revision", reviewed);
                    LogEvent("part1_rerate", logName + " clip " + (choice + 1) + " trial " + rec.Trial.Index
                        + (reviewed ? " (after replay)" : ""));
                }
            }
        }

        /// <summary>
        /// End-of-group summary: "Clip k" with its three ratings (no condition names), Replay and Re-rate per
        /// clip, Continue. result: k = re-rate clip k, 100 + k = replay clip k, -1 = continue.
        /// </summary>
        private IEnumerator GroupSummary(List<RatingRecord> recs, bool allowReplay, string title, Action<int> result)
        {
            int answer = -2;
            _ui.ClearHmd();
            _ui.SetHmdSize(1760, 420 + 118 * recs.Count);
            _ui.PlaceHmdCanvas(_rig.HeadPosition, _rig.YawForward(), PanelDistance);
            _ui.Title(_ui.HmdContent, title);
            _ui.Note(_ui.HmdContent, T("group_summary_hint"), TextAnchor.MiddleCenter);
            for (int k = 0; k < recs.Count; k++)
            {
                int idx = k;
                RatingRecord r = recs[k];
                RectTransform row = _ui.NewRow(_ui.HmdContent, 24, TextAnchor.MiddleCenter, 100);
                string text = TF("group_segment", k + 1) + "      " + T("scale_depth_short") + " " + r.Depth + "     "
                    + T("scale_comfort_short") + " " + r.Comfort + "     " + T("scale_artifact_short") + " " + r.Artifact;
                Text label = _ui.Body(row, text, TextAnchor.MiddleLeft, 36);
                label.gameObject.AddComponent<LayoutElement>().preferredWidth = 1000;
                if (allowReplay)
                {
                    _ui.HmdButton(row, T("group_replay"), () => answer = 100 + idx, 200, 88, 34, ButtonKind.Secondary, "Btn_Replay_" + (k + 1));
                }
                _ui.HmdButton(row, T("group_rerate"), () => answer = idx, 200, 88, 34, ButtonKind.Secondary, "Btn_Edit_" + (k + 1));
                if (k < 9)
                {
                    _ui.AddShortcut(KeyCode.Alpha1 + k, () => answer = idx);
                }
            }
            _ui.Spacer(_ui.HmdContent, 8);
            RectTransform cont = _ui.NewRow(_ui.HmdContent, 0, TextAnchor.MiddleCenter, 104);
            _ui.HmdButton(cont, T("continue"), () => answer = -1, 380, 100, 38, ButtonKind.Primary, "Btn_Continue");
            _ui.AddShortcut(KeyCode.Space, () => answer = -1);
            _ui.AddShortcut(KeyCode.Return, () => answer = -1);
            while (answer == -2 && !Interrupted)
            {
                yield return null;
            }
            _ui.ShowHmd(false);
            result(answer);
        }

        // ------------------------------------------------------------------ post-questionnaire
        private IEnumerator PostQuestionnaireStep()
        {
            // user 11-19: only the open question (feedback, anything that felt uncomfortable); answering is optional
            ShowGray();
            ShowHmdNotice("ssq_hmd_title", "ssq_hmd");
            bool done = false;
            _ui.ClearDesktop();
            Transform c = _ui.DesktopContent;
            _ui.Title(c, T("post_title"));
            _ui.Spacer(c, 8);
            _ui.Body(c, T("post_comments"));
            InputField comments = _ui.NewInput(c, "", 26, 1400, 220, true, "Input_Comments");
            _ui.Spacer(c, 8);
            RectTransform row = _ui.NewRow(c, 0, TextAnchor.MiddleCenter, 72);
            _ui.NewButton(row, T("submit"), 30, () => done = true, 380, 68, ButtonKind.Primary, "Btn_Submit");
            while (!done && !Interrupted)
            {
                yield return null;
            }
            if (done)
            {
                _s.Log.WriteJson("postquestionnaire.json", new Dictionary<string, object>
                {
                    { "participant_id", _s.Plan.ParticipantId },
                    { "comments", comments.text },
                    { "timestamp_utc", UtcNow() },
                });
            }
            _ui.ShowDesktopPanel(false);
        }

        // ------------------------------------------------------------------ headset panels
        private IEnumerator ShowHmdPanel(string titleKey, string bodyKey, string buttonKey, Action onClick, Func<bool> isDone)
        {
            return ShowHmdPanelText(T(titleKey), string.IsNullOrEmpty(bodyKey) ? "" : T(bodyKey), buttonKey, onClick, isDone);
        }

        /// <summary>Headset panel with a title, a body (multi-line bodies as a left-aligned block) and one button.</summary>
        private IEnumerator ShowHmdPanelText(string title, string body, string buttonKey, Action onClick, Func<bool> isDone)
        {
            _ui.ClearHmd();
            _ui.SetHmdSize(1700, 1000);
            _ui.PlaceHmdCanvas(_rig.HeadPosition, _rig.YawForward(), PanelDistance);
            _ui.Title(_ui.HmdContent, title);
            if (!string.IsNullOrEmpty(body))
            {
                _ui.Spacer(_ui.HmdContent, 4);
                if (body.Contains("\n"))
                {
                    _ui.BodyBlock(_ui.HmdContent, body, 1440);
                }
                else
                {
                    Text b = _ui.Body(_ui.HmdContent, body, TextAnchor.MiddleCenter);
                    b.lineSpacing = 1.3f;
                }
            }
            _ui.Spacer(_ui.HmdContent, 16);
            RectTransform row = _ui.NewRow(_ui.HmdContent, 10, TextAnchor.MiddleCenter, 114);
            _ui.HmdButton(row, T(buttonKey), onClick, 380, 104, 38, ButtonKind.Primary, "Btn_Continue");
            _ui.AddShortcut(KeyCode.Space, onClick);
            _ui.AddShortcut(KeyCode.Return, onClick);
            while (!isDone() && !Interrupted)
            {
                yield return null;
            }
            _ui.ShowHmd(false);
        }

        /// <summary>Headset message without buttons: a large accent title and an optional short line.</summary>
        private void ShowHmdNotice(string titleKey, string bodyKey)
        {
            _ui.ClearHmd();
            _ui.SetHmdSize(1600, 600);
            _ui.PlaceHmdCanvas(_rig.HeadPosition, _rig.YawForward(), PanelDistance);
            _ui.Notice(_ui.HmdContent, T(titleKey), string.IsNullOrEmpty(bodyKey) ? null : T(bodyKey));
        }

        private IEnumerator AskYesNo(string questionKey, Action<int> result, Action<float> rt)
        {
            return AskTwo(questionKey, "yes", "no", result, rt);
        }

        /// <summary>Question with two answers: 1 for the first button (Y), 0 for the second (N); -1 when skipped.</summary>
        private IEnumerator AskTwo(string questionKey, string firstKey, string secondKey, Action<int> result, Action<float> rt)
        {
            int answer = -1;
            float shown = Time.realtimeSinceStartup;
            _ui.ClearHmd();
            _ui.SetHmdSize(1500, 560);
            _ui.PlaceHmdCanvas(_rig.HeadPosition, _rig.YawForward(), PanelDistance);
            _ui.Title(_ui.HmdContent, T(questionKey));
            _ui.Spacer(_ui.HmdContent, 10);
            RectTransform row = _ui.NewRow(_ui.HmdContent, 60, TextAnchor.MiddleCenter, 140);
            _ui.HmdButton(row, T(firstKey), () => answer = 1, 380, 128, 42, ButtonKind.Secondary, "Btn_Yes");
            _ui.HmdButton(row, T(secondKey), () => answer = 0, 380, 128, 42, ButtonKind.Secondary, "Btn_No");
            _ui.AddShortcut(KeyCode.Y, () => answer = 1);
            _ui.AddShortcut(KeyCode.N, () => answer = 0);
            while (answer < 0 && !Interrupted)
            {
                yield return null;
            }
            rt(Time.realtimeSinceStartup - shown);
            result(answer);
            _ui.ShowHmd(false);
        }

        private IEnumerator AskPreference(Action<int?> result, Action<float> rt)
        {
            int? answer = null;
            float shown = Time.realtimeSinceStartup;
            _ui.ClearHmd();
            _ui.SetHmdSize(1840, 600);
            _ui.PlaceHmdCanvas(_rig.HeadPosition, _rig.YawForward(), PanelDistance);
            _ui.Title(_ui.HmdContent, T("part2_q2"));
            _ui.Spacer(_ui.HmdContent, 10);
            RectTransform row = _ui.NewRow(_ui.HmdContent, 10, TextAnchor.MiddleCenter, 170);
            string[] keys = { "pref_m3", "pref_m2", "pref_m1", "pref_0", "pref_p1", "pref_p2", "pref_p3" };
            for (int k = 0; k < keys.Length; k++)
            {
                int value = k - 3;
                _ui.HmdButton(row, (value > 0 ? "+" : "") + value + "\n" + T(keys[k]), () => answer = value, 232, 160, 30,
                    ButtonKind.Secondary, "Btn_Pref_" + (k + 1));
                _ui.AddShortcut(KeyCode.Alpha1 + k, () => answer = value);
            }
            while (answer == null && !Interrupted)
            {
                yield return null;
            }
            rt(Time.realtimeSinceStartup - shown);
            result(answer);
            _ui.ShowHmd(false);
        }

        /// <summary>
        /// Five-level scale shown as a vertical list from 5 (top) to 1. When re-rating, the current answer is
        /// shown as selected. 0 when skipped.
        /// </summary>
        private IEnumerator AskScale(string prefix, string titleKey, string optionPrefix, int current, Action<int> result, Action<float> rt)
        {
            int answer = 0;
            float shown = Time.realtimeSinceStartup;
            _ui.ClearHmd();
            _ui.SetHmdSize(1500, 1000);
            _ui.PlaceHmdCanvas(_rig.HeadPosition, _rig.YawForward(), PanelDistance);
            _ui.Title(_ui.HmdContent, prefix + T(titleKey), StudyTheme.Dark.TitleSize - 6);
            _ui.Spacer(_ui.HmdContent, 4);
            for (int score = 5; score >= 1; score--)
            {
                int s = score;
                RectTransform row = _ui.NewRow(_ui.HmdContent, 0, TextAnchor.MiddleCenter, 116);
                Button b = _ui.HmdButton(row, s + "    " + T(optionPrefix + "_" + s), () => answer = s, 1200, 108, 38,
                    ButtonKind.Secondary, "Btn_Score_" + s);
                if (s == current)
                {
                    b.GetComponent<StudyButtonStyle>().SetSelected(true);
                }
                _ui.AddShortcut(KeyCode.Alpha0 + s, () => answer = s);
                _ui.AddShortcut(KeyCode.Keypad0 + s, () => answer = s);
            }
            while (answer == 0 && !Interrupted)
            {
                yield return null;
            }
            rt(Time.realtimeSinceStartup - shown);
            result(answer);
            _ui.ShowHmd(false);
        }
    }
}
