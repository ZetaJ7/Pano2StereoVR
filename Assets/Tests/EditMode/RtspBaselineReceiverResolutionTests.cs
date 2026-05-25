using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace Pano2StereoVR.Tests
{
    public sealed class RtspBaselineReceiverResolutionTests
    {
        [Test]
        public void ApplyOutputResolutionUpdatesDimensionsWithoutStartingReceiver()
        {
            Type receiverType = Type.GetType("Pano2StereoVR.RtspBaselineReceiver, Assembly-CSharp");
            Assert.NotNull(receiverType, "RtspBaselineReceiver runtime type must be available.");

            var host = new GameObject("rtsp-resolution-test-host");
            host.SetActive(false);
            Component receiver = host.AddComponent(receiverType);

            try
            {
                MethodInfo method = receiverType.GetMethod(
                    "ApplyOutputResolution",
                    BindingFlags.Instance | BindingFlags.Public
                );
                Assert.NotNull(method, "RtspBaselineReceiver must expose ApplyOutputResolution.");

                object result = method.Invoke(receiver, new object[] { 2884, 1442, false });

                Assert.AreEqual(true, result);
                Assert.AreEqual(2884, ReadIntProperty(receiver, receiverType, "OutputWidth"));
                Assert.AreEqual(1442, ReadIntProperty(receiver, receiverType, "OutputHeight"));
                Assert.IsFalse(ReadBoolProperty(receiver, receiverType, "IsRunning"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void ApplyOutputResolutionRejectsOversizedFrameWithoutChangingDimensions()
        {
            Type receiverType = Type.GetType("Pano2StereoVR.RtspBaselineReceiver, Assembly-CSharp");
            Assert.NotNull(receiverType, "RtspBaselineReceiver runtime type must be available.");

            var host = new GameObject("rtsp-resolution-limit-test-host");
            host.SetActive(false);
            Component receiver = host.AddComponent(receiverType);

            try
            {
                MethodInfo method = receiverType.GetMethod(
                    "ApplyOutputResolution",
                    BindingFlags.Instance | BindingFlags.Public
                );
                Assert.NotNull(method, "RtspBaselineReceiver must expose ApplyOutputResolution.");

                Assert.AreEqual(true, method.Invoke(receiver, new object[] { 2884, 1442, false }));

                object oversizedResult = method.Invoke(receiver, new object[] { 100000, 100000, false });

                Assert.AreEqual(false, oversizedResult);
                Assert.AreEqual(2884, ReadIntProperty(receiver, receiverType, "OutputWidth"));
                Assert.AreEqual(1442, ReadIntProperty(receiver, receiverType, "OutputHeight"));
                Assert.IsFalse(ReadBoolProperty(receiver, receiverType, "IsRunning"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        private static int ReadIntProperty(object target, Type targetType, string propertyName)
        {
            PropertyInfo property = targetType.GetProperty(
                propertyName,
                BindingFlags.Instance | BindingFlags.Public
            );
            Assert.NotNull(property, propertyName + " property missing.");
            return (int)property.GetValue(target);
        }

        private static bool ReadBoolProperty(object target, Type targetType, string propertyName)
        {
            PropertyInfo property = targetType.GetProperty(
                propertyName,
                BindingFlags.Instance | BindingFlags.Public
            );
            Assert.NotNull(property, propertyName + " property missing.");
            return (bool)property.GetValue(target);
        }
    }
}
