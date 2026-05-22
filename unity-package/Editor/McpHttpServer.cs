using System;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace UnityMcpBridge.Editor
{
    [InitializeOnLoad]
    public static class McpHttpServer
    {
        private const int BasePort = 29400;
        private const int PortRange = 100;
        private const int MaxPortRetries = 5;

        private static HttpListener _listener;
        private static CancellationTokenSource _cts;
        private static int _port;
        private static bool _running;

        static McpHttpServer()
        {
            Start();
            AssemblyReloadEvents.beforeAssemblyReload += Stop;
            EditorApplication.quitting += Stop;
        }

        private static int PreferredPort
        {
            get
            {
                var projectPath = ProjectPath;
                using var md5 = MD5.Create();
                var hash = md5.ComputeHash(Encoding.UTF8.GetBytes(projectPath));
                return BasePort + (BitConverter.ToUInt16(hash, 0) % PortRange);
            }
        }

        [MenuItem("Tools/MCP Bridge/Start Server")]
        public static void Start()
        {
            if (_running) return;

            var preferred = PreferredPort;
            for (int i = 0; i < MaxPortRetries; i++)
            {
                var port = preferred + i;
                if (port >= BasePort + PortRange) port -= PortRange;
                try
                {
                    _listener = new HttpListener();
                    // "localhost" can resolve to IPv6-only on some Windows hosts, leaving the
                    // server (which connects via 127.0.0.1) unable to reach the listener.
                    // Bind to IPv4 explicitly so connection succeeds regardless of the OS's
                    // localhost resolution order.
                    _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
                    _listener.Start();
                    _port = port;
                    _running = true;
                    _cts = new CancellationTokenSource();

                    File.WriteAllText(PortFilePath, _port.ToString());

                    Task.Run(() => ListenLoop(_cts.Token));
                    Debug.Log($"[MCP Bridge] Server started on port {_port} (preferred: {preferred}, id: {ProjectId})");
                    return;
                }
                catch (Exception)
                {
                    _listener?.Close();
                    _listener = null;
                }
            }

            Debug.LogError("[MCP Bridge] Failed to start - no available port");
        }

        [MenuItem("Tools/MCP Bridge/Stop Server")]
        public static void Stop()
        {
            if (!_running) return;
            _running = false;
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;

            try { _listener?.Close(); } catch { }
            _listener = null;

            try { if (File.Exists(PortFilePath)) File.Delete(PortFilePath); } catch { }

            Debug.Log("[MCP Bridge] Server stopped");
        }

        private static string ProjectPath =>
            Path.GetDirectoryName(Application.dataPath);

        private static string ProjectId
        {
            get
            {
                using var md5 = MD5.Create();
                var hash = md5.ComputeHash(Encoding.UTF8.GetBytes(ProjectPath));
                return BitConverter.ToString(hash, 0, 4).Replace("-", "").ToLower();
            }
        }

        private static string PortFilePath =>
            Path.Combine(Path.GetTempPath(), $"unity-mcp-port-{ProjectId}.txt");

        private static async Task ListenLoop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested && _running)
            {
                try
                {
                    var context = await _listener.GetContextAsync();
                    _ = Task.Run(() => HandleRequest(context));
                }
                catch when (ct.IsCancellationRequested || !_running)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[MCP Bridge] Listen error: {ex.Message}");
                }
            }
        }

        private static async Task HandleRequest(HttpListenerContext context)
        {
            var request = context.Request;
            var response = context.Response;

            response.Headers.Add("Access-Control-Allow-Origin", "*");
            response.ContentType = "application/json; charset=utf-8";

            string responseJson;
            try
            {
                string body = null;
                if (request.HasEntityBody)
                {
                    using var reader = new StreamReader(request.InputStream, Encoding.UTF8);
                    body = await reader.ReadToEndAsync();
                }

                var path = request.Url.AbsolutePath;

                if (path == "/api/health" && request.HttpMethod == "GET")
                {
                    responseJson = await McpMainThreadDispatcher.RunOnMainThread(
                        McpRequestRouter.HandleHealth);
                }
                else if (request.HttpMethod == "POST")
                {
                    var requestBody = string.IsNullOrEmpty(body) ? new JObject() : JObject.Parse(body);
                    responseJson = await McpRequestRouter.RouteAsync(path, requestBody);
                }
                else
                {
                    responseJson = McpUtils.Error("Method not allowed");
                    response.StatusCode = 405;
                }
            }
            catch (Exception ex)
            {
                responseJson = McpUtils.Error(ex.Message);
                response.StatusCode = 500;
            }

            var buffer = Encoding.UTF8.GetBytes(responseJson);
            response.ContentLength64 = buffer.Length;
            await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
            response.Close();
        }
    }
}
