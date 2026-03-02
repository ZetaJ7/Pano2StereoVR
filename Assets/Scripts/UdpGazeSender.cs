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

        public event Action<int, float> ModeMessageSent;

        public HeadPoseTracker PoseTracker => poseTracker;
        public int CurrentMode { get; private set; }
        public int LastSentMode { get; private set; } = -1;
        public float LastModeSentTime { get; private set; } = -1f;
        public long GazePacketsSent { get; private set; }
        public long CombinedPacketsSent { get; private set; }
        public long ModePacketsSent { get; private set; }
        public long PacketSendErrors { get; private set; }
        public bool IsConnected => _udpClient != null;

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
            if (SendPayload(payload))
            {
                GazePacketsSent += 1;
            }
        }

        private void SendModeOnly(int mode)
        {
            string payload = "{\"mode\":" + mode.ToString(CultureInfo.InvariantCulture) + "}";
            if (SendPayload(payload))
            {
                ModePacketsSent += 1;
                LastSentMode = mode;
                LastModeSentTime = Time.unscaledTime;
                ModeMessageSent?.Invoke(mode, LastModeSentTime);
            }
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
            if (SendPayload(payload))
            {
                CombinedPacketsSent += 1;
            }
        }

        private bool SendPayload(string payload)
        {
            if (_udpClient == null)
            {
                return false;
            }

            try
            {
                byte[] bytes = Encoding.UTF8.GetBytes(payload);
                _udpClient.Send(bytes, bytes.Length);
                return true;
            }
            catch (Exception ex)
            {
                PacketSendErrors += 1;
                Debug.LogWarning("[UdpGazeSender] send failed: " + ex.Message);
                return false;
            }
        }
    }
}
