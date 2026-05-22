using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnityMcpBridge.Editor
{
    public static class McpUtils
    {
        private static readonly Dictionary<string, Type> _typeCache = new();


        public static string Success(object data = null)
        {
            return JsonConvert.SerializeObject(new { success = true, data },
                new JsonSerializerSettings { ReferenceLoopHandling = ReferenceLoopHandling.Ignore });
        }

        public static string Error(string message)
        {
            return JsonConvert.SerializeObject(new { success = false, error = message });
        }

        public static GameObject FindByPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;

            var parts = path.Split('/');

            // Prefab Mode
            var prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
            if (prefabStage != null)
            {
                var prefabRoot = prefabStage.prefabContentsRoot;
                if (parts[0] != prefabRoot.name) return null;
                if (parts.Length == 1) return prefabRoot;
                var childPath = path.Substring(parts[0].Length + 1);
                var child = prefabRoot.transform.Find(childPath);
                return child != null ? child.gameObject : null;
            }

            // Active Scene
            var roots = SceneManager.GetActiveScene().GetRootGameObjects();
            var root = roots.FirstOrDefault(r => r.name == parts[0]);

            // Fallback: DontDestroyOnLoad
            if (root == null && Application.isPlaying)
            {
                var temp = new GameObject("[temp_ddol_find]");
                UnityEngine.Object.DontDestroyOnLoad(temp);
                var ddolRoots = temp.scene.GetRootGameObjects();
                UnityEngine.Object.DestroyImmediate(temp);
                root = ddolRoots.FirstOrDefault(r => r.name == parts[0]);
            }

            if (root == null) return null;
            if (parts.Length == 1) return root;

            var childPath2 = path.Substring(parts[0].Length + 1);
            var child2 = root.transform.Find(childPath2);
            return child2 != null ? child2.gameObject : null;
        }

        public static Type FindType(string typeName)
        {
            if (string.IsNullOrEmpty(typeName)) return null;

            if (_typeCache.TryGetValue(typeName, out var cached))
                return cached;

            var type = FindTypeUncached(typeName);
            if (type != null)
                _typeCache[typeName] = type;
            return type;
        }

        private static Type FindTypeUncached(string typeName)
        {
            var type = Type.GetType(typeName);
            if (type != null) return type;

            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            foreach (var assembly in assemblies)
            {
                type = assembly.GetType(typeName);
                if (type != null) return type;
            }

            string[] namespaces = { "UnityEngine", "UnityEngine.UI", "TMPro", "UnityEngine.Rendering.Universal" };
            foreach (var ns in namespaces)
            {
                foreach (var assembly in assemblies)
                {
                    type = assembly.GetType($"{ns}.{typeName}");
                    if (type != null) return type;
                }
            }

            // Short name fallback — 사용자 프로젝트 커스텀 네임스페이스 대응.
            foreach (var assembly in assemblies)
            {
                Type[] types;
                try
                {
                    types = assembly.GetTypes();
                }
                catch (System.Reflection.ReflectionTypeLoadException e)
                {
                    types = e.Types.Where(t => t != null).ToArray();
                }

                foreach (var t in types)
                {
                    if (t.Name == typeName) return t;
                }
            }

            return null;
        }

        public static void ClearTypeCache()
        {
            _typeCache.Clear();
        }
    }
}
