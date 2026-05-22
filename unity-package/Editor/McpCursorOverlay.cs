using UnityEngine;
using UnityEngine.UI;

namespace UnityMcpBridge.Editor
{
    public static class McpCursorOverlay
    {
        private const int DefaultCursorSize = 64;

        private static Canvas _canvas;
        private static RectTransform _cursor;
        private static Sprite _generatedSprite;

        public static void Show(float x, float y)
        {
            if (!Application.isPlaying) return;

            EnsureCreated();

            _cursor.gameObject.SetActive(true);
            _cursor.position = new Vector3(x, y, 0f);
        }

        private static void EnsureCreated()
        {
            if (_canvas != null) return;

            var settings = McpBridgeSettings.GetOrCreate();
            var size = settings != null && settings.cursorSizePx > 0
                ? settings.cursorSizePx
                : DefaultCursorSize;

            var canvasGo = new GameObject("[MCP] CursorOverlay");
            Object.DontDestroyOnLoad(canvasGo);
            _canvas = canvasGo.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 30000;

            var cursorGo = new GameObject("Cursor");
            cursorGo.transform.SetParent(canvasGo.transform, false);
            _cursor = cursorGo.AddComponent<RectTransform>();
            _cursor.sizeDelta = new Vector2(size, size);

            var image = cursorGo.AddComponent<Image>();
            image.sprite = ResolveSprite(settings, size);
            image.raycastTarget = false;
            image.color = new Color(1f, 0f, 1f, 0.9f);
        }

        private static Sprite ResolveSprite(McpBridgeSettings settings, int size)
        {
            if (settings != null && settings.cursorSprite != null)
                return settings.cursorSprite;

            if (_generatedSprite != null) return _generatedSprite;

            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.DontSave,
                filterMode = FilterMode.Point
            };
            var pixels = new Color32[size * size];
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = new Color32(255, 255, 255, 255);
            tex.SetPixels32(pixels);
            tex.Apply();

            _generatedSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
            _generatedSprite.hideFlags = HideFlags.DontSave;
            return _generatedSprite;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            _canvas = null;
            _cursor = null;
            _generatedSprite = null;
        }
    }
}
