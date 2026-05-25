using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Pano2StereoVR.Editor
{
    public static class XrSuperResolutionSetup
    {
        private const string MenuPath = "Tools/Pano2StereoVR/Setup XR Super Resolution (URP FSR)";
        private const string SettingsFolder = "Assets/Settings";
        private const string RenderingFolder = "Assets/Settings/Rendering";
        private const string PipelineAssetPath = "Assets/Settings/Rendering/Pano2StereoVR_XR_FSR.asset";
        private const float DefaultRenderScale = 1.00f;
        private const float DefaultFsrSharpness = 0.82f;

        [MenuItem(MenuPath)]
        private static void Setup()
        {
            EnsureFolder("Assets", "Settings");
            EnsureFolder(SettingsFolder, "Rendering");

            UniversalRenderPipelineAsset pipelineAsset = ResolveOrCreatePipelineAsset();
            if (pipelineAsset == null)
            {
                Debug.LogError("[XrSuperResolutionSetup] Failed to create or locate a URP asset.");
                return;
            }

            ConfigurePipelineAsset(pipelineAsset);
            GraphicsSettings.defaultRenderPipeline = pipelineAsset;
            QualitySettings.renderPipeline = pipelineAsset;

            EditorUtility.SetDirty(pipelineAsset);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log(
                "[XrSuperResolutionSetup] URP native baseline configured with renderScale="
                + pipelineAsset.renderScale.ToString("F2")
                + " at " + AssetDatabase.GetAssetPath(pipelineAsset));
        }

        private static UniversalRenderPipelineAsset ResolveOrCreatePipelineAsset()
        {
            UniversalRenderPipelineAsset current = ResolveCurrentPipelineAsset();
            if (current != null)
            {
                return current;
            }

            UniversalRenderPipelineAsset assetAtPath =
                AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelineAssetPath);
            if (assetAtPath != null)
            {
                return assetAtPath;
            }

            ScriptableRendererData rendererData = CreateRendererAsset(PipelineAssetPath);
            if (rendererData == null)
            {
                return null;
            }

            UniversalRenderPipelineAsset newAsset = UniversalRenderPipelineAsset.Create(rendererData);
            AssetDatabase.CreateAsset(newAsset, PipelineAssetPath);
            return newAsset;
        }

        private static void ConfigurePipelineAsset(UniversalRenderPipelineAsset pipelineAsset)
        {
            if (pipelineAsset == null)
            {
                return;
            }

            pipelineAsset.renderScale = DefaultRenderScale;
            pipelineAsset.upscalingFilter = UpscalingFilterSelection.Auto;
            pipelineAsset.fsrOverrideSharpness = false;
            pipelineAsset.fsrSharpness = DefaultFsrSharpness;
        }

        private static UniversalRenderPipelineAsset ResolveCurrentPipelineAsset()
        {
            UniversalRenderPipelineAsset current =
                GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
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

        private static ScriptableRendererData CreateRendererAsset(string pipelineAssetPath)
        {
            MethodInfo createRendererAsset =
                typeof(UniversalRenderPipelineAsset).GetMethod(
                    "CreateRendererAsset",
                    BindingFlags.Static | BindingFlags.NonPublic);
            if (createRendererAsset == null)
            {
                return null;
            }

            object rendererData = createRendererAsset.Invoke(
                null,
                new object[] { pipelineAssetPath, RendererType.UniversalRenderer, true, "Renderer" });
            return rendererData as ScriptableRendererData;
        }

        private static void EnsureFolder(string parentFolder, string childFolderName)
        {
            string fullPath = Path.Combine(parentFolder, childFolderName).Replace("\\", "/");
            if (AssetDatabase.IsValidFolder(fullPath))
            {
                return;
            }

            AssetDatabase.CreateFolder(parentFolder, childFolderName);
        }
    }
}
