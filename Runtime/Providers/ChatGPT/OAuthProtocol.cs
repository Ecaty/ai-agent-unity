using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace Ecaty.Agents.Providers.ChatGPT
{
    internal static class OAuthProtocol
    {
        internal const string Issuer = "https://auth.openai.com";
        internal const string TokenEndpoint = Issuer + "/api/accounts/oauth/token";
        internal const string Resource = "https://api.openai.com/v1";
        internal const string RequiredScope = "chatgpt.tokens.use.direct";
        internal static string Random(int count = 32)
        {
            var bytes = new byte[count];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(bytes);
            return Encode(bytes);
        }
        internal static string Challenge(string verifier)
        { using (var sha = SHA256.Create()) return Encode(sha.ComputeHash(Encoding.ASCII.GetBytes(verifier))); }
        internal static string Encode(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        internal static byte[] Decode(string value)
        { var text = value.Replace('-', '+').Replace('_', '/'); return Convert.FromBase64String(text.PadRight((text.Length + 3) / 4 * 4, '=')); }
        internal static bool Equal(string left, string right)
        {
            if (left == null || right == null || left.Length != right.Length) return false;
            int difference = 0;
            for (int i = 0; i < left.Length; i++) difference |= left[i] ^ right[i];
            return difference == 0;
        }
        internal static Dictionary<string, string> Query(string query)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var pair in query.TrimStart('?').Split('&'))
            {
                if (pair.Length == 0) continue;
                var parts = pair.Split(new[] { '=' }, 2);
                var name = WebUtility.UrlDecode(parts[0]);
                if (result.ContainsKey(name)) throw new InvalidOperationException("Duplicate sign-in response parameter.");
                result.Add(name, WebUtility.UrlDecode(parts.Length == 2 ? parts[1] : ""));
            }
            return result;
        }
        internal static string Form(IEnumerable<KeyValuePair<string, string>> pairs) => string.Join("&", pairs.Select(p => Uri.EscapeDataString(p.Key) + "=" + Uri.EscapeDataString(p.Value)));

        internal static async Task<JObject> VerifyIdentity(HttpClient http, string token, string clientId, string nonce, string expectedSubject, CancellationToken cancellation)
        {
            var parts = (token ?? "").Split('.');
            if (parts.Length != 3) throw new InvalidOperationException("Invalid sign-in identity.");
            var header = JObject.Parse(Encoding.UTF8.GetString(Decode(parts[0])));
            if ((string)header["alg"] != "RS256" || header["crit"] != null) throw new InvalidOperationException("Unsupported identity signature.");
            var kid = (string)header["kid"];
            if (string.IsNullOrEmpty(kid)) throw new InvalidOperationException("Missing identity signing key.");
            using (var response = await http.GetAsync(Issuer + "/.well-known/jwks.json", cancellation).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                var keys = JObject.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
                var key = (keys["keys"] as JArray)?.OfType<JObject>().SingleOrDefault(k => (string)k["kid"] == kid && (string)k["kty"] == "RSA");
                if (key == null || (key["use"] != null && (string)key["use"] != "sig") || (key["alg"] != null && (string)key["alg"] != "RS256"))
                    throw new InvalidOperationException("Identity signing key unavailable.");
                var modulus = Decode((string)key["n"]);
                if (modulus.Length < 256) throw new InvalidOperationException("Invalid identity signing key.");
                using (var rsa = RSA.Create())
                {
                    rsa.ImportParameters(new RSAParameters { Modulus = modulus, Exponent = Decode((string)key["e"]) });
                    if (!rsa.VerifyData(Encoding.ASCII.GetBytes(parts[0] + "." + parts[1]), Decode(parts[2]), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
                        throw new InvalidOperationException("Identity signature verification failed.");
                }
            }
            var claims = JObject.Parse(Encoding.UTF8.GetString(Decode(parts[1])));
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var audience = claims["aud"];
            bool matches = audience?.Type == JTokenType.String ? (string)audience == clientId : audience is JArray list && list.Any(a => (string)a == clientId);
            if ((string)claims["iss"] != Issuer || !matches || string.IsNullOrEmpty((string)claims["sub"]) ||
                claims["exp"]?.Type != JTokenType.Integer || (long)claims["exp"] < now - 5 ||
                claims["iat"]?.Type != JTokenType.Integer || (long)claims["iat"] > now + 5 ||
                (claims["nbf"] != null && (long)claims["nbf"] > now + 5) ||
                (claims["azp"] != null && (string)claims["azp"] != clientId) ||
                (audience is JArray audiences && audiences.Count > 1 && (string)claims["azp"] != clientId) ||
                (nonce != null && !Equal((string)claims["nonce"], nonce)) ||
                (expectedSubject != null && (string)claims["sub"] != expectedSubject))
                throw new InvalidOperationException("Sign-in identity validation failed.");
            return claims;
        }

        internal sealed class Callback : IDisposable
        {
            private readonly TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
            internal string Uri { get; }
            internal Callback()
            { listener.Start(4); Uri = "http://127.0.0.1:" + ((IPEndPoint)listener.LocalEndpoint).Port + "/auth/callback"; }
            internal async Task<Dictionary<string, string>> ReceiveAsync(string state, CancellationToken cancellation)
            {
                using (cancellation.Register(() => listener.Stop()))
                {
                    while (true)
                    {
                        cancellation.ThrowIfCancellationRequested();
                        TcpClient client;
                        try { client = await listener.AcceptTcpClientAsync().ConfigureAwait(false); }
                        catch (Exception) when (cancellation.IsCancellationRequested) { throw new OperationCanceledException(cancellation); }
                        using (client)
                        using (cancellation.Register(() => client.Close()))
                        {
                            var stream = client.GetStream();
                            var buffer = new byte[8192]; int length = 0;
                            while (length < buffer.Length)
                            {
                                int read = await stream.ReadAsync(buffer, length, buffer.Length - length, cancellation).ConfigureAwait(false);
                                if (read == 0) break;
                                length += read;
                                if (Encoding.ASCII.GetString(buffer, 0, length).Contains("\r\n\r\n")) break;
                            }
                            var first = Encoding.ASCII.GetString(buffer, 0, length).Split(new[] { "\r\n" }, StringSplitOptions.None)[0].Split(' ');
                            Dictionary<string, string> values = null;
                            try
                            {
                                if (first.Length == 3 && first[0] == "GET" && first[1].StartsWith("/auth/callback?", StringComparison.Ordinal))
                                { var parsed = Query(first[1].Substring(first[1].IndexOf('?'))); if (parsed.TryGetValue("state", out var returned) && Equal(state, returned)) values = parsed; }
                            }
                            catch (InvalidOperationException) { }
                            string body = values != null ? "Response received. You can return to the application." : "Invalid connection response.";
                            byte[] reply = Encoding.UTF8.GetBytes("HTTP/1.1 " + (values != null ? "200 OK" : "400 Bad Request") + "\r\nContent-Type: text/plain; charset=utf-8\r\nCache-Control: no-store\r\nConnection: close\r\nContent-Length: " + Encoding.UTF8.GetByteCount(body) + "\r\n\r\n" + body);
                            await stream.WriteAsync(reply, 0, reply.Length, cancellation).ConfigureAwait(false);
                            if (values != null) return values;
                        }
                    }
                }
            }
            public void Dispose() => listener.Stop();
        }
    }
}
