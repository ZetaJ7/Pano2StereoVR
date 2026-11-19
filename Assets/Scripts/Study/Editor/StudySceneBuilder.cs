using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Pano2StereoVR.Study.Editor
{
    /// <summary>
    /// Creates Assets/Scenes/StudyScene.unity: one GameObject with StudyController and the three study
    /// shaders assigned (so that player builds include them). Everything else is built at runtime.
    /// Batch mode: -executeMethod Pano2StereoVR.Study.Editor.StudySceneBuilder.CreateStudySceneBatch
    /// </summary>
    public static class StudySceneBuilder
    {
        public const string ScenePath = "Assets/Scenes/StudyScene.unity";
        private const string SphereShaderPath = "Assets/Shaders/StudyStereoTB.shader";
        private const string QuadShaderPath = "Assets/Shaders/StudyStereoQuadTB.shader";
        private const string ColorShaderPath = "Assets/Shaders/StudyUnlitColor.shader";

        [MenuItem("Tools/Pano2StereoVR/Create Study Scene")]
        public static void CreateStudyScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }
            Build();
            EditorSceneManager.OpenScene(ScenePath);
        }

        [MenuItem("Tools/Pano2StereoVR/Use Study Scene As First Build Scene")]
        public static void UseStudySceneForBuild()
        {
            var scenes = EditorBuildSettings.scenes.Where(s => s.path != ScenePath).ToList();
            scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
            Debug.Log("[StudySceneBuilder] " + ScenePath + " is now the first build scene.");
        }

        public static void CreateStudySceneBatch()
        {
            Build();
        }

        private static void Build()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var go = new GameObject("StudyController");
            var controller = go.AddComponent<StudyController>();
            controller.SphereShader = AssetDatabase.LoadAssetAtPath<Shader>(SphereShaderPath);
            controller.QuadShader = AssetDatabase.LoadAssetAtPath<Shader>(QuadShaderPath);
            controller.ColorShader = AssetDatabase.LoadAssetAtPath<Shader>(ColorShaderPath);
            if (controller.SphereShader == null || controller.QuadShader == null || controller.ColorShader == null)
            {
                Debug.LogError("[StudySceneBuilder] a study shader is missing under Assets/Shaders");
            }
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log("[StudySceneBuilder] wrote " + ScenePath);
        }
    }
}
