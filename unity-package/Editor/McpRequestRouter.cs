using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityMcpBridge.Editor.Handlers;

namespace UnityMcpBridge.Editor
{
    public static class McpRequestRouter
    {
        public static string HandleHealth()
        {
            return McpUtils.Success(new
            {
                unityVersion = Application.unityVersion,
                projectPath = Application.dataPath.Replace("/Assets", ""),
                activeScene = EditorSceneManager.GetActiveScene().name,
                isPlaying = EditorApplication.isPlaying
            });
        }

        private static string HandleRefresh()
        {
            AssetDatabase.Refresh();
            return McpUtils.Success(new { refreshed = true });
        }

        public static Task<string> RouteAsync(string path, JObject body)
        {
            // Screenshot needs to wait for an async file write on the HTTP worker
            // thread, so it cannot run inside the main-thread dispatcher closure.
            if (path == "/api/editor/screenshot")
            {
                return ScreenshotHandler.CaptureAsync(body);
            }

            return McpMainThreadDispatcher.RunOnMainThread(() => RouteSync(path, body));
        }

        private static string RouteSync(string path, JObject body)
        {
            return path switch
            {
                "/api/scene/query"          => SceneHandler.Query(body),
                "/api/scene/save"           => SceneHandler.Save(body),
                "/api/scene/open"           => SceneHandler.Open(body),
                "/api/scene/new"            => SceneHandler.NewScene(body),
                "/api/object/create"        => GameObjectHandler.Create(body),
                "/api/object/delete"        => GameObjectHandler.Delete(body),
                "/api/object/rename"        => GameObjectHandler.Rename(body),
                "/api/transform/get"        => TransformHandler.Get(body),
                "/api/transform/set"        => TransformHandler.Set(body),
                "/api/component/add"        => ComponentHandler.Add(body),
                "/api/component/remove"     => ComponentHandler.Remove(body),
                "/api/component/list"       => ComponentHandler.List(body),
                "/api/property/get"         => PropertyHandler.Get(body),
                "/api/property/set"         => PropertyHandler.Set(body),
                "/api/prefab/open"          => PrefabHandler.Open(body),
                "/api/prefab/instantiate"   => PrefabHandler.Instantiate(body),
                "/api/prefab/save"          => PrefabHandler.Save(body),
                "/api/editor/refresh"       => HandleRefresh(),
                "/api/editor/log"           => EditorLogHandler.Get(body),
                "/api/console/get"          => ConsoleHandler.Get(body),
                "/api/console/clear"        => ConsoleHandler.Clear(body),
                "/api/play/start"           => PlayModeHandler.Play(body),
                "/api/play/stop"            => PlayModeHandler.Stop(body),
                "/api/play/pause"           => PlayModeHandler.Pause(body),
                "/api/play/state"           => PlayModeHandler.GetState(body),
                "/api/input/tap"            => InputHandler.Tap(body),
                "/api/input/hold"           => InputHandler.Hold(body),
                "/api/input/swipe"          => InputHandler.Swipe(body),
                "/api/input/raycast"        => InputHandler.Raycast(body),
                "/api/input/press_key"      => InputHandler.PressKey(body),
                _ => McpUtils.Error($"Unknown endpoint: {path}")
            };
        }
    }
}
