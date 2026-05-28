using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace Pano2StereoVR.Tests
{
    public sealed class ExperimentControllerModeLabelTests
    {
        [TestCase(1, "Baseline")]
        [TestCase(2, "Pose-agnostic")]
        [TestCase(3, "Pose-aware")]
        public void ModeOverlayLabelUsesPaperConditionNames(int mode, string expected)
        {
            Type controllerType = Type.GetType("Pano2StereoVR.ExperimentController, Assembly-CSharp");
            Assert.NotNull(controllerType, "ExperimentController runtime type must be available.");

            MethodInfo method = controllerType.GetMethod(
                "GetModeOverlayLabel",
                BindingFlags.Static | BindingFlags.NonPublic
            );
            Assert.NotNull(method, "ExperimentController must expose GetModeOverlayLabel.");

            object result = method.Invoke(null, new object[] { mode });

            Assert.AreEqual(expected, result);
        }

        [Test]
        public void ModeButtonOrderPutsBaselineFirst()
        {
            Type controllerType = Type.GetType("Pano2StereoVR.ExperimentController, Assembly-CSharp");
            Assert.NotNull(controllerType, "ExperimentController runtime type must be available.");

            MethodInfo method = controllerType.GetMethod(
                "GetModeButtonOrder",
                BindingFlags.Static | BindingFlags.NonPublic
            );
            Assert.NotNull(method, "ExperimentController must expose GetModeButtonOrder.");

            object result = method.Invoke(null, Array.Empty<object>());

            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, (int[])result);
        }

        [Test]
        public void RuntimeResolutionSpecUsesStereoShmWidthForMatching()
        {
            Type controllerType = Type.GetType("Pano2StereoVR.ExperimentController, Assembly-CSharp");
            Assert.NotNull(controllerType, "ExperimentController runtime type must be available.");

            MethodInfo method = controllerType.GetMethod(
                "DoesShmResolutionMatchPreset",
                BindingFlags.Static | BindingFlags.NonPublic
            );
            Assert.NotNull(method, "ExperimentController must expose DoesShmResolutionMatchPreset.");

            Type presetType = controllerType.GetNestedType(
                "RuntimeResolutionPreset",
                BindingFlags.NonPublic
            );
            Assert.NotNull(presetType, "RuntimeResolutionPreset enum missing.");
            object p1080 = Enum.Parse(presetType, "P1080");

            Assert.AreEqual(true, method.Invoke(null, new[] { p1080, 4368, 1092 }));
            Assert.AreEqual(false, method.Invoke(null, new[] { p1080, 2184, 1092 }));
        }

        [Test]
        public void ShmPreviewDefaultRatioUsesQuarterAreaPreview()
        {
            Type controllerType = Type.GetType("Pano2StereoVR.ExperimentController, Assembly-CSharp");
            Assert.NotNull(controllerType, "ExperimentController runtime type must be available.");

            GameObject host = new GameObject("experiment-controller-preview-ratio-test");
            Component controller = host.AddComponent(controllerType);

            try
            {
                FieldInfo ratioField = controllerType.GetField(
                    "shmPreviewWidthRatio",
                    BindingFlags.Instance | BindingFlags.NonPublic
                );
                Assert.NotNull(ratioField, "ExperimentController must expose shmPreviewWidthRatio.");

                Assert.AreEqual(0.20f, (float)ratioField.GetValue(controller), 0.001f);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void ShmPreviewRectAtDefaultRatioIsQuarterOfPreviousPreviewArea()
        {
            Type controllerType = Type.GetType("Pano2StereoVR.ExperimentController, Assembly-CSharp");
            Assert.NotNull(controllerType, "ExperimentController runtime type must be available.");

            MethodInfo method = controllerType.GetMethod(
                "CalculateShmPreviewRect",
                BindingFlags.Static | BindingFlags.NonPublic
            );
            Assert.NotNull(method, "ExperimentController must expose CalculateShmPreviewRect.");

            Rect defaultPreview = (Rect)method.Invoke(null, new object[] { 1920f, 1080f, 4368f, 1092f, 0.20f });
            Rect previousPreview = (Rect)method.Invoke(null, new object[] { 1920f, 1080f, 4368f, 1092f, 0.40f });

            Assert.AreEqual(0.5f, defaultPreview.width / previousPreview.width, 0.01f);
            Assert.AreEqual(0.25f, (defaultPreview.width * defaultPreview.height)
                / (previousPreview.width * previousPreview.height), 0.02f);
        }

        [Test]
        public void BottomPerformanceRectLeavesRightSideForPreview()
        {
            Type controllerType = Type.GetType("Pano2StereoVR.ExperimentController, Assembly-CSharp");
            Assert.NotNull(controllerType, "ExperimentController runtime type must be available.");

            MethodInfo previewMethod = controllerType.GetMethod(
                "CalculateShmPreviewRect",
                BindingFlags.Static | BindingFlags.NonPublic
            );
            Assert.NotNull(previewMethod, "ExperimentController must expose CalculateShmPreviewRect.");
            MethodInfo performanceMethod = controllerType.GetMethod(
                "CalculatePerformanceOverlayRect",
                BindingFlags.Static | BindingFlags.NonPublic
            );
            Assert.NotNull(performanceMethod, "ExperimentController must expose CalculatePerformanceOverlayRect.");

            Rect preview = (Rect)previewMethod.Invoke(null, new object[] { 1920f, 1080f, 4368f, 1092f, 0.20f });
            Rect performance = (Rect)performanceMethod.Invoke(null, new object[] { 1920f, 1080f, preview });

            Assert.AreEqual(20f, 1920f - preview.xMax, 0.01f);
            Assert.AreEqual(20f, 1080f - preview.yMax, 0.01f);
            Assert.LessOrEqual(performance.xMax, preview.x - 12f);
            Assert.AreEqual(12f, performance.x, 0.01f);
        }

        [Test]
        public void PerformanceOverlayTextIsSingleLineAndOmitsReceiverDetails()
        {
            Type controllerType = Type.GetType("Pano2StereoVR.ExperimentController, Assembly-CSharp");
            Assert.NotNull(controllerType, "ExperimentController runtime type must be available.");

            GameObject host = new GameObject("experiment-controller-performance-overlay-test");
            Component controller = host.AddComponent(controllerType);

            try
            {
                MethodInfo method = controllerType.GetMethod(
                    "GetPerformanceOverlayText",
                    BindingFlags.Instance | BindingFlags.NonPublic
                );
                Assert.NotNull(method, "ExperimentController must expose GetPerformanceOverlayText.");

                string text = (string)method.Invoke(controller, Array.Empty<object>());

                Assert.False(text.Contains("\n"));
                StringAssert.StartsWith("Performance:", text);
                Assert.False(text.Contains("Receiver"));
                Assert.False(text.Contains("opened"));
                Assert.False(text.Contains("seq"));
                Assert.False(text.Contains("busy"));
                Assert.False(text.Contains("torn"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }
    }
}
