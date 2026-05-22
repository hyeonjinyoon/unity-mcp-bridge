using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace UnityMcpBridge.Editor.Handlers
{
    public static class EditorLogHandler
    {
        private const int DefaultLineCount = 200;
        private const int DefaultTailBytes = 1_000_000;

        public static string Get(JObject body)
        {
            var lineCount = body.Value<int?>("lines") ?? DefaultLineCount;
            var grep = body.Value<string>("grep");
            var tailBytes = body.Value<int?>("tailBytes") ?? DefaultTailBytes;

            var path = Application.consoleLogPath;
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return McpUtils.Error($"Editor log file not found: {path ?? "(empty)"}");

            Regex regex = null;
            if (!string.IsNullOrEmpty(grep))
            {
                try
                {
                    regex = new Regex(grep, RegexOptions.Compiled);
                }
                catch (Exception ex)
                {
                    return McpUtils.Error($"Invalid grep pattern: {ex.Message}");
                }
            }

            var lines = new List<string>();
            long fileLength;

            try
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete);
                fileLength = fs.Length;

                var skipFirstPartial = fileLength > tailBytes;
                if (skipFirstPartial)
                    fs.Seek(-tailBytes, SeekOrigin.End);

                using var reader = new StreamReader(fs);
                string line;
                var first = true;
                while ((line = reader.ReadLine()) != null)
                {
                    if (first && skipFirstPartial)
                    {
                        first = false;
                        continue;
                    }
                    first = false;

                    if (regex != null && !regex.IsMatch(line))
                        continue;

                    lines.Add(line);
                }
            }
            catch (Exception ex)
            {
                return McpUtils.Error($"Failed to read editor log: {ex.Message}");
            }

            var start = Math.Max(0, lines.Count - lineCount);
            var tail = lines.GetRange(start, lines.Count - start);

            return McpUtils.Success(new
            {
                path,
                fileLength,
                matchedLines = lines.Count,
                returnedLines = tail.Count,
                lines = tail,
            });
        }
    }
}
