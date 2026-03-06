using UnityEngine;

namespace Pano2StereoVR
{
    [RequireComponent(typeof(Renderer))]
    public sealed class BaselinePanoramaRenderer : MonoBehaviour
    {
        [SerializeField] private RtspBaselineReceiver receiver;
        [SerializeField] private Renderer targetRenderer;
        [SerializeField] private string textureProperty = "_MainTex";
        [SerializeField] private string gammaProperty = "_Gamma";
        [SerializeField] private string flipXProperty = "_FlipX";
        [SerializeField] private string flipYProperty = "_FlipY";
        [SerializeField] private bool followMainCamera = true;
        [SerializeField] private Vector3 followOffset = Vector3.zero;
        [SerializeField] [Min(1f)] private float sphereScale = 100f;
        [SerializeField] private bool autoSetSphereScale = true;
        [SerializeField] private bool autoAssignMonoMaterial = true;
        [SerializeField] [Range(0.5f, 3.0f)] private float gamma = 1f;
        [SerializeField] private bool flipX = true;
        [SerializeField] private bool flipY = true;

        private int _texturePropertyId;
        private int _gammaPropertyId;
        private int _flipXPropertyId;
        private int _flipYPropertyId;
        private Transform _mainCameraTransform;
        private Texture _lastTexture;
        private bool _rendererVisible;

        public bool HasTargetRenderer => targetRenderer != null;
        public bool RendererEnabled => targetRenderer != null && targetRenderer.enabled;
        public bool HasBoundTexture => _lastTexture != null;
        public bool RendererVisible => _rendererVisible;
        public int BoundTextureWidth { get; private set; }
        public int BoundTextureHeight { get; private set; }

        private void Reset()
        {
            targetRenderer = GetComponent<Renderer>();
        }

        private void Awake()
        {
            _texturePropertyId = Shader.PropertyToID(textureProperty);
            _gammaPropertyId = Shader.PropertyToID(gammaProperty);
            _flipXPropertyId = Shader.PropertyToID(flipXProperty);
            _flipYPropertyId = Shader.PropertyToID(flipYProperty);

            if (targetRenderer == null)
            {
                targetRenderer = GetComponent<Renderer>();
            }

            EnsureMaterialReady();
            ResolveMainCamera();
            EnsureReasonableScale();
        }

        private void Update()
        {
            if (receiver == null)
            {
                receiver = FindObjectOfType<RtspBaselineReceiver>();
            }

            ResolveMainCamera();
            AlignToMainCamera();
            EnsureReasonableScale();
            EnsureMaterialReady();

            Texture texture = receiver != null ? receiver.CurrentTexture : null;
            if (texture != null)
            {
                ApplyTexture(texture);
            }

            UpdateVisibilityDiagnostics();
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
            if (!followMainCamera || _mainCameraTransform == null)
            {
                return;
            }

            transform.position = _mainCameraTransform.position + followOffset;
        }

        private void EnsureReasonableScale()
        {
            if (!autoSetSphereScale)
            {
                return;
            }

            float targetScale = Mathf.Max(1f, sphereScale);
            transform.localScale = Vector3.one * targetScale;
        }

        private void EnsureMaterialReady()
        {
            if (!autoAssignMonoMaterial || targetRenderer == null)
            {
                return;
            }

            Material current = targetRenderer.sharedMaterial;
            if (current != null && current.shader != null && current.shader.name == "Pano2Stereo/MonoPanorama")
            {
                return;
            }

            Shader shader = Shader.Find("Pano2Stereo/MonoPanorama");
            if (shader == null)
            {
                return;
            }

            targetRenderer.material = new Material(shader);
        }

        private void ApplyTexture(Texture texture)
        {
            if (targetRenderer == null)
            {
                return;
            }

            Material material = targetRenderer.material;
            if (_lastTexture != texture)
            {
                material.SetTexture(_texturePropertyId, texture);
                _lastTexture = texture;
            }

            material.SetFloat(_gammaPropertyId, gamma);
            material.SetFloat(_flipXPropertyId, flipX ? 1f : 0f);
            material.SetFloat(_flipYPropertyId, flipY ? 1f : 0f);
            BoundTextureWidth = texture.width;
            BoundTextureHeight = texture.height;
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
    }
}
