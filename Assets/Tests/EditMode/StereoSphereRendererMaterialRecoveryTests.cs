using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace Pano2StereoVR.Tests
{
    public sealed class StereoSphereRendererMaterialRecoveryTests
    {
        [Test]
        public void ApplyFrameRebindsTextureAfterBaselineMaterialReplacement()
        {
            Shader stereoShader = Shader.Find("Pano2Stereo/StereoPanorama");
            Shader monoShader = Shader.Find("Pano2Stereo/MonoPanorama");
            Assert.NotNull(stereoShader, "Stereo panorama shader must be available.");
            Assert.NotNull(monoShader, "Mono panorama shader must be available.");

            GameObject host = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            host.name = "stereo-material-recovery-test";
            Renderer renderer = host.GetComponent<Renderer>();
            var texture = new Texture2D(4, 2, TextureFormat.RGB24, false);
            Type rendererType = Type.GetType("Pano2StereoVR.StereoSphereRenderer, Assembly-CSharp");
            Assert.NotNull(rendererType, "StereoSphereRenderer runtime type must be available.");
            Component component = host.AddComponent(rendererType);

            try
            {
                MethodInfo awake = rendererType.GetMethod(
                    "Awake",
                    BindingFlags.Instance | BindingFlags.NonPublic
                );
                Assert.NotNull(awake, "StereoSphereRenderer must initialize from Awake.");
                awake.Invoke(component, Array.Empty<object>());

                MethodInfo applyFrame = rendererType.GetMethod(
                    "ApplyFrame",
                    BindingFlags.Instance | BindingFlags.NonPublic
                );
                Assert.NotNull(applyFrame, "StereoSphereRenderer must expose ApplyFrame internally.");

                applyFrame.Invoke(component, new object[] { texture, 3 });
                Assert.AreSame(texture, renderer.material.GetTexture("_MainTex"));

                UnityEngine.Object.DestroyImmediate(renderer.material);
                renderer.material = new Material(monoShader);

                applyFrame.Invoke(component, new object[] { texture, 3 });

                Assert.AreEqual(stereoShader, renderer.material.shader);
                Assert.AreSame(texture, renderer.material.GetTexture("_MainTex"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(texture);
                UnityEngine.Object.DestroyImmediate(host);
            }
        }
    }
}
