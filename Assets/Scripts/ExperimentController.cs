using UnityEngine;

namespace Pano2StereoVR
{
    public sealed class ExperimentController : MonoBehaviour
    {
        [SerializeField] private UdpGazeSender udpGazeSender;
        [SerializeField] private KeyCode mode1Key = KeyCode.Alpha1;
        [SerializeField] private KeyCode mode2Key = KeyCode.Alpha2;
        [SerializeField] private KeyCode mode3Key = KeyCode.Alpha3;

        public int CurrentMode => udpGazeSender != null ? udpGazeSender.CurrentMode : 3;

        private void Update()
        {
            if (udpGazeSender == null)
            {
                return;
            }

            if (Input.GetKeyDown(mode1Key))
            {
                udpGazeSender.SetMode(1);
                Debug.Log("[ExperimentController] mode -> 1");
            }
            if (Input.GetKeyDown(mode2Key))
            {
                udpGazeSender.SetMode(2);
                Debug.Log("[ExperimentController] mode -> 2");
            }
            if (Input.GetKeyDown(mode3Key))
            {
                udpGazeSender.SetMode(3);
                Debug.Log("[ExperimentController] mode -> 3");
            }
        }
    }
}
