using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

namespace Pano2StereoVR
{
    public sealed class HeadPoseTracker : MonoBehaviour
    {
        private enum PoseSource
        {
            None,
            XRNode,
            MouseLook,
            CameraTransform,
            DebugOverride,
        }

        [SerializeField] private Transform headTransform;
        [SerializeField] private bool useMainCameraWhenUnset = true;
        [SerializeField] private bool preferXrNodePose = true;
        [SerializeField] private bool logPoseSourceChanges = true;
        [SerializeField] private bool enableMouseLook = true;
        [SerializeField] private KeyCode toggleMouseLookKey = KeyCode.M;
        [SerializeField] private bool requireRightMouseButton = true;
        [SerializeField] private bool lockCursorWhileMouseLooking = true;
        [SerializeField] private bool hideCursorWhileMouseLooking = true;
        [SerializeField] [Min(0.01f)] private float mouseYawSensitivity = 3.0f;
        [SerializeField] [Min(0.01f)] private float mousePitchSensitivity = 2.5f;
        [SerializeField] private bool invertMouseY = false;
        [SerializeField] [Range(10f, 89f)] private float mousePitchLimit = 85f;
        [SerializeField] private bool enableDebugOverrideHotkeys = false;
        [SerializeField] private KeyCode centerVectorKey = KeyCode.F1;
        [SerializeField] private KeyCode leftVectorKey = KeyCode.F2;
        [SerializeField] private KeyCode topVectorKey = KeyCode.F3;
        [SerializeField] private KeyCode clearOverrideKey = KeyCode.F4;

        public Vector3 ServerForwardUnit { get; private set; } = new Vector3(1f, 0f, 0f);
        public bool IsDebugOverrideActive { get; private set; }
        public Vector3 DebugOverrideVector { get; private set; } = new Vector3(1f, 0f, 0f);
        public bool IsMouseLookEnabled => _mouseLookEnabled;
        public string CurrentPoseSourceLabel => GetPoseSourceLabel(_currentPoseSource);
        public event Action<string, Vector3> DebugOverrideApplied;
        public event Action<Vector3> DebugOverrideCleared;

        private static readonly List<XRNodeState> XrNodeStates = new List<XRNodeState>(8);
        private PoseSource _currentPoseSource = PoseSource.None;
        private bool _hasPoseSourceLog;
        private bool _mouseLookEnabled;
        private bool _mouseAnglesInitialized;
        private bool _mouseLookCaptureActive;
        private float _mouseYawDeg;
        private float _mousePitchDeg;

        private void Awake()
        {
            EnsureHeadTransform();
        }

        private void OnDisable()
        {
            SetMouseLookCapture(false);
        }

        private void Reset()
        {
            if (Camera.main != null)
            {
                headTransform = Camera.main.transform;
            }
        }

        private void Update()
        {
            HandleDebugHotkeys();
            HandleMouseLookToggleHotkey();

            if (IsDebugOverrideActive)
            {
                ServerForwardUnit = DebugOverrideVector;
                UpdatePoseSource(PoseSource.DebugOverride);
                return;
            }

            if (_mouseLookEnabled)
            {
                EnsureHeadTransform();
                if (headTransform == null)
                {
                    return;
                }

                ApplyMouseLookToTransform();
                if (TryMapForward(headTransform.forward, out Vector3 mappedMouseForward))
                {
                    ServerForwardUnit = mappedMouseForward;
                    UpdatePoseSource(PoseSource.MouseLook);
                }
                return;
            }

            Vector3 forward = Vector3.zero;
            bool usedXrNodePose = false;
            if (preferXrNodePose && TryGetHeadForwardFromXr(out forward))
            {
                usedXrNodePose = true;
            }
            else
            {
                EnsureHeadTransform();
                if (headTransform == null)
                {
                    return;
                }
                forward = headTransform.forward;
            }

            if (!TryMapForward(forward, out Vector3 mappedForward))
            {
                return;
            }

            ServerForwardUnit = mappedForward;
            UpdatePoseSource(usedXrNodePose ? PoseSource.XRNode : PoseSource.CameraTransform);
        }

        public void SetDebugOverride(Vector3 serverUnit)
        {
            SetDebugOverride(serverUnit, "custom");
        }

        public void SetDebugOverride(Vector3 serverUnit, string presetLabel)
        {
            if (serverUnit.sqrMagnitude < 1e-8f)
            {
                return;
            }

            DebugOverrideVector = serverUnit.normalized;
            IsDebugOverrideActive = true;
            Debug.Log(
                "[HeadPoseTracker] debug override set (" + presetLabel + ") to " + DebugOverrideVector
            );
            DebugOverrideApplied?.Invoke(presetLabel, DebugOverrideVector);
        }

        public void ClearDebugOverride()
        {
            if (!IsDebugOverrideActive)
            {
                return;
            }

            Vector3 clearedVector = DebugOverrideVector;
            IsDebugOverrideActive = false;
            Debug.Log("[HeadPoseTracker] debug override cleared");
            DebugOverrideCleared?.Invoke(clearedVector);
        }

        public void SetMouseLookEnabled(bool enabled)
        {
            if (!enableMouseLook)
            {
                enabled = false;
            }
            if (_mouseLookEnabled == enabled)
            {
                return;
            }

            _mouseLookEnabled = enabled;
            if (_mouseLookEnabled)
            {
                EnsureHeadTransform();
                SyncMouseAnglesFromTransform();
            }
            SetMouseLookCapture(false);
            Debug.Log(
                "[HeadPoseTracker] mouse look "
                + (_mouseLookEnabled ? "enabled" : "disabled")
                + " (toggle=" + toggleMouseLookKey + ")"
            );
        }

        private void EnsureHeadTransform()
        {
            if (headTransform == null && useMainCameraWhenUnset && Camera.main != null)
            {
                headTransform = Camera.main.transform;
            }
        }

        private void HandleMouseLookToggleHotkey()
        {
            if (!enableMouseLook)
            {
                return;
            }
            if (Input.GetKeyDown(toggleMouseLookKey))
            {
                SetMouseLookEnabled(!_mouseLookEnabled);
            }
        }

        private void ApplyMouseLookToTransform()
        {
            if (headTransform == null)
            {
                return;
            }
            if (!_mouseAnglesInitialized)
            {
                SyncMouseAnglesFromTransform();
            }

            bool capture = !requireRightMouseButton || Input.GetMouseButton(1);
            SetMouseLookCapture(capture);
            if (!capture)
            {
                return;
            }

            float mouseX = Input.GetAxis("Mouse X");
            float mouseY = Input.GetAxis("Mouse Y");
            if (Mathf.Abs(mouseX) < 1e-6f && Mathf.Abs(mouseY) < 1e-6f)
            {
                return;
            }

            _mouseYawDeg += mouseX * mouseYawSensitivity;
            float pitchDelta = mouseY * mousePitchSensitivity;
            _mousePitchDeg += invertMouseY ? pitchDelta : -pitchDelta;
            _mousePitchDeg = Mathf.Clamp(_mousePitchDeg, -mousePitchLimit, mousePitchLimit);

            headTransform.localRotation = Quaternion.Euler(_mousePitchDeg, _mouseYawDeg, 0f);
        }

        private void SyncMouseAnglesFromTransform()
        {
            if (headTransform == null)
            {
                return;
            }

            Vector3 localEuler = headTransform.localEulerAngles;
            _mousePitchDeg = NormalizeSignedAngle(localEuler.x);
            _mouseYawDeg = NormalizeSignedAngle(localEuler.y);
            _mouseAnglesInitialized = true;
        }

        private void SetMouseLookCapture(bool capture)
        {
            if (_mouseLookCaptureActive == capture)
            {
                return;
            }

            _mouseLookCaptureActive = capture;
            Cursor.lockState = capture && lockCursorWhileMouseLooking
                ? CursorLockMode.Locked
                : CursorLockMode.None;
            Cursor.visible = !(capture && hideCursorWhileMouseLooking);
        }

        private static float NormalizeSignedAngle(float angleDeg)
        {
            return Mathf.Repeat(angleDeg + 180f, 360f) - 180f;
        }

        private static bool TryMapForward(Vector3 forward, out Vector3 mappedForward)
        {
            mappedForward = new Vector3(forward.z, -forward.x, forward.y);
            if (mappedForward.sqrMagnitude < 1e-8f)
            {
                return false;
            }
            mappedForward = mappedForward.normalized;
            return true;
        }

        private void HandleDebugHotkeys()
        {
            if (!enableDebugOverrideHotkeys)
            {
                return;
            }

            if (Input.GetKeyDown(centerVectorKey))
            {
                SetDebugOverride(new Vector3(1f, 0f, 0f), "F1_center");
            }
            if (Input.GetKeyDown(leftVectorKey))
            {
                SetDebugOverride(new Vector3(0f, 1f, 0f), "F2_left");
            }
            if (Input.GetKeyDown(topVectorKey))
            {
                SetDebugOverride(new Vector3(0f, 0f, 1f), "F3_top");
            }
            if (Input.GetKeyDown(clearOverrideKey))
            {
                ClearDebugOverride();
            }
        }

        private static bool TryGetHeadForwardFromXr(out Vector3 forward)
        {
            forward = Vector3.zero;
            XrNodeStates.Clear();
            InputTracking.GetNodeStates(XrNodeStates);
            for (int index = 0; index < XrNodeStates.Count; index++)
            {
                XRNodeState state = XrNodeStates[index];
                if (state.nodeType != XRNode.CenterEye && state.nodeType != XRNode.Head)
                {
                    continue;
                }
                if (!state.tracked)
                {
                    continue;
                }
                if (!state.TryGetRotation(out Quaternion rotation))
                {
                    continue;
                }
                Vector3 candidateForward = rotation * Vector3.forward;
                if (candidateForward.sqrMagnitude < 1e-8f)
                {
                    continue;
                }
                forward = candidateForward.normalized;
                return true;
            }
            return false;
        }

        private void UpdatePoseSource(PoseSource poseSource)
        {
            if (_hasPoseSourceLog && _currentPoseSource == poseSource)
            {
                return;
            }

            _currentPoseSource = poseSource;
            _hasPoseSourceLog = true;
            if (!logPoseSourceChanges)
            {
                return;
            }

            Debug.Log("[HeadPoseTracker] pose source -> " + GetPoseSourceLabel(poseSource));
        }

        private static string GetPoseSourceLabel(PoseSource poseSource)
        {
            switch (poseSource)
            {
                case PoseSource.XRNode:
                    return "HMD";
                case PoseSource.MouseLook:
                    return "Mouse";
                case PoseSource.CameraTransform:
                    return "Camera";
                case PoseSource.DebugOverride:
                    return "DebugOverride";
                default:
                    return "Unknown";
            }
        }
    }
}
