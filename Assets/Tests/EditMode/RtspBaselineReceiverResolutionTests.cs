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

        [Test]
        public void SetStreamingActiveFalseDisablesReceiverComponent()
        {
            Type receiverType = Type.GetType("Pano2StereoVR.RtspBaselineReceiver, Assembly-CSharp");
            Assert.NotNull(receiverType, "RtspBaselineReceiver runtime type must be available.");

            var host = new GameObject("rtsp-streaming-active-test-host");
            host.SetActive(false);
            Component receiver = host.AddComponent(receiverType);

            try
            {
                MethodInfo method = receiverType.GetMethod(
                    "SetStreamingActive",
                    BindingFlags.Instance | BindingFlags.Public
                );
                Assert.NotNull(method, "RtspBaselineReceiver must expose SetStreamingActive.");

                ((Behaviour)receiver).enabled = true;
                method.Invoke(receiver, new object[] { false });

                Assert.IsFalse(((Behaviour)receiver).enabled);
                Assert.IsFalse(ReadBoolProperty(receiver, receiverType, "IsRunning"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void HasCompleteBufferedFrameRequiresAtLeastOneFullFrame()
        {
            Type receiverType = Type.GetType("Pano2StereoVR.RtspBaselineReceiver, Assembly-CSharp");
            Assert.NotNull(receiverType, "RtspBaselineReceiver runtime type must be available.");

            MethodInfo method = receiverType.GetMethod(
                "HasCompleteBufferedFrame",
                BindingFlags.Static | BindingFlags.NonPublic
            );
            Assert.NotNull(method, "RtspBaselineReceiver must check complete buffered frames.");

            Assert.IsFalse((bool)method.Invoke(null, new object[] { 1023, 1024 }));
            Assert.IsTrue((bool)method.Invoke(null, new object[] { 1024, 1024 }));
            Assert.IsTrue((bool)method.Invoke(null, new object[] { 2048, 1024 }));
        }

        [Test]
        public void TryParseFfprobeFrameRateAcceptsFractionAndDecimalOutput()
        {
            Type receiverType = Type.GetType("Pano2StereoVR.RtspBaselineReceiver, Assembly-CSharp");
            Assert.NotNull(receiverType, "RtspBaselineReceiver runtime type must be available.");

            MethodInfo method = receiverType.GetMethod(
                "TryParseFfprobeFrameRate",
                BindingFlags.Static | BindingFlags.NonPublic
            );
            Assert.NotNull(method, "RtspBaselineReceiver must parse ffprobe frame-rate output.");

            object[] fractionArgs = { "0/0\n60000/1001\n", 0f };
            Assert.IsTrue((bool)method.Invoke(null, fractionArgs));
            Assert.AreEqual(59.94006f, (float)fractionArgs[1], 0.001f);

            object[] decimalArgs = { "N/A\n60\n", 0f };
            Assert.IsTrue((bool)method.Invoke(null, decimalArgs));
            Assert.AreEqual(60f, (float)decimalArgs[1], 0.001f);
        }

        [Test]
        public void TryCopyLatestFrameForApplyReturnsIndependentSnapshot()
        {
            Type receiverType = Type.GetType("Pano2StereoVR.RtspBaselineReceiver, Assembly-CSharp");
            Assert.NotNull(receiverType, "RtspBaselineReceiver runtime type must be available.");

            var host = new GameObject("rtsp-copy-frame-test-host");
            host.SetActive(false);
            Component receiver = host.AddComponent(receiverType);

            try
            {
                byte[] latestFrame = { 1, 2, 3, 4, 5, 6 };
                receiverType.GetField("_latestFrame", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(receiver, latestFrame);
                receiverType.GetField("_latestFrameId", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(receiver, 4);
                receiverType.GetField("_appliedFrameId", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(receiver, 1);

                MethodInfo method = receiverType.GetMethod(
                    "TryCopyLatestFrameForApply",
                    BindingFlags.Instance | BindingFlags.NonPublic
                );
                Assert.NotNull(method, "RtspBaselineReceiver must snapshot frames before texture upload.");

                object[] args = { null, 0, 0 };
                bool copied = (bool)method.Invoke(receiver, args);

                Assert.IsTrue(copied);
                byte[] snapshot = (byte[])args[0];
                Assert.AreEqual(latestFrame, snapshot);
                Assert.AreNotSame(latestFrame, snapshot);
                Assert.AreEqual(4, args[1]);
                Assert.AreEqual(2, args[2]);
                Assert.AreEqual(
                    4,
                    receiverType.GetField("_appliedFrameId", BindingFlags.Instance | BindingFlags.NonPublic)
                        .GetValue(receiver)
                );
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
