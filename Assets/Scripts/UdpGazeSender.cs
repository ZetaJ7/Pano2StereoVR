using System;
using System.Globalization;
using System.Net.Sockets;
using System.Text;
using UnityEngine;

namespace Pano2StereoVR
{
    public sealed class UdpGazeSender : MonoBehaviour
    {
        [SerializeField] private HeadPoseTracker poseTracker;
        [SerializeField] private string host = "127.0.0.1";
        [SerializeField] private int port = 50051;
        [SerializeField] [Range(1, 120)] private int sendRateHz = 60;
        [SerializeField] private bool includeModeInGazePacket = false;
        [SerializeField] [Range(1, 3)] private int initialMode = 3;

        private UdpClient _udpClient;
        private float _nextSendTime;

        public int CurrentMode { get; private set; }

        private void Awake()
        {
            CurrentMode = initialMode;
        }

        private void OnEnable()
        {
            TryConnect();
            SendModeOnly(CurrentMode);
        }

        private void OnDisable()
        {
            if (_udpClient != null)
            {
                _udpClient.Close();
                _udpClient = null;
            }
        }

        private void Update()
        {
            if (_udpClient == null || poseTracker == null)
            {
                return;
            }
            if (Time.unscaledTime < _nextSendTime)
            {
                return;
            }

            float period = 1f / Mathf.Max(1, sendRateHz);
            _nextSendTime = Time.unscaledTime + period;

            Vector3 u0 = poseTracker.ServerForwardUnit;
            if (includeModeInGazePacket)
            {
                SendGazeAndMode(u0, CurrentMode);
            }
            else
            {
                SendGazeOnly(u0);
            }
        }

        public void SetMode(int mode)
        {
            int clamped = Mathf.Clamp(mode, 1, 3);
            CurrentMode = clamped;
            SendModeOnly(clamped);
        }

        private void TryConnect()
        {
            try
            {
                _udpClient = new UdpClient();
                _udpClient.Connect(host, port);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[UdpGazeSender] connect failed: " + ex.Message);
                _udpClient = null;
            }
        }

        private void SendGazeOnly(Vector3 u0)
        {
            string payload = string.Format(
                CultureInfo.InvariantCulture,
                "{{\"u0\":[{0:F6},{1:F6},{2:F6}]}}",
                u0.x,
                u0.y,
                u0.z
            );
            SendPayload(payload);
        }

        private void SendModeOnly(int mode)
        {
            string payload = "{\"mode\":" + mode.ToString(CultureInfo.InvariantCulture) + "}";
            SendPayload(payload);
        }

        private void SendGazeAndMode(Vector3 u0, int mode)
        {
            string payload = string.Format(
                CultureInfo.InvariantCulture,
                "{{\"u0\":[{0:F6},{1:F6},{2:F6}],\"mode\":{3}}}",
                u0.x,
                u0.y,
                u0.z,
                mode
            );
            SendPayload(payload);
        }

        private void SendPayload(string payload)
        {
            if (_udpClient == null)
            {
                return;
            }

            try
            {
                byte[] bytes = Encoding.UTF8.GetBytes(payload);
                _udpClient.Send(bytes, bytes.Length);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[UdpGazeSender] send failed: " + ex.Message);
            }
        }
    }
}
