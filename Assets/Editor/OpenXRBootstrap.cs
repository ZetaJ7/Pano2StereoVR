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
        private enum RuntimeTarget
        {
            DesktopScreen,
            VrOpenXr,
        }

        private const string SettingsStorePath = "Assets/XR/Settings/XRGeneralSettingsPerBuildTarget.asset";
        private const string OpenXRLoaderPath = "Assets/XR/Loaders/OpenXRLoader.asset";
        private const string OpenXRSettingsPath = "Assets/XR/Settings/OpenXR Package Settings.asset";
        private const string OpenXRSettingsKey = "com.unity.xr.openxr.settings4";
        private const string RuntimeTargetPrefKey = "Pano2StereoVR.OpenXRBootstrap.RuntimeTarget";
        private const string FixOpenXRMenuPath = "Tools/Pano2StereoVR/Fix OpenXR Setup";
        private const string DesktopScreenMenuPath = "Tools/Pano2StereoVR/Runtime Target/Desktop Screen";
        private const string VrOpenXrMenuPath = "Tools/Pano2StereoVR/Runtime Target/VR OpenXR";

        static OpenXRBootstrap()
        {
            EditorApplication.delayCall += EnsureOpenXRConfiguredDelayed;
        }

        private static void EnsureOpenXRConfiguredDelayed()
        {
            ApplyRuntimeTarget(GetRuntimeTarget(), false);
        }

        [MenuItem(FixOpenXRMenuPath)]
        private static void EnsureOpenXRConfiguredFromMenu()
        {
            SetRuntimeTarget(RuntimeTarget.VrOpenXr, true);
        }

        [MenuItem(DesktopScreenMenuPath)]
        private static void SelectDesktopScreen()
        {
            SetRuntimeTarget(RuntimeTarget.DesktopScreen, true);
        }

        [MenuItem(DesktopScreenMenuPath, true)]
        private static bool ValidateDesktopScreen()
        {
            Menu.SetChecked(DesktopScreenMenuPath, GetRuntimeTarget() == RuntimeTarget.DesktopScreen);
            return true;
        }

        [MenuItem(VrOpenXrMenuPath)]
        private static void SelectVrOpenXr()
        {
            SetRuntimeTarget(RuntimeTarget.VrOpenXr, true);
        }

        [MenuItem(VrOpenXrMenuPath, true)]
        private static bool ValidateVrOpenXr()
        {
            Menu.SetChecked(VrOpenXrMenuPath, GetRuntimeTarget() == RuntimeTarget.VrOpenXr);
            return true;
        }

        private static void SetRuntimeTarget(RuntimeTarget target, bool forceLog)
        {
            EditorPrefs.SetString(RuntimeTargetPrefKey, target.ToString());
            ApplyRuntimeTarget(target, forceLog);
        }

        private static RuntimeTarget GetRuntimeTarget()
        {
            string rawValue = EditorPrefs.GetString(
                RuntimeTargetPrefKey,
                RuntimeTarget.VrOpenXr.ToString()
            );
            if (System.Enum.TryParse(rawValue, out RuntimeTarget target))
            {
                return target;
            }
            return RuntimeTarget.VrOpenXr;
        }

        private static void ApplyRuntimeTarget(RuntimeTarget target, bool forceLog = false)
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

            if (target == RuntimeTarget.VrOpenXr && !EnsureOpenXRLoader(managerSettings, ref changed, forceLog))
            {
                return;
            }

            bool autoStartXr = ShouldAutoStartXr(target);
            ApplyManagerAutomaticStartup(managerSettings, autoStartXr, ref changed);
            XRGeneralSettings standaloneGeneralSettings =
                settingsPerBuildTarget.SettingsForBuildTarget(BuildTargetGroup.Standalone);
            if (standaloneGeneralSettings != null &&
                standaloneGeneralSettings.InitManagerOnStart != autoStartXr)
            {
                standaloneGeneralSettings.InitManagerOnStart = autoStartXr;
                EditorUtility.SetDirty(standaloneGeneralSettings);
                changed = true;
            }

            if (target == RuntimeTarget.VrOpenXr && EnsureOpenXRSettingsConfigObject())
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
                Debug.Log(
                    "[OpenXRBootstrap] Runtime target set to " + target
                    + " (XR auto-start=" + autoStartXr + ")."
                );
            }
        }

        private static bool ShouldAutoStartXr(RuntimeTarget target)
        {
            return target == RuntimeTarget.VrOpenXr;
        }

        private static bool EnsureOpenXRLoader(
            XRManagerSettings managerSettings,
            ref bool changed,
            bool forceLog)
        {
            OpenXRLoader openXRLoader = AssetDatabase.LoadAssetAtPath<OpenXRLoader>(OpenXRLoaderPath);
            if (openXRLoader == null)
            {
                if (forceLog)
                {
                    Debug.LogError("[OpenXRBootstrap] OpenXRLoader asset is missing.");
                }
                return false;
            }

            if (managerSettings.activeLoaders.Count == 1 && managerSettings.activeLoaders[0] == openXRLoader)
            {
                return true;
            }

            managerSettings.TrySetLoaders(new List<XRLoader> { openXRLoader });
            changed = true;
            return true;
        }

        private static void ApplyManagerAutomaticStartup(
            XRManagerSettings managerSettings,
            bool autoStartXr,
            ref bool changed)
        {
            if (managerSettings.automaticLoading != autoStartXr)
            {
                managerSettings.automaticLoading = autoStartXr;
                changed = true;
            }

            if (managerSettings.automaticRunning != autoStartXr)
            {
                managerSettings.automaticRunning = autoStartXr;
                changed = true;
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
