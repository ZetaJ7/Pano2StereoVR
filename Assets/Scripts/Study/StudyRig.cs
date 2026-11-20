using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

namespace Pano2StereoVR.Study
{
    /// <summary>How a recenter (set forward) treats the head pitch (user 11-20). There are no automatic recenters.</summary>
    public enum RecenterMode
    {
        /// <summary>R / status-bar button: the current head yaw and pitch both map to content yaw 0, pitch 0.</summary>
        YawAndPitch,
        /// <summary>Shift+R: only the yaw; the pitch offset is cleared (level content horizon).</summary>
        YawLevel,
    }

    /// <summary>
    /// Camera rig for the study. The camera is driven from the XR head pose (or the mouse on the desktop),
    /// the panorama sphere follows the camera position every frame, so the content behaves as 3DoF
    /// (rotation only). The rig rotation is a yaw offset combined with a pitch offset (Recenter); head angles are
    /// always taken from the camera's world rotation, so they include the whole rig rotation. A pitch offset tilts
    /// the content horizon by that angle (meant for posture changes only).
    /// </summary>
    public sealed class StudyRig : MonoBehaviour
    {
        public Camera Camera;
        public Transform Sphere;
        public float MouseSensitivity = 3f;

        private readonly List<InputDevice> _devices = new List<InputDevice>();
        private float _mouseYaw;
        private float _mousePitch;
        private float _yawOffset;
        private float _pitchOffset;

        /// <summary>Yaw (deg) of the tracking space that maps to content yaw 0.</summary>
        public float YawOffsetDeg => _yawOffset;
        /// <summary>Pitch (deg, + = up) of the tracking space that maps to content pitch 0; 0 = level horizon.</summary>
        public float PitchOffsetDeg => _pitchOffset;

        public bool XrActive => XRSettings.isDeviceActive;

        private void Awake()
        {
            Application.onBeforeRender += OnBeforeRender;
        }

        /// <summary>Assign the camera and sphere; the camera pose is then driven only by this component.</summary>
        public void Initialize(Camera cam, Transform sphere)
        {
            Camera = cam;
            Sphere = sphere;
            if (Camera != null)
            {
                XRDevice.DisableAutoXRCameraTracking(Camera, true);
            }
        }

        private void OnDestroy()
        {
            Application.onBeforeRender -= OnBeforeRender;
        }

        private void Update()
        {
            UpdatePose(true);
        }

        private void OnBeforeRender()
        {
            UpdatePose(false);
        }

        private void UpdatePose(bool allowMouse)
        {
            if (Camera == null)
            {
                return;
            }
            Transform cam = Camera.transform;
            if (XrActive && TryGetHeadPose(out Vector3 pos, out Quaternion rot))
            {
                cam.localPosition = pos;
                cam.localRotation = rot;
            }
            else if (!XrActive)
            {
                if (allowMouse && Input.GetMouseButton(1))
                {
                    _mouseYaw += Input.GetAxis("Mouse X") * MouseSensitivity;
                    _mousePitch = Mathf.Clamp(_mousePitch - Input.GetAxis("Mouse Y") * MouseSensitivity, -85f, 85f);
                }
                if (allowMouse)
                {
                    float k = 60f * Time.unscaledDeltaTime;
                    if (Input.GetKey(KeyCode.LeftArrow)) { _mouseYaw -= k; }
                    if (Input.GetKey(KeyCode.RightArrow)) { _mouseYaw += k; }
                    if (Input.GetKey(KeyCode.UpArrow)) { _mousePitch = Mathf.Clamp(_mousePitch - k, -85f, 85f); }
                    if (Input.GetKey(KeyCode.DownArrow)) { _mousePitch = Mathf.Clamp(_mousePitch + k, -85f, 85f); }
                }
                cam.localPosition = Vector3.zero;
                cam.localRotation = Quaternion.Euler(_mousePitch, _mouseYaw, 0f);
            }
            if (Sphere != null)
            {
                Sphere.position = cam.position;
            }
        }

        private bool TryGetHeadPose(out Vector3 position, out Quaternion rotation)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;
            InputDevice head = InputDevices.GetDeviceAtXRNode(XRNode.CenterEye);
            if (head.isValid
                && head.TryGetFeatureValue(CommonUsages.centerEyeRotation, out rotation))
            {
                head.TryGetFeatureValue(CommonUsages.centerEyePosition, out position);
                return true;
            }
            head = InputDevices.GetDeviceAtXRNode(XRNode.Head);
            if (head.isValid && head.TryGetFeatureValue(CommonUsages.deviceRotation, out rotation))
            {
                head.TryGetFeatureValue(CommonUsages.devicePosition, out position);
                return true;
            }
            return false;
        }

        public string HeadDeviceName()
        {
            _devices.Clear();
            InputDevices.GetDevicesWithCharacteristics(InputDeviceCharacteristics.HeadMounted, _devices);
            if (_devices.Count == 0)
            {
                return XrActive ? XRSettings.loadedDeviceName : "desktop (no HMD)";
            }
            InputDevice d = _devices[0];
            return string.IsNullOrEmpty(d.manufacturer) ? d.name : d.manufacturer + " " + d.name;
        }

        /// <summary>
        /// Rotate the rig so the current head yaw maps to content yaw 0; with <see cref="RecenterMode.YawAndPitch"/>
        /// the head pitch maps to content pitch 0 as well (user 11-20). Roll is ignored. The rig rotation is
        /// inverse(yaw offset, then pitch offset), so the head yaw after the call is exactly 0 in every mode.
        /// </summary>
        public void Recenter(RecenterMode mode)
        {
            if (Camera == null)
            {
                return;
            }
            YawPitchOf(Camera.transform.localRotation * Vector3.forward, out float yaw, out float pitch);
            _yawOffset = yaw;
            _pitchOffset = mode == RecenterMode.YawAndPitch ? pitch : 0f;
            transform.rotation = Quaternion.Inverse(Quaternion.Euler(-_pitchOffset, _yawOffset, 0f));
        }

        /// <summary>Yaw and pitch (deg, + = right / up) of a direction (x right, y up, z forward).</summary>
        public static void YawPitchOf(Vector3 f, out float yaw, out float pitch)
        {
            float n = f.magnitude;
            if (n < 1e-9f)
            {
                yaw = 0f;
                pitch = 0f;
                return;
            }
            yaw = Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
            pitch = Mathf.Asin(Mathf.Clamp(f.y / n, -1f, 1f)) * Mathf.Rad2Deg;
        }

        /// <summary>
        /// Desktop only (no effect in XR): point the simulated head at a content direction (yaw, pitch in
        /// degrees, + = right / up), through the whole rig rotation (yaw and pitch offsets). Used by the desktop
        /// autopilot to simulate the head for orientation alignment.
        /// </summary>
        public void SetDesktopView(float contentYaw, float contentPitch)
        {
            if (XrActive)
            {
                return;
            }
            Vector3 world = Quaternion.Euler(-contentPitch, contentYaw, 0f) * Vector3.forward;
            YawPitchOf(Quaternion.Inverse(transform.rotation) * world, out float yaw, out float pitch);
            _mouseYaw = yaw;
            _mousePitch = Mathf.Clamp(-pitch, -85f, 85f);
            UpdatePose(false);
        }

        /// <summary>Head yaw, pitch, roll in degrees relative to the content (yaw 0 = ERP centre, + = right / up).</summary>
        public void HeadAngles(out float yaw, out float pitch, out float roll)
        {
            Quaternion q = Camera != null ? Camera.transform.rotation : Quaternion.identity;
            Vector3 f = q * Vector3.forward;
            yaw = Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
            pitch = Mathf.Asin(Mathf.Clamp(f.y, -1f, 1f)) * Mathf.Rad2Deg;
            float z = q.eulerAngles.z;
            roll = z > 180f ? z - 360f : z;
        }

        /// <summary>Yaw-only forward direction of the head, for placing panels in front of the viewer.</summary>
        public Vector3 YawForward()
        {
            HeadAngles(out float yaw, out _, out _);
            return Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
        }

        public Vector3 HeadPosition => Camera != null ? Camera.transform.position : Vector3.zero;

        public static float WrapDegrees(float a)
        {
            a %= 360f;
            if (a > 180f) { a -= 360f; }
            if (a < -180f) { a += 360f; }
            return a;
        }
    }
}
