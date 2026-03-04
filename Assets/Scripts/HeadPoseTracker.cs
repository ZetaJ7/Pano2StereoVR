using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

namespace Pano2StereoVR
{
    public sealed class HeadPoseTracker : MonoBehaviour
    {
        [SerializeField] private Transform headTransform;
        [SerializeField] private bool useMainCameraWhenUnset = true;
        [SerializeField] private bool preferXrNodePose = true;
        [SerializeField] private bool logPoseSourceChanges = true;
        [SerializeField] private bool enableDebugOverrideHotkeys = false;
        [SerializeField] private KeyCode centerVectorKey = KeyCode.F1;
        [SerializeField] private KeyCode leftVectorKey = KeyCode.F2;
        [SerializeField] private KeyCode topVectorKey = KeyCode.F3;
        [SerializeField] private KeyCode clearOverrideKey = KeyCode.F4;

        public Vector3 ServerForwardUnit { get; private set; } = new Vector3(1f, 0f, 0f);
        public bool IsDebugOverrideActive { get; private set; }
        public Vector3 DebugOverrideVector { get; private set; } = new Vector3(1f, 0f, 0f);
        public event Action<string, Vector3> DebugOverrideApplied;
        public event Action<Vector3> DebugOverrideCleared;

        private static readonly List<XRNodeState> XrNodeStates = new List<XRNodeState>(8);
        private bool _lastUsedXrNodePose;
        private bool _hasPoseSourceLog;

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
            if (IsDebugOverrideActive)
            {
                ServerForwardUnit = DebugOverrideVector;
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
                if (headTransform == null && useMainCameraWhenUnset && Camera.main != null)
                {
                    headTransform = Camera.main.transform;
                }
                if (headTransform == null)
                {
                    return;
                }
                forward = headTransform.forward;
            }

            Vector3 mapped = new Vector3(forward.z, -forward.x, forward.y);
            if (mapped.sqrMagnitude < 1e-8f)
            {
                return;
            }
            ServerForwardUnit = mapped.normalized;
            LogPoseSourceIfChanged(usedXrNodePose);
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

        private void LogPoseSourceIfChanged(bool usedXrNodePose)
        {
            if (!logPoseSourceChanges)
            {
                return;
            }
            if (_hasPoseSourceLog && _lastUsedXrNodePose == usedXrNodePose)
            {
                return;
            }

            _lastUsedXrNodePose = usedXrNodePose;
            _hasPoseSourceLog = true;
            Debug.Log(
                "[HeadPoseTracker] pose source -> "
                + (usedXrNodePose ? "XRNode" : "CameraTransform")
            );
        }
    }
}
