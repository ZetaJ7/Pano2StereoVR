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
        [SerializeField] [Range(0.05f, 0.5f)] private float shmPreviewWidthRatio = 0.20f;
        [SerializeField] [Min(0.1f)] private float applyTimeoutSeconds = 1.0f;
        [SerializeField] [Range(0.25f, 2.0f)] private float fpsSampleWindowSeconds = 1.0f;
        [SerializeField] private bool writeValidationLog = true;
        [SerializeField] private string validationLogFileName = "g3_mode_validation.jsonl";

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

        private const int ModeMin = 1;
        private const int ModeMax = 3;
        private const int ModeBaseline = 1;
        private const string XrSuperResolutionControllerTypeName =
            "Pano2StereoVR.XrSuperResolutionController, Assembly-CSharp";
        private static readonly int[] ModeButtonOrder = { ModeBaseline, 2, 3 };
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

            EnsureSharedMemoryPipelineActive();
            StopLegacyBaselineComponents();
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
            StopLegacyBaselineComponents();
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

            if (Input.GetKeyDown(resolutionPreviousKey))
            {
                CycleResolutionPreset(-1, "hotkey_previous");
            }
            if (Input.GetKeyDown(resolutionNextKey))
            {
                CycleResolutionPreset(1, "hotkey_next");
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

            GUIStyle compactLabelStyle = new GUIStyle(GUI.skin.label)
            {
                margin = new RectOffset(0, 0, 0, 1),
                padding = new RectOffset(0, 0, 0, 0),
                fontSize = 13
            };
            GUIStyle titleStyle = new GUIStyle(compactLabelStyle)
            {
                fontStyle = FontStyle.Bold,
                margin = new RectOffset(0, 0, 0, 2),
                fontSize = 14
            };
            GUIStyle modeTitleStyle = new GUIStyle(titleStyle);
            modeTitleStyle.normal.textColor = new Color(0.45f, 1.0f, 0.45f);
            GUIStyle compactButtonStyle = new GUIStyle(GUI.skin.button)
            {
                margin = new RectOffset(0, 0, 0, 0),
                padding = new RectOffset(8, 8, 2, 2),
                fontSize = 13
            };
            GUIStyle performanceLabelStyle = new GUIStyle(compactLabelStyle)
            {
                fontSize = 11,
                alignment = TextAnchor.MiddleLeft
            };

            int currentMode = ResolveCurrentOverlayMode();
            int selectedMode = ResolveDisplayedMode();

            DrawTopModeOverlay(currentMode, selectedMode, modeTitleStyle, compactButtonStyle, compactLabelStyle);
            Rect previewRect = CalculateCurrentShmPreviewRect();
            DrawShmPreview(previewRect);
            DrawBottomPerformanceOverlay(performanceLabelStyle, previewRect);
        }

        private void DrawTopModeOverlay(
            int currentMode,
            int selectedMode,
            GUIStyle modeTitleStyle,
            GUIStyle compactButtonStyle,
            GUIStyle compactLabelStyle)
        {
            float boxHeight = 44f;
            float boxWidth = Mathf.Min(Screen.width - 24f, 760f);
            Rect boxRect = new Rect(12f, 12f, boxWidth, boxHeight);

            Color previousColor = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.72f);
            GUI.Box(boxRect, GUIContent.none);
            GUI.color = previousColor;

            GUILayout.BeginArea(new Rect(boxRect.x + 10f, boxRect.y + 9f, boxRect.width - 20f, boxRect.height - 12f));
            GUILayout.BeginHorizontal();
            GUILayout.Label(
                "Mode: " + GetModeOverlayLabel(currentMode),
                modeTitleStyle,
                GUILayout.Width(170f)
            );
            GUILayout.Label("Select:", compactLabelStyle, GUILayout.Width(48f));
            DrawModeButtonGroup(selectedMode, compactButtonStyle);
            GUILayout.FlexibleSpace();
            GUILayout.Label(
                "IPD: " + (_currentIpd * 1000f).ToString("F1", CultureInfo.InvariantCulture) + "mm",
                compactLabelStyle,
                GUILayout.Width(96f)
            );
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        private void DrawBottomPerformanceOverlay(GUIStyle performanceLabelStyle, Rect previewRect)
        {
            Rect rect = CalculatePerformanceOverlayRect(Screen.width, Screen.height, previewRect);
            if (rect.width <= 0f)
            {
                return;
            }

            Color previousColor = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.62f);
            GUI.Box(rect, GUIContent.none);
            GUI.color = previousColor;
            GUI.Label(
                new Rect(rect.x + 8f, rect.y + 1f, rect.width - 16f, rect.height - 2f),
                GetPerformanceOverlayText(),
                performanceLabelStyle
            );
        }

        private string GetPerformanceOverlayText()
        {
            string writerFps = sharedMemoryReceiver != null
                ? sharedMemoryReceiver.ObservedWriterFps.ToString("F1", CultureInfo.InvariantCulture)
                : "0.0";
            string textureText = stereoSphereRenderer != null && stereoSphereRenderer.HasBoundTexture
                ? stereoSphereRenderer.BoundTextureWidth.ToString(CultureInfo.InvariantCulture)
                    + "x" + stereoSphereRenderer.BoundTextureHeight.ToString(CultureInfo.InvariantCulture)
                : "unbound";

            return "Performance: Unity " + _unityFpsSmoothed.ToString("F1", CultureInfo.InvariantCulture)
                + " fps | SHM " + _shmReceiveFps.ToString("F1", CultureInfo.InvariantCulture)
                + " fps | Writer " + writerFps + " fps | Texture " + textureText;
        }

        private static Rect CalculatePerformanceOverlayRect(
            float screenWidth,
            float screenHeight,
            Rect previewRect)
        {
            float left = 12f;
            float gap = 12f;
            float right = previewRect.width > 0f
                ? previewRect.x - gap
                : screenWidth - 12f;
            float width = Mathf.Min(760f, right - left);
            return new Rect(left, screenHeight - 28f, Mathf.Max(0f, width), 20f);
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

            GUILayout.Label(
                "SHM in: " + (sharedMemoryReceiver != null && sharedMemoryReceiver.Width > 0
                    ? FormatResolution(sharedMemoryReceiver.Width, sharedMemoryReceiver.Height)
                    : "waiting"),
                compactLabelStyle
            );
            GUILayout.Label("SHM expected: " + spec.ShmDisplayText, compactLabelStyle);

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

        private string GetReceiverPromptMessage()
        {
            if (sharedMemoryReceiver == null)
            {
                return "Receiver requires a SharedMemoryReceiver.";
            }

            if (!sharedMemoryReceiver.IsOpened)
            {
                return "Waiting for shared memory from pano2stereo.";
            }

            if (sharedMemoryReceiver.StereoTexture == null || sharedMemoryReceiver.AcceptedFrames <= 0)
            {
                return "Waiting for SHM frames.";
            }

            if (_hasPendingRequest && sharedMemoryReceiver.CurrentMode != _lastRequestedMode)
            {
                return "Mode requested; waiting for pano2stereo to publish mode "
                    + _lastRequestedMode.ToString(CultureInfo.InvariantCulture) + ".";
            }

            return string.Empty;
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
            WriteValidationEvent(
                "resolution_selected",
                CurrentMode,
                BuildResolutionDetail(spec, reason, changed)
            );

            Debug.Log(
                "[ExperimentController] resolution preset -> "
                + spec.DisplayText
                + " (--downsample " + spec.DownsampleArgument + ")"
            );
        }

        private string GetResolutionWarningMessage(RuntimeResolutionSpec spec)
        {
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
            if (_hasPendingRequest && _lastRequestedMode >= ModeMin && _lastRequestedMode <= ModeMax)
            {
                return _lastRequestedMode;
            }

            if (_lastAppliedMode >= ModeMin && _lastAppliedMode <= ModeMax)
            {
                return _lastAppliedMode;
            }

            return Mathf.Clamp(CurrentMode, ModeMin, ModeMax);
        }

        private int ResolveCurrentOverlayMode()
        {
            if (_lastAppliedMode >= ModeMin && _lastAppliedMode <= ModeMax)
            {
                return _lastAppliedMode;
            }

            return Mathf.Clamp(CurrentMode, ModeMin, ModeMax);
        }

        private static string GetModeOverlayLabel(int mode)
        {
            switch (mode)
            {
                case 1:
                    return "Baseline";
                case 2:
                    return "Pose-agnostic";
                case 3:
                    return "Pose-aware";
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
            GUIStyle compactButtonStyle)
        {
            GUILayout.BeginHorizontal();
            for (int index = 0; index < ModeButtonOrder.Length; index++)
            {
                int mode = ModeButtonOrder[index];
                DrawModeButton(mode, displayedMode, compactButtonStyle);
            }
            GUILayout.EndHorizontal();
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
                default:
                    return 76f;
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

            int column2Lines = 6;
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
            if (!string.IsNullOrEmpty(GetResolutionWarningMessage(GetResolutionSpec(resolutionPreset))))
            {
                column3Lines += 2;
            }
            if (!string.IsNullOrEmpty(GetReceiverPromptMessage()))
            {
                column3Lines += 3;
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
            if (udpGazeSender == null)
            {
                Debug.LogWarning("[ExperimentController] cannot switch mode without UDP sender.");
                WriteValidationEvent("mode_request_failed", clampedMode, "udp_missing");
                return;
            }

            EnsureSharedMemoryPipelineActive();
            StopLegacyBaselineComponents();

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

        private void SendCurrentIpd(string reason)
        {
            if (udpGazeSender == null)
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

        private void EnsureSharedMemoryPipelineActive()
        {
            if (sharedMemoryReceiver != null)
            {
                sharedMemoryReceiver.enabled = true;
            }
            if (stereoSphereRenderer != null)
            {
                stereoSphereRenderer.enabled = true;
            }
            if (udpGazeSender != null)
            {
                udpGazeSender.enabled = true;
            }
        }

        private void StopLegacyBaselineComponents()
        {
            if (rtspBaselineReceiver != null)
            {
                rtspBaselineReceiver.SetStreamingActive(false);
            }
            if (baselinePanoramaRenderer != null)
            {
                baselinePanoramaRenderer.enabled = false;
            }
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

        private Rect CalculateCurrentShmPreviewRect()
        {
            if (!showShmPreview || sharedMemoryReceiver == null || sharedMemoryReceiver.StereoTexture == null)
            {
                return Rect.zero;
            }

            Texture texture = sharedMemoryReceiver.StereoTexture;
            return CalculateShmPreviewRect(
                Screen.width,
                Screen.height,
                texture.width,
                texture.height,
                shmPreviewWidthRatio
            );
        }

        private void DrawShmPreview(Rect previewRect)
        {
            if (previewRect.width <= 0f || previewRect.height <= 0f || sharedMemoryReceiver == null)
            {
                return;
            }

            Texture texture = sharedMemoryReceiver.StereoTexture;
            if (texture == null)
            {
                return;
            }

            Color previousColor = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.62f);
            GUI.Box(new Rect(previewRect.x - 5f, previewRect.y - 21f, previewRect.width + 10f, previewRect.height + 26f), GUIContent.none);
            GUI.color = previousColor;
            GUI.Label(new Rect(previewRect.x + 3f, previewRect.y - 19f, 120f, 18f), "SHM Preview");
            GUI.DrawTextureWithTexCoords(previewRect, texture, new Rect(0f, 1f, 1f, -1f), false);
        }

        private static Rect CalculateShmPreviewRect(
            float screenWidth,
            float screenHeight,
            float textureWidth,
            float textureHeight,
            float widthRatio)
        {
            float width = Mathf.Clamp(screenWidth * Mathf.Max(0.01f, widthRatio), 120f, screenWidth * 0.45f);
            float aspect = textureWidth / Mathf.Max(1f, textureHeight);
            float height = width / Mathf.Max(0.1f, aspect);
            float maxHeight = screenHeight * 0.32f;
            if (height > maxHeight)
            {
                height = maxHeight;
                width = height * aspect;
            }

            float x = screenWidth - width - 20f;
            float y = screenHeight - height - 20f;
            return new Rect(Mathf.Max(0f, x), Mathf.Max(0f, y), width, height);
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

            if (sharedMemoryReceiver == null)
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







