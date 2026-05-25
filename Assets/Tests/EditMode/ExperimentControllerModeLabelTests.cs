using System;
using System.Reflection;
using NUnit.Framework;

namespace Pano2StereoVR.Tests
{
    public sealed class ExperimentControllerModeLabelTests
    {
        [TestCase(1, "Mono")]
        [TestCase(2, "Pose-agnostic")]
        [TestCase(3, "Pose-aware")]
        [TestCase(4, "Baseline")]
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
    }
}
