# Unity AI Agent

Reusable Unity package `com.ecaty.ai-agent`, independent of Flecs and of any AI vendor. It provides a minimal UI Toolkit chat view (messages, input, Send and Mic), generic provider/authentication/tool/speech interfaces, and an Editor verification entry point.

## Ownership

- `Runtime/UI`: shared UXML/USS and disposable view bindings. The view emits input events; it has no credentials, API URLs or model selection logic.
- `Runtime/AgentContracts.cs`: `IAgentProvider`, optional `IAgentAuthenticator`, `IAgentTool`, `ISpeechToText` and conversation request data.
- Future provider adapters, authentication/session handling and agent orchestration belong in this package. ChatGPT is first; other services require their own adapter and supported access method.
- The consuming app supplies instructions and world/tool callbacks. This package must not reference `Ecaty.Flecs`, a native DLL or product-specific scenes.

Clone `Runtime/UI/AgentChat.uxml` into an app-owned `UIDocument` with configured PanelSettings. Construct `AgentChatView` with the VisualTreeAsset, add its Root to the document, subscribe to MessageSubmitted/DictationRequested and enable it only when the respective services are ready. Call view methods on Unity's main thread and dispose bindings when removing the view.

The package includes no provider implementation, credentials, authentication flow, model calls, microphone recorder or speech engine yet. No fake AI replies are supplied, and Ecaty's startup does not mount the panel until an actual provider/session is wired. Provider-neutral UI does not imply every provider supports account sign-in, tools or voice.

Verify in Unity with `Ecaty.Agents.Editor.AgentPackageVerification.RunAll()`. This checks import and event/state behavior, not visual appearance or a live AI service.

Canonical project context: [Ecaty AGENTS](https://github.com/Ecaty/Ecaty/blob/main/AGENTS.md), [PROJECT](https://github.com/Ecaty/Ecaty/blob/main/docs/PROJECT.md), [DEVELOPMENT](https://github.com/Ecaty/Ecaty/blob/main/docs/DEVELOPMENT.md), [STATUS](https://github.com/Ecaty/Ecaty/blob/main/docs/STATUS.md).
