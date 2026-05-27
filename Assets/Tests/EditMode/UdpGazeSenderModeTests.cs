using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace Pano2StereoVR.Tests
{
    public sealed class UdpGazeSenderModeTests
    {
        [Test]
        public void AwakeDefaultsToBaselineModeOne()
        {
            Type senderType = Type.GetType("Pano2StereoVR.UdpGazeSender, Assembly-CSharp");
            Assert.NotNull(senderType, "UdpGazeSender runtime type must be available.");

            GameObject host = new GameObject("udp-gaze-sender-default-mode-test");
            host.SetActive(false);
            Component sender = host.AddComponent(senderType);

            try
            {
                InvokePrivate(sender, "Awake");

                PropertyInfo currentMode = senderType.GetProperty(
                    "CurrentMode",
                    BindingFlags.Instance | BindingFlags.Public
                );
                Assert.NotNull(currentMode, "UdpGazeSender must expose CurrentMode.");

                Assert.AreEqual(1, currentMode.GetValue(sender));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void SetModeAcceptsBaselineModeOne()
        {
            Type senderType = Type.GetType("Pano2StereoVR.UdpGazeSender, Assembly-CSharp");
            Assert.NotNull(senderType, "UdpGazeSender runtime type must be available.");

            GameObject host = new GameObject("udp-gaze-sender-mode-test");
            host.SetActive(false);
            Component sender = host.AddComponent(senderType);

            try
            {
                InvokePrivate(sender, "Awake");

                MethodInfo setMode = senderType.GetMethod("SetMode", BindingFlags.Instance | BindingFlags.Public);
                Assert.NotNull(setMode, "UdpGazeSender must expose SetMode.");
                setMode.Invoke(sender, new object[] { 1 });

                PropertyInfo currentMode = senderType.GetProperty(
                    "CurrentMode",
                    BindingFlags.Instance | BindingFlags.Public
                );
                Assert.NotNull(currentMode, "UdpGazeSender must expose CurrentMode.");

                Assert.AreEqual(1, currentMode.GetValue(sender));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void SetModeClampsModeFourToPoseAwareModeThree()
        {
            Type senderType = Type.GetType("Pano2StereoVR.UdpGazeSender, Assembly-CSharp");
            Assert.NotNull(senderType, "UdpGazeSender runtime type must be available.");

            GameObject host = new GameObject("udp-gaze-sender-mode-clamp-test");
            host.SetActive(false);
            Component sender = host.AddComponent(senderType);

            try
            {
                InvokePrivate(sender, "Awake");

                MethodInfo setMode = senderType.GetMethod("SetMode", BindingFlags.Instance | BindingFlags.Public);
                Assert.NotNull(setMode, "UdpGazeSender must expose SetMode.");
                setMode.Invoke(sender, new object[] { 4 });

                PropertyInfo currentMode = senderType.GetProperty(
                    "CurrentMode",
                    BindingFlags.Instance | BindingFlags.Public
                );
                Assert.NotNull(currentMode, "UdpGazeSender must expose CurrentMode.");

                Assert.AreEqual(3, currentMode.GetValue(sender));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        private static void InvokePrivate(object target, string methodName)
        {
            MethodInfo method = target.GetType().GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.NonPublic
            );
            Assert.NotNull(method, target.GetType().Name + " must expose " + methodName + ".");
            method.Invoke(target, Array.Empty<object>());
        }
    }
}
