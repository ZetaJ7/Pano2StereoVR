using System.Collections.Generic;
using UnityEditor;
using UnityEditor.XR.Management;
using UnityEngine;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;

namespace Pano2StereoVR.Editor
{
    [InitializeOnLoad]
    public static class OpenXRBootstrap
    {
        private const string SettingsStorePath = "Assets/XR/Settings/XRGeneralSettingsPerBuildTarget.asset";
        private const string OpenXRLoaderPath = "Assets/XR/Loaders/OpenXRLoader.asset";
        private const string OpenXRSettingsPath = "Assets/XR/Settings/OpenXR Package Settings.asset";
        private const string OpenXRSettingsKey = "com.unity.xr.openxr.settings4";
        private const string MenuPath = "Tools/Pano2StereoVR/Fix OpenXR Setup";

        static OpenXRBootstrap()
        {
            EditorApplication.delayCall += EnsureOpenXRConfiguredDelayed;
        }

        private static void EnsureOpenXRConfiguredDelayed()
        {
            EnsureOpenXRConfigured(false);
        }

        [MenuItem(MenuPath)]
        private static void EnsureOpenXRConfiguredFromMenu()
        {
            EnsureOpenXRConfigured(true);
        }

        private static void EnsureOpenXRConfigured(bool forceLog = false)
        {
            bool changed = false;
            XRGeneralSettingsPerBuildTarget settingsPerBuildTarget =
                EnsurePerBuildTargetSettings(ref changed);
            if (settingsPerBuildTarget == null)
            {
                if (forceLog)
                {
                    Debug.LogError("[OpenXRBootstrap] Unable to find/create XR settings store.");
                }
                return;
            }

            XRManagerSettings managerSettings =
                EnsureStandaloneManagerSettings(settingsPerBuildTarget, ref changed);
            if (managerSettings == null)
            {
                if (forceLog)
                {
                    Debug.LogError("[OpenXRBootstrap] Unable to initialize Standalone XR manager.");
                }
                return;
            }

            OpenXRLoader openXRLoader = AssetDatabase.LoadAssetAtPath<OpenXRLoader>(OpenXRLoaderPath);
            if (openXRLoader == null)
            {
                if (forceLog)
                {
                    Debug.LogError("[OpenXRBootstrap] OpenXRLoader asset is missing.");
                }
                return;
            }

            if (managerSettings.activeLoaders.Count != 1 || managerSettings.activeLoaders[0] != openXRLoader)
            {
                managerSettings.TrySetLoaders(new List<XRLoader> { openXRLoader });
                changed = true;
            }

            if (!managerSettings.automaticLoading)
            {
                managerSettings.automaticLoading = true;
                changed = true;
            }

            if (!managerSettings.automaticRunning)
            {
                managerSettings.automaticRunning = true;
                changed = true;
            }

            XRGeneralSettings standaloneGeneralSettings =
                settingsPerBuildTarget.SettingsForBuildTarget(BuildTargetGroup.Standalone);
            if (standaloneGeneralSettings != null && !standaloneGeneralSettings.InitManagerOnStart)
            {
                standaloneGeneralSettings.InitManagerOnStart = true;
                EditorUtility.SetDirty(standaloneGeneralSettings);
                changed = true;
            }

            if (EnsureOpenXRSettingsConfigObject())
            {
                changed = true;
            }

            if (changed)
            {
                EditorUtility.SetDirty(settingsPerBuildTarget);
                EditorUtility.SetDirty(managerSettings);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }

            if (changed || forceLog)
            {
                Debug.Log("[OpenXRBootstrap] OpenXR setup verified for Standalone.");
            }
        }

        private static XRGeneralSettingsPerBuildTarget EnsurePerBuildTargetSettings(ref bool changed)
        {
            if (EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.k_SettingsKey,
                out XRGeneralSettingsPerBuildTarget settingsPerBuildTarget) &&
                settingsPerBuildTarget != null)
            {
                return settingsPerBuildTarget;
            }

            settingsPerBuildTarget =
                AssetDatabase.LoadAssetAtPath<XRGeneralSettingsPerBuildTarget>(SettingsStorePath);
            if (settingsPerBuildTarget == null)
            {
                settingsPerBuildTarget = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
                AssetDatabase.CreateAsset(settingsPerBuildTarget, SettingsStorePath);
                changed = true;
            }

            EditorBuildSettings.AddConfigObject(
                XRGeneralSettings.k_SettingsKey,
                settingsPerBuildTarget,
                true);
            changed = true;

            return settingsPerBuildTarget;
        }

        private static XRManagerSettings EnsureStandaloneManagerSettings(
            XRGeneralSettingsPerBuildTarget settingsPerBuildTarget,
            ref bool changed)
        {
            if (!settingsPerBuildTarget.HasSettingsForBuildTarget(BuildTargetGroup.Standalone))
            {
                settingsPerBuildTarget.CreateDefaultSettingsForBuildTarget(BuildTargetGroup.Standalone);
                changed = true;
            }

            if (!settingsPerBuildTarget.HasManagerSettingsForBuildTarget(BuildTargetGroup.Standalone))
            {
                settingsPerBuildTarget.CreateDefaultManagerSettingsForBuildTarget(
                    BuildTargetGroup.Standalone);
                changed = true;
            }

            return settingsPerBuildTarget.ManagerSettingsForBuildTarget(BuildTargetGroup.Standalone);
        }

        private static bool EnsureOpenXRSettingsConfigObject()
        {
            if (EditorBuildSettings.TryGetConfigObject<UnityEngine.Object>(
                    OpenXRSettingsKey,
                    out UnityEngine.Object currentObject) &&
                currentObject is IPackageSettings)
            {
                return false;
            }

            UnityEngine.Object packageSettingsObject = AssetDatabase.LoadMainAssetAtPath(OpenXRSettingsPath);
            if (packageSettingsObject == null)
            {
                return false;
            }

            if (!(packageSettingsObject is IPackageSettings))
            {
                Debug.LogError(
                    "[OpenXRBootstrap] OpenXR settings config object is not a package settings asset.");
                return false;
            }

            EditorBuildSettings.AddConfigObject(OpenXRSettingsKey, packageSettingsObject, true);
            return true;
        }
    }
}
