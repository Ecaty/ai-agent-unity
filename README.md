# Ecaty AI Agent

Reusable Unity 6000.4 UI Toolkit chat, provider/auth/tool/speech contracts, conversation controller, owning-thread dispatcher and developer trace viewer. The Windows ChatGPT adapter uses the documented local/open-source account flow, protected credential storage and streamed local tools. The package has no Flecs dependency; the application supplies instructions and callbacks.

Provide AgentUiAssets (UXML + PanelSettings), an IAgentProvider/authenticator and host tools to AgentChatPanel.Create. A service adapter can be replaced without changing the view. Font/icon assets are bundled locally: Google Material Symbols Rounded, Apache-2.0; source/hashes/license in Runtime/UI/Resources. Microphone is disabled until an ISpeechToText implementation is wired.

**Ecaty > Agent Trace** shows grouped, collapsible local requests/tool results with search, errors and copy. Configure the collector explicitly; do not log authentication, publish private traces or confuse this developer window with the product UI. Capture is off unless the host enables it. The Editor assembly owns the viewer; runtime code owns the collector.

Windows DPAPI state uses LocalAppData/<app>/Agents/chatgpt.dat. Disconnect clears this application's active credentials; it does not revoke server authorization. No Codex credentials, API keys, paid fallback or existing ChatGPT history are used.

Verified with synthetic OAuth/Responses transport and real Ecaty world/tools, pending live account/inference verification. Account/plan eligibility is provider-controlled; other platforms, IL2CPP and speech need separate implementations/tests.

Canonical context: [AGENTS](https://github.com/Ecaty/Ecaty/blob/main/AGENTS.md), [PROJECT](https://github.com/Ecaty/Ecaty/blob/main/docs/PROJECT.md), [DEVELOPMENT](https://github.com/Ecaty/Ecaty/blob/main/docs/DEVELOPMENT.md), [AI integration](https://github.com/Ecaty/Ecaty/blob/main/docs/AI-INTEGRATION.md), [STATUS](https://github.com/Ecaty/Ecaty/blob/main/docs/STATUS.md).
