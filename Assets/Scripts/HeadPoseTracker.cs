using UnityEngine;

namespace Pano2StereoVR
{
    public sealed class HeadPoseTracker : MonoBehaviour
    {
        [SerializeField] private Transform headTransform;
        [SerializeField] private bool useMainCameraWhenUnset = true;

        public Vector3 ServerForwardUnit { get; private set; } = new Vector3(1f, 0f, 0f);

        private void Reset()
        {
            if (Camera.main != null)
            {
                headTransform = Camera.main.transform;
            }
        }

        private void Update()
        {
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
    }
}
