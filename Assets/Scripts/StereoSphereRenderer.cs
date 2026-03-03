using UnityEngine;

namespace Pano2StereoVR
{
    [RequireComponent(typeof(Renderer))]
    public sealed class StereoSphereRenderer : MonoBehaviour
    {
        [SerializeField] private SharedMemoryReceiver receiver;
        [SerializeField] private Renderer targetRenderer;
        [SerializeField] private string textureProperty = "_MainTex";
        [SerializeField] private string modeProperty = "_Mode";
        [SerializeField] private string previewEyeProperty = "_PreviewEye";
        [SerializeField] private string flipXProperty = "_FlipX";
        [SerializeField] private string flipYProperty = "_FlipY";
        [SerializeField] private string swapEyesProperty = "_SwapEyes";
        [SerializeField] private bool updateOnEveryFrame = true;
        [SerializeField] private bool followMainCamera = true;
        [SerializeField] private Vector3 followOffset = Vector3.zero;
        [SerializeField] private bool autoSetSphereScale = true;
        [SerializeField] [Min(1f)] private float sphereScale = 100f;
        [SerializeField] [Range(0f, 1f)] private float previewEyeInMono = 0f;
        [SerializeField] private bool autoRecentreIfCameraOutside = true;
        [SerializeField] [Min(1f)] private float recenterDistance = 20f;
        [SerializeField] private bool autoAssignStereoMaterial = true;
        [SerializeField] private bool flipX = true;
        [SerializeField] private bool flipY = true;
        [SerializeField] private bool swapEyes = false;
        [SerializeField] private bool enableDebugToggleHotkeys = true;
        [SerializeField] private KeyCode toggleFlipXKey = KeyCode.F7;
        [SerializeField] private KeyCode toggleSwapEyesKey = KeyCode.F8;

        private int _texturePropertyId;
        private int _modePropertyId;
        private int _previewEyePropertyId;
        private int _flipXPropertyId;
        private int _flipYPropertyId;
        private int _swapEyesPropertyId;
        private Texture _lastTexture;
        private bool _subscribedToReceiver;
        private Transform _mainCameraTransform;
        private bool _rendererVisible;

        public bool HasTargetRenderer => targetRenderer != null;
        public bool RendererEnabled => targetRenderer != null && targetRenderer.enabled;
        public bool HasBoundTexture => _lastTexture != null;
        public bool RendererVisible => _rendererVisible;
        public int BoundTextureWidth { get; private set; }
        public int BoundTextureHeight { get; private set; }
        public float CameraDistance { get; private set; }
        public bool FlipX => flipX;
        public bool FlipY => flipY;
        public bool SwapEyes => swapEyes;

        private void Reset()
        {
            targetRenderer = GetComponent<Renderer>();
        }

        private void Awake()
        {
            _texturePropertyId = Shader.PropertyToID(textureProperty);
            _modePropertyId = Shader.PropertyToID(modeProperty);
            _previewEyePropertyId = Shader.PropertyToID(previewEyeProperty);
            _flipXPropertyId = Shader.PropertyToID(flipXProperty);
            _flipYPropertyId = Shader.PropertyToID(flipYProperty);
            _swapEyesPropertyId = Shader.PropertyToID(swapEyesProperty);
            if (targetRenderer == null)
            {
                targetRenderer = GetComponent<Renderer>();
            }
            EnsureMaterialReady();
            EnsureReasonableScale();
            ResolveMainCamera();
            TryAttachReceiver();
            UpdateVisibilityDiagnostics();
        }

        private void OnEnable()
        {
            TryAttachReceiver();
        }

        private void OnDisable()
        {
            if (receiver != null && _subscribedToReceiver)
            {
                receiver.FrameUpdated -= OnFrameUpdated;
                _subscribedToReceiver = false;
            }
        }

        private void Update()
        {
            HandleDebugHotkeys();
            AlignToMainCamera();
            EnsureReasonableScale();
            TryAttachReceiver();
            if (!updateOnEveryFrame || receiver == null)
            {
                UpdateVisibilityDiagnostics();
                return;
            }
            ApplyFrame(receiver.StereoTexture, receiver.CurrentMode);
            UpdateVisibilityDiagnostics();
        }

        private void OnFrameUpdated(Texture2D texture, int mode)
        {
            if (updateOnEveryFrame)
            {
                return;
            }
            ApplyFrame(texture, mode);
        }

        private void TryAttachReceiver()
        {
            if (receiver == null)
            {
                receiver = FindObjectOfType<SharedMemoryReceiver>();
            }
            if (receiver != null && !_subscribedToReceiver)
            {
                receiver.FrameUpdated += OnFrameUpdated;
                _subscribedToReceiver = true;
            }
        }

        private void ResolveMainCamera()
        {
            if (_mainCameraTransform != null)
            {
                return;
            }
            Camera mainCamera = Camera.main;
            if (mainCamera != null)
            {
                _mainCameraTransform = mainCamera.transform;
            }
        }

        private void AlignToMainCamera()
        {
            ResolveMainCamera();
            if (_mainCameraTransform == null)
            {
                return;
            }

            Vector3 targetPosition = _mainCameraTransform.position + followOffset;
            CameraDistance = Vector3.Distance(transform.position, targetPosition);
            if (followMainCamera || (autoRecentreIfCameraOutside && CameraDistance > recenterDistance))
            {
                transform.position = targetPosition;
                CameraDistance = 0f;
            }
        }

        private void EnsureReasonableScale()
        {
            float targetScale = Mathf.Max(1f, sphereScale);
            bool tooSmallScale = transform.localScale.x < 10f
                && transform.localScale.y < 10f
                && transform.localScale.z < 10f;
            if (autoSetSphereScale || tooSmallScale)
            {
                transform.localScale = Vector3.one * targetScale;
            }
        }

        private void EnsureMaterialReady()
        {
            if (!autoAssignStereoMaterial || targetRenderer == null)
            {
                return;
            }

            Material current = targetRenderer.sharedMaterial;
            if (current != null
                && current.shader != null
                && current.shader.name == "Pano2Stereo/StereoPanorama")
            {
                return;
            }

            Shader shader = Shader.Find("Pano2Stereo/StereoPanorama");
            if (shader == null)
            {
                return;
            }

            targetRenderer.material = new Material(shader);
        }

        private void UpdateVisibilityDiagnostics()
        {
            if (targetRenderer == null)
            {
                _rendererVisible = false;
                return;
            }
            _rendererVisible = targetRenderer.enabled && targetRenderer.isVisible;
        }

        private void HandleDebugHotkeys()
        {
            if (!enableDebugToggleHotkeys)
            {
                return;
            }

            if (Input.GetKeyDown(toggleFlipXKey))
            {
                flipX = !flipX;
                Debug.Log("[StereoSphereRenderer] flipX -> " + flipX);
            }
            if (Input.GetKeyDown(toggleSwapEyesKey))
            {
                swapEyes = !swapEyes;
                Debug.Log("[StereoSphereRenderer] swapEyes -> " + swapEyes);
            }
        }

        private void ApplyFrame(Texture texture, int mode)
        {
            if (texture == null || targetRenderer == null)
            {
                return;
            }

            EnsureMaterialReady();
            Material material = targetRenderer.material;
            if (_lastTexture != texture)
            {
                material.SetTexture(_texturePropertyId, texture);
                _lastTexture = texture;
            }
            material.SetFloat(_modePropertyId, mode);
            material.SetFloat(_previewEyePropertyId, previewEyeInMono);
            material.SetFloat(_flipXPropertyId, flipX ? 1f : 0f);
            material.SetFloat(_flipYPropertyId, flipY ? 1f : 0f);
            material.SetFloat(_swapEyesPropertyId, swapEyes ? 1f : 0f);
            BoundTextureWidth = texture.width;
            BoundTextureHeight = texture.height;
        }
    }
}
