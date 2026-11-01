using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Ecaty.Agents.Security;
using Ecaty.Agents.Diagnostics;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Ecaty.Agents.Providers.ChatGPT
{
    public sealed class ChatGptProvider : IAgentProvider, IAgentAuthenticator, IDisposable
    {
        public string Id => "chatgpt";
        public string DisplayName => "ChatGPT";
        public bool IsSignedIn => connection.IsSignedIn;
        public string Model { get; private set; }
        private readonly HttpClient http;
        private readonly bool ownsHttp;
        private readonly ChatGptConnection connection;
        private readonly SemaphoreSlim requests = new SemaphoreSlim(1, 1);

        public ChatGptProvider(string applicationName) : this(applicationName, null, new WindowsSecretStore(applicationName), UnityEngine.Application.OpenURL) { }
        public ChatGptProvider(string applicationName, HttpClient httpClient, ISecretStore secretStore, Action<string> openBrowser)
        {
            ownsHttp = httpClient == null;
            http = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(90) };
            connection = new ChatGptConnection(applicationName, http, secretStore, openBrowser);
        }
        public async Task SignInAsync(CancellationToken cancellationToken)
        { await connection.SignInAsync(cancellationToken); Model = null; }
        // Disconnect clears this application's credentials; it does not log out the browser.
        public async Task SignOutAsync(CancellationToken cancellationToken)
        { await connection.DisconnectAsync(cancellationToken); Model = null; }

        public async Task<string> ReplyAsync(AgentRequest request, IProgress<string> textDelta, CancellationToken cancellationToken)
        {
            await requests.WaitAsync(cancellationToken).ConfigureAwait(false);
            var requestId = Guid.NewGuid().ToString("N");
            var elapsed = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                AgentTrace.Record(requestId, "request", DisplayName, request.Messages.LastOrDefault(m => m.Role == AgentRole.User)?.Text);
                var token = await connection.AccessTokenAsync(cancellationToken).ConfigureAwait(false);
                if (Model == null) Model = await ChooseModel(token, cancellationToken).ConfigureAwait(false);
                var tools = request.Tools.ToDictionary(t => t.Name, StringComparer.Ordinal);
                var input = new JArray(request.Messages.Select(m => new JObject {
                    ["type"] = "message", ["role"] = m.Role == AgentRole.User ? "user" : "assistant", ["content"] = m.Text
                }));
                var definitions = new JArray(tools.Values.Select(t => new JObject {
                    ["type"] = "function", ["name"] = t.Name, ["description"] = t.Description,
                    ["parameters"] = JObject.Parse(t.InputJsonSchema), ["strict"] = true
                }));
                var visible = new StringBuilder();
                int calls = 0;
                for (int turn = 0; turn < 10; turn++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var body = new JObject {
                        ["model"] = Model, ["instructions"] = request.Instructions, ["input"] = input,
                        ["stream"] = true, ["store"] = false, ["parallel_tool_calls"] = false,
                        ["include"] = new JArray("reasoning.encrypted_content")
                    };
                    if (definitions.Count > 0) body["tools"] = new JArray(new JObject {
                        ["type"] = "namespace", ["name"] = "app", ["description"] = "Tools supplied by the current application.", ["tools"] = definitions
                    });
                    var completed = await StreamResponse(token, body, delta => { visible.Append(delta); textDelta?.Report(delta); }, cancellationToken).ConfigureAwait(false);
                    var output = completed["output"] as JArray ?? throw new AgentServiceException("The reply was incomplete. Please try again.");
                    foreach (var item in output) input.Add(item.DeepClone());
                    var functionCalls = output.OfType<JObject>().Where(o => (string)o["type"] == "function_call").ToList();
                    if (functionCalls.Count == 0)
                    {
                        var answer = visible.ToString().Trim();
                        AgentTrace.Record(requestId, "completed", DisplayName, result: answer, milliseconds: elapsed.ElapsedMilliseconds);
                        return answer;
                    }
                    foreach (var call in functionCalls)
                    {
                        if (++calls > 24) throw new AgentServiceException("This request needs more steps. Try a smaller change.");
                        var name = (string)call["name"];
                        var toolNamespace = (string)call["namespace"];
                        if (name != null && name.StartsWith("app.", StringComparison.Ordinal)) { name = name.Substring(4); toolNamespace = "app"; }
                        string result;
                        if ((toolNamespace != null && toolNamespace != "app") || name == null || !tools.TryGetValue(name, out var tool))
                            result = "{\"error\":\"Unknown application tool. Use only the supplied tools.\"}";
                        else
                        {
                            var arguments = (string)call["arguments"] ?? "{}";
                            if (arguments.Length > 65536) result = "{\"error\":\"Tool input is too large.\"}";
                            else
                            {
                                var watch = System.Diagnostics.Stopwatch.StartNew();
                                AgentTrace.Record(requestId, "tool-start", name, arguments);
                                try { result = await tool.ExecuteAsync(arguments, cancellationToken).ConfigureAwait(false); }
                                catch (OperationCanceledException) { throw; }
                                catch (Exception error) { result = new JObject { ["error"] = error.Message }.ToString(Formatting.None); }
                                AgentTrace.Record(requestId, "tool-result", name, arguments, result, watch.ElapsedMilliseconds);
                            }
                        }
                        if (result.Length > 65536) result = "{\"error\":\"Result is too large; narrow the query.\"}";
                        input.Add(new JObject { ["type"] = "function_call_output", ["call_id"] = (string)call["call_id"], ["output"] = result });
                    }
                    if (visible.Length > 0) { visible.Append('\n'); textDelta?.Report("\n"); }
                }
                throw new AgentServiceException("This request needs more steps. Try a smaller change.");
            }
            catch (Exception error)
            {
                AgentTrace.Record(requestId, "error", DisplayName, result: error is AgentServiceException ? error.Message : error is OperationCanceledException ? "Cancelled" : "Connection or processing error", milliseconds: elapsed.ElapsedMilliseconds);
                throw;
            }
            finally { requests.Release(); }
        }
        private async Task<string> ChooseModel(string token, CancellationToken cancellation)
        {
            using (var request = Authorized(HttpMethod.Get, OAuthProtocol.Resource + "/models", token))
            using (var response = await http.SendAsync(request, cancellation).ConfigureAwait(false))
            {
                var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                EnsureSuccess(response, body);
                var catalog = JObject.Parse(body);
                var models = (catalog["models"] as JArray ?? catalog["data"] as JArray)?.OfType<JObject>()
                    .Where(m => m["visibility"] == null || (string)m["visibility"] == "list")
                    .Select(m => (string)m["slug"] ?? (string)m["id"]).Where(s => !string.IsNullOrEmpty(s)).ToList();
                if (models == null || models.Count == 0) throw new AgentServiceException("No models are available for this connection.");
                return models.Contains("gpt-6.1-sol") ? "gpt-6.1-sol" : models[0];
            }
        }
        private async Task<JObject> StreamResponse(string token, JObject body, Action<string> delta, CancellationToken cancellation)
        {
            using (var request = Authorized(HttpMethod.Post, OAuthProtocol.Resource + "/responses", token))
            {
                request.Content = new StringContent(body.ToString(Formatting.None), Encoding.UTF8, "application/json");
                using (var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation).ConfigureAwait(false))
                {
                    if (!response.IsSuccessStatusCode)
                        EnsureSuccess(response, await response.Content.ReadAsStringAsync().ConfigureAwait(false));
                    using (cancellation.Register(() => response.Dispose()))
                    using (var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    using (var reader = new StreamReader(stream, Encoding.UTF8))
                    {
                        var data = new StringBuilder();
                        try
                        {
                            while (!reader.EndOfStream)
                            {
                                cancellation.ThrowIfCancellationRequested();
                                var line = await reader.ReadLineAsync().ConfigureAwait(false);
                                if (line == null) break;
                                if (line.StartsWith("data:", StringComparison.Ordinal))
                                { if (data.Length > 0) data.Append('\n'); data.Append(line.Substring(5).TrimStart()); if (data.Length > 4 * 1024 * 1024) throw new AgentServiceException("The reply was too large."); continue; }
                                if (line.Length != 0 || data.Length == 0) continue;
                                var payload = data.ToString(); data.Clear();
                                if (payload == "[DONE]") break;
                                var item = JObject.Parse(payload);
                                switch ((string)item["type"])
                                {
                                    case "response.output_text.delta":
                                    case "response.refusal.delta": delta((string)item["delta"] ?? ""); break;
                                    case "response.completed":
                                        var completed = item["response"] as JObject;
                                        if (completed == null || (string)completed["status"] != "completed") throw new AgentServiceException("The reply was incomplete.");
                                        return completed;
                                    case "response.failed": throw Failure(item["response"]?["error"]?["code"]?.ToString());
                                    case "error": throw Failure(item["code"]?.ToString() ?? item["error"]?["code"]?.ToString());
                                    case "response.incomplete": throw new AgentServiceException("The reply was interrupted. Please try again.");
                                }
                            }
                        }
                        catch (Exception) when (cancellation.IsCancellationRequested) { throw new OperationCanceledException(cancellation); }
                    }
                }
            }
            throw new AgentServiceException("The connection ended before the reply completed. Please try again.");
        }
        private static HttpRequestMessage Authorized(HttpMethod method, string uri, string token)
        { var request = new HttpRequestMessage(method, uri); request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token); return request; }
        private static void EnsureSuccess(HttpResponseMessage response, string body)
        {
            if (response.IsSuccessStatusCode) return;
            string code = null;
            try { code = (string)JObject.Parse(body)["error"]?["code"]; } catch (JsonException) { }
            if ((int)response.StatusCode == 401) throw new AgentServiceException("Please connect ChatGPT again.", "sign_in_required");
            throw Failure(code);
        }
        private static AgentServiceException Failure(string code)
        {
            if (code == "subscription_sharing_usage_limit_exceeded") return new AgentServiceException("Your ChatGPT usage limit was reached. Please try again later.", code);
            if (code == "subscription_sharing_usage_unavailable") return new AgentServiceException("Your ChatGPT plan isn't available for this connection.", code);
            return new AgentServiceException("ChatGPT couldn't complete the request. Please try again.");
        }
        public void Dispose() { if (ownsHttp) http.Dispose(); }
    }
}
