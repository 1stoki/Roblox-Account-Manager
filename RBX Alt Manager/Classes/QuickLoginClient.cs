using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace RBX_Alt_Manager.Classes
{
    internal sealed class QuickLoginClient : IDisposable
    {
        private const string Service = "https://apis.roblox.com/auth-token-service/v1/login/";
        private readonly HttpClient http;
        private readonly CookieContainer cookies;
        private string privateKey;
        private string csrf;
        private bool validated, issuedSession, completed, accepted;

        public string Code { get; private set; }
        public DateTimeOffset ExpiresAt { get; private set; }

        public QuickLoginClient()
        {
            cookies = new CookieContainer();
            http = new HttpClient(new HttpClientHandler { CookieContainer = cookies, UseCookies = true, AllowAutoRedirect = false });
            Configure();
        }

        internal QuickLoginClient(HttpMessageHandler handler, CookieContainer cookieContainer)
        {
            cookies = cookieContainer;
            http = new HttpClient(handler);
            Configure();
        }

        private void Configure()
        {
            http.Timeout = TimeSpan.FromSeconds(20);
            http.DefaultRequestHeaders.UserAgent.ParseAdd("RobloxAccountManager-Fork/1.0");
            http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
            http.DefaultRequestHeaders.Referrer = new Uri("https://www.roblox.com/");
            http.DefaultRequestHeaders.TryAddWithoutValidation("Origin", "https://www.roblox.com");
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            if (Code != null) throw new InvalidOperationException("A Quick Login request is already active.");
            JObject response = await SendAsync(HttpMethod.Post, Service + "create", null, cancellationToken).ConfigureAwait(false);
            Code = response.Value<string>("code");
            privateKey = response.Value<string>("privateKey");
            if (string.IsNullOrEmpty(Code) || !Regex.IsMatch(Code, "^[A-Za-z0-9]{6}$") || string.IsNullOrEmpty(privateKey))
                throw new InvalidOperationException("Roblox did not return a valid Quick Login request.");
            ExpiresAt = DateTimeOffset.TryParse(response.Value<string>("expirationTime"), out DateTimeOffset expires)
                ? expires : DateTimeOffset.UtcNow.AddMinutes(5);
            if (ExpiresAt <= DateTimeOffset.UtcNow)
                throw new InvalidOperationException("The Quick Login code has expired. Check the computer clock and request a new code.");
        }

        public async Task<string> GetStatusAsync(CancellationToken cancellationToken)
        {
            EnsureActive();
            JObject response = await SendAsync(HttpMethod.Post, Service + "status", new JObject
            {
                ["code"] = Code, ["privateKey"] = privateKey
            }, cancellationToken).ConfigureAwait(false);
            string status = response.Value<string>("status");
            validated = status == "Validated";
            if (status != "Created" && status != "UserLinked" && status != "Validated" && status != "Cancelled" && status != "Expired")
                throw new InvalidOperationException("Roblox returned an unsupported Quick Login status. Use the normal browser sign-in option.");
            return status;
        }

        public async Task<QuickLoginAccount> CompleteAsync(CancellationToken cancellationToken)
        {
            EnsureActive();
            if (!validated) throw new InvalidOperationException("Approve this Quick Login request on your signed-in device first.");
            if (completed) throw new InvalidOperationException("This Quick Login request has already been completed.");

            await SendAsync(HttpMethod.Post, "https://auth.roblox.com/v2/login", new JObject
            {
                ["ctype"] = "AuthToken", ["cvalue"] = Code, ["password"] = privateKey
            }, cancellationToken).ConfigureAwait(false);
            string securityToken = cookies.GetCookies(new Uri("https://www.roblox.com/"))[".ROBLOSECURITY"]?.Value;
            if (string.IsNullOrEmpty(securityToken))
                throw new InvalidOperationException("Roblox did not create an account session. Complete any additional verification using its normal browser sign-in.");
            issuedSession = true;

            JObject user = await SendAsync(HttpMethod.Get, "https://users.roblox.com/v1/users/authenticated", null, cancellationToken).ConfigureAwait(false);
            long id = user.Value<long?>("id") ?? 0;
            string name = user.Value<string>("name");
            if (id <= 0 || string.IsNullOrWhiteSpace(name))
                throw new InvalidOperationException("The new Roblox session could not be verified.");
            completed = true;
            privateKey = null;
            return new QuickLoginAccount(id, name, securityToken);
        }

        public void AcceptSession()
        {
            if (!completed) throw new InvalidOperationException("No verified account session is ready to save.");
            accepted = true;
        }

        public async Task CleanupAsync()
        {
            using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3)))
            {
                try
                {
                    if (issuedSession && !accepted)
                        await SendAsync(HttpMethod.Post, "https://auth.roblox.com/v2/logout", null, timeout.Token).ConfigureAwait(false);
                    else if (!completed && !string.IsNullOrEmpty(Code))
                        await SendAsync(HttpMethod.Post, Service + "cancel", new JObject { ["code"] = Code }, timeout.Token).ConfigureAwait(false);
                }
                catch { /* Cleanup never overrides the original result or keeps the dialog open. */ }
            }
            privateKey = null;
            csrf = null;
        }

        private void EnsureActive()
        {
            if (Code == null || string.IsNullOrEmpty(privateKey)) throw new InvalidOperationException("Request a new Quick Login code.");
            if (DateTimeOffset.UtcNow >= ExpiresAt) throw new InvalidOperationException("The Quick Login code has expired. Request a new code.");
        }

        private async Task<JObject> SendAsync(HttpMethod method, string url, JObject body, CancellationToken cancellationToken)
        {
            for (int attempt = 0; attempt < 2; attempt++)
            {
                using (var request = new HttpRequestMessage(method, url))
                {
                    if (body != null) request.Content = new StringContent(body.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json");
                    if (!string.IsNullOrEmpty(csrf)) request.Headers.TryAddWithoutValidation("x-csrf-token", csrf);
                    using (HttpResponseMessage response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false))
                    {
                        // Only retry Roblox's standard CSRF handshake. Never handle or solve challenges.
                        if (attempt == 0 && response.StatusCode == HttpStatusCode.Forbidden &&
                            !response.Headers.Contains("rblx-challenge-id") &&
                            response.Headers.TryGetValues("x-csrf-token", out var tokens) && !string.IsNullOrEmpty(tokens.FirstOrDefault()))
                        {
                            csrf = tokens.First();
                            continue;
                        }
                        if (!response.IsSuccessStatusCode)
                            throw new InvalidOperationException($"Roblox Quick Login returned HTTP {(int)response.StatusCode}. Try again later or use the normal browser sign-in option.");
                        string content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        if (string.IsNullOrWhiteSpace(content)) return new JObject();
                        try { return JObject.Parse(content); }
                        catch (Newtonsoft.Json.JsonException) { throw new InvalidOperationException("Roblox returned an unreadable Quick Login response."); }
                    }
                }
            }
            throw new InvalidOperationException("Roblox did not accept the Quick Login request.");
        }

        public void Dispose()
        {
            privateKey = null;
            csrf = null;
            http.Dispose();
        }
    }

    internal sealed class QuickLoginAccount
    {
        public long UserId { get; }
        public string Username { get; }
        public string SecurityToken { get; }

        public QuickLoginAccount(long userId, string username, string securityToken)
        {
            UserId = userId;
            Username = username;
            SecurityToken = securityToken;
        }
    }
}
