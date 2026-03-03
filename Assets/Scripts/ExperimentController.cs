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
        [SerializeField] private KeyCode mode1Key = KeyCode.Alpha1;
        [SerializeField] private KeyCode mode2Key = KeyCode.Alpha2;
        [SerializeField] private KeyCode mode3Key = KeyCode.Alpha3;
        [SerializeField] private bool showOverlay = true;
        [SerializeField] private bool showShmPreview = true;
        [SerializeField] [Range(0.1f, 0.5f)] private float shmPreviewWidthRatio = 0.40f;
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
            _fpsWindowStartTime = -1f;
            _fpsWindowStartAcceptedFrames = 0;
            _shmReceiveFps = 0f;
            _unityFpsSmoothed = 0f;
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
            if (udpGazeSender == null)
            {
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

        private void OnGUI()
        {
            if (!showOverlay)
            {
                return;
            }

            string requestState = _hasPendingRequest
                ? (_requestTimedOut ? "Timeout" : "Pending")
                : "Applied";
            float requestAge = _requestTime < 0f ? 0f : Time.unscaledTime - _requestTime;
            float sentAge = _sentTime < 0f ? 0f : Time.unscaledTime - _sentTime;
            float appliedAge = _appliedTime < 0f ? 0f : Time.unscaledTime - _appliedTime;

            GUI.color = Color.black;
            GUI.Box(new Rect(16, 16, 730, 360), GUIContent.none);
            GUI.color = Color.white;

            GUILayout.BeginArea(new Rect(26, 24, 710, 344));
            GUILayout.Label("G3 Mode Verification");
            GUILayout.Label("Requested: " + _lastRequestedMode + " (" + requestState + ")");
            GUILayout.Label("Sent: " + _lastSentMode + " | age=" + sentAge.ToString("F2") + "s");
            GUILayout.Label(
                "Applied: " + _lastAppliedMode + " | age=" + appliedAge.ToString("F2")
                + "s | receiver=" + CurrentMode
            );
            GUILayout.Label("Request age: " + requestAge.ToString("F2") + "s");
            if (_lastAppliedLatencyMs >= 0f)
            {
                GUILayout.Label("Last apply latency: " + _lastAppliedLatencyMs.ToString("F1") + " ms");
            }
            if (headPoseTracker != null)
            {
                GUILayout.Label(
                    "u0: " + FormatVector(headPoseTracker.ServerForwardUnit)
                    + " | source="
                    + (headPoseTracker.IsDebugOverrideActive ? "DebugOverride" : "HMD")
                );
                if (headPoseTracker.IsDebugOverrideActive)
                {
                    GUILayout.Label("Debug override vector: " + FormatVector(headPoseTracker.DebugOverrideVector));
                }
            }
            if (udpGazeSender != null)
            {
                GUILayout.Label(
                    "UDP: connected=" + (udpGazeSender.IsConnected ? "yes" : "no")
                    + " mode_packets=" + udpGazeSender.ModePacketsSent
                    + " gaze_packets=" + udpGazeSender.GazePacketsSent
                    + " combined_packets=" + udpGazeSender.CombinedPacketsSent
                    + " send_errors=" + udpGazeSender.PacketSendErrors
                );
            }
            if (sharedMemoryReceiver != null)
            {
                GUILayout.Label(
                    "SHM: accepted=" + sharedMemoryReceiver.AcceptedFrames
                    + " mode_changes=" + sharedMemoryReceiver.ModeChangesApplied
                    + " writer_busy=" + sharedMemoryReceiver.WriterBusySkips
                    + " torn_reject=" + sharedMemoryReceiver.TornRejected
                );
            }
            GUILayout.Label(
                "Perf: unity_fps=" + _unityFpsSmoothed.ToString("F1")
                + " shm_recv_fps=" + _shmReceiveFps.ToString("F1")
                + " shm_seq_fps="
                + (sharedMemoryReceiver != null ? sharedMemoryReceiver.ObservedWriterFps.ToString("F1") : "0.0")
            );
            if (stereoSphereRenderer != null)
            {
                GUILayout.Label(
                    "Render: target=" + (stereoSphereRenderer.HasTargetRenderer ? "yes" : "no")
                    + " enabled=" + (stereoSphereRenderer.RendererEnabled ? "yes" : "no")
                    + " visible=" + (stereoSphereRenderer.RendererVisible ? "yes" : "no")
                    + " tex_bound=" + (stereoSphereRenderer.HasBoundTexture ? "yes" : "no")
                );
                if (stereoSphereRenderer.HasBoundTexture)
                {
                    GUILayout.Label(
                        "Texture: " + stereoSphereRenderer.BoundTextureWidth + "x"
                        + stereoSphereRenderer.BoundTextureHeight
                        + " cam_dist=" + stereoSphereRenderer.CameraDistance.ToString("F2")
                    );
                }
                GUILayout.Label(
                    "Orientation: flip_x=" + (stereoSphereRenderer.FlipX ? "on" : "off")
                    + " flip_y=" + (stereoSphereRenderer.FlipY ? "on" : "off")
                    + " swap_eyes=" + (stereoSphereRenderer.SwapEyes ? "on" : "off")
                    + " (F7/F8)"
                );
            }
            GUILayout.Label(
                "Switch count: requested=" + RequestedSwitchCount
                + " applied=" + AppliedSwitchCount + " timeout=" + TimeoutCount
            );
            if (!string.IsNullOrEmpty(_validationLogPath))
            {
                GUILayout.Label("Log: " + _validationLogPath);
            }
            GUILayout.EndArea();

            DrawShmPreview();
        }

        private void RequestModeSwitch(int mode)
        {
            int clampedMode = Mathf.Clamp(mode, 1, 3);
            bool alreadyApplied = sharedMemoryReceiver != null
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
                Debug.Log("[ExperimentController] requested mode already applied -> " + clampedMode);
                WriteValidationEvent("mode_requested", clampedMode, "keyboard_already_applied");
                return;
            }

            _hasPendingRequest = true;
            udpGazeSender.SetMode(clampedMode);
            Debug.Log("[ExperimentController] requested mode -> " + clampedMode);
            WriteValidationEvent("mode_requested", clampedMode, "keyboard");
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
            if (headPoseTracker == null && udpGazeSender != null)
            {
                headPoseTracker = udpGazeSender.PoseTracker;
            }
        }

        private void DrawShmPreview()
        {
            if (!showShmPreview || sharedMemoryReceiver == null || sharedMemoryReceiver.StereoTexture == null)
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

            if (sharedMemoryReceiver == null)
            {
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
                string escapedDetail = detail.Replace("\"", "\\\"");
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
