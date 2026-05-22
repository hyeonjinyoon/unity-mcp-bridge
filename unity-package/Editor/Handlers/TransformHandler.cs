using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace UnityMcpBridge.Editor.Handlers
{
    public static class TransformHandler
    {
        public static string Get(JObject body)
        {
            var path = body.Value<string>("path");
            if (string.IsNullOrEmpty(path))
                return McpUtils.Error("path is required");

            var go = McpUtils.FindByPath(path);
            if (go == null)
                return McpUtils.Error($"Object not found: {path}");

            var local = body.Value<bool?>("local") ?? true;
            var t = go.transform;

            return McpUtils.Success(new
            {
                position = Vec3(local ? t.localPosition : t.position),
                rotation = Vec3(local ? t.localEulerAngles : t.eulerAngles),
                scale = Vec3(t.localScale),
                local
            });
        }

        public static string Set(JObject body)
        {
            var path = body.Value<string>("path");
            if (string.IsNullOrEmpty(path))
                return McpUtils.Error("path is required");

            var go = McpUtils.FindByPath(path);
            if (go == null)
                return McpUtils.Error($"Object not found: {path}");

            var local = body.Value<bool?>("local") ?? true;
            var t = go.transform;
            Undo.RecordObject(t, "MCP Set Transform");

            var pos = body["position"] as JObject;
            if (pos != null)
            {
                var v = ParseVec3(pos, local ? t.localPosition : t.position);
                if (local) t.localPosition = v;
                else t.position = v;
            }

            var rot = body["rotation"] as JObject;
            if (rot != null)
            {
                var v = ParseVec3(rot, local ? t.localEulerAngles : t.eulerAngles);
                if (local) t.localEulerAngles = v;
                else t.eulerAngles = v;
            }

            var scale = body["scale"] as JObject;
            if (scale != null)
            {
                t.localScale = ParseVec3(scale, t.localScale);
            }

            return McpUtils.Success(new
            {
                position = Vec3(local ? t.localPosition : t.position),
                rotation = Vec3(local ? t.localEulerAngles : t.eulerAngles),
                scale = Vec3(t.localScale)
            });
        }

        private static Vector3 ParseVec3(JObject obj, Vector3 fallback)
        {
            return new Vector3(
                obj.Value<float?>("x") ?? fallback.x,
                obj.Value<float?>("y") ?? fallback.y,
                obj.Value<float?>("z") ?? fallback.z
            );
        }

        private static object Vec3(Vector3 v) => new { x = v.x, y = v.y, z = v.z };
    }
}
