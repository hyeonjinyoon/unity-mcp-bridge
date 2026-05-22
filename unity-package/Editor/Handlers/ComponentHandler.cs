using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace UnityMcpBridge.Editor.Handlers
{
    public static class ComponentHandler
    {
        public static string Add(JObject body)
        {
            var path = body.Value<string>("path");
            var typeName = body.Value<string>("componentType");
            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(typeName))
                return McpUtils.Error("path and componentType are required");

            var go = McpUtils.FindByPath(path);
            if (go == null)
                return McpUtils.Error($"Object not found: {path}");

            var type = McpUtils.FindType(typeName);
            if (type == null)
                return McpUtils.Error($"Component type not found: {typeName}");

            var component = Undo.AddComponent(go, type);
            return McpUtils.Success(new
            {
                added = component.GetType().FullName,
                objectPath = path
            });
        }

        public static string Remove(JObject body)
        {
            var path = body.Value<string>("path");
            var typeName = body.Value<string>("componentType");
            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(typeName))
                return McpUtils.Error("path and componentType are required");

            var go = McpUtils.FindByPath(path);
            if (go == null)
                return McpUtils.Error($"Object not found: {path}");

            var type = McpUtils.FindType(typeName);
            if (type == null)
                return McpUtils.Error($"Component type not found: {typeName}");

            var component = go.GetComponent(type);
            if (component == null)
                return McpUtils.Error($"Component not found on object: {typeName}");

            Undo.DestroyObjectImmediate(component);
            return McpUtils.Success(new { removed = typeName, objectPath = path });
        }

        public static string List(JObject body)
        {
            var path = body.Value<string>("path");
            if (string.IsNullOrEmpty(path))
                return McpUtils.Error("path is required");

            var go = McpUtils.FindByPath(path);
            if (go == null)
                return McpUtils.Error($"Object not found: {path}");

            var result = new List<object>();
            foreach (var c in go.GetComponents<Component>())
            {
                if (c == null) continue;
                var type = c.GetType();
                result.Add(new
                {
                    typeName = type.Name,
                    fullName = type.FullName,
                    assemblyQualified = type.AssemblyQualifiedName
                });
            }

            return McpUtils.Success(new { objectPath = path, components = result });
        }
    }
}
