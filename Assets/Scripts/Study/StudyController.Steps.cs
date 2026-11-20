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
    /// Flow steps of the study session (SSQ, break, headset fitting, stereo test, natural viewing range, gaze
    /// training, Parts 1 and 2).
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
                _ui.Banner(c, T(noticeKey));
            }
            if (isBreak)
            {
                timer = _ui.Colored(c, "", StudyTheme.Light.TextSecondary, TextAnchor.MiddleCenter, 28);
            }
            _ui.Title(c, T("ssq_title"));
            _ui.Secondary(c, T("ssq_instruction"), TextAnchor.MiddleCenter);
            string[] levels = { T("ssq_level_0"), T("ssq_level_1"), T("ssq_level_2"), T("ssq_level_3") };
            var rows = new List<StudyUi.ChoiceRow>();
            RectTransform[] cols = _ui.NewColumns(c, 2, 60, 8);
            int half = (StudySchema.SsqItems.Length + 1) / 2;
            for (int i = 0; i < StudySchema.SsqItems.Length; i++)
            {
                string key = "ssq_" + StudySchema.SsqItems[i];
                rows.Add(_ui.NewChoiceRow(cols[i < half ? 0 : 1], T(key), StudyTexts.Note(key), levels, 24, 21, 248, 98, 52));
            }
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
                    timer.text = BreakTimerText(left, 0);
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
            // fixed height: the line is tall while the seconds are shown large, and the page must not jump when it ends
            var timerSlot = timer.gameObject.AddComponent<LayoutElement>();
            timerSlot.minHeight = 104;
            timerSlot.preferredHeight = 104;
            _ui.Spacer(c, 24);
            RectTransform row = _ui.NewRow(c, 0, TextAnchor.MiddleCenter, 76);
            Button go = _ui.NewButton(row, T("break_ready"), 30, () => ready = true, 420, 72, ButtonKind.Primary, "Btn_Continue");
            while (!ready && !Interrupted)
            {
                float left = minBreakSeconds - (Time.realtimeSinceStartup - t0);
                timer.text = BreakTimerText(left, 76);
                go.interactable = left <= 0;
                yield return null;
            }
            float breakS = Time.realtimeSinceStartup - t0;
            _s.Session["break_s"] = Round(breakS, 1);
            WriteSession();
            LogEvent("break_end", CsvLog.Format(breakS) + " s" + (ready ? "" : " (skipped)"));
            _ui.ShowDesktopPanel(false);
        }

        /// <summary>
        /// Break countdown with the remaining seconds bold in the accent colour (large when digitSize is given);
        /// the closing line once the minimum break is over.
        /// </summary>
        private string BreakTimerText(float left, int digitSize)
        {
            StudyTheme.Palette p = StudyTheme.Light;
            if (left <= 0f)
            {
                return StudyMarkup.ToRich(T("break_timer_done"), p.Emphasis);
            }
            string seconds = StudyTheme.Tint(Mathf.CeilToInt(left).ToString(CultureInfo.InvariantCulture), p.Emphasis, true);
            if (digitSize > 0)
            {
                seconds = "<size=" + digitSize.ToString(CultureInfo.InvariantCulture) + ">" + seconds + "</size>";
            }
            return StudyMarkup.ToRich(TF("break_timer", seconds), p.Emphasis);
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
                _ui.Strong(_ui.HmdContent, (trial.Block == "practice" ? StudyMarkup.Em(T("practice_label")) + "  " : "") + T("stereo_question"),
                    TextAnchor.MiddleCenter);
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
        /// result(alignment time in s, deviation at the start in deg). dwellSeconds below 0 = the plan's dwell;
        /// showDeviation false hides the "off by x deg / hold still" line (natural viewing range, user 11-20).
        /// </summary>
        private IEnumerator AlignToReference(float refYaw, float refPitch, string cueText, float panelDrop, Action<float, float> result,
            float dwellSeconds = -1f, bool showDeviation = true)
        {
            AlignmentPlan a = _s.Plan.Alignment;
            float dwell = dwellSeconds >= 0f ? dwellSeconds : a.DwellSeconds;
            Quaternion refRot = Quaternion.Euler(-refPitch, refYaw, 0f);
            ShowCue(refRot * Vector3.forward);
            _alignRing.SetActive(true);
            SetAlignFeedback(false, 0f);
            _ui.ClearHmd();
            _ui.SetHmdSize(1500, 270);
            _ui.PlaceHmdCanvas(_rig.HeadPosition, Quaternion.Euler(0f, refYaw, 0f) * Vector3.forward, PanelDistance, panelDrop);
            _ui.Body(_ui.HmdContent, cueText, TextAnchor.MiddleCenter);
            Text devText = showDeviation ? _ui.Secondary(_ui.HmdContent, "", TextAnchor.MiddleCenter, 34) : null;
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
                float progress = !inside ? 0f : (dwell <= 0f ? 1f : (now - insideSince) / dwell);
                SetAlignFeedback(inside, progress);
                if (devText != null)
                {
                    string devLine = TF("align_deviation", dev.ToString("0.0", CultureInfo.InvariantCulture));
                    devText.text = inside ? StudyTheme.Tint(devLine + "      " + T("align_hold"), StudyTheme.Dark.Success, true) : devLine;
                }
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

        // ------------------------------------------------------------------ natural viewing range (before the gaze training)
        private void BuildEyeTarget(EyeRangePlan er)
        {
            if (_eyeTarget != null)
            {
                Destroy(_eyeTarget);
            }
            _eyeTarget = Track(GameObject.CreatePrimitive(PrimitiveType.Sphere));
            Destroy(_eyeTarget.GetComponent<Collider>());
            _eyeTarget.name = "EyeRangeTarget";
            _eyeTarget.transform.localScale = Vector3.one * 2f * PanelDistance * Mathf.Tan(0.5f * er.DotSizeDeg * Mathf.Deg2Rad);
            if (_eyeTargetMat == null)
            {
                _eyeTargetMat = Track(new Material(ColorShader));
            }
            _eyeTarget.GetComponent<Renderer>().sharedMaterial = _eyeTargetMat;
            _eyeTarget.SetActive(false);
        }

        /// <summary>The coloured dot at eccentricity eccDeg along directionDeg (0 deg = top, clockwise, as the gaze-training ring).</summary>
        private void ShowEyeTarget(EyeRangePlan er, float directionDeg, float eccDeg)
        {
            Vector3 dir = EyeRangeDirection(er.ReferenceYawDeg, er.ReferencePitchDeg, directionDeg, eccDeg);
            _eyeTarget.transform.position = _rig.HeadPosition + dir * PanelDistance;
            if (!_eyeTarget.activeSelf)
            {
                Color[] palette = StudyTheme.GazeDotColors;
                int k = Math.Max(0, er.DirectionsDeg.IndexOf(directionDeg));
                _eyeTargetMat.SetColor("_Color", palette[k % palette.Length]);
                _eyeTarget.SetActive(true);
            }
            _eyeRefYaw = er.ReferenceYawDeg;
            _eyeRefPitch = er.ReferencePitchDeg;
            _eyeDirDeg = directionDeg;
            _eyeDotEccDeg = eccDeg;
        }

        private void HideEyeTarget()
        {
            if (_eyeTarget != null)
            {
                _eyeTarget.SetActive(false);
            }
        }

        /// <summary>
        /// Natural viewing range measurement (user decisions 11-20), before the gaze practice, so the participant has not
        /// been told to keep the head still, and its texts do not mention it. Continuous outward sweeps (option A, user
        /// 11-20: natural viewing is a continuous search). Per sweep: the head is aligned to the central cross (alignment
        /// tolerance, the step's short dwell, no deviation text), the cross disappears, and a dot starts at the cross and
        /// moves outward along the sweep direction at a constant angular speed up to the maximum eccentricity, then
        /// disappears; no button press is needed. Every displayed frame goes to eyerange_trace.csv (dot eccentricity,
        /// head yaw and pitch relative to the reference, head rotation since the sweep onset and its component toward
        /// the sweep direction); eyerange.csv gets one summary row per sweep (head onset eccentricity = dot
        /// eccentricity when the rotation first exceeds the threshold, or censored). Both are written at the end of
        /// each sweep. Manual Recenter is locked from the first alignment to the last sweep. A QA skip writes the
        /// remaining sweeps with skipped = 1 (a sweep stopped midway keeps its frames). The provisional rule then gives
        /// a and the PAw correction radius for Part 2; when the step is disabled or skipped, the plan default (15 deg)
        /// is used and logged.
        /// </summary>
        private IEnumerator EyeRangeStep()
        {
            EyeRangePlan er = _s.Plan.EyeRange;
            if (!er.Enabled || er.Sweeps.Count == 0)
            {
                LogEvent("eyerange_skipped", "disabled in plan");
                FinishEyeRange("default_disabled");
                yield break;
            }
            ShowGray();
            bool go = false;
            yield return ShowHmdPanelText(T("eyerange_title"), TF("eyerange_intro", er.Sweeps.Count), "start", () => go = true, () => go);
            Recenter();
            BuildEyeTarget(er);
            string pid = _s.Plan.ParticipantId;
            _recenterLocked = true; // user 11-20: as in the gaze training, no manual recenter while the head is measured
            for (int i = 0; i < er.Sweeps.Count; i++)
            {
                EyeRangeSweep sw = er.Sweeps[i];
                var res = new EyeRangeSweepResult
                {
                    Sweep = sw.Index, DirectionDeg = sw.DirectionDeg, Repeat = sw.Repeat, SpeedDegPerSecond = er.SpeedDegPerSecond, Skipped = true,
                };
                var trace = new List<object[]>();
                float alignS = 0f, startDev = 0f;
                if (!Interrupted)
                {
                    _s.TrialLabel = (i + 1) + "/" + er.Sweeps.Count + " dir " + sw.DirectionDeg.ToString("0", CultureInfo.InvariantCulture)
                        + " rep " + (sw.Repeat + 1);
                    yield return AlignToReference(er.ReferenceYawDeg, er.ReferencePitchDeg, T("eyerange_cue"), -0.42f,
                        (t, dv) => { alignS = t; startDev = dv; }, er.AlignDwellSeconds, false);
                }
                if (!Interrupted)
                {
                    _rig.HeadAngles(out float y0, out float p0, out _);
                    float onsetYaw = StudyRig.WrapDegrees(y0 - er.ReferenceYawDeg);
                    float onsetPitch = p0 - er.ReferencePitchDeg;
                    res.OnsetDevDeg = (float)StudySchema.AngleFromReference(onsetYaw, onsetPitch);
                    var eccs = new List<float>();
                    var rots = new List<float>();
                    float t0 = Time.realtimeSinceStartup;
                    _eyeSweeping = true;
                    bool reachedMax = false; // the sweep ended normally (at the head onset or at the maximum)
                    bool stoppedEarly = false;
                    while (!Interrupted)
                    {
                        float ts = Time.realtimeSinceStartup - t0;
                        float ecc = Mathf.Min(er.SpeedDegPerSecond * ts, er.MaxEccentricityDeg);
                        ShowEyeTarget(er, sw.DirectionDeg, ecc);
                        _rig.HeadAngles(out float yaw, out float pitch, out _);
                        float hy = StudyRig.WrapDegrees(yaw - er.ReferenceYawDeg);
                        float hp = pitch - er.ReferencePitchDeg;
                        float rot = (float)StudySchema.GreatCircleDeg(onsetYaw, onsetPitch, hy, hp);
                        float along = (float)StudySchema.RotationAlongDirection(onsetYaw, onsetPitch, hy, hp, sw.DirectionDeg);
                        trace.Add(new object[]
                        {
                            pid, sw.Index, Round(sw.DirectionDeg, 1), sw.Repeat, Round(ts, 4), Round(ecc, 3), Round(hy, 3), Round(hp, 3),
                            Round(rot, 3), Round(along, 3),
                        });
                        eccs.Add(ecc);
                        rots.Add(rot);
                        if (er.StopAtOnset && rot > er.HeadThresholdDeg)
                        {
                            // user 11-20: the head-onset criterion is met; the dot disappears right now
                            reachedMax = true;
                            stoppedEarly = true;
                            break;
                        }
                        if (ecc >= er.MaxEccentricityDeg)
                        {
                            reachedMax = true;
                            break;
                        }
                        yield return null;
                    }
                    _eyeSweeping = false;
                    HideEyeTarget();
                    if (reachedMax)
                    {
                        res.HeadOnsetEccDeg = EyeRangePlan.OnsetEccentricity(eccs, rots, er.HeadThresholdDeg, out bool censored);
                        res.Censored = censored;
                        res.StoppedEarly = stoppedEarly;
                        res.MaxHeadRotDeg = rots.Count > 0 ? rots.Max() : 0f;
                        res.Skipped = false;
                    }
                }
                _s.EyeRangeSweeps.Add(res);
                _s.Log.EyeRangeTrace.AppendRows(trace);
                bool sk = res.Skipped;
                _s.Log.EyeRange.Append(pid, res.Sweep, Round(res.DirectionDeg, 1), res.Repeat, Round(res.SpeedDegPerSecond, 3),
                    sk ? (object)"" : Round(res.OnsetDevDeg, 2), sk || res.Censored ? (object)"" : Round(res.HeadOnsetEccDeg, 2),
                    sk ? (object)"" : (res.Censored ? 1 : 0), sk ? (object)"" : (res.StoppedEarly ? 1 : 0),
                    sk ? (object)"" : Round(res.MaxHeadRotDeg, 2), sk ? 1 : 0);
                if (!sk)
                {
                    LogEvent("eyerange_sweep", string.Format(CultureInfo.InvariantCulture,
                        "sweep {0} dir {1:0} rep {2}: aligned {3:0.00} s (start dev {4:0.0} deg), onset dev {5:0.00} deg, "
                        + "head onset at {6} deg{7}, max rotation {8:0.00} deg, {9} frames", res.Sweep, res.DirectionDeg, res.Repeat + 1,
                        alignS, startDev, res.OnsetDevDeg, res.Censored ? "censored" : res.HeadOnsetEccDeg.ToString("0.00", CultureInfo.InvariantCulture),
                        res.StoppedEarly ? " (stopped there)" : "", res.MaxHeadRotDeg, trace.Count));
                }
                if (!Interrupted && i + 1 < er.Sweeps.Count)
                {
                    yield return Wait(er.GapSeconds);
                }
            }
            _eyeSweeping = false;
            _recenterLocked = false;
            HideEyeTarget();
            _s.TrialLabel = "";
            bool skipped = _s.EyeRangeSweeps.Any(x => x.Skipped);
            if (skipped)
            {
                _s.Skips++;
                LogEvent("qa_skip", "eyerange: " + _s.EyeRangeSweeps.Count(x => x.Skipped) + " sweep(s) skipped");
            }
            FinishEyeRange(skipped ? "default_skipped" : "measured");
            if (!Interrupted)
            {
                yield return EyeRangeResultPage(er);
            }
        }

        // ------------------------------------------------------------------ natural viewing range: result page
        /// <summary>
        /// Result page (user 11-20): in the headset, mirrored on the desktop for the experimenter, a diagram of the
        /// directions around a centre point with a radial bar at each direction's eye-only range (the median of its
        /// sweeps, "≥40°" when censored), reference rings every 10 deg, and one line with a. The values are those
        /// written to session.json "eye_range" (per_direction.shown, a_shown). The step ends when the participant
        /// presses "Next" (trigger on the button, or Space); a QA skip also ends it. The display and confirmation
        /// times go to session.json and events.csv.
        /// </summary>
        private IEnumerator EyeRangeResultPage(EyeRangePlan er)
        {
            var block = (Dictionary<string, object>)_s.Session["eye_range"];
            var perDir = ((List<object>)block["per_direction"]).Cast<Dictionary<string, object>>()
                .Select(m => new RangeValue
                {
                    DirectionDeg = (float)Convert.ToDouble(m["direction_deg"], CultureInfo.InvariantCulture),
                    ValueDeg = m["median_head_onset_ecc_deg"] == null ? double.NaN : Convert.ToDouble(m["median_head_onset_ecc_deg"], CultureInfo.InvariantCulture),
                    Shown = (string)m["shown"],
                }).ToList();
            string aShown = (string)block["a_shown"];
            ShowGray();

            // experimenter mirror on the desktop (no button: the participant ends the page)
            _ui.ClearDesktop();
            Transform c = _ui.DesktopContent;
            _ui.Title(c, T("eyerange_result_title"));
            BuildRangeDiagram(c, 520f, perDir, er.MaxEccentricityDeg);
            _ui.Strong(c, TF("eyerange_result_line", aShown), TextAnchor.MiddleCenter);
            _ui.Note(c, T("eyerange_result_desktop"), TextAnchor.MiddleCenter);

            bool next = false;
            _ui.ClearHmd();
            _ui.SetHmdSize(1400, 1180);
            _ui.PlaceHmdCanvas(_rig.HeadPosition, _rig.YawForward(), PanelDistance);
            Transform h = _ui.HmdContent;
            _ui.Title(h, T("eyerange_result_title"));
            BuildRangeDiagram(h, 640f, perDir, er.MaxEccentricityDeg);
            _ui.Strong(h, TF("eyerange_result_line", aShown), TextAnchor.MiddleCenter);
            _ui.Note(h, T("eyerange_result_note"), TextAnchor.MiddleCenter);
            RectTransform row = _ui.NewRow(h, 10, TextAnchor.MiddleCenter, 114);
            _ui.HmdButton(row, T("next"), () => next = true, 380, 104, 38, ButtonKind.Primary, "Btn_Continue");
            _ui.AddShortcut(KeyCode.Space, () => next = true);
            _ui.AddShortcut(KeyCode.Return, () => next = true);

            float t0 = Time.realtimeSinceStartup;
            string shownUtc = UtcNow();
            block["result_shown_utc"] = shownUtc;
            WriteSession();
            LogEvent("eyerange_result_shown", "a " + aShown + ", PAw theta " + StudyPlan.FormatTheta(_s.PawThetaDeg) + " deg; "
                + string.Join(" ", perDir.Select(v => CsvLog.Format(v.DirectionDeg) + ":" + v.Shown)));
            while (!next && !Interrupted)
            {
                yield return null;
            }
            float onPage = Time.realtimeSinceStartup - t0;
            block["result_confirm_utc"] = next ? UtcNow() : null;
            block["result_time_on_page_s"] = Round(onPage, 2);
            WriteSession();
            LogEvent(next ? "eyerange_result_confirmed" : "eyerange_result_skipped", CsvLog.Format(Round(onPage, 2)) + " s on the page");
            _ui.ShowHmd(false);
            _ui.ShowDesktopPanel(false);
        }

        private sealed class RangeValue
        {
            public float DirectionDeg;
            public double ValueDeg;
            public string Shown;
        }

        /// <summary>
        /// Diagram of the eye-only range: rings and spokes (one generated texture), a radial bar and an end dot per
        /// direction (0 deg = up, clockwise as seen by the participant), the shown value beside each bar, ring labels.
        /// Built from plain images under a fixed-size slot of the page layout.
        /// </summary>
        private void BuildRangeDiagram(Transform parent, float size, List<RangeValue> values, float maxDeg)
        {
            StudyTheme.Palette p = _ui.PaletteFor(parent);
            RectTransform slot = StudyUi.NewRect("RangeDiagram", parent);
            var le = slot.gameObject.AddComponent<LayoutElement>();
            le.minHeight = le.preferredHeight = size;
            le.minWidth = le.preferredWidth = size;
            float outer = 0.40f * size; // radius of the maximum eccentricity
            List<float> rings = EyeRangeShapes.RingDegrees(maxDeg);

            RectTransform ringRect = Child(slot, "Rings", Vector2.zero, new Vector2(2f * outer + 8f, 2f * outer + 8f));
            Image ringImg = ringRect.gameObject.AddComponent<Image>();
            ringImg.sprite = RangeRingSprite(rings.Select(r => r / maxDeg).ToList(), values.Count);
            ringImg.color = StudyTheme.WithAlpha(p.TextMuted, 0.75f);
            ringImg.raycastTarget = false;
            foreach (float r in rings)
            {
                float rr = outer * r / maxDeg;
                Vector2 at = Polar(rr, 22.5f) + new Vector2(18f, 6f);
                Label(slot, r.ToString("0.#", CultureInfo.InvariantCulture) + "°", at, p.NoteSize - 2, p.TextMuted, false);
            }
            foreach (RangeValue v in values)
            {
                if (double.IsNaN(v.ValueDeg))
                {
                    continue;
                }
                float len = outer * Mathf.Clamp01((float)v.ValueDeg / maxDeg);
                RectTransform bar = Child(slot, "Bar", Vector2.zero, new Vector2(Mathf.Max(1f, len), 10f));
                bar.pivot = new Vector2(0f, 0.5f);
                bar.anchoredPosition = Vector2.zero;
                bar.localRotation = Quaternion.Euler(0f, 0f, 90f - v.DirectionDeg);
                Image bi = bar.gameObject.AddComponent<Image>();
                bi.color = p.Emphasis;
                bi.raycastTarget = false;
                RectTransform end = Child(slot, "End", Polar(len, v.DirectionDeg), new Vector2(26f, 26f));
                Image ei = _ui.Round(end.gameObject.AddComponent<Image>(), 13);
                ei.color = p.Emphasis;
                ei.raycastTarget = false;
                Label(slot, v.Shown, Polar(len + 46f, v.DirectionDeg), p.BodySize - 4, p.Text, true);
            }
            RectTransform centre = Child(slot, "Centre", Vector2.zero, new Vector2(18f, 18f));
            Image ci = _ui.Round(centre.gameObject.AddComponent<Image>(), 9);
            ci.color = p.Text;
            ci.raycastTarget = false;
        }

        private static Vector2 Polar(float radius, float directionDeg)
        {
            float a = directionDeg * Mathf.Deg2Rad;
            return new Vector2(Mathf.Sin(a), Mathf.Cos(a)) * radius;
        }

        private static RectTransform Child(RectTransform parent, string name, Vector2 position, Vector2 sizeDelta)
        {
            RectTransform r = StudyUi.NewRect(name, parent);
            r.anchorMin = r.anchorMax = r.pivot = new Vector2(0.5f, 0.5f);
            r.sizeDelta = sizeDelta;
            r.anchoredPosition = position;
            return r;
        }

        private void Label(RectTransform parent, string text, Vector2 position, int size, Color color, bool bold)
        {
            Text t = _ui.NewText(parent, text, size, TextAnchor.MiddleCenter, color, bold);
            var r = (RectTransform)t.transform;
            r.anchorMin = r.anchorMax = r.pivot = new Vector2(0.5f, 0.5f);
            r.sizeDelta = new Vector2(150f, size * 1.6f);
            r.anchoredPosition = position;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
        }

        /// <summary>Rings and spokes of the diagram as one sprite (built once per session).</summary>
        private Sprite RangeRingSprite(List<float> ringFractions, int spokes)
        {
            if (_rangeRingSprite != null)
            {
                return _rangeRingSprite;
            }
            const int size = 512;
            byte[] cov = EyeRangeShapes.RangeRings(size, ringFractions, spokes, 3f);
            _rangeRingSprite = Track(Sprite.Create(CoverageTexture(cov, size, size, "EyeRangeRings"), new Rect(0, 0, size, size),
                new Vector2(0.5f, 0.5f), 100f));
            return _rangeRingSprite;
        }

        private Texture2D CoverageTexture(byte[] coverage, int width, int height, string name)
        {
            Texture2D tex = Track(new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                name = name,
            });
            var px = new Color32[coverage.Length];
            for (int i = 0; i < px.Length; i++)
            {
                px[i] = new Color32(255, 255, 255, coverage[i]);
            }
            tex.SetPixels32(px);
            tex.Apply(false, false);
            return tex;
        }

        // ------------------------------------------------------------------ Part 2: advance cue (fast-forward icon)
        /// <summary>
        /// Small semi-transparent fast-forward icon (two right-pointing triangles, about 1.875 deg wide) on its own
        /// world-space canvas, so the clip is not covered by text (user 11-19: no text during Part 2 playback).
        /// </summary>
        private void EnsureAdvanceIcon()
        {
            if (_advanceIcon != null)
            {
                return;
            }
            _advanceIcon = Track(new GameObject("AdvanceIcon", typeof(RectTransform)));
            var canvas = _advanceIcon.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = _rig.Camera;
            var rt = (RectTransform)_advanceIcon.transform;
            float widthUnits = 2000f * PanelDistance * Mathf.Tan(0.5f * AdvanceIconWidthDeg * Mathf.Deg2Rad); // 1 unit = 1 mm
            rt.sizeDelta = new Vector2(widthUnits, widthUnits * 0.62f);
            rt.localScale = Vector3.one * 0.001f;
            const int w = 96, hpx = 60;
            Texture2D tex = CoverageTexture(EyeRangeShapes.FastForward(w, hpx), w, hpx, "AdvanceIcon");
            RectTransform img = StudyUi.NewRect("Icon", rt);
            StudyUi.Stretch(img);
            Image image = img.gameObject.AddComponent<Image>();
            image.sprite = Track(Sprite.Create(tex, new Rect(0, 0, w, hpx), new Vector2(0.5f, 0.5f), 100f));
            image.color = new Color(1f, 1f, 1f, 0.55f);
            image.raycastTarget = false;
            _advanceIcon.SetActive(false);
        }

        private const float AdvanceIconWidthDeg = 1.875f; // user 11-20: 1.5 x the first 1.25 deg

        /// <summary>Show the icon at the trial's reference direction (where the alignment cross was).</summary>
        private void ShowAdvanceIcon(float refYaw, float refPitch)
        {
            EnsureAdvanceIcon();
            Vector3 dir = Quaternion.Euler(-refPitch, refYaw, 0f) * Vector3.forward;
            _advanceIcon.transform.position = _rig.HeadPosition + dir * PanelDistance;
            _advanceIcon.transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
            _advanceIcon.SetActive(true);
        }

        private void HideAdvanceIcon()
        {
            if (_advanceIcon != null)
            {
                _advanceIcon.SetActive(false);
            }
        }

        /// <summary>Choose the Part 2 PAw correction radius, write session.json "eye_range" and log the choice.</summary>
        private void FinishEyeRange(string source)
        {
            EyeRangePlan er = _s.Plan.EyeRange;
            Dictionary<string, object> block = _s.ResolveEyeRange(er, source);
            _s.Session["eye_range"] = block;
            WriteSession();
            string medians = string.Join(" ", ((List<object>)block["per_direction"]).Cast<Dictionary<string, object>>()
                .Select(m => CsvLog.Format(m["direction_deg"]) + ":" + (m["median_head_onset_ecc_deg"] == null ? "-" : CsvLog.Format(m["median_head_onset_ecc_deg"]))));
            string shown = string.Join(" ", ((List<object>)block["per_direction"]).Cast<Dictionary<string, object>>()
                .Select(m => CsvLog.Format(m["direction_deg"]) + ":" + m["shown"]));
            LogEvent("eyerange_result", string.Format(CultureInfo.InvariantCulture,
                "a {0} deg (shown {1}), PAw theta {2} deg ({3}); rule {4}, {5} > {6:0.##} deg, {7:0.##} deg/s up to {8:0.##} deg, provisional; "
                + "censored {9}, stopped early {10}; median by direction {11}; shown by direction {12}",
                block["a_deg"] == null ? "-" : CsvLog.Format(block["a_deg"]), block["a_shown"], StudyPlan.FormatTheta(_s.PawThetaDeg), _s.PawThetaSource,
                er.Rule, er.Measure, er.HeadThresholdDeg, er.SpeedDegPerSecond, er.MaxEccentricityDeg, block["censored"], block["stopped_early"],
                medians, shown));
            LogEvent("paw_theta", StudyPlan.FormatTheta(_s.PawThetaDeg) + " deg (" + _s.PawThetaSource + ")");
        }

        /// <summary>Before Part 2: when the eye-range step did not run, use the plan default (15 deg) and log it.</summary>
        private void EnsurePawTheta()
        {
            if (_s.PawThetaDeg > 0f)
            {
                return;
            }
            EyeRangePlan er = _s.Plan.EyeRange;
            string source = !er.Present ? "default_no_block"
                : !_s.Plan.Flow.Any(f => f.Kind == FlowStepKind.EyeRange) ? "default_not_in_flow"
                : !er.Enabled ? "default_disabled" : "default_skipped";
            FinishEyeRange(source);
        }

        /// <summary>Part 2 stimulus with the PAw correction radius of this participant (PA names are unchanged).</summary>
        private string Part2File(string file)
        {
            return StudyPlan.ResolvePawTheta(file, _s.PawThetaDeg);
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
                _ui.ShowHint(StudyMarkup.Em(_hintLabel));
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
            // hintKey null and no label: no text at all (Part 2 playback, user 11-19: the line below the view drew the head away)
            string text = string.IsNullOrEmpty(hintKey) ? "" : T(hintKey);
            if (text.Length == 0 && string.IsNullOrEmpty(_hintLabel))
            {
                return;
            }
            _ui.ShowHint(string.IsNullOrEmpty(_hintLabel) ? text
                : text.Length == 0 ? StudyMarkup.Em(_hintLabel) : StudyMarkup.Em(_hintLabel) + "      " + text);
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
            HideAdvanceIcon();
        }

        // ------------------------------------------------------------------ Part 2: PA vs PAw
        /// <summary>One part2.csv row; paw_theta_deg is the participant's PAw correction radius (the same in every row).</summary>
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
                Round(_s.PawThetaDeg, 2), skipped ? 1 : 0);
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
                }, null, null); // user 11-19: no text during Part 2 playback
                Func<bool> abort = req.Abort;
                req.Abort = () => headOut || (abort != null && abort());
                bool fullViewed = false; // the clip has played completely once (advancing allowed)
                Action allowed = req.AdvanceAllowed;
                req.AdvanceAllowed = () =>
                {
                    fullViewed = true;
                    allowed?.Invoke();
                    // user 11-20: a small fast-forward icon where the cross was, no text (hidden by StopAdvance)
                    ShowAdvanceIcon(t.ReferenceYawDeg, t.ReferencePitchDeg);
                    LogEvent("part2_advance_cue", "trial " + t.Index + " segment " + seg);
                };
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

                if (fullViewed)
                {
                    // user 11-19: the clip has already played completely once, so the participant may go on or replay it
                    int pick = -1;
                    yield return AskTwoText(T("part2_head_title"), T("part2_head_done"),
                        seg == "A" ? "part2_head_done_next_a" : "part2_head_done_next_b", "part2_head_done_replay", v => pick = v);
                    bool goOn = Interrupted || pick != 0;
                    LogEvent("part2_head_after_view", string.Format(CultureInfo.InvariantCulture,
                        "trial {0} segment {1}: {2:0.0} deg > {3:0.0} at {4:0.00} s after a complete viewing: {5}", t.Index, seg,
                        outDeg, threshold, stats.ViewSeconds, goOn ? "go on" : "replay"));
                    if (goOn)
                    {
                        head.AddRange(rows);
                        result.Stats = stats;
                        result.MaxDeg = max;
                        result.ReminderOff = !check;
                        yield break;
                    }
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
                if (fullViewed)
                {
                    // the participant chose to replay after a complete viewing: no further prompt
                }
                else if (done >= HeadRestartsBeforeChoice && done % HeadRestartsBeforeChoice == 0)
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
                    (t.IsMain ? "" : StudyMarkup.Em(T("practice_label")) + "  ") + StudyMarkup.Em(T(labelKey)) + "  " + T("part2_cue"),
                    -0.42f, (a, d) => { });
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
            EnsurePawTheta(); // user 11-20: PAw files of the participant's correction radius (15 deg when not measured)
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
            StudyClipPlayer.Slot slotA = _clips.Prepare(0, StimulusPath(Part2File(trials[0].FileA)));
            StudyClipPlayer.Slot slotB = _clips.Prepare(1, StimulusPath(Part2File(trials[0].FileB)));

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
                    (t.IsMain ? "" : StudyMarkup.Em(T("practice_label")) + "  ") + StudyMarkup.Em(T("segment_first")) + "  " + T("part2_cue"),
                    -0.42f, (a, d) => { alignS = a; startDev = d; });
                bool playedA = false, playedB = false;
                var segA = new Part2Segment();
                var segB = new Part2Segment();
                if (!Interrupted)
                {
                    yield return PlayPart2Segment(t, part.Options, "A", Part2File(t.FileA), 0, slotA, "segment_first", head, segA);
                    statsA = segA.Stats;
                    maxA = segA.MaxDeg;
                    playedA = true;
                    ShowHmdNotice("segment_second", null); // user 11-19: clip labels appear only outside playback
                    yield return Wait(tm.GapBetweenAbSeconds);
                    _ui.ShowHmd(false);
                }
                if (!Interrupted)
                {
                    yield return PlayPart2Segment(t, part.Options, "B", Part2File(t.FileB), 1, slotB, "segment_second", head, segB);
                    statsB = segB.Stats;
                    maxB = segB.MaxDeg;
                    playedB = true;
                }

                _recenterLocked = false;

                // preload the next pair while the participant answers
                if (i + 1 < trials.Count)
                {
                    slotA = _clips.Prepare(0, StimulusPath(Part2File(trials[i + 1].FileA)));
                    slotB = _clips.Prepare(1, StimulusPath(Part2File(trials[i + 1].FileB)));
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
            _ui.SetHmdSize(1760, 420 + 122 * recs.Count);
            _ui.PlaceHmdCanvas(_rig.HeadPosition, _rig.YawForward(), PanelDistance);
            _ui.Title(_ui.HmdContent, title);
            _ui.Note(_ui.HmdContent, T("group_summary_hint"), TextAnchor.MiddleCenter);
            for (int k = 0; k < recs.Count; k++)
            {
                int idx = k;
                RatingRecord r = recs[k];
                RectTransform row = _ui.NewRow(_ui.HmdContent, 20, TextAnchor.MiddleCenter, 104);
                _ui.Fill(row, StudyTheme.Dark.RowFill);
                row.GetComponent<HorizontalLayoutGroup>().padding = new RectOffset(36, 14, 0, 0);
                string text = "<b>" + TF("group_segment", k + 1) + "</b>      " + SummaryScore("scale_depth_short", r.Depth) + "     "
                    + SummaryScore("scale_comfort_short", r.Comfort) + "     " + SummaryScore("scale_artifact_short", r.Artifact);
                Text label = _ui.Body(row, text, TextAnchor.MiddleLeft, 36);
                label.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1; // the buttons sit at the right end of the row
                if (allowReplay)
                {
                    _ui.HmdButton(row, T("group_replay"), () => answer = 100 + idx, 200, 80, 34, ButtonKind.Secondary, "Btn_Replay_" + (k + 1));
                }
                _ui.HmdButton(row, T("group_rerate"), () => answer = idx, 200, 80, 34, ButtonKind.Secondary, "Btn_Edit_" + (k + 1));
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

        /// <summary>"Depth 4" for the summary row: the scale name quiet, the participant's own score large and bold.</summary>
        private static string SummaryScore(string labelKey, int value)
        {
            StudyTheme.Palette p = StudyTheme.Dark;
            return StudyTheme.Tint(T(labelKey), p.TextSecondary) + " <size=44>"
                + StudyTheme.Tint(value.ToString(CultureInfo.InvariantCulture), p.Emphasis, true) + "</size>";
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
            _ui.Strong(c, T("post_comments"));
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
            RectTransform row = _ui.NewRow(_ui.HmdContent, 10, TextAnchor.MiddleCenter, 190);
            string[] keys = { "pref_m3", "pref_m2", "pref_m1", "pref_0", "pref_p1", "pref_p2", "pref_p3" };
            for (int k = 0; k < keys.Length; k++)
            {
                int value = k - 3;
                string number = (value > 0 ? "+" : "") + value.ToString(CultureInfo.InvariantCulture);
                _ui.HmdButton(row, "<size=44><b>" + number + "</b></size>\n" + T(keys[k]), () => answer = value, 232, 180, 30,
                    ButtonKind.Choice, "Btn_Pref_" + (k + 1));
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
            _ui.SetHmdSize(1500, 1060);
            _ui.PlaceHmdCanvas(_rig.HeadPosition, _rig.YawForward(), PanelDistance);
            _ui.QuestionHeader(_ui.HmdContent, prefix, T(titleKey));
            for (int score = 5; score >= 1; score--)
            {
                int s = score;
                RectTransform row = _ui.NewRow(_ui.HmdContent, 0, TextAnchor.MiddleCenter, 112);
                Button b = _ui.HmdScaleOption(row, s.ToString(CultureInfo.InvariantCulture), T(optionPrefix + "_" + s), () => answer = s,
                    1200, 108, 38, "Btn_Score_" + s);
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
