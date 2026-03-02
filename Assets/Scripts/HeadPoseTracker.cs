using UnityEngine;

namespace Pano2StereoVR
{
    public sealed class HeadPoseTracker : MonoBehaviour
    {
        [SerializeField] private Transform headTransform;
        [SerializeField] private bool useMainCameraWhenUnset = true;
        [SerializeField] private bool enableDebugOverrideHotkeys = false;
        [SerializeField] private KeyCode centerVectorKey = KeyCode.F1;
        [SerializeField] private KeyCode leftVectorKey = KeyCode.F2;
        [SerializeField] private KeyCode topVectorKey = KeyCode.F3;
        [SerializeField] private KeyCode clearOverrideKey = KeyCode.F4;

        public Vector3 ServerForwardUnit { get; private set; } = new Vector3(1f, 0f, 0f);
        public bool IsDebugOverrideActive { get; private set; }
        public Vector3 DebugOverrideVector { get; private set; } = new Vector3(1f, 0f, 0f);

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

            if (headTransform == null && useMainCameraWhenUnset && Camera.main != null)
            {
                headTransform = Camera.main.transform;
            }
            if (headTransform == null)
            {
                return;
            }

            Vector3 forward = headTransform.forward;
            Vector3 mapped = new Vector3(forward.z, -forward.x, forward.y);
            if (mapped.sqrMagnitude < 1e-8f)
            {
                return;
            }
            ServerForwardUnit = mapped.normalized;
        }

        public void SetDebugOverride(Vector3 serverUnit)
        {
            if (serverUnit.sqrMagnitude < 1e-8f)
            {
                return;
            }

            DebugOverrideVector = serverUnit.normalized;
            IsDebugOverrideActive = true;
            Debug.Log("[HeadPoseTracker] debug override set to " + DebugOverrideVector);
        }

        public void ClearDebugOverride()
        {
            if (!IsDebugOverrideActive)
            {
                return;
            }

            IsDebugOverrideActive = false;
            Debug.Log("[HeadPoseTracker] debug override cleared");
        }

        private void HandleDebugHotkeys()
        {
            if (!enableDebugOverrideHotkeys)
            {
                return;
            }

            if (Input.GetKeyDown(centerVectorKey))
            {
                SetDebugOverride(new Vector3(1f, 0f, 0f));
            }
            if (Input.GetKeyDown(leftVectorKey))
            {
                SetDebugOverride(new Vector3(0f, 1f, 0f));
            }
            if (Input.GetKeyDown(topVectorKey))
            {
                SetDebugOverride(new Vector3(0f, 0f, 1f));
            }
            if (Input.GetKeyDown(clearOverrideKey))
            {
                ClearDebugOverride();
            }
        }
    }
}
