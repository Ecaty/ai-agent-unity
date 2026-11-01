using System;
namespace Ecaty.Agents
{
    public sealed class AgentServiceException : Exception
    {
        public string Code { get; }
        public AgentServiceException(string message, string code = null) : base(message) { Code = code; }
    }
}
