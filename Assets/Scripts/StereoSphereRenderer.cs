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
        [SerializeField] private bool updateOnEveryFrame = true;

        private int _texturePropertyId;
        private int _modePropertyId;
        private Texture _lastTexture;

        private void Reset()
        {
            targetRenderer = GetComponent<Renderer>();
        }

        private void Awake()
        {
            _texturePropertyId = Shader.PropertyToID(textureProperty);
            _modePropertyId = Shader.PropertyToID(modeProperty);
            if (targetRenderer == null)
            {
                targetRenderer = GetComponent<Renderer>();
            }
        }

        private void OnEnable()
        {
            if (receiver != null)
            {
                receiver.FrameUpdated += OnFrameUpdated;
            }
        }

        private void OnDisable()
        {
            if (receiver != null)
            {
                receiver.FrameUpdated -= OnFrameUpdated;
            }
        }

        private void Update()
        {
            if (!updateOnEveryFrame || receiver == null)
            {
                return;
            }
            ApplyFrame(receiver.StereoTexture, receiver.CurrentMode);
        }

        private void OnFrameUpdated(Texture2D texture, int mode)
        {
            if (updateOnEveryFrame)
            {
                return;
            }
            ApplyFrame(texture, mode);
        }

        private void ApplyFrame(Texture texture, int mode)
        {
            if (texture == null || targetRenderer == null)
            {
                return;
            }

            Material material = targetRenderer.material;
            if (_lastTexture != texture)
            {
                material.SetTexture(_texturePropertyId, texture);
                _lastTexture = texture;
            }
            material.SetFloat(_modePropertyId, mode);
        }
    }
}
