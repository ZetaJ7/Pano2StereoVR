using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEditor.XR.Management;
using UnityEngine;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features;
using UnityEngine.XR.OpenXR.Features.Interactions;

namespace Pano2StereoVR.Study.Editor
{
    /// <summary>
    /// Command-line checks for the study worktree (GUI editor, no -batchmode on this machine):
    ///   -executeMethod Pano2StereoVR.Study.Editor.StudyCheckBuild.RunAll
    /// runs the study EditMode tests, enables the Oculus Touch interaction profile (Pico controllers via
    /// SteamVR), builds a Windows desktop player of StudyScene with XR start-up off (restored afterwards),
    /// writes Builds/check_report.txt and exits (code 0 when tests passed and the build succeeded).
    /// </summary>
    public static class StudyCheckBuild
    {
        private const string BuildPath = "Builds/DesktopCheck/Pano2StereoStudy.exe";
        private const string ReportPath = "Builds/check_report.txt";
        private static readonly StringBuilder Report = new StringBuilder();
        private static bool _testsOk;

        public static void RunAll()
        {
            Directory.CreateDirectory("Builds");
            Line("RunAll start " + DateTime.Now.ToString("s") + ", Unity " + Application.unityVersion);
            var api = ScriptableObject.CreateInstance<TestRunnerApi>();
            api.RegisterCallbacks(new Callbacks());
            var filter = new Filter { testMode = TestMode.EditMode, assemblyNames = new[] { "Pano2StereoVR.Study.Tests" } };
            api.Execute(new ExecutionSettings(filter));
        }

        private sealed class Callbacks : ICallbacks
        {
            public void RunStarted(ITestAdaptor testsToRun)
            {
                Line("tests to run: " + testsToRun.TestCaseCount);
            }

            public void RunFinished(ITestResultAdaptor result)
            {
                _testsOk = result.FailCount == 0 && result.PassCount > 0;
                Line("tests finished: pass " + result.PassCount + ", fail " + result.FailCount + ", skip " + result.SkipCount
                    + ", inconclusive " + result.InconclusiveCount + ", state " + result.ResultState);
                EditorApplication.delayCall += AfterTests;
            }

            public void TestStarted(ITestAdaptor test)
            {
            }

            public void TestFinished(ITestResultAdaptor result)
            {
                if (!result.HasChildren)
                {
                    Line("  " + result.TestStatus + "  " + result.Test.FullName
                        + (string.IsNullOrEmpty(result.Message) ? "" : "  :: " + result.Message.Replace("\n", " ")));
                }
            }
        }

        private static void AfterTests()
        {
            int code = 1;
            try
            {
                EnableTouchProfile();
                bool built = BuildDesktop();
                code = _testsOk && built ? 0 : 1;
            }
            catch (Exception e)
            {
                Line("exception: " + e);
            }
            Line("exit code " + code);
            File.WriteAllText(ReportPath, Report.ToString());
            EditorApplication.Exit(code);
        }

        private static void EnableTouchProfile()
        {
            OpenXRSettings settings = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Standalone);
            if (settings == null)
            {
                Line("OpenXR settings for Standalone not found");
                return;
            }
            var touch = settings.GetFeature<OculusTouchControllerProfile>();
            if (touch == null)
            {
                Line("Oculus Touch Controller Profile feature not found");
                return;
            }
            if (!touch.enabled)
            {
                touch.enabled = true;
                EditorUtility.SetDirty(touch);
                EditorUtility.SetDirty(settings);
                AssetDatabase.SaveAssets();
                Line("enabled Oculus Touch Controller Profile (Standalone)");
            }
            else
            {
                Line("Oculus Touch Controller Profile already enabled");
            }
            foreach (var f in settings.GetFeatures<OpenXRInteractionFeature>())
            {
                Line("  interaction profile " + f.GetType().Name + " enabled=" + f.enabled);
            }
        }

        private static bool BuildDesktop()
        {
            XRGeneralSettings xr = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(BuildTargetGroup.Standalone);
            bool restore = xr != null && xr.InitManagerOnStart;
            if (xr != null)
            {
                xr.InitManagerOnStart = false;
                EditorUtility.SetDirty(xr);
                AssetDatabase.SaveAssets();
            }
            Line("XR InitManagerOnStart for the build: false (was " + restore + ")");
            try
            {
                var options = new BuildPlayerOptions
                {
                    scenes = new[] { StudySceneBuilder.ScenePath },
                    locationPathName = BuildPath,
                    target = BuildTarget.StandaloneWindows64,
                    targetGroup = BuildTargetGroup.Standalone,
                    options = BuildOptions.None,
                };
                BuildReport report = BuildPipeline.BuildPlayer(options);
                Line("build " + report.summary.result + ", " + report.summary.totalErrors + " errors, "
                    + report.summary.totalWarnings + " warnings, " + report.summary.totalTime + ", " + Path.GetFullPath(BuildPath));
                return report.summary.result == BuildResult.Succeeded;
            }
            finally
            {
                if (xr != null && restore)
                {
                    xr.InitManagerOnStart = true;
                    EditorUtility.SetDirty(xr);
                    AssetDatabase.SaveAssets();
                    Line("XR InitManagerOnStart restored to true");
                }
            }
        }

        private static void Line(string s)
        {
            Report.AppendLine(s);
            Debug.Log("[StudyCheckBuild] " + s);
        }
    }
}
