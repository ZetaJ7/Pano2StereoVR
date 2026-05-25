using System;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace Pano2StereoVR
{
    public sealed class ExperimentController : MonoBehaviour
    {
        private enum RuntimeResolutionPreset
        {
            P1080 = 0,
            P2K = 1,
            P4K = 2
        }

        private readonly struct RuntimeResolutionSpec
        {
            public RuntimeResolutionSpec(
                RuntimeResolutionPreset preset,
                string label,
                string downsampleArgument,
                int width,
                int height)
            {
                Preset = preset;
                Label = label;
                DownsampleArgument = downsampleArgument;
                Width = width;
                Height = height;
            }

            public RuntimeResolutionPreset Preset { get; }
            public string Label { get; }
            public string DownsampleArgument { get; }
            public int Width { get; }
            public int Height { get; }
            public string DisplayText => Label + " " + Width.ToString(CultureInfo.InvariantCulture)
                + "x" + Height.ToString(CultureInfo.InvariantCulture);
            public int ShmWidth => Width * 2;
            public int ShmHeight => Height;
            public string ShmDisplayText => ShmWidth.ToString(CultureInfo.InvariantCulture)
                + "x" + ShmHeight.ToString(CultureInfo.InvariantCulture);
        }

        [SerializeField] private SharedMemoryReceiver sharedMemoryReceiver;
        [SerializeField] private UdpGazeSender udpGazeSender;
        [SerializeField] private HeadPoseTracker headPoseTracker;
        [SerializeField] private StereoSphereRenderer stereoSphereRenderer;
        [SerializeField] private RtspBaselineReceiver rtspBaselineReceiver;
        [SerializeField] private BaselinePanoramaRenderer baselinePanoramaRenderer;
        [SerializeField] private MonoBehaviour xrSuperResolutionController;
        [SerializeField] private bool autoCreateXrSuperResolutionController = false;
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
        [SerializeField] private RuntimeResolutionPreset resolutionPreset = RuntimeResolutionPreset.P1080;
        [SerializeField] private KeyCode resolutionPreviousKey = KeyCode.F5;
        [SerializeField] private KeyCode resolutionNextKey = KeyCode.F6;
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
        private const string XrSuperResolutionControllerTypeName =
            "Pano2StereoVR.XrSuperResolutionController, Assembly-CSharp";
        private static readonly int[] ModeButtonOrder = { Mode4Baseline, 1, 2, 3 };
        private static readonly RuntimeResolutionSpec[] ResolutionSpecs =
        {
            new RuntimeResolutionSpec(RuntimeResolutionPreset.P1080, "1080", "1080", 2184, 1092),
            new RuntimeResolutionSpec(RuntimeResolutionPreset.P2K, "2K", "2k", 2884, 1442),
            new RuntimeResolutionSpec(RuntimeResolutionPreset.P4K, "4K", "4k", 5768, 2884)
        };

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
            resolutionPreset = NormalizeResolutionPreset(resolutionPreset);
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

            if (Input.GetKeyDown(resolutionPreviousKey))
            {
                CycleResolutionPreset(-1, "hotkey_previous");
            }
            if (Input.GetKeyDown(resolutionNextKey))
            {
                CycleResolutionPreset(1, "hotkey_next");
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
                padding = new RectOffset(0, 0, 0, 0),
                fontSize = 14
            };
            GUIStyle titleStyle = new GUIStyle(compactLabelStyle)
            {
                fontStyle = FontStyle.Bold,
                margin = new RectOffset(0, 0, 0, 2),
                fontSize = 15
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
                padding = new RectOffset(8, 8, 2, 2),
                fontSize = 13
            };
            GUIStyle compactTextFieldStyle = new GUIStyle(GUI.skin.textField)
            {
                margin = new RectOffset(0, 0, 0, 0),
                padding = new RectOffset(5, 5, 3, 3),
                fontSize = 13
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
            float columnGap = 8f;
            float column1Width = headPoseTracker != null && headPoseTracker.IsDebugOverrideActive ? 380f : 344f;
            float column2Width = _isMode4Active ? 234f : 228f;
            float column3Width = (hasRtspPrompt || hasRtspError) ? 470f : (_isMode4Active ? 420f : 340f);
            float desiredInnerWidth = column1Width + column2Width + column3Width + columnGap * 2f;
            float maxInnerWidth = Screen.width - 32f;
            float widthScale = desiredInnerWidth > maxInnerWidth ? maxInnerWidth / desiredInnerWidth : 1f;
            column1Width *= widthScale;
            column2Width *= widthScale;
            column3Width *= widthScale;
            float innerWidth = desiredInnerWidth * widthScale;
            float boxWidth = innerWidth + 20f;
            int displayedMode = ResolveDisplayedMode();
            string displayedModeLabel = GetModeOverlayLabel(displayedMode);



            GUI.color = Color.black;
            GUI.Box(new Rect(12f, 12f, boxWidth, boxHeight), GUIContent.none);
            GUI.color = Color.white;

            GUILayout.BeginArea(new Rect(22f, 19f, innerWidth, boxHeight - 10f));
            GUILayout.BeginHorizontal();

            GUILayout.BeginVertical(GUILayout.Width(column1Width));
            GUILayout.Label(displayedModeLabel, modeTitleStyle);
            DrawModeButtonGroup(displayedMode, compactButtonStyle, compactLabelStyle);
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

            if (xrSuperResolutionController != null)
            {
                string statusMessage = GetOptionalStringProperty(
                    xrSuperResolutionController,
                    "StatusMessage"
                );
                if (!string.IsNullOrEmpty(statusMessage))
                {
                    GUILayout.Label(statusMessage, wrapLabelStyle);
                }
                string hotkeyHint = GetOptionalStringProperty(
                    xrSuperResolutionController,
                    "HotkeyHint"
                );
                if (!string.IsNullOrEmpty(hotkeyHint))
                {
                    GUILayout.Label(hotkeyHint, compactLabelStyle);
                }
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
            DrawResolutionControls(compactLabelStyle, compactButtonStyle, warningLabelStyle);
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

        private void DrawResolutionControls(
            GUIStyle compactLabelStyle,
            GUIStyle compactButtonStyle,
            GUIStyle warningLabelStyle)
        {
            RuntimeResolutionSpec spec = GetResolutionSpec(resolutionPreset);
            GUILayout.Label("Preset: " + spec.DisplayText, compactLabelStyle);
            GUILayout.BeginHorizontal();
            DrawResolutionButton(RuntimeResolutionPreset.P1080, "1080", compactButtonStyle);
            DrawResolutionButton(RuntimeResolutionPreset.P2K, "2K", compactButtonStyle);
            DrawResolutionButton(RuntimeResolutionPreset.P4K, "4K", compactButtonStyle);
            GUILayout.EndHorizontal();
            GUILayout.Label("Keys: " + resolutionPreviousKey + "/" + resolutionNextKey, compactLabelStyle);

            if (_isMode4Active)
            {
                GUILayout.Label(
                    "RTSP out: " + (rtspBaselineReceiver != null
                        ? FormatResolution(rtspBaselineReceiver.OutputWidth, rtspBaselineReceiver.OutputHeight)
                        : "unbound"),
                    compactLabelStyle
                );
            }
            else
            {
                GUILayout.Label(
                    "SHM in: " + (sharedMemoryReceiver != null && sharedMemoryReceiver.Width > 0
                        ? FormatResolution(sharedMemoryReceiver.Width, sharedMemoryReceiver.Height)
                        : "waiting"),
                    compactLabelStyle
                );
                GUILayout.Label("SHM expected: " + spec.ShmDisplayText, compactLabelStyle);
            }

            string warning = GetResolutionWarningMessage(spec);
            if (!string.IsNullOrEmpty(warning))
            {
                GUILayout.Label(warning, warningLabelStyle);
            }
        }

        private void DrawResolutionButton(
            RuntimeResolutionPreset preset,
            string label,
            GUIStyle compactButtonStyle)
        {
            bool selected = resolutionPreset == preset;
            string text = selected ? "[" + label + "]" : label;
            if (GUILayout.Button(
                    text,
                    compactButtonStyle,
                    GUILayout.Width(52f),
                    GUILayout.Height(20f),
                    GUILayout.ExpandWidth(false)))
            {
                SetResolutionPreset(preset, "overlay");
            }
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

        private void CycleResolutionPreset(int delta, string reason)
        {
            int currentIndex = GetResolutionSpecIndex(resolutionPreset);
            int nextIndex = (currentIndex + delta + ResolutionSpecs.Length) % ResolutionSpecs.Length;
            SetResolutionPreset(ResolutionSpecs[nextIndex].Preset, reason);
        }

        private void SetResolutionPreset(RuntimeResolutionPreset preset, string reason)
        {
            RuntimeResolutionPreset nextPreset = NormalizeResolutionPreset(preset);
            RuntimeResolutionPreset previousPreset = resolutionPreset;
            resolutionPreset = nextPreset;

            RuntimeResolutionSpec spec = GetResolutionSpec(resolutionPreset);
            bool changed = previousPreset != resolutionPreset;
            if (_isMode4Active)
            {
                ApplySelectedResolutionToMode4(reason, true);
            }
            else
            {
                WriteValidationEvent(
                    "resolution_selected",
                    CurrentMode,
                    BuildResolutionDetail(spec, reason, changed)
                );
            }

            Debug.Log(
                "[ExperimentController] resolution preset -> "
                + spec.DisplayText
                + " (--downsample " + spec.DownsampleArgument + ")"
            );
        }

        private void ApplySelectedResolutionToMode4(string reason, bool restartIfRunning)
        {
            if (rtspBaselineReceiver == null)
            {
                WriteValidationEvent("resolution_rejected", Mode4Baseline, "rtsp_receiver_missing");
                return;
            }

            RuntimeResolutionSpec spec = GetResolutionSpec(resolutionPreset);
            bool changed = rtspBaselineReceiver.OutputWidth != spec.Width
                || rtspBaselineReceiver.OutputHeight != spec.Height;
            bool applied = rtspBaselineReceiver.ApplyOutputResolution(
                spec.Width,
                spec.Height,
                restartIfRunning
            );
            WriteValidationEvent(
                applied ? "resolution_applied" : "resolution_rejected",
                Mode4Baseline,
                BuildResolutionDetail(spec, reason, changed)
                + ",restart=" + FormatYesNo(restartIfRunning)
            );
        }

        private string GetResolutionWarningMessage(RuntimeResolutionSpec spec)
        {
            if (_isMode4Active)
            {
                return string.Empty;
            }

            if (sharedMemoryReceiver == null
                || sharedMemoryReceiver.Width <= 0
                || sharedMemoryReceiver.Height <= 0)
            {
                return "Start Python with --downsample " + spec.DownsampleArgument + ".";
            }

            if (DoesShmResolutionMatchPreset(
                    spec.Preset,
                    sharedMemoryReceiver.Width,
                    sharedMemoryReceiver.Height))
            {
                return string.Empty;
            }

            return "SHM mismatch. Restart Python with --downsample " + spec.DownsampleArgument + ".";
        }

        private static bool DoesShmResolutionMatchPreset(
            RuntimeResolutionPreset preset,
            int shmWidth,
            int shmHeight)
        {
            RuntimeResolutionSpec spec = GetResolutionSpec(preset);
            return shmWidth == spec.ShmWidth && shmHeight == spec.ShmHeight;
        }

        private static RuntimeResolutionPreset NormalizeResolutionPreset(RuntimeResolutionPreset preset)
        {
            for (int i = 0; i < ResolutionSpecs.Length; i++)
            {
                if (ResolutionSpecs[i].Preset == preset)
                {
                    return preset;
                }
            }
            return RuntimeResolutionPreset.P1080;
        }

        private static RuntimeResolutionSpec GetResolutionSpec(RuntimeResolutionPreset preset)
        {
            RuntimeResolutionPreset normalized = NormalizeResolutionPreset(preset);
            for (int i = 0; i < ResolutionSpecs.Length; i++)
            {
                if (ResolutionSpecs[i].Preset == normalized)
                {
                    return ResolutionSpecs[i];
                }
            }
            return ResolutionSpecs[0];
        }

        private static int GetResolutionSpecIndex(RuntimeResolutionPreset preset)
        {
            RuntimeResolutionPreset normalized = NormalizeResolutionPreset(preset);
            for (int i = 0; i < ResolutionSpecs.Length; i++)
            {
                if (ResolutionSpecs[i].Preset == normalized)
                {
                    return i;
                }
            }
            return 0;
        }

        private static string BuildResolutionDetail(
            RuntimeResolutionSpec spec,
            string reason,
            bool changed)
        {
            return "preset=" + spec.Label
                + ",size=" + FormatResolution(spec.Width, spec.Height)
                + ",downsample=" + spec.DownsampleArgument
                + ",reason=" + reason
                + ",changed=" + FormatYesNo(changed);
        }

        private static string FormatResolution(int width, int height)
        {
            return width.ToString(CultureInfo.InvariantCulture)
                + "x"
                + height.ToString(CultureInfo.InvariantCulture);
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
                    return "Mono";
                case 2:
                    return "Pose-agnostic";
                case 3:
                    return "Pose-aware";
                case 4:
                    return "Baseline";
                default:
                    return "Unknown";
            }
        }

        private static int[] GetModeButtonOrder()
        {
            return (int[])ModeButtonOrder.Clone();
        }

        private void DrawModeButtonGroup(
            int displayedMode,
            GUIStyle compactButtonStyle,
            GUIStyle compactLabelStyle)
        {
            GUILayout.BeginHorizontal();
            for (int index = 0; index < ModeButtonOrder.Length; index++)
            {
                int mode = ModeButtonOrder[index];
                DrawModeButton(mode, displayedMode, compactButtonStyle);
            }
            GUILayout.EndHorizontal();
            GUILayout.Label("Keys: 4/1/2/3", compactLabelStyle);
        }

        private void DrawModeButton(
            int mode,
            int displayedMode,
            GUIStyle compactButtonStyle)
        {
            string label = GetModeOverlayLabel(mode);
            string text = displayedMode == mode ? "[" + label + "]" : label;
            if (GUILayout.Button(
                    text,
                    compactButtonStyle,
                    GUILayout.Width(GetModeButtonWidth(mode)),
                    GUILayout.Height(20f),
                    GUILayout.ExpandWidth(false)))
            {
                RequestModeSwitch(mode);
            }
        }

        private static float GetModeButtonWidth(int mode)
        {
            switch (mode)
            {
                case 2:
                    return 112f;
                case 3:
                    return 92f;
                case 4:
                    return 76f;
                default:
                    return 54f;
            }
        }

        private float CalculateCompactOverlayHeight()
        {
            int column1Lines = 5;
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

            int column2Lines = _isMode4Active ? 4 : 6;
            if (xrSuperResolutionController != null)
            {
                if (!string.IsNullOrEmpty(GetOptionalStringProperty(
                        xrSuperResolutionController,
                        "StatusMessage")))
                {
                    column2Lines += 1;
                }
                if (!string.IsNullOrEmpty(GetOptionalStringProperty(
                        xrSuperResolutionController,
                        "HotkeyHint")))
                {
                    column2Lines += 1;
                }
            }
            int column3Lines = 6;
            if (_isMode4Active)
            {
                column3Lines += 3;
            }
            else if (!string.IsNullOrEmpty(GetResolutionWarningMessage(GetResolutionSpec(resolutionPreset))))
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
            float lineHeight = 18f;
            float chromeHeight = 24f;
            return Mathf.Min(chromeHeight + maxLines * lineHeight, Screen.height * 0.42f);
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
            WriteValidationEvent("mode_applied", Mode4Baseline, "baseline", 0f);
            Debug.Log("[ExperimentController] mode applied -> 4 (Baseline)");
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

            if (active)
            {
                ApplySelectedResolutionToMode4(reason, true);
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
            if (xrSuperResolutionController == null)
            {
                TryResolveOptionalXrSuperResolutionController();
            }
            if (headPoseTracker == null && udpGazeSender != null)
            {
                headPoseTracker = udpGazeSender.PoseTracker;
            }
        }

        private void TryResolveOptionalXrSuperResolutionController()
        {
            Type controllerType = Type.GetType(XrSuperResolutionControllerTypeName, false);
            if (controllerType == null || !typeof(MonoBehaviour).IsAssignableFrom(controllerType))
            {
                return;
            }

            xrSuperResolutionController = FindObjectOfType(controllerType) as MonoBehaviour;
            if (xrSuperResolutionController == null && Application.isPlaying && autoCreateXrSuperResolutionController)
            {
                xrSuperResolutionController = gameObject.AddComponent(controllerType) as MonoBehaviour;
                if (xrSuperResolutionController != null)
                {
                    Debug.Log(
                        "[ExperimentController] auto-created XrSuperResolutionController on "
                        + gameObject.name
                    );
                }
            }
        }

        private static string GetOptionalStringProperty(MonoBehaviour component, string propertyName)
        {
            if (component == null || string.IsNullOrEmpty(propertyName))
            {
                return string.Empty;
            }

            var property = component.GetType().GetProperty(propertyName);
            if (property == null || property.PropertyType != typeof(string))
            {
                return string.Empty;
            }

            return property.GetValue(component) as string ?? string.Empty;
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
                string escapedCondition = GetModeOverlayLabel(mode).Replace("\"", "\\\"");
                string line = "{\"ts\":\"" + timestamp
                    + "\",\"event\":\"" + eventType
                    + "\",\"mode\":" + mode.ToString(CultureInfo.InvariantCulture)
                    + ",\"condition\":\"" + escapedCondition + "\""
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







