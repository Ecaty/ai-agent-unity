using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Ecaty.Agents
{
    public enum AgentRole { User, Assistant }

    public sealed class AgentMessage
    {
        public AgentRole Role { get; }
        public string Text { get; }
        public AgentMessage(AgentRole role, string text) { Role = role; Text = text ?? ""; }
    }

    public sealed class AgentRequest
    {
        public string Instructions { get; }
        public IReadOnlyList<AgentMessage> Messages { get; }
        public IReadOnlyList<IAgentTool> Tools { get; }

        public AgentRequest(string instructions, IReadOnlyList<AgentMessage> messages, IReadOnlyList<IAgentTool> tools)
        {
            Instructions = instructions ?? "";
            Messages = messages ?? throw new ArgumentNullException(nameof(messages));
            Tools = tools ?? Array.Empty<IAgentTool>();
        }
    }

    public interface IAgentProvider
    {
        string Id { get; }
        string DisplayName { get; }
        // Adapters emit text deltas and return the complete final assistant text.
        // Provider-specific function-call messages/tool loops remain inside the adapter.
        Task<string> ReplyAsync(AgentRequest request, IProgress<string> textDelta, CancellationToken cancellationToken);
    }

    public interface IAgentAuthenticator
    {
        bool IsSignedIn { get; }
        Task SignInAsync(CancellationToken cancellationToken);
        Task SignOutAsync(CancellationToken cancellationToken);
    }

    public interface IAgentTool
    {
        string Name { get; }
        string Description { get; }
        string InputJsonSchema { get; }
        Task<string> ExecuteAsync(string argumentsJson, CancellationToken cancellationToken);
    }

    public interface ISpeechToText
    {
        Task<string> TranscribeAsync(byte[] audio, string mediaType, CancellationToken cancellationToken);
    }
}
