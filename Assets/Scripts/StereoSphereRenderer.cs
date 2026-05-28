using UnityEngine;

namespace Pano2StereoVR
{
    [RequireComponent(typeof(Renderer))]
    [RequireComponent(typeof(MeshFilter))]
    public sealed class StereoSphereRenderer : MonoBehaviour
    {
        private enum DisplaySurface
        {
            Planar = 0,
            Sphere = 1,
        }

        [SerializeField] private SharedMemoryReceiver receiver;
        [SerializeField] private Renderer targetRenderer;
        [SerializeField] private string textureProperty = "_MainTex";
        [SerializeField] private string modeProperty = "_Mode";
        [SerializeField] private string previewEyeProperty = "_PreviewEye";
        [SerializeField] private string flipXProperty = "_FlipX";
        [SerializeField] private string flipYProperty = "_FlipY";
        [SerializeField] private string swapEyesProperty = "_SwapEyes";
        [SerializeField] private bool updateOnEveryFrame = true;
        [SerializeField] private DisplaySurface displaySurface = DisplaySurface.Planar;
        [SerializeField] private bool autoConfigureRenderSurface = true;
        [SerializeField] private Vector3 planarPosition = new Vector3(0f, 1.6f, 3.0f);
        [SerializeField] private Vector3 planarEulerAngles = new Vector3(0f, 180f, 0f);
        [SerializeField] private Vector2 planarSize = new Vector2(5.0f, 2.5f);
        [SerializeField] private bool followMainCamera = false;
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
        private MeshFilter _meshFilter;
        private Transform _mainCameraTransform;
        private bool _rendererVisible;
        private static Mesh _fallbackPlanarQuadMesh;

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
            if (_meshFilter == null)
            {
                _meshFilter = GetComponent<MeshFilter>();
            }
            ConfigureRenderSurface();
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
            ConfigureRenderSurface();
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

        private void ConfigureRenderSurface()
        {
            if (!autoConfigureRenderSurface || displaySurface != DisplaySurface.Planar)
            {
                return;
            }

            if (_meshFilter == null)
            {
                _meshFilter = GetComponent<MeshFilter>();
            }
            if (_meshFilter != null)
            {
                Mesh quad = GetPlanarQuadMesh();
                if (quad != null && _meshFilter.sharedMesh != quad)
                {
                    _meshFilter.sharedMesh = quad;
                }
            }

            transform.position = planarPosition;
            transform.rotation = Quaternion.Euler(planarEulerAngles);
            transform.localScale = new Vector3(
                Mathf.Max(0.01f, planarSize.x),
                Mathf.Max(0.01f, planarSize.y),
                1f
            );
        }

        private static Mesh GetPlanarQuadMesh()
        {
            Mesh builtInQuad = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
            if (builtInQuad != null)
            {
                return builtInQuad;
            }

            if (_fallbackPlanarQuadMesh != null)
            {
                return _fallbackPlanarQuadMesh;
            }

            _fallbackPlanarQuadMesh = new Mesh
            {
                name = "Quad"
            };
            _fallbackPlanarQuadMesh.vertices = new[]
            {
                new Vector3(-0.5f, -0.5f, 0f),
                new Vector3(0.5f, -0.5f, 0f),
                new Vector3(-0.5f, 0.5f, 0f),
                new Vector3(0.5f, 0.5f, 0f)
            };
            _fallbackPlanarQuadMesh.uv = new[]
            {
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(0f, 1f),
                new Vector2(1f, 1f)
            };
            _fallbackPlanarQuadMesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
            _fallbackPlanarQuadMesh.RecalculateNormals();
            _fallbackPlanarQuadMesh.RecalculateBounds();
            return _fallbackPlanarQuadMesh;
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
            if (displaySurface == DisplaySurface.Planar && !followMainCamera)
            {
                return;
            }
            if (followMainCamera || (autoRecentreIfCameraOutside && CameraDistance > recenterDistance))
            {
                transform.position = targetPosition;
                CameraDistance = 0f;
            }
        }

        private void EnsureReasonableScale()
        {
            if (displaySurface == DisplaySurface.Planar)
            {
                return;
            }

            float targetScale = Mathf.Max(1f, sphereScale);
            bool tooSmallScale = transform.localScale.x < 10f
                && transform.localScale.y < 10f
                && transform.localScale.z < 10f;
            if (autoSetSphereScale || tooSmallScale)
            {
                transform.localScale = Vector3.one * targetScale;
            }
        }

        private bool EnsureMaterialReady()
        {
            if (!autoAssignStereoMaterial || targetRenderer == null)
            {
                return false;
            }

            Material current = targetRenderer.sharedMaterial;
            if (current != null
                && current.shader != null
                && current.shader.name == "Pano2Stereo/StereoPanorama")
            {
                return false;
            }

            Shader shader = Shader.Find("Pano2Stereo/StereoPanorama");
            if (shader == null)
            {
                return false;
            }

            SetTargetMaterial(new Material(shader));
            return true;
        }

        private Material GetTargetMaterialForWrite()
        {
            if (targetRenderer == null)
            {
                return null;
            }

            return Application.isPlaying ? targetRenderer.material : targetRenderer.sharedMaterial;
        }

        private void SetTargetMaterial(Material material)
        {
            if (targetRenderer == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                targetRenderer.material = material;
            }
            else
            {
                targetRenderer.sharedMaterial = material;
            }
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

            if (EnsureMaterialReady())
            {
                _lastTexture = null;
            }
            Material material = GetTargetMaterialForWrite();
            if (material == null)
            {
                return;
            }
            if (_lastTexture != texture || material.GetTexture(_texturePropertyId) != texture)
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
