using System;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace UnityMcpBridge.Editor.Handlers
{
    public static class PrefabHandler
    {
        public static string Open(JObject body)
        {
            var prefabPath = body.Value<string>("prefabPath");
            if (string.IsNullOrEmpty(prefabPath))
                return McpUtils.Error("prefabPath is required");

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
                return McpUtils.Error($"Prefab not found: {prefabPath}");

            var stage = PrefabStageUtility.OpenPrefab(prefabPath);
            if (stage == null)
                return McpUtils.Error($"Failed to open prefab: {prefabPath}");

            return McpUtils.Success(new
            {
                prefabPath,
                name = prefab.name,
                opened = true
            });
        }

        public static string Instantiate(JObject body)
        {
            var prefabPath = body.Value<string>("prefabPath");
            if (string.IsNullOrEmpty(prefabPath))
                return McpUtils.Error("prefabPath is required");

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
                return McpUtils.Error($"Prefab not found: {prefabPath}");

            var parentPath = body.Value<string>("parentPath");
            Transform parentTransform = null;
            if (!string.IsNullOrEmpty(parentPath))
            {
                var parent = McpUtils.FindByPath(parentPath);
                if (parent != null) parentTransform = parent.transform;
            }

            var instance = parentTransform != null
                ? PrefabUtility.InstantiatePrefab(prefab, parentTransform) as GameObject
                : PrefabUtility.InstantiatePrefab(prefab) as GameObject;

            if (instance == null)
                return McpUtils.Error("Failed to instantiate prefab");

            Undo.RegisterCreatedObjectUndo(instance, "MCP Instantiate Prefab");

            if (parentTransform is RectTransform && instance.transform is RectTransform rt)
            {
                if (rt.anchorMin == Vector2.zero && rt.anchorMax == Vector2.one)
                {
                    rt.anchoredPosition = Vector2.zero;
                    rt.sizeDelta = Vector2.zero;
                }
            }

            var pos = body["position"] as JObject;
            if (pos != null)
            {
                instance.transform.localPosition = new Vector3(
                    pos.Value<float?>("x") ?? 0,
                    pos.Value<float?>("y") ?? 0,
                    pos.Value<float?>("z") ?? 0);
            }

            return McpUtils.Success(new
            {
                instanceId = instance.GetInstanceID(),
                name = instance.name,
                prefab = prefabPath
            });
        }

        public static string Save(JObject body)
        {
            var sourcePath = body.Value<string>("sourcePath");
            var targetPath = body.Value<string>("targetPath");

            if (string.IsNullOrEmpty(sourcePath))
                return McpUtils.Error("sourcePath is required");
            if (string.IsNullOrEmpty(targetPath))
                return McpUtils.Error("targetPath is required");
            if (!targetPath.StartsWith("Assets/"))
                return McpUtils.Error("targetPath must start with 'Assets/'");
            if (!targetPath.EndsWith(".prefab"))
                return McpUtils.Error("targetPath must end with '.prefab'");

            var obj = McpUtils.FindByPath(sourcePath);
            if (obj == null)
                return McpUtils.Error($"Source object not found: {sourcePath}");

            var dir = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
                AssetDatabase.Refresh();
            }

            GameObject saved;
            try
            {
                saved = PrefabUtility.SaveAsPrefabAssetAndConnect(
                    obj, targetPath, InteractionMode.AutomatedAction);
            }
            catch (Exception ex)
            {
                return McpUtils.Error($"SaveAsPrefabAsset failed: {ex.Message}");
            }

            if (saved == null)
                return McpUtils.Error("SaveAsPrefabAsset returned null");

            return McpUtils.Success(new
            {
                prefabPath = targetPath,
                name = saved.name
            });
        }
    }
}
