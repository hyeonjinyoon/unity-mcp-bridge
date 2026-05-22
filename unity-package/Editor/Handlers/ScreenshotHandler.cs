using System;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace UnityMcpBridge.Editor.Handlers
{
    public static class ScreenshotHandler
    {
        private const int FileWaitTimeoutMs = 3000;
        private const int PollIntervalMs = 50;
        private const int MaxResponseHeight = 720;

        // 모든 스크린샷은 프로젝트 루트의 ScreenShots/ 폴더로 저장한다. 호출자가 fileName을 지정해도
        // Path.GetFileName()으로 디렉터리 구분자를 제거하므로 저장 위치는 항상 ScreenShots/ 안으로 강제된다.
        private const string ScreenshotFolder = "ScreenShots";

        public static async Task<string> CaptureAsync(JObject body)
        {
            var projectRoot = Path.GetDirectoryName(Application.dataPath);

            var fileName = ResolveFileName(body);
            var captureRelPath = Path.Combine(ScreenshotFolder, fileName);
            var fullPath = Path.Combine(projectRoot, captureRelPath);
            var dir = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            // 1. Main thread: schedule capture. Force a player loop update + repaint
            // so Edit-mode Game View has a fresh frame before delayCall fires.
            await McpMainThreadDispatcher.RunOnMainThread(() =>
            {
                var gameViewType = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
                var gameViews = Resources.FindObjectsOfTypeAll(gameViewType);
                var gameView = gameViews.Length > 0
                    ? (EditorWindow)gameViews[0]
                    : EditorWindow.GetWindow(gameViewType, utility: false, title: null, focus: false);

                // Play 모드는 Game View가 OS 포커스와 무관하게 계속 렌더링되므로 Focus() 생략해
                // 코딩 중 포커스 훔김을 회피한다. Edit 모드는 Unity 제약상 Game View가 OS 전경에
                // 있어야만 렌더 파이프라인이 돌기 때문에 Focus() 호출이 불가피하다.
                if (!EditorApplication.isPlaying)
                    gameView.Focus();
                gameView.Repaint();

                EditorApplication.QueuePlayerLoopUpdate();
                InternalEditorUtility.RepaintAllViews();

                EditorApplication.delayCall += () =>
                {
                    ScreenCapture.CaptureScreenshot(captureRelPath);
                };
                return string.Empty;
            });

            // 2. HTTP worker thread: poll for the file to be fully written.
            byte[] rawBytes = null;
            var deadline = DateTime.UtcNow.AddMilliseconds(FileWaitTimeoutMs);
            while (DateTime.UtcNow < deadline)
            {
                if (File.Exists(fullPath))
                {
                    try
                    {
                        var bytes = File.ReadAllBytes(fullPath);
                        if (bytes.Length > 0)
                        {
                            rawBytes = bytes;
                            break;
                        }
                    }
                    catch (IOException)
                    {
                        // Still being written, keep polling
                    }
                }
                await Task.Delay(PollIntervalMs);
            }

            if (rawBytes == null)
            {
                return McpUtils.Error($"Screenshot file was not ready within {FileWaitTimeoutMs}ms: {fullPath}");
            }

            // 3. Main thread: Texture decode + downscale to MaxResponseHeight + PNG encode.
            var originalWidth = 0;
            var originalHeight = 0;
            var responseWidth = 0;
            var responseHeight = 0;

            var base64 = await McpMainThreadDispatcher.RunOnMainThread(() =>
            {
                var responseBytes = EncodeDownscaled(
                    rawBytes, MaxResponseHeight,
                    out originalWidth, out originalHeight,
                    out responseWidth, out responseHeight);
                return Convert.ToBase64String(responseBytes);
            });

            var note = originalWidth > 0
                ? $"{originalWidth}x{originalHeight} → {responseWidth}x{responseHeight}"
                : null;

            return McpUtils.Success(new
            {
                path = fullPath,
                imageBase64 = base64,
                mimeType = "image/png",
                note
            });
        }

        private static string ResolveFileName(JObject body)
        {
            var raw = body?["fileName"]?.ToString();
            if (string.IsNullOrWhiteSpace(raw))
                return $"ScreenShot_{DateTime.Now:yyMMdd_HHmmss_fff}.png";

            var name = Path.GetFileName(raw.Trim());
            foreach (var c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');

            if (string.IsNullOrEmpty(name))
                return $"ScreenShot_{DateTime.Now:yyMMdd_HHmmss_fff}.png";

            if (!name.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                name += ".png";

            return name;
        }

        private static byte[] EncodeDownscaled(
            byte[] originalPng, int maxHeight,
            out int originalWidth, out int originalHeight,
            out int responseWidth, out int responseHeight)
        {
            originalWidth = 0;
            originalHeight = 0;
            responseWidth = 0;
            responseHeight = 0;

            var tex = new Texture2D(2, 2);
            try
            {
                if (!tex.LoadImage(originalPng))
                    return originalPng;

                originalWidth = tex.width;
                originalHeight = tex.height;

                if (tex.height <= maxHeight)
                {
                    responseWidth = originalWidth;
                    responseHeight = originalHeight;
                    return originalPng;
                }

                var ratio = (float)maxHeight / tex.height;
                var newWidth = Mathf.Max(1, Mathf.RoundToInt(tex.width * ratio));
                var newHeight = maxHeight;

                responseWidth = newWidth;
                responseHeight = newHeight;

                var rt = RenderTexture.GetTemporary(newWidth, newHeight, 0);
                var prevActive = RenderTexture.active;
                Texture2D resized = null;
                try
                {
                    Graphics.Blit(tex, rt);
                    RenderTexture.active = rt;
                    resized = new Texture2D(newWidth, newHeight, TextureFormat.RGBA32, false);
                    resized.ReadPixels(new Rect(0, 0, newWidth, newHeight), 0, 0);
                    resized.Apply();
                    return resized.EncodeToPNG();
                }
                finally
                {
                    RenderTexture.active = prevActive;
                    RenderTexture.ReleaseTemporary(rt);
                    if (resized != null) UnityEngine.Object.DestroyImmediate(resized);
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(tex);
            }
        }
    }
}
