using System;
using System.Reflection;
using NUnit.Framework;

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
    }
}
