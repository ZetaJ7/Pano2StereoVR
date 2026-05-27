using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace Pano2StereoVR.Tests
{
    public sealed class ExperimentControllerBaselineShmTests
    {
        [Test]
        public void ModeOneBaselineKeepsSharedMemoryPipelineEnabled()
        {
            Type controllerType = RequireRuntimeType("Pano2StereoVR.ExperimentController, Assembly-CSharp");
            Type receiverType = RequireRuntimeType("Pano2StereoVR.SharedMemoryReceiver, Assembly-CSharp");
            Type senderType = RequireRuntimeType("Pano2StereoVR.UdpGazeSender, Assembly-CSharp");
            Type rendererType = RequireRuntimeType("Pano2StereoVR.StereoSphereRenderer, Assembly-CSharp");
            Type rtspType = RequireRuntimeType("Pano2StereoVR.RtspBaselineReceiver, Assembly-CSharp");
            Type monoRendererType = RequireRuntimeType("Pano2StereoVR.BaselinePanoramaRenderer, Assembly-CSharp");

            GameObject controllerHost = new GameObject("controller-baseline-shm-test");
            GameObject shmHost = new GameObject("shm-baseline-shm-test");
            GameObject udpHost = new GameObject("udp-baseline-shm-test");
            GameObject stereoHost = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            stereoHost.name = "stereo-baseline-shm-test";
            controllerHost.SetActive(false);
            shmHost.SetActive(false);
            udpHost.SetActive(false);
            stereoHost.SetActive(false);

            Component controller = controllerHost.AddComponent(controllerType);
            Behaviour receiver = (Behaviour)shmHost.AddComponent(receiverType);
            Behaviour sender = (Behaviour)udpHost.AddComponent(senderType);
            Behaviour renderer = (Behaviour)stereoHost.AddComponent(rendererType);

            try
            {
                InvokePrivate(sender, "Awake");
                SetPrivateField(controller, "sharedMemoryReceiver", receiver);
                SetPrivateField(controller, "udpGazeSender", sender);
                SetPrivateField(controller, "stereoSphereRenderer", renderer);

                InvokePrivate(controller, "RequestModeSwitch", 1);

                Assert.AreEqual(1, GetPublicProperty(sender, "CurrentMode"));
                Assert.IsTrue(receiver.enabled, "Baseline mode must keep the shared-memory receiver enabled.");
                Assert.IsTrue(renderer.enabled, "Baseline mode must keep stereo SHM rendering enabled.");
                Assert.IsTrue(sender.enabled, "Baseline mode must keep UDP mode commands enabled.");
                Assert.IsNull(stereoHost.GetComponent(rtspType), "Baseline mode must not create an RTSP receiver.");
                Assert.IsNull(
                    stereoHost.GetComponent(monoRendererType),
                    "Baseline mode must not create the mono RTSP panorama renderer."
                );
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(stereoHost);
                UnityEngine.Object.DestroyImmediate(udpHost);
                UnityEngine.Object.DestroyImmediate(shmHost);
                UnityEngine.Object.DestroyImmediate(controllerHost);
            }
        }

        [Test]
        public void ModeOneBaselineRequestSendsModeOneOverUdp()
        {
            Type controllerType = RequireRuntimeType("Pano2StereoVR.ExperimentController, Assembly-CSharp");
            Type receiverType = RequireRuntimeType("Pano2StereoVR.SharedMemoryReceiver, Assembly-CSharp");
            Type senderType = RequireRuntimeType("Pano2StereoVR.UdpGazeSender, Assembly-CSharp");
            Type rendererType = RequireRuntimeType("Pano2StereoVR.StereoSphereRenderer, Assembly-CSharp");

            GameObject controllerHost = new GameObject("controller-baseline-udp-test");
            GameObject shmHost = new GameObject("shm-baseline-udp-test");
            GameObject udpHost = new GameObject("udp-baseline-udp-test");
            GameObject stereoHost = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            stereoHost.name = "stereo-baseline-udp-test";
            controllerHost.SetActive(false);
            shmHost.SetActive(false);
            udpHost.SetActive(false);
            stereoHost.SetActive(false);

            Component controller = controllerHost.AddComponent(controllerType);
            Component receiver = shmHost.AddComponent(receiverType);
            Component sender = udpHost.AddComponent(senderType);
            Component renderer = stereoHost.AddComponent(rendererType);

            try
            {
                InvokePrivate(sender, "Awake");
                SetPrivateField(controller, "sharedMemoryReceiver", receiver);
                SetPrivateField(controller, "udpGazeSender", sender);
                SetPrivateField(controller, "stereoSphereRenderer", renderer);

                InvokePrivate(controller, "RequestModeSwitch", 1);

                Assert.AreEqual(1, GetPublicProperty(sender, "CurrentMode"));
                Assert.AreEqual(0L, GetPublicProperty(controller, "AppliedSwitchCount"));
                Assert.AreEqual(true, GetPrivateField(controller, "_hasPendingRequest"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(stereoHost);
                UnityEngine.Object.DestroyImmediate(udpHost);
                UnityEngine.Object.DestroyImmediate(shmHost);
                UnityEngine.Object.DestroyImmediate(controllerHost);
            }
        }

        [Test]
        public void ModeOneBaselineCompletesWhenSharedMemoryAppliesModeOne()
        {
            Type controllerType = RequireRuntimeType("Pano2StereoVR.ExperimentController, Assembly-CSharp");
            Type receiverType = RequireRuntimeType("Pano2StereoVR.SharedMemoryReceiver, Assembly-CSharp");
            Type senderType = RequireRuntimeType("Pano2StereoVR.UdpGazeSender, Assembly-CSharp");
            Type rendererType = RequireRuntimeType("Pano2StereoVR.StereoSphereRenderer, Assembly-CSharp");

            GameObject controllerHost = new GameObject("controller-baseline-applied-test");
            GameObject shmHost = new GameObject("shm-baseline-applied-test");
            GameObject udpHost = new GameObject("udp-baseline-applied-test");
            GameObject stereoHost = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            stereoHost.name = "stereo-baseline-applied-test";
            controllerHost.SetActive(false);
            shmHost.SetActive(false);
            udpHost.SetActive(false);
            stereoHost.SetActive(false);

            Component controller = controllerHost.AddComponent(controllerType);
            Component receiver = shmHost.AddComponent(receiverType);
            Component sender = udpHost.AddComponent(senderType);
            Component renderer = stereoHost.AddComponent(rendererType);

            try
            {
                InvokePrivate(sender, "Awake");
                SetPrivateField(controller, "sharedMemoryReceiver", receiver);
                SetPrivateField(controller, "udpGazeSender", sender);
                SetPrivateField(controller, "stereoSphereRenderer", renderer);

                InvokePrivate(controller, "RequestModeSwitch", 1);
                InvokePrivate(controller, "OnModeApplied", 1, 2UL, 1.0f);

                Assert.AreEqual(false, GetPrivateField(controller, "_hasPendingRequest"));
                Assert.AreEqual(1L, GetPublicProperty(controller, "AppliedSwitchCount"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(stereoHost);
                UnityEngine.Object.DestroyImmediate(udpHost);
                UnityEngine.Object.DestroyImmediate(shmHost);
                UnityEngine.Object.DestroyImmediate(controllerHost);
            }
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic
            );
            Assert.NotNull(field, target.GetType().Name + " must expose " + fieldName + ".");
            field.SetValue(target, value);
        }

        private static object GetPrivateField(object target, string fieldName)
        {
            FieldInfo field = target.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic
            );
            Assert.NotNull(field, target.GetType().Name + " must expose " + fieldName + ".");
            return field.GetValue(target);
        }

        private static Type RequireRuntimeType(string typeName)
        {
            Type type = Type.GetType(typeName);
            Assert.NotNull(type, typeName + " must be available.");
            return type;
        }

        private static object GetPublicProperty(object target, string propertyName)
        {
            PropertyInfo property = target.GetType().GetProperty(
                propertyName,
                BindingFlags.Instance | BindingFlags.Public
            );
            Assert.NotNull(property, target.GetType().Name + " must expose " + propertyName + ".");
            return property.GetValue(target);
        }

        private static void InvokePrivate(object target, string methodName, params object[] args)
        {
            MethodInfo method = target.GetType().GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.NonPublic
            );
            Assert.NotNull(method, target.GetType().Name + " must expose " + methodName + ".");
            method.Invoke(target, args ?? Array.Empty<object>());
        }
    }
}
