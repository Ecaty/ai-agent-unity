using System;
using System.Collections.Generic;
using Ecaty.Agents.UI;
using UnityEditor;
using UnityEngine.UIElements;

namespace Ecaty.Agents.Editor
{
    public static class AgentPackageVerification
    {
        private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
        public static string RunAll()
        {
            var checks = new List<string>();
            var layout = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Packages/com.ecaty.ai-agent/Runtime/UI/AgentChat.uxml");
            Require(layout, "Chat UXML did not import");
            using (var view = new AgentChatView(layout))
            {
                string submitted = null;
                view.MessageSubmitted += text => submitted = text;
                view.SetDraft("  make it blue  ");
                Require(!view.TrySubmit(), "Disconnected UI accepted input");
                view.SetReady(true);
                Require(view.TrySubmit() && submitted == "make it blue", "Message was not routed through the generic event");
                Require(!view.TrySubmit(), "Empty input accepted");
                Require(!view.Root.Q<Button>("sendButton").enabledSelf, "Send remained enabled after clearing the draft");
                checks.Add("UXML imported; disconnected/empty submissions blocked; provider-neutral message event");
                view.SetDraft("second request");
                view.SetBusy(true);
                Require(!view.TrySubmit(), "Busy UI accepted input");
                view.SetBusy(false);
                Require(view.TrySubmit(), "UI did not recover after request completion");
                Require(!view.Root.Q<Button>("microphoneButton").enabledSelf, "Unconfigured microphone enabled");
                view.SetDictationAvailable(true);
                Require(view.Root.Q<Button>("microphoneButton").enabledSelf, "Speech adapter availability not reflected");
                checks.Add("busy state and optional speech capability control input");
                var message = view.AppendMessage("<b>literal model text</b>", false);
                Require(!message.enableRichText && message.text == "<b>literal model text</b>", "Model text interpreted as UI markup");
                checks.Add("model text remains literal; no Flecs or provider SDK dependency");
            }
            var disposed = new AgentChatView(layout);
            disposed.Dispose();
            try { disposed.SetReady(true); throw new Exception("Disposed UI remained active"); }
            catch (ObjectDisposedException) { checks.Add("disposed view rejects further use"); }
            return string.Join("; ", checks);
        }
    }
}
