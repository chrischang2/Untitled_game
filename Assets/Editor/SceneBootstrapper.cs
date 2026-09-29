using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UntitledGame.CameraControl;
using UntitledGame.Companion;
using UntitledGame.GenAI;
using UntitledGame.Player;
using UntitledGame.Progression;

namespace UntitledGame.EditorTools
{
    /// <summary>Builds the base gameplay scene (ground, player, companion, camera) from scratch.</summary>
    public static class SceneBootstrapper
    {
        private const string ScenePath = "Assets/Scenes/Main.unity";

        [MenuItem("Untitled Game/Build Base Scene")]
        public static void BuildBaseScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            CreateGround();
            CreateLight();
            Transform player = CreatePlayer();
            CreateCompanion(player);
            CreateCamera(player);

            System.IO.Directory.CreateDirectory("Assets/Scenes");
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            Debug.Log($"Base scene built and saved to {ScenePath}");
        }

        private static void CreateGround()
        {
            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.localScale = new Vector3(10f, 1f, 10f);
        }

        private static void CreateLight()
        {
            var lightGo = new GameObject("Directional Light");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        }

        private static Transform CreatePlayer()
        {
            GameObject player = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            player.name = "Player";
            player.tag = "Player";
            player.transform.position = new Vector3(0f, 1f, 0f);

            Object.DestroyImmediate(player.GetComponent<CapsuleCollider>());
            var controller = player.AddComponent<CharacterController>();
            controller.center = new Vector3(0f, 1f, 0f);
            player.AddComponent<PlayerController>();

            return player.transform;
        }

        private static void CreateCompanion(Transform player)
        {
            GameObject companion = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            companion.name = "Companion";
            companion.transform.position = new Vector3(1.5f, 0.75f, -1.5f);
            companion.transform.localScale = Vector3.one * 0.75f;

            var companionController = companion.AddComponent<CompanionController>();
            var unlockSystem = companion.AddComponent<PhraseUnlockSystem>();
            var llmClient = companion.AddComponent<OllamaLLMClient>();
            var brain = companion.AddComponent<CompanionBrain>();

            var controllerSo = new SerializedObject(companionController);
            controllerSo.FindProperty("target").objectReferenceValue = player;
            controllerSo.ApplyModifiedProperties();

            var brainSo = new SerializedObject(brain);
            brainSo.FindProperty("llmClientBehaviour").objectReferenceValue = llmClient;
            brainSo.FindProperty("phraseUnlockSystem").objectReferenceValue = unlockSystem;
            brainSo.ApplyModifiedProperties();
        }

        private static void CreateCamera(Transform player)
        {
            var cameraGo = new GameObject("Main Camera");
            cameraGo.tag = "MainCamera";
            cameraGo.AddComponent<Camera>();
            cameraGo.AddComponent<AudioListener>();
            var follow = cameraGo.AddComponent<CameraFollow>();

            var followSo = new SerializedObject(follow);
            followSo.FindProperty("target").objectReferenceValue = player;
            followSo.ApplyModifiedProperties();

            cameraGo.transform.position = player.position + new Vector3(0f, 4f, -6f);
        }
    }
}
