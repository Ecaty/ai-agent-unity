using System;
using UnityEngine.UIElements;

namespace Ecaty.Agents.UI
{
    public sealed class AgentChatView : IDisposable
    {
        public VisualElement Root { get; }
        public event Action<string> MessageSubmitted;
        public event Action DictationRequested;
        public event Action ConnectionRequested;
        private readonly Button connection;
        private readonly Label status;
        private readonly TextField input;
        private readonly Button send;
        private readonly Button microphone;
        private readonly ScrollView messages;
        private readonly Button history;
        private bool historyExpanded = true;
        private bool ready, busy, dictationAvailable, disposed;

        public AgentChatView(VisualTreeAsset layout)
        {
            if (!layout) throw new ArgumentNullException(nameof(layout));
            Root = layout.CloneTree();
            Root.pickingMode = PickingMode.Ignore;
            Root.RegisterCallback<GeometryChangedEvent>(OnGeometry);
            input = Required<TextField>("messageInput");
            send = Required<Button>("sendButton");
            microphone = Required<Button>("microphoneButton");
            messages = Required<ScrollView>("messages");
            history = Required<Button>("historyButton");
            history.Add(new AgentIcon(AgentIcon.Kind.History));
            history.style.display = DisplayStyle.None;
            history.clicked += ToggleHistory;
            connection = Required<Button>("connectionButton");
            status = Required<Label>("connectionStatus");
            connection.text = ""; send.text = ""; microphone.text = "";
            connection.Add(new AgentIcon(AgentIcon.Kind.Account));
            send.Add(new AgentIcon(AgentIcon.Kind.Send));
            microphone.Add(new AgentIcon(AgentIcon.Kind.Microphone));
            messages.style.display = DisplayStyle.None;
            input.RegisterCallback<KeyDownEvent>(OnKey, TrickleDown.TrickleDown);
            connection.clicked += OnConnect;
            send.clicked += OnSend;
            microphone.clicked += OnDictation;
            input.RegisterValueChangedCallback(OnDraftChanged);
            RefreshControls();
        }

        public void ClearMessages()
        { Check(); messages.Clear(); messages.style.display = DisplayStyle.None; history.style.display = DisplayStyle.None; historyExpanded = true; }
        public void SetHistoryExpanded(bool expanded)
        {
            Check(); historyExpanded = expanded;
            messages.style.display = expanded && messages.childCount > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            history.tooltip = expanded ? "Hide conversation" : "Show conversation";
            history.EnableInClassList("agent-chat__history--collapsed", !expanded);
        }
        private void ToggleHistory() => SetHistoryExpanded(!historyExpanded);
        private void OnGeometry(GeometryChangedEvent e) => Root.EnableInClassList("agent-chat--compact", e.newRect.width < 420);
        public void SetConnection(string label, string hint, bool enabled)
        { Check(); connection.tooltip = label; connection.SetEnabled(enabled); status.text = hint ?? ""; status.style.display = string.IsNullOrEmpty(hint) ? DisplayStyle.None : DisplayStyle.Flex; }
        private void OnConnect() => ConnectionRequested?.Invoke();
        public void SetReady(bool value) { Check(); ready = value; Root.EnableInClassList("agent-chat--connected", value); RefreshControls(); }
        public void SetBusy(bool value) { Check(); busy = value; Root.EnableInClassList("agent-chat--busy", value); RefreshControls(); }
        public void SetDictationAvailable(bool value) { Check(); dictationAvailable = value; RefreshControls(); }
        public void SetDraft(string text) { Check(); input.value = text ?? ""; RefreshControls(); }

        public Label AppendMessage(string text, bool fromUser)
        {
            Check();
            var label = new Label(text ?? "") { enableRichText = false };
            label.AddToClassList("agent-chat__message");
            if (fromUser) label.AddToClassList("agent-chat__message--user");
            messages.Add(label);
            history.style.display = DisplayStyle.Flex;
            SetHistoryExpanded(true);
            messages.schedule.Execute(() => messages.ScrollTo(label));
            return label;
        }

        public bool TrySubmit()
        {
            Check();
            var text = input.value?.Trim();
            if (!ready || busy || string.IsNullOrEmpty(text) || MessageSubmitted == null) return false;
            input.value = "";
            RefreshControls();
            MessageSubmitted.Invoke(text);
            return true;
        }

        private T Required<T>(string name) where T : VisualElement =>
            Root.Q<T>(name) ?? throw new InvalidOperationException("Missing chat UI element: " + name);
        private void OnKey(KeyDownEvent e)
        { if (e.keyCode == UnityEngine.KeyCode.Return && !e.shiftKey && TrySubmit()) e.StopImmediatePropagation(); }
        private void OnSend() => TrySubmit();
        private void OnDictation() { if (ready && !busy && dictationAvailable) DictationRequested?.Invoke(); }
        private void OnDraftChanged(ChangeEvent<string> change) => RefreshControls();
        private void RefreshControls()
        {
            input.SetEnabled(!busy);
            send.SetEnabled(ready && !busy && !string.IsNullOrWhiteSpace(input.value));
            microphone.SetEnabled(ready && !busy && dictationAvailable);
        }
        private void Check() { if (disposed) throw new ObjectDisposedException(nameof(AgentChatView)); }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            connection.clicked -= OnConnect;
            ConnectionRequested = null;
            send.clicked -= OnSend;
            microphone.clicked -= OnDictation;
            input.UnregisterCallback<KeyDownEvent>(OnKey, TrickleDown.TrickleDown);
            Root.UnregisterCallback<GeometryChangedEvent>(OnGeometry);
            history.clicked -= ToggleHistory;
            input.UnregisterValueChangedCallback(OnDraftChanged);
            MessageSubmitted = null;
            DictationRequested = null;
            ready = false;
            RefreshControls();
        }
    }
}
