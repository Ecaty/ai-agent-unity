using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Newtonsoft.Json;

namespace Ecaty.Agents.Diagnostics
{
    public sealed class AgentTraceEntry
    {
        public string Time, Request, Stage, Name, Arguments, Result;
        public long Milliseconds;
    }
    public static class AgentTrace
    {
        private static readonly object Gate = new object();
        private static readonly List<AgentTraceEntry> entries = new List<AgentTraceEntry>();
        private static string folder, file;
        private static int revision;
        public static bool Enabled { get; set; }
        public static int Revision { get { lock (Gate) return revision; } }
        public static string FilePath { get { lock (Gate) return file; } }
        public static void Configure(string directory, bool enabled)
        { lock (Gate) { if (folder != directory) file = null; folder = directory; Enabled = enabled; } }
        public static AgentTraceEntry[] Snapshot() { lock (Gate) return entries.ToArray(); }
        public static void ClearView() { lock (Gate) { entries.Clear(); revision++; } }
        public static void Record(string request, string stage, string name, string arguments = null, string result = null, long milliseconds = 0)
        {
            if (!Enabled) return;
            var entry = new AgentTraceEntry { Time = DateTimeOffset.UtcNow.ToString("O"), Request = request,
                Stage = stage, Name = name, Arguments = Redact(arguments), Result = Redact(result), Milliseconds = milliseconds };
            lock (Gate)
            {
                entries.Add(entry); if (entries.Count > 500) entries.RemoveAt(0); revision++;
                if (string.IsNullOrEmpty(folder)) return;
                try
                {
                    Directory.CreateDirectory(folder);
                    if (file == null || (System.IO.File.Exists(file) && new FileInfo(file).Length > 10 * 1024 * 1024))
                        file = Path.Combine(folder, DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 6) + ".jsonl");
                    System.IO.File.AppendAllText(file, JsonConvert.SerializeObject(entry) + Environment.NewLine);
                }
                catch (IOException) { } // Diagnostics must not break a user's request.
                catch (UnauthorizedAccessException) { }
            }
        }
        private static string Redact(string text)
        {
            if (text == null) return null;
            if (text.Length > 65536) text = text.Substring(0, 65536) + " [trace truncated]";
            text = Regex.Replace(text, @"(?i)Bearer\s+[^\s""']+", "Bearer [redacted]");
            text = Regex.Replace(text, @"\beyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\b", "[redacted JWT]");
            text = Regex.Replace(text, @"\b(?:sk-(?:proj-)?[A-Za-z0-9_-]{20,}|gh[pousr]_[A-Za-z0-9]{20,})\b", "[redacted key]");
            return text;
        }
    }
}
