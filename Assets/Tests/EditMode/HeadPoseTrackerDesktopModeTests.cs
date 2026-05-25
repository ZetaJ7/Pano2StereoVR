using System;
using System.Reflection;
using NUnit.Framework;

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
