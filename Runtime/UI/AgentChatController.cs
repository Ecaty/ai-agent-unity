using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine.UIElements;

namespace Ecaty.Agents.UI
{
    public sealed class AgentChatController : IDisposable
    {
        private readonly AgentChatView view;
        private readonly IAgentProvider provider;
        private readonly IAgentAuthenticator authentication;
        private readonly string instructions, connectLabel, connectionHint;
        private readonly IReadOnlyList<IAgentTool> tools;
        private readonly List<AgentMessage> messages = new List<AgentMessage>();
        private CancellationTokenSource operation;
        private bool disposed, busy;
        private int generation;
        public AgentChatController(AgentChatView view, IAgentProvider provider, IAgentAuthenticator authentication,
            string instructions, IReadOnlyList<IAgentTool> tools, string connectLabel = "Connect AI", string connectionHint = "Connect to start.")
        {
            this.view = view; this.provider = provider; this.authentication = authentication;
            this.instructions = instructions; this.tools = tools; this.connectLabel = connectLabel; this.connectionHint = connectionHint;
            view.MessageSubmitted += OnSubmit;
            view.ConnectionRequested += OnConnect;
            UpdateConnection();
        }
        public async Task ConnectAsync()
        {
            if (disposed || busy || authentication == null) return;
            busy = true; operation = new CancellationTokenSource();
            view.SetBusy(true); view.SetConnection(connectLabel, "Connecting…", false);
            try { await authentication.SignInAsync(operation.Token); if (!disposed) UpdateConnection(); }
            catch (OperationCanceledException) { if (!disposed) UpdateConnection("Connection cancelled."); }
            catch (Exception e) { if (!disposed) UpdateConnection(Friendly(e, "Couldn't connect. Please try again.")); }
            finally { busy = false; operation?.Dispose(); operation = null; if (!disposed) view.SetBusy(false); }
        }
        public async Task<string> SendAsync(string text)
        {
            if (disposed || busy || string.IsNullOrWhiteSpace(text)) return null;
            if (authentication != null && !authentication.IsSignedIn) { UpdateConnection(); return null; }
            int current = ++generation; busy = true;
            operation = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            messages.Add(new AgentMessage(AgentRole.User, text));
            view.AppendMessage(text, true);
            var reply = view.AppendMessage("", false);
            string streaming = "";
            var progress = new Progress<string>(delta => { if (!disposed && busy && generation == current) { streaming += delta; reply.text = streaming; } });
            view.SetBusy(true); view.SetConnection("Disconnect", "", false);
            try
            {
                var result = await provider.ReplyAsync(new AgentRequest(instructions, messages.ToArray(), tools), progress, operation.Token);
                if (disposed || generation != current) return null;
                busy = false; reply.text = result;
                if (string.IsNullOrWhiteSpace(result)) reply.RemoveFromHierarchy();
                messages.Add(new AgentMessage(AgentRole.Assistant, result));
                UpdateConnection();
                return result;
            }
            catch (OperationCanceledException) { if (!disposed) { reply.text = "Request stopped."; UpdateConnection(); } return null; }
            catch (Exception e) { if (!disposed) { reply.text = Friendly(e, "Couldn't complete that. Please try again."); UpdateConnection(); } return null; }
            finally { busy = false; operation?.Dispose(); operation = null; if (!disposed) view.SetBusy(false); }
        }
        private async void OnSubmit(string text) { await SendAsync(text); }
        private async void OnConnect()
        {
            if (disposed || busy) return;
            if (authentication != null && authentication.IsSignedIn)
            {
                try { await authentication.SignOutAsync(CancellationToken.None); messages.Clear(); view.ClearMessages(); UpdateConnection(); }
                catch (Exception) { UpdateConnection("Couldn't disconnect. Please try again."); }
            }
            else await ConnectAsync();
        }
        private void UpdateConnection(string status = null)
        {
            bool connected = authentication == null || authentication.IsSignedIn;
            view.SetReady(connected);
            view.SetConnection(connected ? "Disconnect" : connectLabel, status ?? (connected ? "" : connectionHint), authentication != null);
        }
        private static string Friendly(Exception error, string fallback) => error is AgentServiceException service ? service.Message : fallback;
        public void Dispose()
        {
            if (disposed) return;
            disposed = true; ++generation; operation?.Cancel();
            view.MessageSubmitted -= OnSubmit; view.ConnectionRequested -= OnConnect;
            view.Dispose();
        }
    }
}
