using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace Pano2StereoVR.Tests
{
    public sealed class HeadPoseTrackerDesktopModeTests
    {
        [Test]
        public void AutoEnableMouseLookWithoutXrEnablesMouseLookWhenXrStartupIsDisabled()
        {
            Type trackerType = Type.GetType("Pano2StereoVR.HeadPoseTracker, Assembly-CSharp");
            Assert.NotNull(trackerType, "HeadPoseTracker runtime type must be available.");

            MethodInfo method = GetShouldAutoEnableMethod(trackerType);

            object result = method.Invoke(null, new object[] { true, true, true, false });

            Assert.AreEqual(true, result);
        }

        [Test]
        public void AutoEnableMouseLookWithoutXrKeepsMouseLookOffWhenXrStartupIsEnabled()
        {
            Type trackerType = Type.GetType("Pano2StereoVR.HeadPoseTracker, Assembly-CSharp");
            Assert.NotNull(trackerType, "HeadPoseTracker runtime type must be available.");

            MethodInfo method = GetShouldAutoEnableMethod(trackerType);

            object result = method.Invoke(null, new object[] { true, true, true, true });

            Assert.AreEqual(false, result);
        }

        [Test]
        public void DesktopMoveInputUsesCameraRelativeWASDAxesWithoutVerticalDrift()
        {
            Type trackerType = Type.GetType("Pano2StereoVR.HeadPoseTracker, Assembly-CSharp");
            Assert.NotNull(trackerType, "HeadPoseTracker runtime type must be available.");

            MethodInfo method = trackerType.GetMethod(
                "CalculateDesktopMoveDelta",
                BindingFlags.Static | BindingFlags.NonPublic
            );
            Assert.NotNull(method, "CalculateDesktopMoveDelta method missing.");

            Quaternion yawRight = Quaternion.Euler(0f, 90f, 0f);
            Vector3 forward = (Vector3)method.Invoke(
                null,
                new object[] { yawRight, 1f, 0f, 2f }
            );
            Vector3 strafeLeft = (Vector3)method.Invoke(
                null,
                new object[] { yawRight, 0f, -1f, 2f }
            );

            Assert.That(forward.x, Is.EqualTo(2f).Within(0.001f));
            Assert.That(forward.y, Is.EqualTo(0f).Within(0.001f));
            Assert.That(forward.z, Is.EqualTo(0f).Within(0.001f));
            Assert.That(strafeLeft.x, Is.EqualTo(0f).Within(0.001f));
            Assert.That(strafeLeft.y, Is.EqualTo(0f).Within(0.001f));
            Assert.That(strafeLeft.z, Is.EqualTo(2f).Within(0.001f));
        }

        private static MethodInfo GetShouldAutoEnableMethod(Type trackerType)
        {
            MethodInfo method = trackerType.GetMethod(
                "ShouldAutoEnableMouseLook",
                BindingFlags.Static | BindingFlags.NonPublic
            );
            Assert.NotNull(method, "ShouldAutoEnableMouseLook method missing.");
            return method;
        }
    }
}
