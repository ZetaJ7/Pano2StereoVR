using System;
using System.Reflection;
using NUnit.Framework;

namespace Pano2StereoVR.Tests
{
    public sealed class OpenXRBootstrapRuntimeTargetTests
    {
        [TestCase("DesktopScreen", false)]
        [TestCase("VrOpenXr", true)]
        public void RuntimeTargetControlsAutomaticXrStartup(
            string targetName,
            bool expectedAutoStart)
        {
            Type bootstrapType = GetBootstrapType();
            MethodInfo method = bootstrapType.GetMethod(
                "ShouldAutoStartXr",
                BindingFlags.Static | BindingFlags.NonPublic
            );
            Assert.NotNull(method, "OpenXRBootstrap must expose ShouldAutoStartXr.");

            Type runtimeTargetType = method.GetParameters()[0].ParameterType;
            object target = Enum.Parse(runtimeTargetType, targetName);

            Assert.AreEqual(expectedAutoStart, method.Invoke(null, new[] { target }));
        }

        [Test]
        public void RuntimeTargetMenusExposeDesktopAndVrModes()
        {
            Type bootstrapType = GetBootstrapType();

            Assert.AreEqual(
                "Tools/Pano2StereoVR/Runtime Target/Desktop Screen",
                ReadConstString(bootstrapType, "DesktopScreenMenuPath")
            );
            Assert.AreEqual(
                "Tools/Pano2StereoVR/Runtime Target/VR OpenXR",
                ReadConstString(bootstrapType, "VrOpenXrMenuPath")
            );
        }

        private static Type GetBootstrapType()
        {
            Type type = Type.GetType("Pano2StereoVR.Editor.OpenXRBootstrap, Assembly-CSharp-Editor");
            Assert.NotNull(type, "OpenXRBootstrap editor type must be available.");
            return type;
        }

        private static string ReadConstString(Type type, string fieldName)
        {
            FieldInfo field = type.GetField(
                fieldName,
                BindingFlags.Static | BindingFlags.NonPublic
            );
            Assert.NotNull(field, fieldName + " constant missing.");
            return (string)field.GetRawConstantValue();
        }
    }
}
