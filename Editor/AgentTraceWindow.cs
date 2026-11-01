using System;
using System.Collections.Generic;
using System.Linq;
using Ecaty.Agents.Diagnostics;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Ecaty.Agents.Editor
{
    public sealed class AgentTraceWindow : EditorWindow
    {
        private ScrollView rows;
        private int revision = -1;
        private string search = "";
        private bool errorsOnly, expandDefault;
        private readonly Dictionary<string, bool> expanded = new Dictionary<string, bool>();
        [MenuItem("Ecaty/Agent Trace")]
        public static void Open() => GetWindow<AgentTraceWindow>("Agent Trace");
        public void CreateGUI()
        {
            rootVisualElement.Clear();
            var toolbar = new Toolbar();
            var capture = new ToolbarToggle { text = "Capture locally", value = AgentTrace.Enabled };
            capture.RegisterValueChangedCallback(e => AgentTrace.Enabled = e.newValue);
            toolbar.Add(capture);
            toolbar.Add(new ToolbarButton(() => ExpandAll(true)) { text = "Expand all" });
            toolbar.Add(new ToolbarButton(() => ExpandAll(false)) { text = "Collapse all" });
            toolbar.Add(new ToolbarButton(() => AgentTrace.ClearView()) { text = "Clear view" });
            toolbar.Add(new ToolbarButton(() => { if (!string.IsNullOrEmpty(AgentTrace.FilePath)) EditorUtility.RevealInFinder(AgentTrace.FilePath); }) { text = "Log file" });
            rootVisualElement.Add(toolbar);
            var filters = new Toolbar();
            var find = new ToolbarSearchField(); find.style.flexGrow = 1;
            find.RegisterValueChangedCallback(e => { search = e.newValue; Rebuild(); }); filters.Add(find);
            var errorFilter = new ToolbarToggle { text = "Errors only" };
            errorFilter.RegisterValueChangedCallback(e => { errorsOnly = e.newValue; Rebuild(); }); filters.Add(errorFilter);
            rootVisualElement.Add(filters);
            var hint = new Label("Local developer trace · inputs/results may contain private world data · credentials are never captured");
            hint.style.whiteSpace = WhiteSpace.Normal; hint.style.marginBottom = 6; rootVisualElement.Add(hint);
            rows = new ScrollView(); rows.style.flexGrow = 1; rootVisualElement.Add(rows);
            Rebuild();
        }
        private void ExpandAll(bool value) { expandDefault = value; expanded.Clear(); Rebuild(); }
        private void OnEnable() => EditorApplication.update += Refresh;
        private void OnDisable() => EditorApplication.update -= Refresh;
        private void Refresh() { if (rows != null && revision != AgentTrace.Revision) Rebuild(); }
        private Foldout Fold(string key, string title)
        {
            var fold = new Foldout { text = title, value = expanded.TryGetValue(key, out var value) ? value : expandDefault };
            fold.RegisterValueChangedCallback(e => { if (e.target == fold) expanded[key] = e.newValue; });
            return fold;
        }
        private static bool IsError(AgentTraceEntry e) => e.Stage == "error" || (e.Result?.Contains("\"error\"") ?? false);
        private static string Short(string text)
        {
            text = (text ?? "").Replace('\n', ' ').Replace('\r', ' ');
            return text.Length > 110 ? text.Substring(0, 110) + "…" : text;
        }
        private static void Detail(VisualElement parent, string label, string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            var bar = new VisualElement(); bar.style.flexDirection = FlexDirection.Row;
            var title = new Label(label); title.style.flexGrow = 1; bar.Add(title);
            bar.Add(new Button(() => EditorGUIUtility.systemCopyBuffer = text) { text = "Copy" }); parent.Add(bar);
            var field = new TextField { value = text, multiline = true, isReadOnly = true };
            field.style.whiteSpace = WhiteSpace.Normal; parent.Add(field);
        }
        private void Rebuild()
        {
            if (rows == null) return;
            revision = AgentTrace.Revision;
            var offset = rows.scrollOffset; rows.Clear();
            foreach (var group in AgentTrace.Snapshot().GroupBy(e => e.Request))
            {
                var entries = group.ToArray();
                if (errorsOnly && !entries.Any(IsError)) continue;
                if (search.Length > 0 && !entries.Any(e => (e.Name + " " + e.Arguments + " " + e.Result).IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0)) continue;
                var first = entries[0]; var last = entries[entries.Length - 1];
                string status = entries.Any(IsError) ? "ERROR" : last.Stage == "completed" ? "Done" : "Running";
                var request = Fold(group.Key, $"{first.Time.Substring(11, 8)} · {status} · {Short(first.Arguments ?? first.Name)}");
                request.style.marginBottom = 10; request.style.paddingTop = 6; request.style.paddingBottom = 6;
                request.style.backgroundColor = new Color(.2f, .22f, .25f, .35f);
                request.Add(new Button(() => EditorGUIUtility.systemCopyBuffer = JsonConvert.SerializeObject(entries, Formatting.Indented)) { text = "Copy request trace" });
                Detail(request, "You", first.Arguments);
                int step = 0;
                foreach (var entry in entries)
                {
                    if (entry.Stage == "tool-start") continue;
                    if (entry.Stage == "tool-result")
                    {
                        var tool = Fold(group.Key + ":" + step++, $"{entry.Name} · {(IsError(entry) ? "ERROR" : "OK")} · {entry.Milliseconds} ms");
                        Detail(tool, "Input", entry.Arguments); Detail(tool, "Result", entry.Result); request.Add(tool);
                    }
                    else if (entry.Stage == "completed" || entry.Stage == "error") Detail(request, entry.Stage == "error" ? "Error" : "Assistant", entry.Result);
                }
                rows.Add(request);
            }
            rows.scrollOffset = offset;
        }
    }
}
