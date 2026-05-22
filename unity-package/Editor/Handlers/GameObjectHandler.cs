using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace UnityMcpBridge.Editor.Handlers
{
    public static class GameObjectHandler
    {
        public static string Create(JObject body)
        {
            var name = body.Value<string>("name") ?? "New GameObject";
            var parentPath = body.Value<string>("parentPath");

            GameObject parent = null;
            if (!string.IsNullOrEmpty(parentPath))
            {
                parent = McpUtils.FindByPath(parentPath);
                if (parent == null)
                    return McpUtils.Error($"Parent not found: {parentPath}");
            }

            var useRectTransform = parent != null && parent.transform is RectTransform;
            var go = useRectTransform
                ? new GameObject(name, typeof(RectTransform))
                : new GameObject(name);

            if (parent != null)
            {
                go.transform.SetParent(parent.transform, worldPositionStays: false);

                if (go.transform is RectTransform rt)
                {
                    rt.anchoredPosition = Vector2.zero;
                    rt.localScale = Vector3.one;
                }
            }

            Undo.RegisterCreatedObjectUndo(go, "MCP Create Object");

            return McpUtils.Success(new
            {
                instanceId = go.GetInstanceID(),
                name = go.name,
                path = GetHierarchyPath(go)
            });
        }

        public static string Delete(JObject body)
        {
            var path = body.Value<string>("path");
            if (string.IsNullOrEmpty(path))
                return McpUtils.Error("path is required");

            var go = McpUtils.FindByPath(path);
            if (go == null)
                return McpUtils.Error($"Object not found: {path}");

            Undo.DestroyObjectImmediate(go);
            return McpUtils.Success(new { deleted = path });
        }

        public static string Rename(JObject body)
        {
            var path = body.Value<string>("path");
            var newName = body.Value<string>("newName");
            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(newName))
                return McpUtils.Error("path and newName are required");

            var go = McpUtils.FindByPath(path);
            if (go == null)
                return McpUtils.Error($"Object not found: {path}");

            Undo.RecordObject(go, "MCP Rename Object");
            go.name = newName;
            return McpUtils.Success(new { path = GetHierarchyPath(go), name = newName });
        }

        private static string GetHierarchyPath(GameObject go)
        {
            var path = go.name;
            var t = go.transform.parent;
            while (t != null)
            {
                path = t.name + "/" + path;
                t = t.parent;
            }
            return path;
        }
    }
}
