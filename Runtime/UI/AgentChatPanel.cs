using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Ecaty.Agents.UI
{
    [RequireComponent(typeof(PanelRenderer))]
    public sealed class AgentChatPanel : MonoBehaviour
    {
        private AgentChatController controller;
        private AgentChatView view;
        private IAgentProvider provider;
        private PanelRenderer panelRenderer;
        private VisualElement host;
        public AgentChatController Controller => controller;

        public static AgentChatPanel Create(AgentUiAssets assets, IAgentProvider provider, IAgentAuthenticator authentication,
            string instructions, IReadOnlyList<IAgentTool> tools, string connectLabel, string connectionHint = "")
        {
            if (!assets || !assets.Layout || !assets.Panel) throw new InvalidOperationException("Chat UI assets are missing.");
            var root = new GameObject("Agent Chat");
            root.SetActive(false);
            var renderer = root.AddComponent<PanelRenderer>();
            renderer.panelSettings = assets.Panel;
            renderer.sortingOrder = 100;
            var panel = root.AddComponent<AgentChatPanel>();
            panel.panelRenderer = renderer;
            panel.provider = provider;
            panel.view = new AgentChatView(assets.Layout);
            panel.view.Root.style.width = Length.Percent(100);
            panel.view.Root.style.maxWidth = 640;
            panel.host = new VisualElement { name = "agentChatHost", pickingMode = PickingMode.Ignore };
            panel.host.style.position = Position.Absolute;
            panel.host.style.left = 16; panel.host.style.right = 16; panel.host.style.bottom = 24;
            panel.host.style.alignItems = Align.Center;
            panel.host.Add(panel.view.Root);
            panel.controller = new AgentChatController(panel.view, provider, authentication, instructions, tools, connectLabel, connectionHint);
            DontDestroyOnLoad(root);
            root.SetActive(true);
            return panel;
        }
        private void OnEnable()
        {
            if (!panelRenderer) panelRenderer = GetComponent<PanelRenderer>();
            panelRenderer.RegisterUIReloadCallback(OnUIReload);
        }
        private void OnDisable()
        {
            if (panelRenderer) panelRenderer.UnregisterUIReloadCallback(OnUIReload);
        }
        private void OnUIReload(PanelRenderer renderer, VisualElement root, int version)
        {
            if (host == null || root == null) return;
            root.pickingMode = PickingMode.Ignore;
            root.Add(host);
        }
        private void OnDestroy()
        {
            controller?.Dispose();
            (provider as IDisposable)?.Dispose();
        }
    }
}
