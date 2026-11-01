using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Ecaty.Agents.Security;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Ecaty.Agents.Providers.ChatGPT
{
    internal sealed class ConnectionRecord
    {
        public string ClientId, Subject, Email, AccessToken, RefreshToken, IdToken, Scope;
        public long ExpiresAt, EarliestRefreshAt;
    }
    internal sealed class ConnectionState
    {
        public string HostId = "urn:uuid:" + Guid.NewGuid().ToString("D");
        public string PendingClientId;
        public ConnectionRecord Active;
        public List<ConnectionRecord> Registrations = new List<ConnectionRecord>();
    }
    internal sealed class ChatGptConnection
    {
        private readonly HttpClient http;
        private readonly ISecretStore store;
        private readonly Action<string> browser;
        private readonly string applicationName;
        private readonly SemaphoreSlim gate = new SemaphoreSlim(1, 1);
        private ConnectionState state;
        internal bool IsSignedIn => state.Active != null && HasScope(state.Active.Scope) && !string.IsNullOrEmpty(state.Active.AccessToken);
        internal string Identity => state.Active?.Subject;

        internal ChatGptConnection(string name, HttpClient http, ISecretStore store, Action<string> browser)
        {
            applicationName = name; this.http = http; this.store = store; this.browser = browser;
            var bytes = store.Load();
            state = bytes == null ? new ConnectionState() : JsonConvert.DeserializeObject<ConnectionState>(Encoding.UTF8.GetString(bytes));
            if (state == null || string.IsNullOrEmpty(state.HostId)) throw new AgentServiceException("Saved connection is unavailable.");
            Save();
        }
        private static bool HasScope(string scope) => (scope ?? "").Split(' ').Contains(OAuthProtocol.RequiredScope);
        private void Save()
        {
            var bytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(state));
            try { store.Save(bytes); } finally { Array.Clear(bytes, 0, bytes.Length); }
        }
        internal async Task SignInAsync(CancellationToken cancellation)
        {
            await gate.WaitAsync(cancellation);
            try
            {
                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation))
                using (var callback = new OAuthProtocol.Callback())
                {
                    timeout.CancelAfter(TimeSpan.FromMinutes(5));
                    var verifier = OAuthProtocol.Random(64); var nonce = OAuthProtocol.Random(); var transactionState = OAuthProtocol.Random();
                    var previous = state.Active;
                    var clientId = previous?.ClientId ?? state.PendingClientId ?? "dynamic_agent_client";
                    var fields = new Dictionary<string, string> {
                        ["client_id"] = clientId, ["ext_agent_host_id"] = state.HostId,
                        ["response_type"] = "code", ["redirect_uri"] = callback.Uri,
                        ["scope"] = "openid profile email offline_access resource.invoke chatgpt.tokens.use.direct",
                        ["resource"] = OAuthProtocol.Resource, ["state"] = transactionState, ["nonce"] = nonce,
                        ["code_challenge_method"] = "S256", ["code_challenge"] = OAuthProtocol.Challenge(verifier)
                    };
                    if (clientId == "dynamic_agent_client") fields["agent_name_hint"] = applicationName;
                    if (previous != null && !string.IsNullOrEmpty(previous.IdToken)) fields["id_token_hint"] = previous.IdToken;
                    browser(OAuthProtocol.Issuer + "/api/accounts/authorize?" + OAuthProtocol.Form(fields));
                    var returned = await callback.ReceiveAsync(transactionState, timeout.Token);
                    if (returned.ContainsKey("error")) throw new AgentServiceException("Sign-in was not completed.");
                    if (clientId == "dynamic_agent_client")
                    {
                        if (!returned.TryGetValue("client_id", out clientId) || string.IsNullOrWhiteSpace(clientId) || clientId == "dynamic_agent_client")
                            throw new AgentServiceException("ChatGPT did not complete app registration.");
                        state.PendingClientId = clientId; Save();
                    }
                    else if (returned.TryGetValue("client_id", out var replacement) && replacement != clientId)
                        throw new AgentServiceException("The sign-in registration did not match.");
                    if (!returned.TryGetValue("code", out var code) || string.IsNullOrEmpty(code)) throw new AgentServiceException("Sign-in response was incomplete.");
                    var tokens = await TokenAsync(new Dictionary<string, string> {
                        ["grant_type"] = "authorization_code", ["client_id"] = clientId, ["code"] = code,
                        ["code_verifier"] = verifier, ["redirect_uri"] = callback.Uri, ["resource"] = OAuthProtocol.Resource
                    }, timeout.Token);
                    var claims = await OAuthProtocol.VerifyIdentity(http, (string)tokens["id_token"], clientId, nonce, previous?.Subject, timeout.Token);
                    state.Active = Record(tokens, clientId, claims, null);
                    state.PendingClientId = null;
                    state.Registrations.RemoveAll(r => r.ClientId == clientId && r.Subject == state.Active.Subject);
                    state.Registrations.Add(new ConnectionRecord { ClientId = clientId, Subject = state.Active.Subject, Email = state.Active.Email });
                    Save();
                    if (!IsSignedIn) throw new AgentServiceException("ChatGPT plan access was not enabled. Connect again and allow plan usage.");
                }
            }
            finally { gate.Release(); }
        }
        internal async Task<string> AccessTokenAsync(CancellationToken cancellation)
        {
            await gate.WaitAsync(cancellation).ConfigureAwait(false);
            try
            {
                if (!IsSignedIn) throw new AgentServiceException("Connect your ChatGPT account first.", "sign_in_required");
                var previous = state.Active;
                long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                if (previous.ExpiresAt > now + 60) return previous.AccessToken;
                if (string.IsNullOrEmpty(previous.RefreshToken)) throw new AgentServiceException("Please connect ChatGPT again.", "sign_in_required");
                if (previous.EarliestRefreshAt > now) throw new AgentServiceException("This connection cannot refresh yet. Try again shortly.");
                var tokens = await TokenAsync(new Dictionary<string, string> {
                    ["grant_type"] = "refresh_token", ["client_id"] = previous.ClientId,
                    ["refresh_token"] = previous.RefreshToken, ["resource"] = OAuthProtocol.Resource
                }, cancellation).ConfigureAwait(false);
                JObject claims = null;
                if (tokens["id_token"] != null)
                    claims = await OAuthProtocol.VerifyIdentity(http, (string)tokens["id_token"], previous.ClientId, null, previous.Subject, cancellation).ConfigureAwait(false);
                state.Active = Record(tokens, previous.ClientId, claims, previous);
                Save();
                if (!IsSignedIn) throw new AgentServiceException("ChatGPT plan access is no longer enabled.", "sign_in_required");
                return state.Active.AccessToken;
            }
            finally { gate.Release(); }
        }
        internal async Task DisconnectAsync(CancellationToken cancellation)
        {
            await gate.WaitAsync(cancellation);
            try { state.Active = null; state.PendingClientId = null; Save(); }
            finally { gate.Release(); }
        }
        private async Task<JObject> TokenAsync(Dictionary<string, string> fields, CancellationToken cancellation)
        {
            using (var response = await http.PostAsync(OAuthProtocol.TokenEndpoint, new FormUrlEncodedContent(fields), cancellation).ConfigureAwait(false))
            {
                var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) throw new AgentServiceException("ChatGPT connection could not be completed. Please connect again.", "sign_in_required");
                return JObject.Parse(body);
            }
        }
        private static ConnectionRecord Record(JObject tokens, string clientId, JObject claims, ConnectionRecord previous)
        {
            var token = (string)tokens["access_token"];
            long expires = (long?)tokens["expires_in"] ?? 0;
            if (string.IsNullOrWhiteSpace(token) || expires <= 0 || !string.Equals((string)tokens["token_type"], "Bearer", StringComparison.OrdinalIgnoreCase))
                throw new AgentServiceException("ChatGPT returned an incomplete connection.");
            return new ConnectionRecord {
                ClientId = clientId, Subject = (string)claims?["sub"] ?? previous?.Subject,
                Email = (string)claims?["email"] ?? previous?.Email,
                IdToken = (string)tokens["id_token"] ?? previous?.IdToken,
                AccessToken = token, RefreshToken = (string)tokens["refresh_token"],
                Scope = (string)tokens["scope"] ?? previous?.Scope,
                ExpiresAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + expires,
                EarliestRefreshAt = (long?)tokens["earliest_refresh_at"] ?? 0
            };
        }
    }
}
