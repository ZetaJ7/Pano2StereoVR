using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

namespace Pano2StereoVR.Study
{
    /// <summary>
    /// Camera rig for the study. The camera is driven from the XR head pose (or the mouse on the desktop),
    /// the panorama sphere follows the camera position every frame, so the content behaves as 3DoF
    /// (rotation only). Recenter() rotates the rig so that the current head yaw faces the content forward.
    /// </summary>
    public sealed class StudyRig : MonoBehaviour
    {
        public Camera Camera;
        public Transform Sphere;
        public float MouseSensitivity = 3f;

        private readonly List<InputDevice> _devices = new List<InputDevice>();
        private float _mouseYaw;
        private float _mousePitch;

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

        /// <summary>Rotate the rig about Y so the current head yaw maps to content yaw 0.</summary>
        public void Recenter()
        {
            if (Camera == null)
            {
                return;
            }
            Vector3 f = Camera.transform.localRotation * Vector3.forward;
            float localYaw = Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
            transform.rotation = Quaternion.Euler(0f, -localYaw, 0f);
        }

        /// <summary>
        /// Desktop only (no effect in XR): point the simulated head at a content direction (yaw, pitch in
        /// degrees, + = right / up). Used by the desktop autopilot to simulate the head for orientation alignment.
        /// </summary>
        public void SetDesktopView(float contentYaw, float contentPitch)
        {
            if (XrActive)
            {
                return;
            }
            _mouseYaw = WrapDegrees(contentYaw - transform.eulerAngles.y);
            _mousePitch = Mathf.Clamp(-contentPitch, -85f, 85f);
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
