using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnityMcpBridge.Editor.Handlers
{
    [InitializeOnLoad]
    public static class PlayModeHandler
    {
        private const string OriginalScenePathKey = "UnityMcpBridge.OriginalScenePath";

        static PlayModeHandler()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        public static string Play(JObject body)
        {
            if (EditorApplication.isPlaying)
                return McpUtils.Error("Already in Play mode");

            bool fromCurrent = body?["fromCurrent"]?.Value<bool>() ?? false;

            // Init 씬이 설정되지 않았으면 자동으로 fromCurrent 동작.
            var settings = McpBridgeSettings.GetOrCreate();
            var initScene = settings != null ? settings.initScene : null;

            if (fromCurrent || initScene == null)
            {
                EditorApplication.isPlaying = true;
                return McpUtils.Success(new
                {
                    state = "playing",
                    scene = "current",
                    note = initScene == null && !fromCurrent
                        ? "InitScene not configured in McpBridgeSettings; playing from current scene"
                        : null
                });
            }

            var scenePath = AssetDatabase.GetAssetPath(initScene);
            if (string.IsNullOrEmpty(scenePath))
                return McpUtils.Error("InitScene asset path could not be resolved");

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return McpUtils.Error("User cancelled save dialog");

            var originalScenePath = SceneManager.GetActiveScene().path;
            if (!string.IsNullOrEmpty(originalScenePath) && originalScenePath != scenePath)
            {
                SessionState.SetString(OriginalScenePathKey, originalScenePath);
            }
            else
            {
                SessionState.EraseString(OriginalScenePathKey);
            }

            EditorSceneManager.OpenScene(scenePath);
            EditorApplication.isPlaying = true;

            return McpUtils.Success(new { state = "starting", scene = initScene.name });
        }

        public static string Stop(JObject body)
        {
            if (!EditorApplication.isPlaying)
                return McpUtils.Error("Not in Play mode");

            EditorApplication.isPlaying = false;
            return McpUtils.Success(new { state = "stopped" });
        }

        public static string Pause(JObject body)
        {
            if (!EditorApplication.isPlaying)
                return McpUtils.Error("Not in Play mode");

            EditorApplication.isPaused = !EditorApplication.isPaused;
            return McpUtils.Success(new { paused = EditorApplication.isPaused });
        }

        public static string GetState(JObject body)
        {
            return McpUtils.Success(new
            {
                isPlaying = EditorApplication.isPlaying,
                isPaused = EditorApplication.isPaused,
                activeScene = SceneManager.GetActiveScene().name
            });
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredEditMode) return;

            var originalScenePath = SessionState.GetString(OriginalScenePathKey, null);
            if (string.IsNullOrEmpty(originalScenePath)) return;
            SessionState.EraseString(OriginalScenePathKey);

            if (!System.IO.File.Exists(originalScenePath)) return;
            EditorSceneManager.OpenScene(originalScenePath);
        }
    }
}
