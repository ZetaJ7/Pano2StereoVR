using System;
using System.IO;
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
                Assert.AreSame(texture, renderer.sharedMaterial.GetTexture("_MainTex"));

                UnityEngine.Object.DestroyImmediate(renderer.sharedMaterial);
                renderer.sharedMaterial = new Material(monoShader);

                applyFrame.Invoke(component, new object[] { texture, 3 });

                Assert.AreEqual(stereoShader, renderer.sharedMaterial.shader);
                Assert.AreSame(texture, renderer.sharedMaterial.GetTexture("_MainTex"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(texture);
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void AwakeDefaultsRendererToPlanarDisplaySurface()
        {
            GameObject host = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            host.name = "stereo-planar-surface-test";
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

                MeshFilter meshFilter = host.GetComponent<MeshFilter>();
                Assert.NotNull(meshFilter, "Planar rendering requires a mesh filter.");
                Assert.AreEqual(
                    "Quad",
                    meshFilter.sharedMesh.name,
                    "The latency experiment should render the final texture on a flat quad."
                );
                Assert.AreEqual(new Vector3(0f, 1.6f, 3.0f), host.transform.position);
                Assert.Less(Quaternion.Angle(Quaternion.Euler(0f, 180f, 0f), host.transform.rotation), 0.01f);
                Assert.AreEqual(new Vector3(5.0f, 2.5f, 1.0f), host.transform.localScale);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void StereoPanoramaShaderIsDoubleSidedForPlanarTarget()
        {
            string shaderText = File.ReadAllText("Assets/Shaders/StereoPanorama.shader");

            StringAssert.Contains("Cull Off", shaderText);
        }
    }
}
