using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.XR;

namespace Pano2StereoVR.Study
{
    /// <summary>
    /// Controller ray for the headset canvas, built on UnityEngine.XR.InputDevices (OpenXR controller
    /// profiles must be enabled). Hovering highlights a button; pulling the trigger clicks it. Either hand
    /// works; the hand whose trigger was pulled last becomes the active pointer.
    /// </summary>
    public sealed class StudyPointer : MonoBehaviour
    {
        public StudyRig Rig;
        public StudyUi Ui;
        public float MaxDistance = 10f;
        public Material LineMaterial;

        private readonly List<InputDevice> _devices = new List<InputDevice>();
        private readonly bool[] _prevTrigger = new bool[2];
        private int _activeHand = 1; // 0 = left, 1 = right
        private LineRenderer _line;
        private Transform _cursor;
        private Button _hovered;

        private void Start()
        {
            var lineGo = new GameObject("PointerRay");
            lineGo.transform.SetParent(transform, false);
            _line = lineGo.AddComponent<LineRenderer>();
            _line.positionCount = 2;
            _line.startWidth = 0.004f;
            _line.endWidth = 0.002f;
            _line.useWorldSpace = true;
            _line.material = LineMaterial != null ? LineMaterial : new Material(Shader.Find("Sprites/Default"));
            _line.startColor = new Color(0.6f, 0.85f, 1f, 0.9f);
            _line.endColor = new Color(0.6f, 0.85f, 1f, 0.2f);
            _line.enabled = false;

            GameObject dot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(dot.GetComponent<Collider>());
            dot.name = "PointerCursor";
            dot.transform.SetParent(transform, false);
            dot.transform.localScale = Vector3.one * 0.012f;
            dot.GetComponent<Renderer>().material = _line.material;
            _cursor = dot.transform;
            _cursor.gameObject.SetActive(false);
        }

        /// <summary>Trigger presses of either hand since start (used to advance self-paced clips without aiming).</summary>
        public int TriggerPresses { get; private set; }

        private void Update()
        {
            if (Rig == null || !Rig.XrActive)
            {
                SetVisible(false);
                SetHover(null);
                return;
            }

            bool clicked = false;
            for (int hand = 0; hand < 2; hand++)
            {
                InputDevice dev = GetHand(hand);
                bool trig = dev.isValid && dev.TryGetFeatureValue(CommonUsages.triggerButton, out bool t) && t;
                if (trig && !_prevTrigger[hand])
                {
                    TriggerPresses++;
                    if (hand != _activeHand)
                    {
                        _activeHand = hand;
                    }
                    else
                    {
                        clicked = true;
                    }
                }
                _prevTrigger[hand] = trig;
            }

            bool canvasActive = Ui != null && Ui.HmdCanvas != null && Ui.HmdCanvas.gameObject.activeInHierarchy && Ui.HmdButtons.Count > 0;
            if (!canvasActive)
            {
                SetVisible(false);
                SetHover(null);
                return;
            }

            InputDevice active = GetHand(_activeHand);
            if (!active.isValid
                || !active.TryGetFeatureValue(CommonUsages.devicePosition, out Vector3 pos)
                || !active.TryGetFeatureValue(CommonUsages.deviceRotation, out Quaternion rot))
            {
                SetVisible(false);
                SetHover(null);
                return;
            }

            Transform rig = Rig.transform;
            Vector3 origin = rig.TransformPoint(pos);
            Vector3 dir = rig.rotation * (rot * Vector3.forward);
            Transform canvas = Ui.HmdCanvas.transform;
            var plane = new Plane(-canvas.forward, canvas.position);
            Vector3 end = origin + dir * MaxDistance;
            Button hit = null;
            if (plane.Raycast(new Ray(origin, dir), out float dist) && dist < MaxDistance)
            {
                end = origin + dir * dist;
                hit = FindButton(end);
            }
            _line.SetPosition(0, origin);
            _line.SetPosition(1, end);
            _cursor.position = end;
            SetVisible(true);
            SetHover(hit);
            if (clicked && hit != null && hit.interactable)
            {
                ExecuteEvents.Execute(hit.gameObject, new PointerEventData(EventSystem.current), ExecuteEvents.pointerClickHandler);
            }
        }

        private InputDevice GetHand(int hand)
        {
            _devices.Clear();
            InputDeviceCharacteristics c = InputDeviceCharacteristics.Controller
                | (hand == 0 ? InputDeviceCharacteristics.Left : InputDeviceCharacteristics.Right);
            InputDevices.GetDevicesWithCharacteristics(c, _devices);
            return _devices.Count > 0 ? _devices[0] : default;
        }

        private Button FindButton(Vector3 worldPoint)
        {
            for (int i = Ui.HmdButtons.Count - 1; i >= 0; i--)
            {
                Button b = Ui.HmdButtons[i];
                if (b == null || !b.isActiveAndEnabled)
                {
                    continue;
                }
                var r = (RectTransform)b.transform;
                Vector3 local = r.InverseTransformPoint(worldPoint);
                if (r.rect.Contains(new Vector2(local.x, local.y)))
                {
                    return b;
                }
            }
            return null;
        }

        private void SetHover(Button b)
        {
            if (_hovered == b)
            {
                return;
            }
            var data = new PointerEventData(EventSystem.current);
            if (_hovered != null)
            {
                ExecuteEvents.Execute(_hovered.gameObject, data, ExecuteEvents.pointerExitHandler);
            }
            _hovered = b;
            if (_hovered != null)
            {
                ExecuteEvents.Execute(_hovered.gameObject, data, ExecuteEvents.pointerEnterHandler);
            }
        }

        private void SetVisible(bool v)
        {
            if (_line != null)
            {
                _line.enabled = v;
            }
            if (_cursor != null)
            {
                _cursor.gameObject.SetActive(v);
            }
        }
    }
}
