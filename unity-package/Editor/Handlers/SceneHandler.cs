using System.Collections.Generic;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnityMcpBridge.Editor.Handlers
{
    public static class SceneHandler
    {
        public static string Query(JObject body)
        {
            var path = body.Value<string>("path") ?? "";
            var depth = body.Value<int?>("depth") ?? 3;
            var includeInactive = body.Value<bool?>("includeInactive") ?? false;

            var prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
            var isPrefabMode = prefabStage != null;
            var sceneName = isPrefabMode
                ? prefabStage.prefabContentsRoot.name
                : SceneManager.GetActiveScene().name;

            if (!string.IsNullOrEmpty(path) && ContainsWildcard(path))
            {
                var parts = path.Split(new[] { '/' }, System.StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0)
                    return McpUtils.Error($"Invalid pattern: {path}");

                var matches = new List<(GameObject go, string path)>();
                var seen = new HashSet<int>();

                if (isPrefabMode)
                {
                    var wildcardRoot = prefabStage.prefabContentsRoot;
                    if (includeInactive || wildcardRoot.activeSelf)
                        MatchPattern(wildcardRoot, wildcardRoot.name, parts, 0, includeInactive, matches, seen);
                }
                else
                {
                    foreach (var wildcardRoot in GetAllSceneRoots())
                    {
                        if (!includeInactive && !wildcardRoot.activeSelf) continue;
                        MatchPattern(wildcardRoot, wildcardRoot.name, parts, 0, includeInactive, matches, seen);
                    }
                }

                if (matches.Count == 0)
                    return McpUtils.Error($"No objects matched pattern: {path}");

                var matchNodes = new List<object>();
                foreach (var (go, goPath) in matches)
                    matchNodes.Add(BuildNode(go, goPath, depth, includeInactive));

                return McpUtils.Success(new
                {
                    sceneName,
                    isPrefabMode,
                    matchCount = matches.Count,
                    objects = matchNodes
                });
            }

            if (!string.IsNullOrEmpty(path))
            {
                GameObject target;
                if (isPrefabMode)
                    target = FindByPathInPrefabStage(prefabStage.prefabContentsRoot, path);
                else
                    target = McpUtils.FindByPath(path);

                if (target == null)
                    return McpUtils.Error($"Object not found: {path}");

                return McpUtils.Success(new
                {
                    sceneName,
                    isPrefabMode,
                    objects = new[] { BuildNode(target, path, depth, includeInactive) }
                });
            }

            var nodes = new List<object>();
            if (isPrefabMode)
            {
                var root = prefabStage.prefabContentsRoot;
                nodes.Add(BuildNode(root, root.name, depth, includeInactive));
            }
            else
            {
                var roots = SceneManager.GetActiveScene().GetRootGameObjects();
                foreach (var root in roots)
                {
                    if (!includeInactive && !root.activeSelf) continue;
                    nodes.Add(BuildNode(root, root.name, depth, includeInactive));
                }

                if (Application.isPlaying)
                {
                    foreach (var root in GetDontDestroyOnLoadRoots())
                    {
                        if (!includeInactive && !root.activeSelf) continue;
                        nodes.Add(BuildNode(root, root.name, depth, includeInactive));
                    }
                }
            }

            return McpUtils.Success(new
            {
                sceneName,
                isPrefabMode,
                objects = nodes
            });
        }

        private static GameObject FindByPathInPrefabStage(GameObject root, string path)
        {
            var parts = path.Split('/');
            if (parts[0] != root.name)
                return null;

            var current = root.transform;
            for (int i = 1; i < parts.Length; i++)
            {
                var child = current.Find(parts[i]);
                if (child == null) return null;
                current = child;
            }
            return current.gameObject;
        }

        public static string Save(JObject body)
        {
            var prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
            if (prefabStage != null)
            {
                UnityEditor.PrefabUtility.SaveAsPrefabAsset(
                    prefabStage.prefabContentsRoot,
                    prefabStage.assetPath);
                return McpUtils.Success(new { prefab = prefabStage.assetPath });
            }

            var scene = SceneManager.GetActiveScene();
            EditorSceneManager.SaveScene(scene);
            return McpUtils.Success(new { scene = scene.name });
        }

        public static string Open(JObject body)
        {
            var scenePath = body.Value<string>("scenePath");
            if (string.IsNullOrEmpty(scenePath))
                return McpUtils.Error("scenePath is required");

            var force = body.Value<bool?>("force") ?? true;
            if (!force)
                EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo();

            EditorSceneManager.OpenScene(scenePath);
            return McpUtils.Success(new { opened = scenePath });
        }

        public static string NewScene(JObject body)
        {
            var force = body.Value<bool?>("force") ?? true;
            if (!force)
                EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            return McpUtils.Success(new { sceneName = scene.name });
        }

        private static GameObject[] GetDontDestroyOnLoadRoots()
        {
            var temp = new GameObject("[temp_ddol_query]");
            Object.DontDestroyOnLoad(temp);
            var ddolScene = temp.scene;
            Object.DestroyImmediate(temp);
            return ddolScene.GetRootGameObjects();
        }

        private static bool ContainsWildcard(string path) => path.IndexOf('*') >= 0;

        private static List<GameObject> GetAllSceneRoots()
        {
            var list = new List<GameObject>(SceneManager.GetActiveScene().GetRootGameObjects());
            if (Application.isPlaying)
                list.AddRange(GetDontDestroyOnLoadRoots());
            return list;
        }

        private static void MatchPattern(
            GameObject node, string currentPath, string[] parts, int index,
            bool includeInactive, List<(GameObject go, string path)> results, HashSet<int> seen)
        {
            if (index >= parts.Length) return;

            var segment = parts[index];
            var isLast = index == parts.Length - 1;

            if (segment == "**")
            {
                if (isLast)
                {
                    if (seen.Add(node.GetInstanceID()))
                        results.Add((node, currentPath));
                }
                else
                {
                    MatchPattern(node, currentPath, parts, index + 1, includeInactive, results, seen);
                }

                var t = node.transform;
                for (int i = 0; i < t.childCount; i++)
                {
                    var child = t.GetChild(i).gameObject;
                    if (!includeInactive && !child.activeSelf) continue;
                    var childPath = $"{currentPath}/{child.name}";
                    MatchPattern(child, childPath, parts, index, includeInactive, results, seen);
                }
                return;
            }

            if (!NameMatches(node.name, segment)) return;

            if (isLast)
            {
                if (seen.Add(node.GetInstanceID()))
                    results.Add((node, currentPath));
                return;
            }

            var nextIdx = index + 1;
            if (parts[nextIdx] == "**")
            {
                MatchPattern(node, currentPath, parts, nextIdx, includeInactive, results, seen);
                return;
            }

            var tr = node.transform;
            for (int i = 0; i < tr.childCount; i++)
            {
                var child = tr.GetChild(i).gameObject;
                if (!includeInactive && !child.activeSelf) continue;
                var childPath = $"{currentPath}/{child.name}";
                MatchPattern(child, childPath, parts, nextIdx, includeInactive, results, seen);
            }
        }

        private static bool NameMatches(string name, string pattern)
        {
            if (pattern.IndexOf('*') < 0)
                return name == pattern;

            var regex = "^" + Regex.Escape(pattern).Replace("\\*", ".*") + "$";
            return Regex.IsMatch(name, regex);
        }

        private static object BuildNode(GameObject go, string currentPath, int depth, bool includeInactive)
        {
            var components = new List<string>();
            foreach (var c in go.GetComponents<Component>())
            {
                if (c != null) components.Add(c.GetType().Name);
            }

            object[] children = null;
            if (depth > 0 && go.transform.childCount > 0)
            {
                var list = new List<object>();
                for (int i = 0; i < go.transform.childCount; i++)
                {
                    var child = go.transform.GetChild(i).gameObject;
                    if (!includeInactive && !child.activeSelf) continue;
                    var childPath = $"{currentPath}/{child.name}";
                    list.Add(BuildNode(child, childPath, depth - 1, includeInactive));
                }
                children = list.ToArray();
            }

            return new
            {
                instanceId = go.GetInstanceID(),
                name = go.name,
                path = currentPath,
                activeSelf = go.activeSelf,
                tag = go.tag,
                layer = go.layer,
                components,
                childCount = go.transform.childCount,
                children
            };
        }
    }
}
