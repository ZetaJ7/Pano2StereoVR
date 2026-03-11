using System;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace Pano2StereoVR
{
    public sealed class ExperimentController : MonoBehaviour
    {
        [SerializeField] private SharedMemoryReceiver sharedMemoryReceiver;
        [SerializeField] private UdpGazeSender udpGazeSender;
        [SerializeField] private HeadPoseTracker headPoseTracker;
        [SerializeField] private StereoSphereRenderer stereoSphereRenderer;
        [SerializeField] private RtspBaselineReceiver rtspBaselineReceiver;
        [SerializeField] private BaselinePanoramaRenderer baselinePanoramaRenderer;
        [SerializeField] private KeyCode mode1Key = KeyCode.Alpha1;
        [SerializeField] private KeyCode mode2Key = KeyCode.Alpha2;
        [SerializeField] private KeyCode mode3Key = KeyCode.Alpha3;
        [SerializeField] private KeyCode mode4Key = KeyCode.Alpha4;
        [SerializeField] private KeyCode quitKey = KeyCode.Escape;
        [SerializeField] private KeyCode ipdIncreaseKey = KeyCode.Equals;
        [SerializeField] private KeyCode ipdIncreaseKeyAlt = KeyCode.KeypadPlus;
        [SerializeField] private KeyCode ipdDecreaseKey = KeyCode.Minus;
        [SerializeField] private KeyCode ipdDecreaseKeyAlt = KeyCode.KeypadMinus;
        [SerializeField] private KeyCode ipdResetKey = KeyCode.Alpha0;
        [SerializeField] private float ipdDefault = 0.065f;
        [SerializeField] private float ipdStep = 0.005f;
        [SerializeField] private float ipdMin = 0.0f;
        [SerializeField] private float ipdMax = 0.130f;
        [SerializeField] private bool showOverlay = true;
        [SerializeField] private bool showShmPreview = true;
        [SerializeField] [Range(0.1f, 0.5f)] private float shmPreviewWidthRatio = 0.40f;
        [SerializeField] [Min(0.1f)] private float applyTimeoutSeconds = 1.0f;
        [SerializeField] [Range(0.25f, 2.0f)] private float fpsSampleWindowSeconds = 1.0f;
        [SerializeField] private bool writeValidationLog = true;
        [SerializeField] private string validationLogFileName = "g3_mode_validation.jsonl";
        [SerializeField] private bool startInMode4 = false;

        private int _lastRequestedMode = 3;
        private int _lastSentMode = -1;
        private int _lastAppliedMode = -1;
        private float _requestTime = -1f;
        private float _sentTime = -1f;
        private float _appliedTime = -1f;
        private float _lastAppliedLatencyMs = -1f;
        private bool _hasPendingRequest;
        private bool _requestTimedOut;
        private string _validationLogPath = string.Empty;
        private float _fpsWindowStartTime = -1f;
        private long _fpsWindowStartAcceptedFrames;
        private float _shmReceiveFps;
        private float _unityFpsSmoothed;
        private float _currentIpd;
        private bool _pendingInitialIpdSync;
        private bool _isMode4Active;
        private string _rtspUrlInput = string.Empty;
        private bool _isRtspUrlFieldFocused;
        private bool _clearRtspUrlFieldFocus;

        private const int ModeMin = 1;
        private const int ModeMax = 4;
        private const int Mode4Baseline = 4;
        private const string RtspUrlFieldControlName = "Mode4RtspUrlField";

        public long RequestedSwitchCount { get; private set; }
        public long AppliedSwitchCount { get; private set; }
        public long TimeoutCount { get; private set; }
        public string ValidationLogPath => _validationLogPath;

        public int CurrentMode
        {
            get
            {
                if (_isMode4Active)
                {
                    return Mode4Baseline;
                }
                if (sharedMemoryReceiver != null)
                {
                    return sharedMemoryReceiver.CurrentMode;
                }
                return udpGazeSender != null ? udpGazeSender.CurrentMode : 3;
            }
        }

        private void OnEnable()
        {
            TryResolveReferences();
            _fpsWindowStartTime = -1f;
            _fpsWindowStartAcceptedFrames = 0;
            _shmReceiveFps = 0f;
            _unityFpsSmoothed = 0f;
            _currentIpd = Mathf.Clamp(ipdDefault, ipdMin, ipdMax);
            _pendingInitialIpdSync = true;
            _isMode4Active = false;
            SyncRtspUrlInputFromReceiver();
            _isRtspUrlFieldFocused = false;
            _clearRtspUrlFieldFocus = false;
            if (udpGazeSender != null)
            {
                udpGazeSender.ModeMessageSent += OnModeSent;
            }
            if (sharedMemoryReceiver != null)
            {
                sharedMemoryReceiver.ModeApplied += OnModeApplied;
                _lastAppliedMode = sharedMemoryReceiver.CurrentMode;
            }
            if (headPoseTracker != null)
            {
                headPoseTracker.DebugOverrideApplied += OnDebugOverrideApplied;
                headPoseTracker.DebugOverrideCleared += OnDebugOverrideCleared;
            }
            if (udpGazeSender != null)
            {
                _lastRequestedMode = udpGazeSender.CurrentMode;
            }

            if (writeValidationLog)
            {
                _validationLogPath = Path.Combine(Application.persistentDataPath, validationLogFileName);
            }

            SetMode4Active(startInMode4, "startup", true);
            if (_isMode4Active)
            {
                _lastRequestedMode = Mode4Baseline;
                _lastAppliedMode = Mode4Baseline;
                _appliedTime = Time.unscaledTime;
            }
            if (writeValidationLog)
            {
                WriteValidationEvent("session_start", CurrentMode, "controller enabled");
            }
        }

        private void OnDisable()
        {
            if (writeValidationLog)
            {
                WriteValidationEvent("session_end", CurrentMode, "controller disabled");
            }
            if (udpGazeSender != null)
            {
                udpGazeSender.ModeMessageSent -= OnModeSent;
            }
            if (sharedMemoryReceiver != null)
            {
                sharedMemoryReceiver.ModeApplied -= OnModeApplied;
            }
            if (headPoseTracker != null)
            {
                headPoseTracker.DebugOverrideApplied -= OnDebugOverrideApplied;
                headPoseTracker.DebugOverrideCleared -= OnDebugOverrideCleared;
            }
        }

        private void Update()
        {
            UpdateOverlayFps();

            if (_isRtspUrlFieldFocused)
            {
                if (Input.GetKeyDown(quitKey))
                {
                    _clearRtspUrlFieldFocus = true;
                }
                return;
            }

            if (Input.GetKeyDown(quitKey))
            {
                RequestQuit();
                return;
            }

            if (Input.GetKeyDown(mode1Key))
            {
                RequestModeSwitch(1);
            }
            if (Input.GetKeyDown(mode2Key))
            {
                RequestModeSwitch(2);
            }
            if (Input.GetKeyDown(mode3Key))
            {
                RequestModeSwitch(3);
            }
            if (Input.GetKeyDown(mode4Key))
            {
                RequestModeSwitch(Mode4Baseline);
            }

            if (_isMode4Active)
            {
                return;
            }

            if (udpGazeSender == null)
            {
                return;
            }

            if (_pendingInitialIpdSync && udpGazeSender.IsConnected)
            {
                SendCurrentIpd("initial_sync");
                _pendingInitialIpdSync = false;
            }

            if (Input.GetKeyDown(ipdIncreaseKey) || Input.GetKeyDown(ipdIncreaseKeyAlt))
            {
                AdjustIpd(ipdStep);
            }
            if (Input.GetKeyDown(ipdDecreaseKey) || Input.GetKeyDown(ipdDecreaseKeyAlt))
            {
                AdjustIpd(-ipdStep);
            }
            if (Input.GetKeyDown(ipdResetKey))
            {
                ResetIpdToDefault();
            }

            if (_hasPendingRequest && !_requestTimedOut)
            {
                float elapsed = Time.unscaledTime - _requestTime;
                if (elapsed > applyTimeoutSeconds)
                {
                    _requestTimedOut = true;
                    TimeoutCount += 1;
                    Debug.LogWarning(
                        "[ExperimentController] mode apply timeout: requested="
                        + _lastRequestedMode + " elapsed=" + elapsed.ToString("F3") + "s"
                    );
                    WriteValidationEvent(
                        "mode_timeout",
                        _lastRequestedMode,
                        "elapsed=" + elapsed.ToString("F3", CultureInfo.InvariantCulture)
                    );
                }
            }
        }

        private void RequestQuit()
        {
            Debug.Log("[ExperimentController] quit requested via ESC");
            WriteValidationEvent("quit_requested", CurrentMode, "keyboard_escape");
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private void OnGUI()
        {
            if (!showOverlay)
            {
                return;
            }

            string requestState = _hasPendingRequest
                ? (_requestTimedOut ? "Timeout" : "Pending")
                : "Applied";
            GUIStyle compactLabelStyle = new GUIStyle(GUI.skin.label)
            {
                margin = new RectOffset(0, 0, 0, 1),
                padding = new RectOffset(0, 0, 0, 0)
            };
            GUIStyle titleStyle = new GUIStyle(compactLabelStyle)
            {
                fontStyle = FontStyle.Bold,
                margin = new RectOffset(0, 0, 0, 2)
            };
            GUIStyle modeTitleStyle = new GUIStyle(titleStyle);
            modeTitleStyle.normal.textColor = new Color(0.45f, 1.0f, 0.45f);
            GUIStyle wrapLabelStyle = new GUIStyle(compactLabelStyle)
            {
                wordWrap = true
            };
            GUIStyle compactButtonStyle = new GUIStyle(GUI.skin.button)
            {
                margin = new RectOffset(0, 0, 0, 0),
                padding = new RectOffset(6, 6, 1, 1)
            };
            GUIStyle compactTextFieldStyle = new GUIStyle(GUI.skin.textField)
            {
                margin = new RectOffset(0, 0, 0, 0),
                padding = new RectOffset(4, 4, 2, 2)
            };
            GUIStyle warningLabelStyle = new GUIStyle(wrapLabelStyle)
            {
                fontStyle = FontStyle.Bold
            };
            warningLabelStyle.normal.textColor = new Color(1.0f, 0.78f, 0.35f);

            string mode4PromptMessage = GetMode4PromptMessage();
            bool hasRtspPrompt = _isMode4Active && !string.IsNullOrEmpty(mode4PromptMessage);
            bool hasRtspError = _isMode4Active
                && rtspBaselineReceiver != null
                && !string.IsNullOrEmpty(rtspBaselineReceiver.LastError);
            float boxHeight = CalculateCompactOverlayHeight();
            float columnGap = 6f;
            float column1Width = headPoseTracker != null && headPoseTracker.IsDebugOverrideActive ? 228f : 208f;
            float column2Width = _isMode4Active ? 216f : 212f;
            float column3Width = (hasRtspPrompt || hasRtspError) ? 440f : (_isMode4Active ? 390f : 300f);
            float desiredInnerWidth = column1Width + column2Width + column3Width + columnGap * 2f;
            float maxInnerWidth = Screen.width - 48f;
            float widthScale = desiredInnerWidth > maxInnerWidth ? maxInnerWidth / desiredInnerWidth : 1f;
            column1Width *= widthScale;
            column2Width *= widthScale;
            column3Width *= widthScale;
            float innerWidth = desiredInnerWidth * widthScale;
            float boxWidth = innerWidth + 16f;
            int displayedMode = ResolveDisplayedMode();
            string displayedModeLabel = GetModeOverlayLabel(displayedMode);



            GUI.color = Color.black;
            GUI.Box(new Rect(16f, 16f, boxWidth, boxHeight), GUIContent.none);
            GUI.color = Color.white;

            GUILayout.BeginArea(new Rect(24f, 22f, innerWidth, boxHeight - 10f));
            GUILayout.BeginHorizontal();

            GUILayout.BeginVertical(GUILayout.Width(column1Width));
            GUILayout.Label(displayedModeLabel, modeTitleStyle);
            GUILayout.Label(
                "Switch: req/sent/app "
                + _lastRequestedMode + "/" + _lastSentMode + "/" + _lastAppliedMode
                + " (" + requestState + ")",
                compactLabelStyle
            );
            if (_lastAppliedLatencyMs >= 0f)
            {
                GUILayout.Label("Mode Switch Latency: " + _lastAppliedLatencyMs.ToString("F1") + " ms", compactLabelStyle);
            }
            GUILayout.BeginHorizontal();
            GUILayout.Label(
                "IPD: " + (_currentIpd * 1000f).ToString("F1", CultureInfo.InvariantCulture) + "mm",
                compactLabelStyle,
                GUILayout.ExpandWidth(false)
            );
            GUILayout.Space(4f);
            if (GUILayout.Button(
                    "Reset IPD",
                    compactButtonStyle,
                    GUILayout.Width(88f),
                    GUILayout.Height(20f),
                    GUILayout.ExpandWidth(false)))
            {
                ResetIpdToDefault();
            }
            GUILayout.EndHorizontal();
            if (headPoseTracker != null)
            {
                GUILayout.Label("View: " + headPoseTracker.CurrentPoseSourceLabel, compactLabelStyle);
                if (headPoseTracker.IsMouseLookEnabled)
                {
                    GUILayout.Label("Mouse: M toggle, RMB drag", compactLabelStyle);
                }
                if (headPoseTracker.IsDebugOverrideActive)
                {
                    GUILayout.Label(
                        "Debug u0: " + FormatVector(headPoseTracker.DebugOverrideVector),
                        wrapLabelStyle
                    );
                }
            }
            GUILayout.EndVertical();

            GUILayout.Space(columnGap);

            GUILayout.BeginVertical(GUILayout.Width(column2Width));
            GUILayout.Label("Performance", titleStyle);
            if (_isMode4Active)
            {
                GUILayout.Label(
                    "Unity/Decode: " + _unityFpsSmoothed.ToString("F1")
                    + " / "
                    + (rtspBaselineReceiver != null ? rtspBaselineReceiver.DecodedFps.ToString("F1") : "0.0")
                    + " fps",
                    compactLabelStyle
                );
            }
            else
            {
                GUILayout.Label(
                    "Unity/SHM: " + _unityFpsSmoothed.ToString("F1")
                    + " / " + _shmReceiveFps.ToString("F1") + " fps",
                    compactLabelStyle
                );
                GUILayout.Label(
                    "Writer FPS: "
                    + (sharedMemoryReceiver != null ? sharedMemoryReceiver.ObservedWriterFps.ToString("F1") : "0.0"),
                    compactLabelStyle
                );
            }

            if (_isMode4Active && baselinePanoramaRenderer != null && baselinePanoramaRenderer.HasBoundTexture)
            {
                GUILayout.Label(
                    "Texture: " + baselinePanoramaRenderer.BoundTextureWidth + "x"
                    + baselinePanoramaRenderer.BoundTextureHeight,
                    compactLabelStyle
                );
            }
            else if (!_isMode4Active && stereoSphereRenderer != null && stereoSphereRenderer.HasBoundTexture)
            {
                GUILayout.Label(
                    "Texture: " + stereoSphereRenderer.BoundTextureWidth + "x"
                    + stereoSphereRenderer.BoundTextureHeight,
                    compactLabelStyle
                );
            }
            else
            {
                GUILayout.Label("Texture: unbound", compactLabelStyle);
            }

            if (_isMode4Active)
            {
                GUILayout.Label(
                    "Frames: decoded="
                    + (rtspBaselineReceiver != null ? rtspBaselineReceiver.DecodedFrames.ToString(CultureInfo.InvariantCulture) : "0")
                    + " dropped="
                    + (rtspBaselineReceiver != null ? rtspBaselineReceiver.DroppedFrames.ToString(CultureInfo.InvariantCulture) : "0"),
                    compactLabelStyle
                );
            }
            else if (sharedMemoryReceiver != null)
            {
                GUILayout.Label(
                    "Frames: accepted=" + sharedMemoryReceiver.AcceptedFrames
                    + " mode_changes=" + sharedMemoryReceiver.ModeChangesApplied,
                    compactLabelStyle
                );
            }
            GUILayout.EndVertical();

            GUILayout.Space(columnGap);

            GUILayout.BeginVertical(GUILayout.Width(column3Width));
            GUILayout.Label("Receiver", titleStyle);
            if (_isMode4Active)
            {
                if (rtspBaselineReceiver != null)
                {
                    GUILayout.Label(
                        "RTSP: running=" + FormatYesNo(rtspBaselineReceiver.IsRunning)
                        + " connected=" + FormatYesNo(rtspBaselineReceiver.IsConnected)
                        + " restarts=" + rtspBaselineReceiver.RestartCount,
                        compactLabelStyle
                    );
                }
                if (baselinePanoramaRenderer != null)
                {
                    GUILayout.Label(
                        "Render: visible=" + FormatYesNo(baselinePanoramaRenderer.RendererVisible)
                        + " tex=" + FormatYesNo(baselinePanoramaRenderer.HasBoundTexture),
                        compactLabelStyle
                    );
                }
                if (rtspBaselineReceiver != null)
                {
                    DrawRtspUrlEditor(compactLabelStyle, compactTextFieldStyle, compactButtonStyle);
                }
                if (!string.IsNullOrEmpty(mode4PromptMessage))
                {
                    GUILayout.Label(mode4PromptMessage, warningLabelStyle);
                }
                if (rtspBaselineReceiver != null && !string.IsNullOrEmpty(rtspBaselineReceiver.LastError))
                {
                    GUILayout.Label("Error: " + rtspBaselineReceiver.LastError, wrapLabelStyle);
                }
            }
            else
            {
                if (sharedMemoryReceiver != null)
                {
                    GUILayout.Label(
                        "SHM: busy=" + sharedMemoryReceiver.WriterBusySkips
                        + " torn=" + sharedMemoryReceiver.TornRejected,
                        compactLabelStyle
                    );
                }
                if (stereoSphereRenderer != null)
                {
                    GUILayout.Label(
                        "Render: visible=" + FormatYesNo(stereoSphereRenderer.RendererVisible)
                        + " tex=" + FormatYesNo(stereoSphereRenderer.HasBoundTexture),
                        compactLabelStyle
                    );
                }
            }
            GUILayout.EndVertical();

            GUILayout.EndHorizontal();
            GUILayout.EndArea();

            DrawShmPreview();
        }

        private void DrawRtspUrlEditor(
            GUIStyle compactLabelStyle,
            GUIStyle compactTextFieldStyle,
            GUIStyle compactButtonStyle)
        {
            if (rtspBaselineReceiver == null)
            {
                _isRtspUrlFieldFocused = false;
                _clearRtspUrlFieldFocus = false;
                return;
            }

            GUILayout.Label("RTSP URL", compactLabelStyle);
            GUILayout.BeginHorizontal();
            GUI.SetNextControlName(RtspUrlFieldControlName);
            string nextInput = GUILayout.TextField(
                _rtspUrlInput ?? string.Empty,
                compactTextFieldStyle,
                GUILayout.MinWidth(180f),
                GUILayout.ExpandWidth(true),
                GUILayout.Height(20f));
            if (!string.Equals(nextInput, _rtspUrlInput, StringComparison.Ordinal))
            {
                _rtspUrlInput = nextInput;
            }
            bool applyClicked = GUILayout.Button(
                "Apply",
                compactButtonStyle,
                GUILayout.Width(54f),
                GUILayout.Height(20f),
                GUILayout.ExpandWidth(false));
            GUILayout.EndHorizontal();

            Event currentEvent = Event.current;
            if (_clearRtspUrlFieldFocus)
            {
                GUI.FocusControl(string.Empty);
                _clearRtspUrlFieldFocus = false;
            }

            _isRtspUrlFieldFocused = string.Equals(
                GUI.GetNameOfFocusedControl(),
                RtspUrlFieldControlName,
                StringComparison.Ordinal);

            if (_isRtspUrlFieldFocused
                && currentEvent != null
                && currentEvent.type == EventType.KeyDown
                && currentEvent.keyCode == quitKey)
            {
                GUI.FocusControl(string.Empty);
                _isRtspUrlFieldFocused = false;
                currentEvent.Use();
                return;
            }

            bool submitPressed = _isRtspUrlFieldFocused
                && currentEvent != null
                && currentEvent.type == EventType.KeyDown
                && (currentEvent.keyCode == KeyCode.Return || currentEvent.keyCode == KeyCode.KeypadEnter);
            if (submitPressed)
            {
                currentEvent.Use();
            }

            if (applyClicked || submitPressed)
            {
                ApplyRtspUrlInput();
            }
        }

        private void SyncRtspUrlInputFromReceiver()
        {
            _rtspUrlInput = rtspBaselineReceiver != null ? rtspBaselineReceiver.StreamUrl : string.Empty;
        }

        private string GetMode4PromptMessage()
        {
            if (!_isMode4Active || rtspBaselineReceiver == null)
            {
                return string.Empty;
            }

            if (string.IsNullOrWhiteSpace(rtspBaselineReceiver.StreamUrl))
            {
                return "Mode4 requires an RTSP URL. Enter a stream address and press Apply.";
            }

            if (rtspBaselineReceiver.IsConnected)
            {
                return string.Empty;
            }

            if (!string.IsNullOrEmpty(rtspBaselineReceiver.LastError))
            {
                return "Mode4 cannot open the current RTSP stream. Check the address or server, then apply again.";
            }

            return "Mode4 is waiting for the RTSP stream. If no video appears, verify the address and stream server.";
        }

        private void ApplyRtspUrlInput()
        {
            if (rtspBaselineReceiver == null)
            {
                Debug.LogWarning("[ExperimentController] cannot apply RTSP URL without RTSP receiver.");
                return;
            }

            string nextUrl = (_rtspUrlInput ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(nextUrl))
            {
                Debug.LogWarning("[ExperimentController] RTSP URL input is empty.");
                WriteValidationEvent("mode4_url_rejected", Mode4Baseline, "empty_input");
                return;
            }

            bool restartIfRunning = _isMode4Active;
            bool changed = !string.Equals(rtspBaselineReceiver.StreamUrl, nextUrl, StringComparison.Ordinal);
            if (!rtspBaselineReceiver.ApplyStreamUrl(nextUrl, restartIfRunning))
            {
                WriteValidationEvent("mode4_url_rejected", Mode4Baseline, "receiver_rejected");
                return;
            }

            SyncRtspUrlInputFromReceiver();
            _clearRtspUrlFieldFocus = true;
            string details = "restart=" + FormatYesNo(restartIfRunning)
                + ",changed=" + FormatYesNo(changed)
                + ",url=" + rtspBaselineReceiver.DisplayUrl;
            WriteValidationEvent("mode4_url_applied", Mode4Baseline, details);
            Debug.Log(
                "[ExperimentController] mode4 RTSP URL applied: "
                + rtspBaselineReceiver.DisplayUrl
                + (restartIfRunning ? " (receiver refreshed)" : " (saved)")
            );
        }

        private int ResolveDisplayedMode()
        {
            if (_isMode4Active)
            {
                return Mode4Baseline;
            }

            if (_hasPendingRequest && _lastRequestedMode >= ModeMin && _lastRequestedMode < Mode4Baseline)
            {
                return _lastRequestedMode;
            }

            if (_lastAppliedMode >= ModeMin && _lastAppliedMode < Mode4Baseline)
            {
                return _lastAppliedMode;
            }

            return Mathf.Clamp(CurrentMode, ModeMin, Mode4Baseline - 1);
        }

        private static string GetModeOverlayLabel(int mode)
        {
            switch (mode)
            {
                case 1:
                    return "MONO";
                case 2:
                    return "Stereo";
                case 3:
                    return "Stereo+HMD";
                case 4:
                    return "Baseline";
                default:
                    return "Unknown";
            }
        }

        private float CalculateCompactOverlayHeight()
        {
            int column1Lines = 3;
            if (_lastAppliedLatencyMs >= 0f)
            {
                column1Lines += 1;
            }
            if (headPoseTracker != null)
            {
                column1Lines += 1;
                if (headPoseTracker.IsMouseLookEnabled)
                {
                    column1Lines += 1;
                }
                if (headPoseTracker.IsDebugOverrideActive)
                {
                    column1Lines += 1;
                }
            }

            int column2Lines = _isMode4Active ? 4 : 5;
            int column3Lines = 3;
            if (_isMode4Active)
            {
                column3Lines += 2;
            }
            if (_isMode4Active && !string.IsNullOrEmpty(GetMode4PromptMessage()))
            {
                column3Lines += 3;
            }
            if (_isMode4Active && rtspBaselineReceiver != null && !string.IsNullOrEmpty(rtspBaselineReceiver.LastError))
            {
                column3Lines += 2;
            }

            int maxLines = Mathf.Max(column1Lines, Mathf.Max(column2Lines, column3Lines));
            float lineHeight = 16f;
            float chromeHeight = 20f;
            return Mathf.Min(chromeHeight + maxLines * lineHeight, Screen.height * 0.30f);
        }

        private static string FormatYesNo(bool value)
        {
            return value ? "yes" : "no";
        }

        private void AdjustIpd(float delta)
        {
            float newIpd = Mathf.Clamp(_currentIpd + delta, ipdMin, ipdMax);
            if (Mathf.Approximately(newIpd, _currentIpd))
            {
                return;
            }
            _currentIpd = newIpd;
            SendCurrentIpd("adjust");
            Debug.Log(
                "[ExperimentController] IPD adjusted to "
                + (_currentIpd * 1000f).ToString("F1", CultureInfo.InvariantCulture) + "mm"
            );
        }

        private void ResetIpdToDefault()
        {
            float defaultIpd = Mathf.Clamp(ipdDefault, ipdMin, ipdMax);
            if (Mathf.Approximately(defaultIpd, _currentIpd))
            {
                return;
            }
            _currentIpd = defaultIpd;
            SendCurrentIpd("reset");
            Debug.Log(
                "[ExperimentController] IPD reset to default: "
                + (_currentIpd * 1000f).ToString("F1", CultureInfo.InvariantCulture) + "mm"
            );
        }

        private void RequestModeSwitch(int mode)
        {
            int clampedMode = Mathf.Clamp(mode, ModeMin, ModeMax);
            if (clampedMode == Mode4Baseline)
            {
                RequestMode4Baseline();
                return;
            }

            if (udpGazeSender == null)
            {
                Debug.LogWarning("[ExperimentController] cannot switch mode without UDP sender.");
                WriteValidationEvent("mode_request_failed", clampedMode, "udp_missing");
                return;
            }

            if (_isMode4Active)
            {
                SetMode4Active(false, "switch_to_mode_" + clampedMode.ToString(CultureInfo.InvariantCulture));
            }

            bool alreadyApplied = sharedMemoryReceiver != null
                && sharedMemoryReceiver.enabled
                && sharedMemoryReceiver.IsOpened
                && sharedMemoryReceiver.CurrentMode == clampedMode
                && _lastAppliedMode == clampedMode;

            _lastRequestedMode = clampedMode;
            _requestTime = Time.unscaledTime;
            _requestTimedOut = false;
            RequestedSwitchCount += 1;

            if (alreadyApplied)
            {
                _hasPendingRequest = false;
                _appliedTime = Time.unscaledTime;
                _lastAppliedLatencyMs = 0f;
                udpGazeSender.SetMode(clampedMode);
                SendCurrentIpd("mode_switch");
                Debug.Log("[ExperimentController] requested mode already applied -> " + clampedMode);
                WriteValidationEvent("mode_requested", clampedMode, "keyboard_already_applied");
                return;
            }

            _hasPendingRequest = true;
            udpGazeSender.SetMode(clampedMode);
            SendCurrentIpd("mode_switch");
            Debug.Log("[ExperimentController] requested mode -> " + clampedMode);
            WriteValidationEvent("mode_requested", clampedMode, "keyboard");
        }

        private void RequestMode4Baseline()
        {
            _lastRequestedMode = Mode4Baseline;
            _requestTime = Time.unscaledTime;
            _requestTimedOut = false;
            RequestedSwitchCount += 1;

            if (_isMode4Active)
            {
                _hasPendingRequest = false;
                _appliedTime = Time.unscaledTime;
                _lastAppliedLatencyMs = 0f;
                WriteValidationEvent("mode_requested", Mode4Baseline, "keyboard_already_applied");
                return;
            }

            WriteValidationEvent("mode_requested", Mode4Baseline, "keyboard");
            SetMode4Active(true, "keyboard");
            if (!_isMode4Active)
            {
                return;
            }
            _hasPendingRequest = false;
            _appliedTime = Time.unscaledTime;
            _lastAppliedLatencyMs = 0f;
            _lastAppliedMode = Mode4Baseline;
            AppliedSwitchCount += 1;
            WriteValidationEvent("mode_applied", Mode4Baseline, "local_baseline", 0f);
            Debug.Log("[ExperimentController] mode applied -> 4 (local baseline)");
        }

        private void SendCurrentIpd(string reason)
        {
            if (_isMode4Active || udpGazeSender == null)
            {
                return;
            }

            udpGazeSender.SendIpd(_currentIpd);
            WriteValidationEvent(
                "ipd_sent",
                CurrentMode,
                "reason=" + reason + ",ipd_mm="
                + (_currentIpd * 1000f).ToString("F1", CultureInfo.InvariantCulture)
            );
            Debug.Log(
                "[ExperimentController] IPD sent (" + reason + "): "
                + (_currentIpd * 1000f).ToString("F1", CultureInfo.InvariantCulture) + "mm"
            );
        }

        private void SetMode4Active(bool active, string reason, bool force = false)
        {
            if (!force && _isMode4Active == active)
            {
                return;
            }

            if (active)
            {
                TryResolveReferences();
                EnsureMode4Components();
                SyncRtspUrlInputFromReceiver();
            }

            if (active && (rtspBaselineReceiver == null || baselinePanoramaRenderer == null))
            {
                Debug.LogWarning("[ExperimentController] cannot enable mode 4 without RTSP receiver and mono renderer.");
                WriteValidationEvent("mode_request_failed", Mode4Baseline, "mode4_components_missing");
                return;
            }

            _isMode4Active = active;

            if (sharedMemoryReceiver != null)
            {
                sharedMemoryReceiver.enabled = !active;
            }
            if (stereoSphereRenderer != null)
            {
                stereoSphereRenderer.enabled = !active;
            }
            if (udpGazeSender != null)
            {
                udpGazeSender.enabled = !active;
            }
            if (rtspBaselineReceiver != null)
            {
                rtspBaselineReceiver.enabled = active;
            }
            if (baselinePanoramaRenderer != null)
            {
                baselinePanoramaRenderer.enabled = active;
            }

            if (active)
            {
                _hasPendingRequest = false;
                _requestTimedOut = false;
                _lastSentMode = -1;
                _shmReceiveFps = 0f;
            }
            else
            {
                _pendingInitialIpdSync = true;
                _fpsWindowStartTime = -1f;
                _isRtspUrlFieldFocused = false;
                _clearRtspUrlFieldFocus = false;
            }

            WriteValidationEvent(active ? "mode4_enabled" : "mode4_disabled", Mode4Baseline, reason);
        }

        private void OnModeSent(int mode, float sentTime)
        {
            _lastSentMode = mode;
            _sentTime = sentTime;
            WriteValidationEvent("mode_sent", mode, "udp");
        }

        private void OnModeApplied(int mode, ulong seq, float appliedTime)
        {
            _lastAppliedMode = mode;
            _appliedTime = appliedTime;
            if (_hasPendingRequest && mode == _lastRequestedMode)
            {
                _hasPendingRequest = false;
                AppliedSwitchCount += 1;
                _lastAppliedLatencyMs = (appliedTime - _requestTime) * 1000f;
                Debug.Log(
                    "[ExperimentController] mode applied -> " + mode
                    + " @seq=" + seq + " latency=" + _lastAppliedLatencyMs.ToString("F1") + "ms"
                );
                WriteValidationEvent(
                    "mode_applied",
                    mode,
                    "seq=" + seq.ToString(CultureInfo.InvariantCulture),
                    _lastAppliedLatencyMs
                );
            }
        }

        private void OnDebugOverrideApplied(string presetLabel, Vector3 u0)
        {
            WriteValidationEvent(
                "u0_override_set",
                CurrentMode,
                "preset=" + presetLabel,
                -1f,
                true,
                u0,
                "DebugOverride"
            );
        }

        private void OnDebugOverrideCleared(Vector3 previousU0)
        {
            WriteValidationEvent(
                "u0_override_cleared",
                CurrentMode,
                "return_to_hmd",
                -1f,
                true,
                previousU0,
                "HMD"
            );
        }

        private void TryResolveReferences()
        {
            if (udpGazeSender == null)
            {
                udpGazeSender = FindObjectOfType<UdpGazeSender>();
            }
            if (sharedMemoryReceiver == null)
            {
                sharedMemoryReceiver = FindObjectOfType<SharedMemoryReceiver>();
            }
            if (headPoseTracker == null)
            {
                headPoseTracker = FindObjectOfType<HeadPoseTracker>();
            }
            if (stereoSphereRenderer == null)
            {
                stereoSphereRenderer = FindObjectOfType<StereoSphereRenderer>();
            }
            if (rtspBaselineReceiver == null)
            {
                rtspBaselineReceiver = FindObjectOfType<RtspBaselineReceiver>();
            }
            if (baselinePanoramaRenderer == null)
            {
                baselinePanoramaRenderer = FindObjectOfType<BaselinePanoramaRenderer>();
            }
            if (headPoseTracker == null && udpGazeSender != null)
            {
                headPoseTracker = udpGazeSender.PoseTracker;
            }
        }

        private void EnsureMode4Components()
        {
            if (rtspBaselineReceiver != null && baselinePanoramaRenderer != null)
            {
                return;
            }

            GameObject hostObject = null;
            if (stereoSphereRenderer != null)
            {
                hostObject = stereoSphereRenderer.gameObject;
            }
            else if (baselinePanoramaRenderer != null)
            {
                hostObject = baselinePanoramaRenderer.gameObject;
            }
            else if (rtspBaselineReceiver != null)
            {
                hostObject = rtspBaselineReceiver.gameObject;
            }

            if (hostObject == null)
            {
                return;
            }

            if (rtspBaselineReceiver == null)
            {
                rtspBaselineReceiver = hostObject.GetComponent<RtspBaselineReceiver>();
                if (rtspBaselineReceiver == null)
                {
                    rtspBaselineReceiver = hostObject.AddComponent<RtspBaselineReceiver>();
                    rtspBaselineReceiver.enabled = false;
                    rtspBaselineReceiver.StopReceiver();
                    Debug.Log("[ExperimentController] auto-created RtspBaselineReceiver on " + hostObject.name);
                }
            }

            if (baselinePanoramaRenderer == null)
            {
                baselinePanoramaRenderer = hostObject.GetComponent<BaselinePanoramaRenderer>();
                if (baselinePanoramaRenderer == null)
                {
                    baselinePanoramaRenderer = hostObject.AddComponent<BaselinePanoramaRenderer>();
                    baselinePanoramaRenderer.enabled = false;
                    Debug.Log("[ExperimentController] auto-created BaselinePanoramaRenderer on " + hostObject.name);
                }
            }
        }

        private void DrawShmPreview()
        {
            if (_isMode4Active || !showShmPreview || sharedMemoryReceiver == null || sharedMemoryReceiver.StereoTexture == null)
            {
                return;
            }

            Texture texture = sharedMemoryReceiver.StereoTexture;
            float width = Mathf.Clamp(Screen.width * shmPreviewWidthRatio, 240f, Screen.width * 0.45f);
            float aspect = texture.width / Mathf.Max(1f, texture.height);
            float height = width / Mathf.Max(0.1f, aspect);
            float maxHeight = Screen.height * 0.32f;
            if (height > maxHeight)
            {
                height = maxHeight;
                width = height * aspect;
            }

            float x = Screen.width - width - 20f;
            float y = Screen.height - height - 20f;

            GUI.color = Color.black;
            GUI.Box(new Rect(x - 6f, y - 26f, width + 12f, height + 32f), GUIContent.none);
            GUI.color = Color.white;
            GUI.Label(new Rect(x + 4f, y - 22f, 240f, 20f), "SHM Preview");
            GUI.DrawTextureWithTexCoords(new Rect(x, y, width, height), texture, new Rect(0f, 1f, 1f, -1f), false);
        }

        private void UpdateOverlayFps()
        {
            float dt = Time.unscaledDeltaTime;
            if (dt > 0.00001f)
            {
                float instantUnityFps = 1f / dt;
                if (_unityFpsSmoothed <= 0f)
                {
                    _unityFpsSmoothed = instantUnityFps;
                }
                else
                {
                    _unityFpsSmoothed = Mathf.Lerp(_unityFpsSmoothed, instantUnityFps, 0.1f);
                }
            }

            if (_isMode4Active || sharedMemoryReceiver == null)
            {
                _shmReceiveFps = 0f;
                return;
            }

            if (_fpsWindowStartTime < 0f)
            {
                _fpsWindowStartTime = Time.unscaledTime;
                _fpsWindowStartAcceptedFrames = sharedMemoryReceiver.AcceptedFrames;
                return;
            }

            float elapsed = Time.unscaledTime - _fpsWindowStartTime;
            if (elapsed < fpsSampleWindowSeconds)
            {
                return;
            }

            long acceptedNow = sharedMemoryReceiver.AcceptedFrames;
            long acceptedDelta = acceptedNow - _fpsWindowStartAcceptedFrames;
            _shmReceiveFps = acceptedDelta / Mathf.Max(0.001f, elapsed);

            _fpsWindowStartTime = Time.unscaledTime;
            _fpsWindowStartAcceptedFrames = acceptedNow;
        }

        private static string FormatVector(Vector3 value)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "({0:F3},{1:F3},{2:F3})",
                value.x,
                value.y,
                value.z
            );
        }

        private void WriteValidationEvent(
            string eventType,
            int mode,
            string detail,
            float latencyMs = -1f,
            bool includeU0 = false,
            Vector3 u0 = default,
            string u0Source = null
        )
        {
            if (!writeValidationLog || string.IsNullOrEmpty(_validationLogPath))
            {
                return;
            }

            try
            {
                string escapedDetail = (detail ?? string.Empty).Replace("\"", "\\\"");
                string timestamp = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
                string line = "{\"ts\":\"" + timestamp
                    + "\",\"event\":\"" + eventType
                    + "\",\"mode\":" + mode.ToString(CultureInfo.InvariantCulture)
                    + ",\"requested\":" + _lastRequestedMode.ToString(CultureInfo.InvariantCulture)
                    + ",\"sent\":" + _lastSentMode.ToString(CultureInfo.InvariantCulture)
                    + ",\"applied\":" + _lastAppliedMode.ToString(CultureInfo.InvariantCulture)
                    + ",\"detail\":\"" + escapedDetail + "\"";
                if (latencyMs >= 0f)
                {
                    line += ",\"latency_ms\":" + latencyMs.ToString("F3", CultureInfo.InvariantCulture);
                }
                if (includeU0)
                {
                    line += ",\"u0_x\":" + u0.x.ToString("F6", CultureInfo.InvariantCulture);
                    line += ",\"u0_y\":" + u0.y.ToString("F6", CultureInfo.InvariantCulture);
                    line += ",\"u0_z\":" + u0.z.ToString("F6", CultureInfo.InvariantCulture);
                    if (!string.IsNullOrEmpty(u0Source))
                    {
                        line += ",\"u0_source\":\"" + u0Source + "\"";
                    }
                }
                line += "}";
                File.AppendAllText(_validationLogPath, line + Environment.NewLine);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[ExperimentController] log write failed: " + ex.Message);
            }
        }
    }
}







