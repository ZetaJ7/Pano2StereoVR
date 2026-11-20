using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR;

namespace Pano2StereoVR.Study
{
    /// <summary>
    /// Subjective-study session runner. Builds the rig, panorama sphere, UI and video player at runtime,
    /// asks for the language, runs check-in, then executes the plan flow (SSQ, headset fitting, stereo test,
    /// Part 1, SSQ, break, SSQ, headset re-fit, natural viewing range, gaze training, Part 2, SSQ, post-questionnaire), writing every
    /// trial to the participant folder as soon as it ends. In QA mode the status bar offers skip controls. The end page offers Exit and Start next participant; the latter
    /// rebuilds the runtime objects in place and starts a fresh session (new folder, nothing carried over).
    /// The flow steps are in StudyController.Steps.cs.
    /// </summary>
    public sealed partial class StudyController : MonoBehaviour
    {
        [Header("Shaders (assigned in the scene so builds include them)")]
        public Shader SphereShader;
        public Shader QuadShader;
        public Shader ColorShader;

        [Header("Options")]
        public string AppVersion = "pano2stereo-study-v3";
        public float SphereRadius = 500f;
        public float PanelDistance = 2f;
        public float PrepareTimeoutSeconds = 20f;

        private StudySettings _settings;
        private readonly StudySessionState _s = new StudySessionState();
        private StudyRig _rig;
        private StudyUi _ui;
        private StudyClipPlayer _clips;
        private StudyPointer _pointer;
        private Material _sphereMat;
        private Material _quadMat;
        private Material _colorMat;
        private GameObject _rdsQuad;
        private Texture2D _rdsTex;
        private GameObject _cueMarker;
        private GameObject _gazeRoot;
        private readonly List<Renderer> _gazeDots = new List<Renderer>();
        private GameObject _eyeTarget;
        private Material _eyeTargetMat;
        private float _eyeRefYaw;
        private float _eyeRefPitch;
        private float _eyeDirDeg;
        private float _eyeDotEccDeg;
        private bool _eyeSweeping;
        private Sprite _rangeRingSprite;
        private GameObject _advanceIcon;
        // staff skip (F8, user 11-20)
        private bool _staffSkip;
        private bool _staffScope;
        private RectTransform _staffPanel;
        private GameObject _staffWait;
        private float _staffOpenedAt;
        private int _resumeInputFrame = -1;
        private bool _eventSystemWasEnabled = true;
        // forward direction (user 11-20 18:50): set only by R / Shift+R, directly in the headset-fitting steps,
        // otherwise through a confirmation; never automatically
        private bool _hmdFitActive;
        private bool _forwardSetInStep;
        private bool _forwardEverSet;
        private int _forwardSets;
        private string _forwardSetKey = "";
        private bool _dialogIsRecenter;
        private bool _dialogYawOnly;
        /// <summary>Direction the headset panels are anchored to: the set forward (content yaw 0, pitch 0), or a Part 2 trial's reference.</summary>
        private Vector3 _panelAnchor = new Vector3(0f, 0f, 1f);
        // Part 2 content rotation (user 11-20): the panorama sphere is turned so the trial's fixation region
        // (reference_yaw_deg, ERP yaw) appears at the set forward; owned here only (the rig moves the sphere position only)
        private Transform _sphere;
        /// <summary>Content (ERP) yaw shown at world yaw 0 right now (sphere rotation); 0 = identity.</summary>
        private float _contentYawOffset;
        /// <summary>Content yaw the current Part 2 trial shows at world yaw 0 while its clips play; 0 outside Part 2 trials.</summary>
        private float _trialYawOffset;
        private readonly List<UnityEngine.Object> _runtime = new List<UnityEngine.Object>();
        private Coroutine _run;
        private RectTransform _abortPanel;
        private InputField _abortReason;
        private readonly List<Button> _endButtons = new List<Button>();
        private RectTransform _exitConfirmRow;
        private bool _restarting;
        private bool _advanceArmed;
        private bool _advanceRequested;
        private int _triggerSeen;
        private bool _hintFollowHead;
        private string _hintLabel = "";
        private bool _skipGroup;
        private bool _skipPart;
        private bool _aligning;
        /// <summary>True while the head direction is measured; manual Recenter is then blocked (user 11-19).</summary>
        private bool _recenterLocked;
        private float _recenterNoticeUntil = -1f;
        private float _alignYaw;
        private float _alignPitch;
        private GameObject _alignRing;
        private readonly List<Renderer> _ringSegments = new List<Renderer>();
        private Material _ringDimMat;
        private Material _ringLitMat;
        private const int RingSegments = 24;

        // ------------------------------------------------------------------ read-only state (autopilot, checks)
        public string CurrentStep => _s.Step;
        public string CurrentTrial => _s.TrialLabel;
        public bool IsFinished => _s.Finished;
        public bool EndReady => _s.EndReady && !_restarting;
        public bool PlanLoaded => _s.Plan != null;
        public bool ClipVisible => _sphereMat != null && _sphereMat.GetFloat("_UseGray") < 0.5f;
        public bool CueVisible => _cueMarker != null && _cueMarker.activeSelf;
        public bool StereoQuadVisible => _rdsQuad != null && _rdsQuad.activeSelf;
        public bool GazeDotsVisible => _gazeRoot != null && _gazeRoot.activeSelf;
        /// <summary>The natural-viewing-range dot is shown.</summary>
        public bool EyeRangeTargetVisible => _eyeTarget != null && _eyeTarget.activeSelf;
        /// <summary>A natural-viewing-range sweep is running (the dot moves outward).</summary>
        public bool EyeRangeSweepActive => _eyeSweeping;
        /// <summary>Current eccentricity of the sweeping dot (deg).</summary>
        public float EyeRangeDotEccentricityDeg => _eyeDotEccDeg;
        /// <summary>Part 2: the fast-forward icon (advancing allowed) is shown.</summary>
        public bool AdvanceIconVisible => _advanceIcon != null && _advanceIcon.activeSelf;
        /// <summary>PAw correction radius chosen for Part 2 (deg); below 0 until chosen.</summary>
        public float PawThetaDeg => _s.PawThetaDeg;
        public bool AdvanceAvailable => _advanceArmed;
        public string SessionDirectory => _s.Log != null ? _s.Log.Directory : "";
        public string Language => _s.Language;
        public string Mode => _settings != null ? _settings.Mode : "";
        public int SessionGeneration => _s.Generation;
        /// <summary>Stimulus files of the loaded plan that are missing under the stimuli folder (all PAw variants counted).</summary>
        public int MissingStimuliCount => _s.MissingStimuli.Count;
        public string StimuliDirectory => _settings != null ? _settings.StimuliDir : "";
        public bool Restarting => _restarting;
        /// <summary>The orientation cross is waiting for the head to stay within the alignment tolerance.</summary>
        public bool Aligning => _aligning;
        public bool SkipControlsActive => _settings != null && _settings.IsQa;
        /// <summary>F8 can skip the current Part 1 clip or Part 2 trial (formal and QA mode, user 11-20).</summary>
        public bool StaffSkipAvailable => _staffScope && !_staffSkip && !_s.Finished && _s.Log != null;
        /// <summary>A staff confirmation (F8 skip or emergency recenter) is open; other input is suspended.</summary>
        public bool StaffDialogOpen => _staffPanel != null && _staffPanel.gameObject.activeSelf;
        /// <summary>The forward has been set (R / Shift+R) in the current headset-fitting step.</summary>
        public bool ForwardSetInStep => _forwardSetInStep;

        /// <summary>Same as R (yawOnly false) or Shift+R (true).</summary>
        public void RequestSetForward(bool yawOnly)
        {
            ManualRecenterMode(yawOnly);
        }

        /// <summary>Same as F8: open the staff-skip confirmation.</summary>
        public void RequestStaffSkipDialog()
        {
            OpenStaffSkipDialog();
        }

        /// <summary>Same as Y (confirm) or N / Esc (cancel) while a staff confirmation is open.</summary>
        public void AnswerStaffSkip(bool confirm)
        {
            AnswerStaffDialog(confirm);
        }

        /// <summary>Y (confirm) or N / Esc (cancel) for the open staff confirmation (F8 skip or emergency recenter).</summary>
        public void AnswerStaffDialog(bool confirm)
        {
            if (!StaffDialogOpen)
            {
                return;
            }
            float open = Time.realtimeSinceStartup - _staffOpenedAt;
            _staffPanel.gameObject.SetActive(false);
            if (_staffWait != null)
            {
                _staffWait.SetActive(false);
            }
            _resumeInputFrame = Time.frameCount + 1; // resume input one frame later, so the same key press is not handled again
            if (_dialogIsRecenter)
            {
                // emergency recenter (user 11-20): the head direction at the confirmation becomes the forward
                if (confirm)
                {
                    Recenter(_dialogYawOnly ? RecenterMode.YawLevel : RecenterMode.YawAndPitch,
                        _dialogYawOnly ? "emergency_yaw" : "emergency_yaw_pitch", "recenter_emergency");
                }
                else
                {
                    LogEvent("recenter_emergency_cancelled", _s.Step + " " + _s.TrialLabel + "; dialog open " + CsvLog.Format(Round(open, 2)) + " s");
                }
                _dialogIsRecenter = false;
                return;
            }
            if (confirm)
            {
                _staffSkip = true;
            }
            LogEvent(confirm ? "staff_skip_confirmed" : "staff_skip_cancelled", _s.Step + " " + _s.TrialLabel + "; dialog open "
                + CsvLog.Format(Round(open, 2)) + " s");
        }

        /// <summary>QA only: abandon the current group (Part 1), trial (Part 2) or step; flagged skipped in the data.</summary>
        public void RequestSkipGroup()
        {
            if (!SkipControlsActive || _s.Finished || _s.Log == null || _skipGroup)
            {
                return;
            }
            _skipGroup = true;
            LogEvent("qa_skip_group_requested", _s.Step + " " + _s.TrialLabel);
        }

        /// <summary>QA only: abandon the rest of the current part or step; flagged skipped in the data.</summary>
        public void RequestSkipPart()
        {
            if (!SkipControlsActive || _s.Finished || _s.Log == null || _skipPart)
            {
                return;
            }
            _skipPart = true;
            LogEvent("qa_skip_part_requested", _s.Step + " " + _s.TrialLabel);
        }

        /// <summary>Desktop only: point the simulated head at the current alignment reference plus a yaw offset.</summary>
        public void DesktopLookAtReference(float yawOffsetDeg)
        {
            if (_rig != null && _aligning)
            {
                _rig.SetDesktopView(_alignYaw + yawOffsetDeg, _alignPitch);
            }
        }

        /// <summary>
        /// Desktop only: point the simulated head from the eye-range reference along the current sweep direction, at
        /// the given eccentricity (deg). Used by the autopilot to let the head follow the dot.
        /// </summary>
        public void DesktopLookAlongEyeRangeSweep(float headEccDeg)
        {
            if (_rig != null && _eyeSweeping)
            {
                Vector3 dir = EyeRangeDirection(_eyeRefYaw, _eyeRefPitch, _eyeDirDeg, headEccDeg);
                _rig.SetDesktopView(Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg, Mathf.Asin(Mathf.Clamp(dir.y, -1f, 1f)) * Mathf.Rad2Deg);
            }
        }

        /// <summary>Direction at eccentricity eccDeg along directionDeg (0 = up, clockwise for the viewer) around a reference.</summary>
        private static Vector3 EyeRangeDirection(float refYaw, float refPitch, float directionDeg, float eccDeg)
        {
            Vector3 local = Quaternion.AngleAxis(-directionDeg, Vector3.forward) * (Quaternion.AngleAxis(-eccDeg, Vector3.right) * Vector3.forward);
            return Quaternion.Euler(-refPitch, refYaw, 0f) * local;
        }

        /// <summary>A QA skip (group, part) or a confirmed staff skip ends every wait of the current trial.</summary>
        private bool Interrupted => _skipGroup || _skipPart || _staffSkip;

        /// <summary>Same effect as the trigger or Space while a self-paced clip may be advanced.</summary>
        public void RequestAdvance()
        {
            if (_advanceArmed)
            {
                _advanceRequested = true;
            }
        }

        /// <summary>Desktop preview eye (0 = left, 1 = right); same effect as the E key. No effect in XR.</summary>
        public void SetPreviewEye(int eye)
        {
            _s.PreviewEye = eye == 0 ? 0f : 1f;
            _sphereMat?.SetFloat("_PreviewEye", _s.PreviewEye);
            _quadMat?.SetFloat("_PreviewEye", _s.PreviewEye);
        }

        // ------------------------------------------------------------------ setup
        private void Awake()
        {
            ResolveSettings();
            LoadTexts();
            BuildWorld();
        }

        private void ResolveSettings()
        {
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            _settings = StudySettings.Resolve(root, Application.isEditor, Environment.GetCommandLineArgs());
            _s.Mode = _settings.Mode;
            if (!string.IsNullOrEmpty(_settings.Warning))
            {
                Debug.LogWarning("[Study] " + _settings.Warning);
            }
        }

        private void LoadTexts()
        {
            StudyTexts.Clear();
            var asset = Resources.Load<TextAsset>("StudyTexts");
            if (asset != null)
            {
                StudyTexts.LoadJson(asset.text);
            }
            else
            {
                Debug.LogError("[Study] Resources/StudyTexts.json is missing");
            }
            if (StudyTexts.TryLoadOverride(_settings.TextsOverride))
            {
                Debug.Log("[Study] text overrides loaded from " + _settings.TextsOverride);
            }
            StudyTexts.Language = _s.Language;
        }

        private T Track<T>(T obj) where T : UnityEngine.Object
        {
            _runtime.Add(obj);
            return obj;
        }

        private void BuildWorld()
        {
            SphereShader = SphereShader != null ? SphereShader : Shader.Find("Pano2Stereo/StudyStereoTB");
            QuadShader = QuadShader != null ? QuadShader : Shader.Find("Pano2Stereo/StudyStereoQuadTB");
            ColorShader = ColorShader != null ? ColorShader : Shader.Find("Pano2Stereo/StudyUnlitColor");

            foreach (Camera c in FindObjectsOfType<Camera>())
            {
                c.gameObject.SetActive(false);
            }
            var rigGo = Track(new GameObject("StudyRig"));
            _rig = rigGo.AddComponent<StudyRig>();
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            camGo.transform.SetParent(rigGo.transform, false);
            Camera cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = SphereRadius * 4f;
            cam.stereoTargetEye = StereoTargetEyeMask.Both;

            GameObject sphere = Track(GameObject.CreatePrimitive(PrimitiveType.Sphere));
            sphere.name = "PanoramaSphere";
            Destroy(sphere.GetComponent<Collider>());
            sphere.transform.localScale = Vector3.one * SphereRadius * 2f;
            _sphereMat = Track(new Material(SphereShader));
            sphere.GetComponent<Renderer>().sharedMaterial = _sphereMat;
            _rig.Initialize(cam, sphere.transform);
            _sphere = sphere.transform;
            _trialYawOffset = 0f;
            ApplyContentYaw(0f);
            ShowGray();

            _colorMat = Track(new Material(ColorShader));
            _colorMat.SetColor("_Color", StudyTheme.CueYellow);
            _ringDimMat = Track(new Material(ColorShader));
            _ringDimMat.SetColor("_Color", StudyTheme.RingDim);
            _ringLitMat = Track(new Material(ColorShader));
            _ringLitMat.SetColor("_Color", StudyTheme.RingLit);
            _cueMarker = Track(BuildCueMarker());
            _cueMarker.SetActive(false);

            _rdsQuad = Track(GameObject.CreatePrimitive(PrimitiveType.Quad));
            _rdsQuad.name = "StereoTestQuad";
            Destroy(_rdsQuad.GetComponent<Collider>());
            _quadMat = Track(new Material(QuadShader));
            _rdsQuad.GetComponent<Renderer>().sharedMaterial = _quadMat;
            _rdsQuad.SetActive(false);

            var uiGo = Track(new GameObject("StudyUi"));
            _ui = uiGo.AddComponent<StudyUi>();
            _ui.Build(cam);
            _ui.RecenterButton.onClick.AddListener(ManualRecenter);
            _ui.AbortButton.onClick.AddListener(OpenAbortPanel);
            _ui.SkipGroupButton.onClick.AddListener(RequestSkipGroup);
            _ui.SkipPartButton.onClick.AddListener(RequestSkipPart);
            _ui.SetBarLabels(T("bar_recenter"), T("bar_abort"), T("bar_skip_group"), T("bar_skip_part"));
            _ui.ShowSkipControls(_settings.IsQa);

            var clipGo = Track(new GameObject("StudyClipPlayer"));
            _clips = clipGo.AddComponent<StudyClipPlayer>();

            var pointerGo = Track(new GameObject("StudyPointer"));
            _pointer = pointerGo.AddComponent<StudyPointer>();
            _pointer.Rig = _rig;
            _pointer.Ui = _ui;
            Material lineMat = Track(new Material(ColorShader));
            lineMat.SetColor("_Color", new Color(0.6f, 0.85f, 1f, 1f));
            _pointer.LineMaterial = lineMat;
            _triggerSeen = 0;
            SetPreviewEye(0);
        }

        private GameObject BuildCueMarker()
        {
            var root = new GameObject("OrientationCue");
            for (int i = 0; i < 2; i++)
            {
                GameObject bar = GameObject.CreatePrimitive(PrimitiveType.Quad);
                Destroy(bar.GetComponent<Collider>());
                bar.transform.SetParent(root.transform, false);
                bar.transform.localScale = i == 0 ? new Vector3(0.16f, 0.025f, 1f) : new Vector3(0.025f, 0.16f, 1f);
                bar.GetComponent<Renderer>().sharedMaterial = _colorMat;
            }
            // dwell progress ring around the cross (orientation alignment)
            _ringSegments.Clear();
            _alignRing = new GameObject("AlignRing");
            _alignRing.transform.SetParent(root.transform, false);
            for (int k = 0; k < RingSegments; k++)
            {
                GameObject seg = GameObject.CreatePrimitive(PrimitiveType.Quad);
                Destroy(seg.GetComponent<Collider>());
                seg.name = "RingSegment" + k;
                seg.transform.SetParent(_alignRing.transform, false);
                float a = 2f * Mathf.PI * k / RingSegments;
                seg.transform.localPosition = new Vector3(Mathf.Sin(a), Mathf.Cos(a), 0f) * 0.12f;
                seg.transform.localRotation = Quaternion.Euler(0f, 0f, -a * Mathf.Rad2Deg);
                seg.transform.localScale = new Vector3(0.022f, 0.012f, 1f);
                Renderer r = seg.GetComponent<Renderer>();
                r.sharedMaterial = _ringDimMat;
                _ringSegments.Add(r);
            }
            _alignRing.SetActive(false);
            return root;
        }

        /// <summary>Destroy every runtime object built by BuildWorld (UI, rig, sphere, players, materials, textures).</summary>
        private void DestroyWorld()
        {
            if (_clips != null)
            {
                _clips.StopAll();
            }
            for (int i = _runtime.Count - 1; i >= 0; i--)
            {
                if (_runtime[i] != null)
                {
                    Destroy(_runtime[i]);
                }
            }
            _runtime.Clear();
            _gazeDots.Clear();
            _ringSegments.Clear();
            _alignRing = null;
            _ringDimMat = null;
            _ringLitMat = null;
            _aligning = false;
            _skipGroup = false;
            _recenterLocked = false;
            _recenterNoticeUntil = -1f;
            _skipPart = false;
            _endButtons.Clear();
            _rig = null;
            _ui = null;
            _clips = null;
            _pointer = null;
            _sphereMat = null;
            _quadMat = null;
            _colorMat = null;
            _rdsQuad = null;
            _rdsTex = null;
            _cueMarker = null;
            _gazeRoot = null;
            _eyeTarget = null;
            _eyeTargetMat = null;
            _eyeSweeping = false;
            _rangeRingSprite = null;
            _advanceIcon = null;
            _staffSkip = false;
            _staffScope = false;
            _staffPanel = null;
            _staffWait = null;
            _resumeInputFrame = -1;
            _hmdFitActive = false;
            _forwardSetInStep = false;
            _forwardEverSet = false;
            _forwardSets = 0;
            _dialogIsRecenter = false;
            _panelAnchor = Vector3.forward;
            _sphere = null;
            _contentYawOffset = 0f;
            _trialYawOffset = 0f;
            _abortPanel = null;
            _abortReason = null;
            _exitConfirmRow = null;
        }

        private void Start()
        {
            _run = StartCoroutine(RunSession());
        }

        private void Update()
        {
            if (_restarting || _ui == null || _rig == null)
            {
                return;
            }
            if (_resumeInputFrame >= 0 && Time.frameCount > _resumeInputFrame)
            {
                SuspendInput(false);
            }
            if (StaffDialogOpen)
            {
                // user 11-20: while the staff-skip confirmation is open only Y, N and Esc are handled
                if (Input.GetKeyDown(KeyCode.Y))
                {
                    AnswerStaffDialog(true);
                }
                else if (Input.GetKeyDown(KeyCode.N) || Input.GetKeyDown(KeyCode.Escape))
                {
                    AnswerStaffDialog(false);
                }
                PlaceStaffWait();
                _ui.SetStatus(StatusLine());
                return;
            }
            if (Input.GetKeyDown(KeyCode.F8) && !IsTyping())
            {
                OpenStaffSkipDialog();
                if (StaffDialogOpen)
                {
                    _ui.SetStatus(StatusLine());
                    return;
                }
            }
            if (_resumeInputFrame >= 0)
            {
                _ui.SetStatus(StatusLine());
                return; // the frame right after the confirmation: no other key handling
            }
            if (Input.GetKeyDown(KeyCode.Escape) && !_s.Finished && _s.Log != null)
            {
                OpenAbortPanel();
            }
            _ui.RecenterButton.interactable = _hmdFitActive || EmergencyRecenterAllowed;
            if (Input.GetKeyDown(KeyCode.R) && !IsTyping())
            {
                // user 11-20: R = yaw and pitch, Shift+R = yaw only (level horizon)
                ManualRecenterMode(Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift));
            }
            if (_settings.IsQa && Input.GetKeyDown(KeyCode.PageDown) && !IsTyping())
            {
                if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
                {
                    RequestSkipPart();
                }
                else
                {
                    RequestSkipGroup();
                }
            }
            if (Input.GetKeyDown(KeyCode.E) && !IsTyping() && !_rig.XrActive)
            {
                SetPreviewEye(_s.PreviewEye < 0.5f ? 1 : 0);
            }
            if (_advanceArmed)
            {
                if ((Input.GetKeyDown(KeyCode.Space) && !IsTyping()) || (_pointer != null && _pointer.TriggerPresses != _triggerSeen))
                {
                    _advanceRequested = true;
                }
                if (_hintFollowHead && _ui.HintVisible)
                {
                    PlaceHint(_rig.YawForward());
                }
            }
            _ui.SetStatus(StatusLine());
        }

        /// <summary>
        /// Experimenter status bar: the participant ID (bold) and the current step stand out, QA mode is an amber
        /// tag, the rest is quiet; backup and recenter notices are bold in the alert colour.
        /// </summary>
        private string StatusLine()
        {
            string sep = StudyTheme.Tint("  ·  ", StudyTheme.StatusDim);
            string mode = _settings.IsQa
                ? StudyTheme.Tint(_settings.Mode.ToUpperInvariant(), StudyTheme.StatusQa, true)
                : _settings.Mode;
            string step = (_s.Step + " " + _s.TrialLabel).Trim();
            string line = StudyTheme.Tint(_s.Plan != null ? _s.Plan.ParticipantId : "-", StudyTheme.StatusStrong, true)
                + sep + mode
                + sep + StudyTheme.Tint(step, StudyTheme.StatusStrong)
                + sep + "dropped " + _s.DroppedTotal.ToString(CultureInfo.InvariantCulture)
                + sep + "XR " + (_rig.XrActive ? "on" : "off")
                + sep + "eye " + (_s.PreviewEye < 0.5f ? "L" : "R")
                + sep + (_s.Log != null ? _s.Log.Directory : _settings.DataDir);
            if (_s.Finished && !_s.EndReady)
            {
                line += sep + T("status_saving");
            }
            if (!string.IsNullOrEmpty(_s.BackupError))
            {
                line += sep + StudyTheme.Tint(TF("status_backup_failed", _s.BackupError), StudyTheme.StatusAlert, true);
            }
            if (Time.realtimeSinceStartup < _recenterNoticeUntil)
            {
                line += sep + StudyTheme.Tint(T("status_recenter_ignored"), StudyTheme.StatusAlert, true);
            }
            line += sep + StudyTheme.Tint(T("status_recenter_keys"), StudyTheme.StatusDim);
            if (StaffSkipAvailable)
            {
                line += sep + StudyTheme.Tint(T("status_staff_skip"), StudyTheme.StatusQa, true);
            }
            return line;
        }

        private static bool IsTyping()
        {
            GameObject sel = UnityEngine.EventSystems.EventSystem.current != null
                ? UnityEngine.EventSystems.EventSystem.current.currentSelectedGameObject : null;
            return sel != null && sel.GetComponent<InputField>() != null;
        }

        /// <summary>
        /// Recenter and log it: events.csv (event "recenter", or "recenter_emergency" outside the headset-fitting steps,
        /// with kind, applied yaw and pitch offsets, step) and the running list "recenters" in session.json (user 11-20).
        /// </summary>
        private void Recenter(RecenterMode mode, string kind, string eventName = "recenter")
        {
            if (_rig == null)
            {
                return;
            }
            _rig.Recenter(mode);
            _forwardSets++;
            _forwardEverSet = true;
            LogEvent(eventName, string.Format(CultureInfo.InvariantCulture, "{0}: yaw offset {1:0.00} deg, pitch offset {2:0.00} deg ({3} {4})",
                kind, _rig.YawOffsetDeg, _rig.PitchOffsetDeg, _s.Step, _s.TrialLabel).Trim());
            List<object> list = _s.Session.TryGetValue("recenters", out object o) ? o as List<object> : null;
            if (list == null)
            {
                list = new List<object>();
                _s.Session["recenters"] = list;
            }
            list.Add(new Dictionary<string, object>
            {
                { "t_utc", UtcNow() }, { "kind", kind }, { "step", _s.Step }, { "trial", _s.TrialLabel },
                { "yaw_offset_deg", Round(_rig.YawOffsetDeg, 2) }, { "pitch_offset_deg", Round(_rig.PitchOffsetDeg, 2) },
            });
            WriteSession();
        }

        /// <summary>Status-bar button: yaw and pitch (as the R key).</summary>
        private void ManualRecenter()
        {
            ManualRecenterMode(false);
        }

        /// <summary>
        /// An emergency recenter may be confirmed now: a session is running and neither the head direction is measured
        /// (natural viewing range, gaze practice, Part 2 alignment to the second clip) nor a clip is playing.
        /// </summary>
        private bool EmergencyRecenterAllowed => _s.Log != null && !_s.Finished && !_recenterLocked && !ClipVisible && !_eyeSweeping;

        /// <summary>
        /// Set the forward (user 11-20 18:50): R / status-bar button = yaw and pitch, Shift+R = yaw only (level
        /// horizon). In the headset-fitting steps it acts at once (the step's Continue needs it). Elsewhere, when
        /// allowed (between trials, on intro, question and summary pages), it opens a confirmation (emergency recenter);
        /// while the head direction is measured or a clip plays it is ignored and logged as recenter_ignored.
        /// There are no automatic recenters; the forward stays until the next manual set.
        /// </summary>
        private void ManualRecenterMode(bool yawOnly)
        {
            if (StaffDialogOpen)
            {
                return;
            }
            string key = yawOnly ? "Shift+R" : "R";
            if (_hmdFitActive)
            {
                Recenter(yawOnly ? RecenterMode.YawLevel : RecenterMode.YawAndPitch, yawOnly ? "manual_yaw" : "manual_yaw_pitch");
                _forwardSetInStep = true;
                _forwardSetKey = key;
                return;
            }
            if (!EmergencyRecenterAllowed)
            {
                _recenterNoticeUntil = Time.realtimeSinceStartup + 3f;
                LogEvent("recenter_ignored", _s.Step + " " + _s.TrialLabel + " (" + key + ")");
                return;
            }
            OpenStaffDialog(T(yawOnly ? "recenter_dialog_title_yaw" : "recenter_dialog_title_pitch"), T("recenter_dialog_note"),
                "StaffRecenterPanel", true, yawOnly);
            LogEvent("recenter_emergency_dialog", _s.Step + " " + _s.TrialLabel + " (" + key + ")");
        }

        // ------------------------------------------------------------------ staff skip (F8, user 11-20)
        /// <summary>
        /// F8: a confirmation on the desktop ("skip the current clip? Y / N or Esc"). While it is open the UI shortcuts,
        /// the controller pointer and the event system are suspended, the headset shows a neutral "please wait" panel
        /// in front of the participant, the Part 2 alignment timer and head-turn check pause. Available only during a
        /// Part 1 clip (playback and first rating) or a Part 2 trial (alignment to the last answer).
        /// </summary>
        private void OpenStaffSkipDialog()
        {
            if (!StaffSkipAvailable || StaffDialogOpen || (_abortPanel != null && _abortPanel.gameObject.activeSelf))
            {
                return;
            }
            OpenStaffDialog(T("staff_skip_title"), T("staff_skip_note"), "StaffSkipPanel", false, false);
            LogEvent("staff_skip_dialog", _s.Step + " " + _s.TrialLabel);
        }

        /// <summary>
        /// Modal staff confirmation on the desktop (title, "Y confirm / N or Esc cancel", note): input suspended, the
        /// headset shows the neutral wait panel. Used by the F8 skip and the emergency recenter.
        /// </summary>
        private void OpenStaffDialog(string title, string note, string name, bool recenter, bool yawOnly)
        {
            if (StaffDialogOpen || (_abortPanel != null && _abortPanel.gameObject.activeSelf))
            {
                return;
            }
            if (_staffPanel != null)
            {
                Destroy(_staffPanel.gameObject);
            }
            _dialogIsRecenter = recenter;
            _dialogYawOnly = yawOnly;
            StudyTheme.Palette p = StudyTheme.Light;
            RectTransform outer = _ui.NewCardFrame(_ui.DesktopCanvas.transform, name, new Vector2(960, 330), p, out RectTransform inner);
            var v = inner.gameObject.AddComponent<VerticalLayoutGroup>();
            v.padding = new RectOffset(48, 48, 36, 36);
            v.spacing = 16;
            v.childControlHeight = true;
            v.childControlWidth = true;
            v.childForceExpandHeight = false;
            v.childAlignment = TextAnchor.MiddleCenter;
            _ui.Colored(inner, title, p.Error, TextAnchor.MiddleCenter, 34, true);
            _ui.Strong(inner, T("staff_skip_hint"), TextAnchor.MiddleCenter);
            _ui.Note(inner, note, TextAnchor.MiddleCenter);
            outer.SetAsLastSibling();
            _staffPanel = outer;
            SuspendInput(true);
            ShowStaffWait();
            _staffOpenedAt = Time.realtimeSinceStartup;
        }

        private void SuspendInput(bool suspend)
        {
            if (suspend)
            {
                _resumeInputFrame = -1;
            }
            _ui.enabled = !suspend;
            if (_pointer != null)
            {
                _pointer.enabled = !suspend;
                if (!suspend)
                {
                    _triggerSeen = _pointer.TriggerPresses;
                }
            }
            UnityEngine.EventSystems.EventSystem es = UnityEngine.EventSystems.EventSystem.current;
            if (es != null)
            {
                if (suspend)
                {
                    _eventSystemWasEnabled = es.enabled;
                    es.enabled = false;
                }
                else
                {
                    es.enabled = _eventSystemWasEnabled;
                }
            }
            if (!suspend)
            {
                _resumeInputFrame = -1;
            }
        }

        /// <summary>Neutral "please wait" panel between the participant and the headset panels (own world-space canvas).</summary>
        private void ShowStaffWait()
        {
            if (_staffWait == null)
            {
                _staffWait = Track(new GameObject("StaffWait", typeof(RectTransform)));
                var canvas = _staffWait.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.WorldSpace;
                canvas.worldCamera = _rig.Camera;
                canvas.sortingOrder = 100;
                var rt = (RectTransform)_staffWait.transform;
                rt.sizeDelta = new Vector2(1800, 1200);
                rt.localScale = Vector3.one * 0.001f;
                RectTransform bg = StudyUi.NewRect("Panel", rt);
                StudyUi.Stretch(bg);
                _ui.Fill(bg, StudyTheme.Dark.Card, 60);
                Text t = _ui.NewText(bg, T("staff_wait"), StudyTheme.Dark.NoticeSize, TextAnchor.MiddleCenter, StudyTheme.Dark.Text, true);
                StudyUi.Stretch((RectTransform)t.transform);
            }
            _staffWait.SetActive(true);
            PlaceStaffWait();
        }

        private void PlaceStaffWait()
        {
            if (_staffWait == null || !_staffWait.activeSelf || _rig.Camera == null)
            {
                return;
            }
            Vector3 fwd = _rig.Camera.transform.forward;
            _staffWait.transform.position = _rig.HeadPosition + fwd * 1.5f;
            _staffWait.transform.rotation = Quaternion.LookRotation(fwd, Vector3.up);
        }

        /// <summary>Start (true) or end (false) the period in which F8 may skip the current clip or trial.</summary>
        private void SetStaffScope(bool on)
        {
            _staffScope = on;
            if (!on && StaffDialogOpen && !_dialogIsRecenter)
            {
                AnswerStaffDialog(false);
            }
        }

        // ------------------------------------------------------------------ session
        private IEnumerator RunSession()
        {
            yield return LanguageStep();
            yield return Checkin();
            List<FlowStep> flow = _s.Plan.Flow;
            for (int si = 0; si < flow.Count; si++)
            {
                FlowStep step = flow[si];
                _s.Step = step.ToString();
                _s.TrialLabel = "";
                _skipGroup = false;
                _skipPart = false;
                LogEvent("step_start", _s.Step);
                switch (step.Kind)
                {
                    case FlowStepKind.Checkin:
                        break;
                    case FlowStepKind.Ssq:
                        yield return SsqStep(step.Argument, 0f, SsqNoticeKey(flow, si));
                        break;
                    case FlowStepKind.BreakSsq:
                        yield return SsqStep(step.Argument, _s.Plan.MinBreakSeconds, "break_notice");
                        break;
                    case FlowStepKind.Break:
                        yield return BreakStep(_s.Plan.MinBreakSeconds);
                        break;
                    case FlowStepKind.HmdFit:
                        yield return HmdFitStep();
                        break;
                    case FlowStepKind.StereoTest:
                        yield return StereoTestStep();
                        break;
                    case FlowStepKind.EyeRange:
                        yield return EyeRangeStep();
                        break;
                    case FlowStepKind.GazeTraining:
                        yield return GazeTrainingStep();
                        break;
                    case FlowStepKind.Part:
                        if (step.PartNumber == 2)
                        {
                            yield return Part2Step(_s.Plan.GetPart(2));
                        }
                        else
                        {
                            yield return Part1Step(_s.Plan.GetPart(1));
                        }
                        break;
                    case FlowStepKind.PostQuestionnaire:
                        yield return PostQuestionnaireStep();
                        break;
                    case FlowStepKind.End:
                        break;
                }
                if (Interrupted)
                {
                    LogEvent("qa_skipped", _s.Step + (_skipPart ? " (rest of step)" : " (group)"));
                }
                LogEvent("step_end", _s.Step);
            }
            _run = null;
            FinishSession("complete", "");
        }

        private IEnumerator LanguageStep()
        {
            _s.Step = "language";
            string chosen = null;
            ShowGray();
            ShowHmdNotice("hmd_wait", null);
            _ui.ClearDesktop();
            Transform c = _ui.DesktopContent;
            _ui.Title(c, StudyTexts.Zh("language_title") + "    " + StudyTexts.En("language_title"));
            _ui.Spacer(c, 28);
            RectTransform row = _ui.NewRow(c, 40, TextAnchor.MiddleCenter, 96);
            _ui.NewButton(row, StudyTexts.Zh("language_zh"), 30, () => chosen = StudyTexts.Chinese, 320, 88, ButtonKind.Primary, "Btn_LangZh");
            _ui.NewButton(row, StudyTexts.En("language_en"), 30, () => chosen = StudyTexts.English, 320, 88, ButtonKind.Primary, "Btn_LangEn");
            while (chosen == null)
            {
                yield return null;
            }
            _s.Language = chosen;
            StudyTexts.Language = chosen;
            _ui.SetBarLabels(T("bar_recenter"), T("bar_abort"), T("bar_skip_group"), T("bar_skip_part"));
            LogEvent("language", chosen);
        }

        private IEnumerator Checkin()
        {
            _s.Step = "checkin";
            bool started = false;
            bool reminder = false;
            bool paperForms = false;
            ShowGray();
            ShowHmdNotice("hmd_wait", null);

            _ui.ClearDesktop();
            Transform c = _ui.DesktopContent;
            StudyTheme.Palette light = StudyTheme.Light;
            _ui.Title(c, T("checkin_title"));
            // who the page is for, and the QA notice, as tags under the title
            RectTransform tags = _ui.BadgeRow(c);
            _ui.Badge(tags, T("checkin_subtitle"), light.Tag, light.OnTag);
            if (_settings.IsQa)
            {
                _ui.Badge(tags, T("checkin_qa_badge"), light.WarningTint, light.Warning);
            }
            _ui.Spacer(c, 16);
            _ui.Secondary(c, T("checkin_hint"));
            RectTransform row = _ui.NewRow(c, 16, TextAnchor.MiddleLeft, 56);
            Text label = _ui.Strong(row, T("checkin_pid"), TextAnchor.MiddleLeft);
            label.gameObject.AddComponent<LayoutElement>().minWidth = 200; // wider when the label needs it (bold, English)
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            InputField pid = _ui.NewInput(row, _settings.ParticipantId, 26, 320, 52, false, "Input_Pid");
            Text status = null;
            _ui.NewButton(row, T("checkin_load"), 24, () => TryLoadPlan(pid.text.Trim(), status), 220, 52, ButtonKind.Secondary, "Btn_Load");
            status = _ui.Secondary(c, "", TextAnchor.UpperLeft, 20);
            _ui.Spacer(c, 6);
            _ui.NewToggle(c, T("checkin_reminder"), 22, v => reminder = v, "Toggle_Reminder");
            _ui.NewToggle(c, T("checkin_paper_forms"), 22, v => paperForms = v, "Toggle_PaperForms");
            _ui.Spacer(c, 18);
            RectTransform startRow = _ui.NewRow(c, 0, TextAnchor.MiddleCenter, 72);
            Button start = _ui.NewButton(startRow, T("checkin_start"), 28, () => started = true, 360, 68, ButtonKind.Primary, "Btn_Start");
            if (!string.IsNullOrEmpty(_settings.ParticipantId))
            {
                TryLoadPlan(_settings.ParticipantId, status);
            }

            while (!started)
            {
                start.interactable = _s.Plan != null && reminder && paperForms && (_settings.IsQa || _s.MissingStimuli.Count == 0);
                yield return null;
            }
            StartLogging(reminder, paperForms);
            _ui.ShowDesktopPanel(false);
        }

        private void TryLoadPlan(string participantId, Text status)
        {
            Color err = StudyTheme.Light.Error;
            Color warn = StudyTheme.Light.Warning;
            if (string.IsNullOrEmpty(participantId))
            {
                status.text = StudyTheme.Tint(T("checkin_need_pid"), err, true);
                return;
            }
            string path = _settings.PlanPath(participantId);
            try
            {
                StudyPlan plan = StudyPlan.Load(path);
                if (!string.Equals(plan.ParticipantId, participantId, StringComparison.Ordinal))
                {
                    throw new FormatException("plan participant_id is " + plan.ParticipantId);
                }
                _s.Plan = plan;
                _s.PlanPath = path;
                _s.PlanSha256 = StudyDataLogger.FileSha256(path);
                List<string> files = plan.AllStimulusFiles().ToList();
                _s.MissingStimuli.Clear();
                _s.MissingStimuli.AddRange(StudyDataLogger.MissingStimuli(_settings.StimuliDir, files));
                _s.SessionDir = StudyDataLogger.NewSessionDirectory(_settings.DataDir, participantId, DateTime.UtcNow, out bool existed);
                _s.SessionDirExisted = existed;
                var lines = new List<string> { StudyTheme.Tint(TF("checkin_loaded", participantId), StudyTheme.Light.Success, true) };
                if (_s.MissingStimuli.Count > 0)
                {
                    lines.Add(_settings.IsQa
                        ? StudyTheme.Tint(TF("checkin_missing_qa", _s.MissingStimuli.Count), warn, true)
                        : StudyTheme.Tint(TF("checkin_missing_blocked", _s.MissingStimuli.Count), err, true));
                    Debug.LogWarning("[Study] missing stimuli under " + _settings.StimuliDir + ":\n" + string.Join("\n", _s.MissingStimuli));
                }
                if (existed)
                {
                    lines.Add(StudyTheme.Tint(T("checkin_existing_data"), warn));
                }
                status.text = string.Join("\n", lines);
                LogEvent("plan_loaded", path + " sha256=" + _s.PlanSha256);
                if (_s.MissingStimuli.Count > 0)
                {
                    LogEvent("stimuli_missing", _s.MissingStimuli.Count + " under " + _settings.StimuliDir + ": "
                        + string.Join(" ", _s.MissingStimuli.Take(40)));
                }
            }
            catch (Exception e)
            {
                _s.Plan = null;
                status.text = StudyTheme.Tint(TF("checkin_load_failed", e.Message), err, true);
                LogEvent("plan_load_failed", path + ": " + e.Message);
            }
        }

        private Dictionary<string, object> PlanSummary(List<string> files)
        {
            StudyPlan p = _s.Plan;
            var parts = new List<object>();
            foreach (StudyPart part in p.Parts)
            {
                parts.Add(new Dictionary<string, object>
                {
                    { "part", part.Number },
                    { "trials", part.Trials.Count },
                    { "main", part.Trials.Count(t => t.IsMain) },
                    { "groups", part.Groups().Count },
                    { "self_paced", part.Options.SelfPaced },
                    { "min_view_s", Round(part.Options.MinViewSeconds) },
                    { "max_view_s", Round(part.Options.MaxViewSeconds) },
                    { "allow_review_on_rerate", part.Options.AllowReviewOnRerate },
                });
            }
            GazeTrainingPlan g = p.GazeTraining;
            EyeRangePlan er = p.EyeRange;
            return new Dictionary<string, object>
            {
                { "flow", p.FlowRaw.Cast<object>().ToList() },
                { "stereotest_trials", p.StereoTest.Enabled ? p.StereoTest.Trials.Count : 0 },
                { "eye_range", new Dictionary<string, object>
                    {
                        { "present", er.Present }, { "enabled", er.Enabled }, { "mode", er.Mode }, { "n_sweeps", er.Sweeps.Count },
                        { "directions_deg", er.DirectionsDeg.Select(x => (object)(double)x).ToList() }, { "repeats", er.Repeats },
                        { "speed_deg_s", Round(er.SpeedDegPerSecond) }, { "max_eccentricity_deg", Round(er.MaxEccentricityDeg) },
                        { "rule", er.Rule }, { "measure", er.Measure }, { "head_threshold_deg", Round(er.HeadThresholdDeg) },
                        { "paw_theta_variants_deg", er.PawThetaVariantsDeg.Select(x => (object)(double)x).ToList() },
                        { "default_paw_theta_deg", Round(er.DefaultPawThetaDeg) },
                    }
                },
                { "gaze_training", new Dictionary<string, object>
                    {
                        { "enabled", g.Enabled }, { "n_dots", g.DotCount }, { "radius_deg", Round(g.RadiusDeg) },
                        { "dwell_s", Round(g.DwellSeconds) }, { "max_head_deviation_deg", Round(g.MaxHeadDeviationDeg) },
                        { "max_attempts", g.MaxAttempts },
                    }
                },
                { "parts", parts },
                { "stimuli_files", files.Count },
                { "stimuli_missing", _s.MissingStimuli.Count },
                { "stimuli_missing_files", _s.MissingStimuli.Take(40).Cast<object>().ToList() },
                { "output_dir", _s.SessionDir },
                { "plan_file", _s.PlanPath },
                { "plan_file_sha256", _s.PlanSha256 },
            };
        }

        private void StartLogging(bool reminder, bool paperForms)
        {
            StudyPlan plan = _s.Plan;
            _s.Log = new StudyDataLogger(_s.SessionDir, plan.ParticipantId);
            _s.Events = new CsvLog(Path.Combine(_s.SessionDir, "events.csv"), new[] { "t_utc", "event", "detail" });
            foreach (string d in _s.DeferredEvents)
            {
                string[] f = d.Split(new[] { '\t' }, 3);
                _s.Events.Append(f[0], f[1], f.Length > 2 ? f[2] : "");
            }
            _s.DeferredEvents.Clear();
            string now = UtcNow();
            _s.Log.WriteJson("checkin.json", new Dictionary<string, object>
            {
                { "participant_id", plan.ParticipantId },
                { "verbal_reminder_given", reminder },
                { "paper_forms_confirmed", paperForms },
                { "language", _s.Language },
                { "mode", _settings.Mode },
                { "timestamp_utc", now },
            });
            List<string> files = plan.AllStimulusFiles().ToList();
            string manifest = StudyDataLogger.StimulusManifestSha256(_settings.StimuliDir, files, out int missing);
            Dictionary<string, object> s = _s.Session;
            s["participant_id"] = plan.ParticipantId;
            s["status"] = "in_progress";
            s["abort_reason"] = "";
            s["mode"] = _settings.Mode;
            s["language"] = _s.Language;
            s["plan_seed"] = plan.Seed;
            s["config_sha256"] = plan.ConfigSha256;
            s["study_id"] = plan.StudyId;
            s["config_version"] = plan.ConfigVersion;
            s["plan_format_version"] = plan.PlanFormatVersion;
            s["data_contract_version"] = StudySchema.ContractVersion;
            s["plan_file"] = _s.PlanPath;
            s["plan_file_sha256"] = _s.PlanSha256;
            s["flow"] = plan.FlowRaw.Cast<object>().ToList();
            s["part_order"] = plan.PartOrder.Cast<object>().ToList();
            s["app_version"] = AppVersion + " (Unity " + Application.unityVersion + ")";
            s["settings_source"] = _settings.Source;
            s["stimuli_dir"] = _settings.StimuliDir;
            s["stimuli_manifest_sha256"] = manifest;
            s["stimuli_missing"] = missing;
            s["xr_active"] = _rig.XrActive;
            s["hmd"] = _rig.HeadDeviceName();
            s["xr_runtime"] = _rig.XrActive ? XRSettings.loadedDeviceName : "";
            s["display_refresh_hz"] = _rig.XrActive ? (double)XRDevice.refreshRate : Screen.currentResolution.refreshRateRatio.value;
            s["start_utc"] = now;
            s["end_utc"] = "";
            s["plan_summary"] = PlanSummary(files);
            WriteSession();
            LogEvent("session_start", _s.SessionDir);
            LogEvent("plan_summary", MiniJson.Serialize(s["plan_summary"], false));
            if (!string.IsNullOrEmpty(StudyTexts.Status))
            {
                LogEvent("texts_status", StudyTexts.Status);
            }
            LogEvent("paths", "plans=" + _settings.PlansDir + " stimuli=" + _settings.StimuliDir + " data=" + _settings.DataDir
                + " backup=" + _settings.BackupDir + " settings=" + _settings.Source);
            _s.SessionStartRealtime = Time.realtimeSinceStartup;
        }

        private void WriteSession()
        {
            _s.Log?.WriteJson("session.json", _s.Session);
        }

        // ------------------------------------------------------------------ end of session
        private void FinishSession(string status, string reason)
        {
            if (_s.Finished)
            {
                return;
            }
            _s.Finished = true;
            _s.EndReady = false;
            StopAdvance();
            _clips.StopAll();
            ShowGray();
            _rdsQuad.SetActive(false);
            _aligning = false;
            _recenterLocked = false;
            if (_alignRing != null)
            {
                _alignRing.SetActive(false);
            }
            _colorMat.SetColor("_Color", StudyTheme.CueYellow);
            _cueMarker.SetActive(false);
            HideGazeDots();
            HideEyeTarget();
            _panelAnchor = Vector3.forward;
            _hmdFitActive = false;
            _trialYawOffset = 0f;
            ApplyContentYaw(0f);
            _s.Step = status;
            _s.TrialLabel = "";
            BuildEndPage(status);
            ShowHmdNotice(status == "complete" ? "end_hmd_title" : "aborted_hmd_title", "end_hmd");
            StartCoroutine(FinishWrite(status, reason));
        }

        private IEnumerator FinishWrite(string status, string reason)
        {
            // show the end page (buttons disabled) before writing and copying the folder
            yield return null;
            if (_s.Log != null)
            {
                _s.Session["status"] = status;
                _s.Session["abort_reason"] = reason ?? "";
                _s.Session["end_utc"] = UtcNow();
                _s.Session["dropped_frames_total"] = _s.DroppedTotal;
                if (_s.GazeAttempts.Count > 0)
                {
                    _s.Session["gaze_training"] = _s.GazeSummary();
                }
                if (_s.EyeRangeSweeps.Count > 0 && !_s.Session.ContainsKey("eye_range"))
                {
                    // aborted during the natural viewing range measurement: keep the medians, no radius was measured
                    _s.Session["eye_range"] = _s.ResolveEyeRange(_s.Plan.EyeRange, "aborted");
                }
                LogEvent("session_end", status + (string.IsNullOrEmpty(reason) ? "" : ": " + reason));
                try
                {
                    WriteSession();
                    LogEvent("session_written", Path.Combine(_s.Log.Directory, "session.json"));
                }
                catch (Exception e)
                {
                    LogEvent("session_write_failed", e.Message);
                    Debug.LogError("[Study] session.json write failed: " + e.Message);
                }
                try
                {
                    _s.BackupPath = _s.Log.Backup(_settings.BackupDir, DateTime.UtcNow);
                    LogEvent("backup", string.IsNullOrEmpty(_s.BackupPath) ? "no backup folder configured" : _s.BackupPath);
                }
                catch (Exception e)
                {
                    _s.BackupError = e.Message;
                    LogEvent("backup_failed", e.Message);
                    Debug.LogError("[Study] backup failed: " + e.Message);
                }
            }
            _s.EndReady = true;
            foreach (Button b in _endButtons)
            {
                if (b != null)
                {
                    b.interactable = true;
                }
            }
            LogEvent("end_page_ready", "");
        }

        private void BuildEndPage(string status)
        {
            _endButtons.Clear();
            _ui.ClearDesktop();
            Transform c = _ui.DesktopContent;
            _ui.Notice(c, T(status == "complete" ? "end_title" : "aborted_title"), T("end_body"));
            _ui.Spacer(c, 56);
            RectTransform row = _ui.NewRow(c, 28, TextAnchor.MiddleCenter, 64);
            Button exit = _ui.NewButton(row, T("end_exit"), 24, OnExitPressed, 220, 60, ButtonKind.Secondary, "Btn_Exit");
            Button next = _ui.NewButton(row, T("end_next"), 24, OnStartNext, 320, 60, ButtonKind.Primary, "Btn_StartNext");
            _exitConfirmRow = _ui.NewRow(c, 20, TextAnchor.MiddleCenter, 60);
            Text q = _ui.Secondary(_exitConfirmRow, T("end_exit_question"), TextAnchor.MiddleRight);
            q.gameObject.AddComponent<LayoutElement>().preferredWidth = 260;
            Button confirm = _ui.NewButton(_exitConfirmRow, T("end_exit_confirm"), 22, OnExitConfirmed, 220, 54, ButtonKind.Danger, "Btn_ExitConfirm");
            Button cancel = _ui.NewButton(_exitConfirmRow, T("cancel"), 22, OnExitCancelled, 180, 54, ButtonKind.Secondary, "Btn_ExitCancel");
            _exitConfirmRow.gameObject.SetActive(false);
            foreach (Button b in new[] { exit, next, confirm, cancel })
            {
                b.interactable = _s.EndReady;
                _endButtons.Add(b);
            }
        }

        private void OnExitPressed()
        {
            if (!EndReady)
            {
                return;
            }
            LogEvent("exit_requested", "");
            _exitConfirmRow.gameObject.SetActive(true);
        }

        private void OnExitCancelled()
        {
            LogEvent("exit_cancelled", "");
            _exitConfirmRow.gameObject.SetActive(false);
        }

        private void OnExitConfirmed()
        {
            if (!EndReady)
            {
                return;
            }
            LogEvent("exit_confirmed", "");
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private void OnStartNext()
        {
            if (!EndReady)
            {
                return;
            }
            LogEvent("start_next_participant", "");
            StartCoroutine(RestartRoutine());
        }

        /// <summary>
        /// Start next participant: stop the old session, destroy all runtime objects (UI, rig, sphere, clip
        /// players, materials, textures), reset the session state, reload settings and texts, rebuild, and
        /// run a new session from the language screen. The next session writes to a new folder.
        /// </summary>
        private IEnumerator RestartRoutine()
        {
            _restarting = true;
            StopAdvance();
            if (_run != null)
            {
                StopCoroutine(_run);
                _run = null;
            }
            DestroyWorld();
            yield return null; // Destroy is deferred to the end of the frame
            _s.Reset();
            ResolveSettings();
            LoadTexts();
            BuildWorld();
            _restarting = false;
            LogEvent("restart", "generation " + _s.Generation);
            _run = StartCoroutine(RunSession());
        }

        // ------------------------------------------------------------------ abort
        private void BuildAbortPanel()
        {
            if (_abortPanel != null)
            {
                Destroy(_abortPanel.gameObject);
            }
            StudyTheme.Palette p = StudyTheme.Light;
            RectTransform outer = _ui.NewCardFrame(_ui.DesktopCanvas.transform, "AbortPanel", new Vector2(958, 398), p, out RectTransform inner);
            var v = inner.gameObject.AddComponent<VerticalLayoutGroup>();
            v.padding = new RectOffset(48, 48, 36, 36);
            v.spacing = 16;
            v.childControlHeight = true;
            v.childControlWidth = true;
            v.childForceExpandHeight = false;
            v.childAlignment = TextAnchor.MiddleCenter;
            _ui.Colored(inner, T("abort_title"), p.Error, TextAnchor.MiddleCenter, 30, true);
            _abortReason = _ui.NewInput(inner, "", 22, 860, 110, true, "Input_AbortReason");
            RectTransform row = _ui.NewRow(inner, 20, TextAnchor.MiddleCenter, 60);
            _ui.NewButton(row, T("abort_confirm"), 22, ConfirmAbort, 260, 56, ButtonKind.Danger, "Btn_AbortConfirm");
            _ui.NewButton(row, T("cancel"), 22, () => _abortPanel.gameObject.SetActive(false), 200, 56, ButtonKind.Secondary, "Btn_AbortCancel");
            _abortPanel = outer;
        }

        private void OpenAbortPanel()
        {
            if (_s.Finished || _s.Log == null || _ui == null)
            {
                return;
            }
            BuildAbortPanel();
            _abortPanel.SetAsLastSibling();
            _abortPanel.gameObject.SetActive(true);
        }

        private void ConfirmAbort()
        {
            string reason = _abortReason.text.Trim();
            if (string.IsNullOrEmpty(reason))
            {
                reason = "(no reason given)";
            }
            _abortPanel.gameObject.SetActive(false);
            if (_run != null)
            {
                StopCoroutine(_run);
                _run = null;
            }
            FinishSession("aborted", reason + " (during " + _s.Step + " " + _s.TrialLabel + ")");
        }

        // ------------------------------------------------------------------ helpers
        /// <summary>
        /// Prominent desktop/headset notice for an SSQ step: before a break, "remove the headset and rest";
        /// right after a part, "remove the headset"; otherwise none.
        /// </summary>
        private static string SsqNoticeKey(List<FlowStep> flow, int i)
        {
            if (i + 1 < flow.Count && flow[i + 1].Kind == FlowStepKind.Break)
            {
                return "break_notice";
            }
            if (i > 0 && flow[i - 1].Kind == FlowStepKind.Part)
            {
                return "remove_notice";
            }
            return null;
        }

        /// <summary>Realtime wait that ends early when a QA skip is requested.</summary>
        private IEnumerator Wait(float seconds)
        {
            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < seconds && !Interrupted)
            {
                yield return null;
            }
        }

        private static string T(string key)
        {
            return StudyTexts.Get(key);
        }

        private static string TF(string key, params object[] args)
        {
            return StudyTexts.Format(key, args);
        }

        private static double Round(double v, int digits = 3)
        {
            return Math.Round(v, digits);
        }

        /// <summary>Full path of a plan stimulus; plan names may contain subfolders with forward slashes (user 11-20).</summary>
        private string StimulusPath(string file)
        {
            return StudyDataLogger.StimulusFullPath(_settings.StimuliDir, file);
        }

        /// <summary>Show a clip; in a Part 2 trial the sphere is turned so the trial's reference appears at the set forward.</summary>
        private void SetSphereTexture(Texture tex)
        {
            _sphereMat.SetTexture("_MainTex", tex);
            _sphereMat.SetFloat("_UseGray", 0f);
            ApplyContentYaw(_trialYawOffset);
        }

        /// <summary>Gray screen; the sphere rotation goes back to identity.</summary>
        private void ShowGray()
        {
            if (_sphereMat != null)
            {
                _sphereMat.SetFloat("_UseGray", 1f);
            }
            ApplyContentYaw(0f);
        }

        /// <summary>
        /// The only place that sets the panorama sphere rotation (user 11-20): yaw-only, content (ERP) yaw
        /// <paramref name="contentYawDeg"/> at world yaw 0. Identity everywhere except while a Part 2 clip is shown.
        /// </summary>
        private void ApplyContentYaw(float contentYawDeg)
        {
            _contentYawOffset = contentYawDeg;
            if (_sphere != null)
            {
                _sphere.rotation = StudyRig.ContentYawRotation(contentYawDeg);
            }
        }

        /// <summary>
        /// Displayed (world) yaw of a Part 2 trial's reference: 0 (the set forward) while the trial's content rotation
        /// applies, because the sphere shows reference_yaw_deg there. Used for the cross, panels, hints, the advance icon
        /// and the head-turn checks.
        /// </summary>
        private float DisplayYaw(StudyTrial t)
        {
            return StudyRig.WrapDegrees(t.ReferenceYawDeg - _trialYawOffset);
        }

        /// <summary>
        /// headpose.csv row. The head yaw is first converted to content (ERP) yaw (world yaw + the applied content yaw
        /// offset, user 11-20); Part 2 rows are then relative to the trial's reference (refYaw in ERP yaw), as before,
        /// so the analysis is unchanged. content_yaw_offset_deg is the applied offset (0 outside Part 2).
        /// </summary>
        private object[] HeadRow(int part, int trialIndex, string segment, float t, long frame, float refYaw, float refPitch)
        {
            _rig.HeadAngles(out float yaw, out float pitch, out float roll);
            yaw = StudyRig.ErpYaw(yaw, _contentYawOffset);
            if (part == 2)
            {
                yaw = StudyRig.WrapDegrees(yaw - refYaw);
                pitch -= refPitch;
            }
            return new object[] { _s.Plan.ParticipantId, part, trialIndex, segment, t, frame, yaw, pitch, roll, Round(_contentYawOffset, 2) };
        }

        /// <summary>Head deviation (deg) from a reference direction given in content yaw/pitch.</summary>
        private float HeadDeviation(float refYaw, float refPitch)
        {
            _rig.HeadAngles(out float yaw, out float pitch, out _);
            return (float)StudySchema.AngleFromReference(StudyRig.WrapDegrees(yaw - refYaw), pitch - refPitch);
        }

        private void WritePlayback(int part, int trialIndex, string segment, PlaybackStats st)
        {
            _s.DroppedTotal += st.FramesDropped;
            if (!string.IsNullOrEmpty(st.FullPath))
            {
                st.File = StudyDataLogger.RelativeStimulusName(_settings.StimuliDir, st.FullPath); // with its subfolder, as in the plan
            }
            _s.Log.Playback.Append(_s.Plan.ParticipantId, part, trialIndex, segment, st.File, st.FramesExpected, st.FramesShown,
                st.FramesDropped, st.DtMeanMs, st.DtP99Ms);
            if (st.Missing || !string.IsNullOrEmpty(st.Error))
            {
                LogEvent("playback_issue", st.File + ": " + (st.Missing ? "missing/failed " : "") + st.Error);
            }
        }

        private void LogEvent(string ev, string detail)
        {
            if (_s.Events == null)
            {
                _s.DeferredEvents.Add(UtcNow() + "\t" + ev + "\t" + (detail ?? ""));
                return;
            }
            try
            {
                _s.Events.Append(UtcNow(), ev, detail ?? "");
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Study] event log failed: " + e.Message);
            }
        }

        private static string UtcNow()
        {
            return DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
        }
    }
}
