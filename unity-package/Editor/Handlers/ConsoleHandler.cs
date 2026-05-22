using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace UnityMcpBridge.Editor.Handlers
{
    [InitializeOnLoad]
    public static class ConsoleHandler
    {
        private struct LogEntry
        {
            public string message;
            public string stackTrace;
            public string type;
            public string timestamp;
        }

        private static readonly List<LogEntry> Logs = new();
        private const int MaxLogs = 500;

        static ConsoleHandler()
        {
            Application.logMessageReceived -= OnLog;
            Application.logMessageReceived += OnLog;
        }

        private static void OnLog(string message, string stackTrace, LogType type)
        {
            lock (Logs)
            {
                Logs.Add(new LogEntry
                {
                    message = message,
                    stackTrace = stackTrace,
                    type = type == LogType.Exception ? "Error" : type.ToString(),
                    timestamp = DateTime.Now.ToString("HH:mm:ss.fff")
                });

                if (Logs.Count > MaxLogs)
                    Logs.RemoveRange(0, Logs.Count - MaxLogs);
            }
        }

        public static string Get(JObject body)
        {
            var count = body.Value<int?>("count") ?? 50;
            var typeFilter = body.Value<string>("type");
            var collapse = body.Value<bool?>("collapse") ?? true;
            var since = body.Value<string>("since");
            var sinceSeconds = body.Value<int?>("sinceSeconds");
            var clear = body.Value<bool?>("clear") ?? false;

            DateTime? sinceTime = null;
            if (sinceSeconds.HasValue)
                sinceTime = DateTime.Now.AddSeconds(-sinceSeconds.Value);
            else if (since != null && DateTime.TryParseExact(since,
                         new[] { "HH:mm:ss", "HH:mm:ss.fff" }, null,
                         System.Globalization.DateTimeStyles.None, out var parsed))
                sinceTime = parsed;

            lock (Logs)
            {
                var filtered = new List<object>();
                for (int i = Logs.Count - 1; i >= 0 && filtered.Count < count; i--)
                {
                    var log = Logs[i];
                    if (typeFilter != null && !log.type.Equals(typeFilter, StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (sinceTime.HasValue && DateTime.TryParseExact(log.timestamp,
                            "HH:mm:ss.fff", null,
                            System.Globalization.DateTimeStyles.None, out var logTime)
                        && logTime < sinceTime.Value)
                        break;

                    if (collapse)
                    {
                        var firstLine = log.message;
                        var nlIdx = firstLine.IndexOf('\n');
                        if (nlIdx >= 0) firstLine = firstLine.Substring(0, nlIdx);
                        filtered.Add(new
                        {
                            message = firstLine,
                            log.type,
                            log.timestamp
                        });
                    }
                    else
                    {
                        filtered.Add(new
                        {
                            log.message,
                            log.stackTrace,
                            log.type,
                            log.timestamp
                        });
                    }
                }

                filtered.Reverse();

                if (clear) Logs.Clear();

                return McpUtils.Success(new { total = Logs.Count, logs = filtered });
            }
        }

        public static string Clear(JObject body)
        {
            lock (Logs)
            {
                var count = Logs.Count;
                Logs.Clear();
                return McpUtils.Success(new { cleared = count });
            }
        }
    }
}
