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
        [SerializeField] private KeyCode mode1Key = KeyCode.Alpha1;
        [SerializeField] private KeyCode mode2Key = KeyCode.Alpha2;
        [SerializeField] private KeyCode mode3Key = KeyCode.Alpha3;
        [SerializeField] private bool showOverlay = true;
        [SerializeField] [Min(0.1f)] private float applyTimeoutSeconds = 1.0f;
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
            if (udpGazeSender != null)
            {
                udpGazeSender.ModeMessageSent += OnModeSent;
            }
            if (sharedMemoryReceiver != null)
            {
                sharedMemoryReceiver.ModeApplied += OnModeApplied;
                _lastAppliedMode = sharedMemoryReceiver.CurrentMode;
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
        }

        private void Update()
        {
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
            GUI.Box(new Rect(16, 16, 630, 250), GUIContent.none);
            GUI.color = Color.white;

            GUILayout.BeginArea(new Rect(26, 24, 610, 238));
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
                "Switch count: requested=" + RequestedSwitchCount
                + " applied=" + AppliedSwitchCount + " timeout=" + TimeoutCount
            );
            if (!string.IsNullOrEmpty(_validationLogPath))
            {
                GUILayout.Label("Log: " + _validationLogPath);
            }
            GUILayout.EndArea();
        }

        private void RequestModeSwitch(int mode)
        {
            int clampedMode = Mathf.Clamp(mode, 1, 3);
            _lastRequestedMode = clampedMode;
            _requestTime = Time.unscaledTime;
            _hasPendingRequest = true;
            _requestTimedOut = false;
            RequestedSwitchCount += 1;

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
            if (headPoseTracker == null && udpGazeSender != null)
            {
                headPoseTracker = udpGazeSender.PoseTracker;
            }
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
            float latencyMs = -1f
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
