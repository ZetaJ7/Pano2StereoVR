using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR;

namespace Pano2StereoVR
{
    [DisallowMultipleComponent]
    public sealed class XrSuperResolutionController : MonoBehaviour
    {
        public enum SuperResolutionPreset
        {
            NativeSharpness,
            Quality,
            Balanced,
            Performance,
            Supersample115,
            Supersample130,
            Supersample150,
            Supersample170,
            Supersample200,
        }

        [SerializeField] private Camera targetCamera;
        [SerializeField] private bool enableOnStartup = false;
        [SerializeField] private SuperResolutionPreset startupPreset = SuperResolutionPreset.Balanced;
        [SerializeField] [Range(0.0f, 1.0f)] private float fsrSharpness = 0.82f;
        [SerializeField] private bool enablePresetHotkeys = false;
        [SerializeField] private KeyCode lowerPresetKey = KeyCode.F9;
        [SerializeField] private KeyCode higherPresetKey = KeyCode.F10;
        [SerializeField] private KeyCode toggleAdaptiveKey = KeyCode.F11;
        [SerializeField] private KeyCode toggleSuperResolutionKey = KeyCode.F12;
        [SerializeField] private bool adaptiveRenderScale;
        [SerializeField] [Range(30.0f, 144.0f)] private float adaptiveTargetFps = 72.0f;
        [SerializeField] [Range(0.5f, 3.0f)] private float adaptiveCheckIntervalSeconds = 1.0f;
        [SerializeField] [Range(0.01f, 0.15f)] private float adaptiveScaleStep = 0.05f;
        [SerializeField] [Range(0.6f, 1.0f)] private float minimumAdaptiveScale = 0.70f;
        [SerializeField] [Range(0.6f, 2.0f)] private float maximumAdaptiveScale = 2.00f;
        [SerializeField] private bool logStateChanges = true;
#if UNITY_EDITOR
        [SerializeField] private bool restoreEditorPipelineOnDisable = true;
#endif

        private SuperResolutionPreset _currentPreset;
        private float _adaptiveTimer;
        private float _smoothedFps;
        private bool _hasOriginalState;
        private float _originalRenderScale;
        private UpscalingFilterSelection _originalUpscalingFilter;
        private bool _originalFsrOverrideSharpness;
        private float _originalFsrSharpness;
        private bool _isSuperResolutionEnabled;
        private string _statusMessage = "SR: idle";
        private static readonly SuperResolutionPreset[] PresetOrder =
        {
            SuperResolutionPreset.Performance,
            SuperResolutionPreset.Balanced,
            SuperResolutionPreset.Quality,
            SuperResolutionPreset.NativeSharpness,
            SuperResolutionPreset.Supersample115,
            SuperResolutionPreset.Supersample130,
            SuperResolutionPreset.Supersample150,
            SuperResolutionPreset.Supersample170,
            SuperResolutionPreset.Supersample200,
        };

        public bool IsConfigured => ResolvePipelineAsset() != null;
        public bool IsAdaptiveRenderScaleEnabled => adaptiveRenderScale;
        public bool IsSuperResolutionEnabled => _isSuperResolutionEnabled;
        public bool IsSupersamplingActive => _isSuperResolutionEnabled && CurrentRenderScale > 1.001f;
        public float CurrentRenderScale => !_isSuperResolutionEnabled
            ? 1.0f
            : ResolvePipelineAsset() != null
            ? ResolvePipelineAsset().renderScale
            : 1.0f;
        public string CurrentPresetLabel => GetPresetLabel(_currentPreset);
        public string HotkeyHint => enablePresetHotkeys ? "SR hotkeys: F9/F10 preset, F11 adaptive, F12 on/off" : string.Empty;
        public string StatusMessage => _statusMessage;
        public int NativeEyeWidth => GetNativeEyeWidth();
        public int NativeEyeHeight => GetNativeEyeHeight();
        public int ScaledEyeWidth => Mathf.Max(1, Mathf.RoundToInt(NativeEyeWidth * CurrentRenderScale));
        public int ScaledEyeHeight => Mathf.Max(1, Mathf.RoundToInt(NativeEyeHeight * CurrentRenderScale));

        private void Awake()
        {
            ResolveTargetCamera();
        }

        private void OnEnable()
        {
            ResolveTargetCamera();
            _currentPreset = startupPreset;
            _adaptiveTimer = 0.0f;
            _smoothedFps = 0.0f;
            SetSuperResolutionEnabled(enableOnStartup, false);
        }

        private void Update()
        {
            UpdateSmoothedFps();
            HandleHotkeys();
            UpdateAdaptiveRenderScale();
        }

        private void OnDisable()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying || !restoreEditorPipelineOnDisable)
            {
                return;
            }

            UniversalRenderPipelineAsset asset = ResolvePipelineAsset();
            if (asset == null || !_hasOriginalState)
            {
                return;
            }

            asset.renderScale = _originalRenderScale;
            asset.upscalingFilter = _originalUpscalingFilter;
            asset.fsrOverrideSharpness = _originalFsrOverrideSharpness;
            asset.fsrSharpness = _originalFsrSharpness;
#endif
        }

        public void ApplyPreset(SuperResolutionPreset preset)
        {
            ApplyPreset(preset, true);
        }

        public void SetSuperResolutionEnabled(bool enabled)
        {
            SetSuperResolutionEnabled(enabled, true);
        }

        private void ApplyPreset(SuperResolutionPreset preset, bool logChange)
        {
            _currentPreset = preset;
            if (_isSuperResolutionEnabled)
            {
                ApplyPipelineScale(GetPresetRenderScale(preset), logChange);
                return;
            }

            UpdateStatusMessage();
            if (logChange && logStateChanges)
            {
                Debug.Log(
                    "[XrSuperResolutionController] preset -> "
                    + GetPresetLabel(_currentPreset)
                    + " (SR currently disabled)");
            }
        }

        private void SetSuperResolutionEnabled(bool enabled, bool logChange)
        {
            _isSuperResolutionEnabled = enabled;
            _adaptiveTimer = 0.0f;
            SetCameraDynamicResolution(enabled);

            if (enabled)
            {
                ApplyPipelineScale(GetPresetRenderScale(_currentPreset), logChange);
                return;
            }

            DisableSuperResolution(logChange);
        }

        private void ApplyPipelineScale(float renderScale, bool logChange)
        {
            UniversalRenderPipelineAsset asset = ResolvePipelineAsset();
            if (asset == null)
            {
                _statusMessage = "SR: unavailable (URP asset missing; run Tools/Pano2StereoVR/Setup XR Super Resolution (URP FSR))";
                if (logChange)
                {
                    Debug.LogWarning("[XrSuperResolutionController] " + _statusMessage);
                }
                return;
            }

            CaptureOriginalState(asset);
            float clampedScale = Mathf.Clamp(renderScale, minimumAdaptiveScale, maximumAdaptiveScale);
            if (clampedScale > 1.001f)
            {
                asset.upscalingFilter = UpscalingFilterSelection.Auto;
                asset.fsrOverrideSharpness = false;
            }
            else
            {
                asset.upscalingFilter = UpscalingFilterSelection.FSR;
                asset.fsrOverrideSharpness = true;
                asset.fsrSharpness = Mathf.Clamp01(fsrSharpness);
            }
            asset.renderScale = clampedScale;
            UpdateStatusMessage();

            if (!logChange || !logStateChanges)
            {
                return;
            }

            Debug.Log("[XrSuperResolutionController] " + _statusMessage);
        }

        private void DisableSuperResolution(bool logChange)
        {
            UniversalRenderPipelineAsset asset = ResolvePipelineAsset();
            if (asset == null)
            {
                _statusMessage = "SR: OFF (URP asset missing)";
                if (logChange)
                {
                    Debug.LogWarning("[XrSuperResolutionController] " + _statusMessage);
                }
                return;
            }

            CaptureOriginalState(asset);
            asset.renderScale = 1.0f;
            asset.upscalingFilter = UpscalingFilterSelection.Auto;
            asset.fsrOverrideSharpness = false;
            UpdateStatusMessage();

            if (logChange && logStateChanges)
            {
                Debug.Log("[XrSuperResolutionController] " + _statusMessage);
            }
        }

        private void UpdateAdaptiveRenderScale()
        {
            if (!adaptiveRenderScale || !_isSuperResolutionEnabled)
            {
                return;
            }

            UniversalRenderPipelineAsset asset = ResolvePipelineAsset();
            if (asset == null)
            {
                return;
            }

            _adaptiveTimer += Time.unscaledDeltaTime;
            if (_adaptiveTimer < adaptiveCheckIntervalSeconds)
            {
                return;
            }

            _adaptiveTimer = 0.0f;
            float nextScale = asset.renderScale;
            if (_smoothedFps < adaptiveTargetFps - 3.0f)
            {
                nextScale -= adaptiveScaleStep;
            }
            else if (_smoothedFps > adaptiveTargetFps + 6.0f)
            {
                nextScale += adaptiveScaleStep;
            }
            else
            {
                UpdateStatusMessage();
                return;
            }

            float clampedScale = Mathf.Clamp(nextScale, minimumAdaptiveScale, maximumAdaptiveScale);
            if (Mathf.Abs(clampedScale - asset.renderScale) < 0.001f)
            {
                UpdateStatusMessage();
                return;
            }

            asset.renderScale = clampedScale;
            UpdateStatusMessage();

            if (logStateChanges)
            {
                Debug.Log("[XrSuperResolutionController] adaptive scale -> " + clampedScale.ToString("F2"));
            }
        }

        private void HandleHotkeys()
        {
            if (!enablePresetHotkeys)
            {
                return;
            }

            if (Input.GetKeyDown(toggleSuperResolutionKey))
            {
                SetSuperResolutionEnabled(!_isSuperResolutionEnabled);
                return;
            }

            if (Input.GetKeyDown(lowerPresetKey))
            {
                ApplyPreset(GetNeighborPreset(-1));
            }

            if (Input.GetKeyDown(higherPresetKey))
            {
                ApplyPreset(GetNeighborPreset(1));
            }

            if (Input.GetKeyDown(toggleAdaptiveKey))
            {
                adaptiveRenderScale = !adaptiveRenderScale;
                _adaptiveTimer = 0.0f;
                UpdateStatusMessage();
                if (logStateChanges)
                {
                    Debug.Log(
                        "[XrSuperResolutionController] adaptive render scale -> "
                        + (adaptiveRenderScale ? "enabled" : "disabled"));
                }
            }
        }

        private void UpdateSmoothedFps()
        {
            float dt = Time.unscaledDeltaTime;
            if (dt <= 0.00001f)
            {
                return;
            }

            float instantFps = 1.0f / dt;
            if (_smoothedFps <= 0.0f)
            {
                _smoothedFps = instantFps;
                return;
            }

            _smoothedFps = Mathf.Lerp(_smoothedFps, instantFps, 0.1f);
        }

        private void CaptureOriginalState(UniversalRenderPipelineAsset asset)
        {
            if (_hasOriginalState || asset == null)
            {
                return;
            }

            _originalRenderScale = asset.renderScale;
            _originalUpscalingFilter = asset.upscalingFilter;
            _originalFsrOverrideSharpness = asset.fsrOverrideSharpness;
            _originalFsrSharpness = asset.fsrSharpness;
            _hasOriginalState = true;
        }

        private void ResolveTargetCamera()
        {
            if (targetCamera == null)
            {
                targetCamera = Camera.main;
            }
        }

        private void SetCameraDynamicResolution(bool enabled)
        {
            ResolveTargetCamera();
            if (targetCamera == null)
            {
                return;
            }

            targetCamera.allowDynamicResolution = enabled;
        }

        private void UpdateStatusMessage()
        {
            UniversalRenderPipelineAsset asset = ResolvePipelineAsset();
            if (asset == null)
            {
                _statusMessage = "SR: unavailable";
                return;
            }

            if (!_isSuperResolutionEnabled)
            {
                _statusMessage =
                    "SR: OFF native 1.00x eye " + NativeEyeWidth + "x" + NativeEyeHeight;
                return;
            }

            string modeLabel = asset.renderScale > 1.001f ? "SS" : "FSR";
            string adaptiveSuffix = adaptiveRenderScale ? " adaptive" : string.Empty;
            string fsrSupportSuffix = SystemInfo.graphicsShaderLevel >= 45 ? string.Empty : " fallback";
            _statusMessage =
                "SR: " + modeLabel + " " + GetPresetLabel(_currentPreset)
                + " " + asset.renderScale.ToString("F2")
                + "x eye " + ScaledEyeWidth + "x" + ScaledEyeHeight
                + adaptiveSuffix
                + fsrSupportSuffix;
        }

        private SuperResolutionPreset GetNeighborPreset(int direction)
        {
            int currentIndex = GetPresetOrderIndex(_currentPreset);
            int nextIndex = Mathf.Clamp(currentIndex + direction, 0, PresetOrder.Length - 1);
            return PresetOrder[nextIndex];
        }

        private static int GetPresetOrderIndex(SuperResolutionPreset preset)
        {
            for (int i = 0; i < PresetOrder.Length; i++)
            {
                if (PresetOrder[i] == preset)
                {
                    return i;
                }
            }

            return 1;
        }

        private static float GetPresetRenderScale(SuperResolutionPreset preset)
        {
            switch (preset)
            {
                case SuperResolutionPreset.NativeSharpness:
                    return 1.00f;
                case SuperResolutionPreset.Quality:
                    return 0.92f;
                case SuperResolutionPreset.Balanced:
                    return 0.85f;
                case SuperResolutionPreset.Performance:
                    return 0.77f;
                case SuperResolutionPreset.Supersample115:
                    return 1.15f;
                case SuperResolutionPreset.Supersample130:
                    return 1.30f;
                case SuperResolutionPreset.Supersample150:
                    return 1.50f;
                case SuperResolutionPreset.Supersample170:
                    return 1.70f;
                case SuperResolutionPreset.Supersample200:
                    return 2.00f;
                default:
                    return 0.85f;
            }
        }

        private static string GetPresetLabel(SuperResolutionPreset preset)
        {
            switch (preset)
            {
                case SuperResolutionPreset.NativeSharpness:
                    return "Native";
                case SuperResolutionPreset.Quality:
                    return "Quality";
                case SuperResolutionPreset.Balanced:
                    return "Balanced";
                case SuperResolutionPreset.Performance:
                    return "Performance";
                case SuperResolutionPreset.Supersample115:
                    return "Supersample115";
                case SuperResolutionPreset.Supersample130:
                    return "Supersample130";
                case SuperResolutionPreset.Supersample150:
                    return "Supersample150";
                case SuperResolutionPreset.Supersample170:
                    return "Supersample170";
                case SuperResolutionPreset.Supersample200:
                    return "Supersample200";
                default:
                    return "Balanced";
            }
        }

        private static UniversalRenderPipelineAsset ResolvePipelineAsset()
        {
            UniversalRenderPipelineAsset current = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (current != null)
            {
                return current;
            }

            UniversalRenderPipelineAsset quality = QualitySettings.renderPipeline as UniversalRenderPipelineAsset;
            if (quality != null)
            {
                return quality;
            }

            return GraphicsSettings.defaultRenderPipeline as UniversalRenderPipelineAsset;
        }

        private static int GetNativeEyeWidth()
        {
            int xrWidth = XRSettings.eyeTextureWidth;
            if (xrWidth > 0)
            {
                return xrWidth;
            }

            return Mathf.Max(1, Screen.width);
        }

        private static int GetNativeEyeHeight()
        {
            int xrHeight = XRSettings.eyeTextureHeight;
            if (xrHeight > 0)
            {
                return xrHeight;
            }

            return Mathf.Max(1, Screen.height);
        }
    }
}
