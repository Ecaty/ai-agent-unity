using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Ecaty.Agents.UI
{
    public sealed class AgentChatPanel : MonoBehaviour
    {
        private AgentChatController controller;
        public AgentChatController Controller => controller;
        private IAgentProvider provider;
        public static AgentChatPanel Create(AgentUiAssets assets, IAgentProvider provider, IAgentAuthenticator authentication,
            string instructions, IReadOnlyList<IAgentTool> tools, string connectLabel, string connectionHint = "Connect to start.")
        {
            if (!assets || !assets.Layout || !assets.Panel) throw new InvalidOperationException("Chat UI assets are missing.");
            var root = new GameObject("Agent Chat");
            root.SetActive(false);
            var document = root.AddComponent<UIDocument>();
            document.panelSettings = assets.Panel;
            var panel = root.AddComponent<AgentChatPanel>();
            panel.provider = provider;
            DontDestroyOnLoad(root);
            root.SetActive(true);
            var view = new AgentChatView(assets.Layout);
            view.Root.style.position = Position.Absolute;
            view.Root.style.right = 12; view.Root.style.bottom = 12;
            view.Root.style.width = 380; view.Root.style.maxHeight = 320;
            document.rootVisualElement.Add(view.Root);
            panel.controller = new AgentChatController(view, provider, authentication, instructions, tools, connectLabel, connectionHint);
            return panel;
        }
        private void OnDestroy() { controller?.Dispose(); (provider as IDisposable)?.Dispose(); }
    }
}
