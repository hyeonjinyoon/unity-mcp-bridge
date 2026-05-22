using System.IO;
using UnityEditor;
using UnityEngine;

namespace UnityMcpBridge.Editor
{
    /// <summary>
    /// 프로젝트별 MCP Bridge 설정. SerializedObject 형태로 저장되어 prefab/씬 외부에서 공유된다.
    ///
    /// 모든 필드는 선택 사항이다. 미설정 시 합리적인 fallback이 동작한다:
    /// - InitScene 미설정 → unity_play(fromCurrent=false)는 현재 씬을 그대로 사용한다(=fromCurrent 동작).
    /// - CursorSprite 미설정 → 64x64 절차적 마젠타 텍스처를 자동 생성하여 사용한다.
    /// </summary>
    public sealed class McpBridgeSettings : ScriptableObject
    {
        private const string SettingsFolder = "Assets/Editor/UnityMcpBridge";
        private const string SettingsAssetPath = SettingsFolder + "/McpBridgeSettings.asset";

        [Tooltip("Optional. unity_play(fromCurrent=false) will switch to this scene and restore the original scene on stop. If null, unity_play falls back to fromCurrent behaviour.")]
        public SceneAsset initScene;

        [Tooltip("Optional. Sprite shown by McpCursorOverlay after tap/hold/swipe. If null, a 64x64 magenta texture is generated procedurally.")]
        public Sprite cursorSprite;

        [Tooltip("Cursor overlay size in pixels (default: 64).")]
        public int cursorSizePx = 64;

        private static McpBridgeSettings _cached;

        public static McpBridgeSettings GetOrCreate()
        {
            if (_cached != null) return _cached;

            var existing = AssetDatabase.LoadAssetAtPath<McpBridgeSettings>(SettingsAssetPath);
            if (existing != null)
            {
                _cached = existing;
                return existing;
            }

            return null;
        }

        public static McpBridgeSettings GetOrCreatePersistent()
        {
            var existing = GetOrCreate();
            if (existing != null) return existing;

            if (!Directory.Exists(SettingsFolder))
                Directory.CreateDirectory(SettingsFolder);

            var asset = CreateInstance<McpBridgeSettings>();
            AssetDatabase.CreateAsset(asset, SettingsAssetPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            _cached = asset;
            return asset;
        }

        [MenuItem("Tools/MCP Bridge/Open Settings")]
        private static void OpenSettings()
        {
            var asset = GetOrCreatePersistent();
            Selection.activeObject = asset;
            EditorGUIUtility.PingObject(asset);
        }
    }
}
