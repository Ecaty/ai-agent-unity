using System;
using UnityEngine.UIElements;

namespace Ecaty.Agents.UI
{
    public sealed class AgentChatView : IDisposable
    {
        public VisualElement Root { get; }
        public event Action<string> MessageSubmitted;
        public event Action DictationRequested;
        private readonly TextField input;
        private readonly Button send;
        private readonly Button microphone;
        private readonly ScrollView messages;
        private bool ready, busy, dictationAvailable, disposed;

        public AgentChatView(VisualTreeAsset layout)
        {
            if (!layout) throw new ArgumentNullException(nameof(layout));
            Root = layout.CloneTree();
            input = Required<TextField>("messageInput");
            send = Required<Button>("sendButton");
            microphone = Required<Button>("microphoneButton");
            messages = Required<ScrollView>("messages");
            send.clicked += OnSend;
            microphone.clicked += OnDictation;
            input.RegisterValueChangedCallback(OnDraftChanged);
            RefreshControls();
        }

        public void SetReady(bool value) { Check(); ready = value; RefreshControls(); }
        public void SetBusy(bool value) { Check(); busy = value; RefreshControls(); }
        public void SetDictationAvailable(bool value) { Check(); dictationAvailable = value; RefreshControls(); }
        public void SetDraft(string text) { Check(); input.value = text ?? ""; RefreshControls(); }

        public Label AppendMessage(string text, bool fromUser)
        {
            Check();
            var label = new Label(text ?? "") { enableRichText = false };
            label.AddToClassList("agent-chat__message");
            if (fromUser) label.AddToClassList("agent-chat__message--user");
            messages.Add(label);
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
        private void OnSend() => TrySubmit();
        private void OnDictation() { if (ready && !busy && dictationAvailable) DictationRequested?.Invoke(); }
        private void OnDraftChanged(ChangeEvent<string> change) => RefreshControls();
        private void RefreshControls()
        {
            input.SetEnabled(ready && !busy);
            send.SetEnabled(ready && !busy && !string.IsNullOrWhiteSpace(input.value));
            microphone.SetEnabled(ready && !busy && dictationAvailable);
        }
        private void Check() { if (disposed) throw new ObjectDisposedException(nameof(AgentChatView)); }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            send.clicked -= OnSend;
            microphone.clicked -= OnDictation;
            input.UnregisterValueChangedCallback(OnDraftChanged);
            MessageSubmitted = null;
            DictationRequested = null;
            ready = false;
            RefreshControls();
        }
    }
}
