using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using RBX_Alt_Manager.Classes;

internal static class Program
{
    private static readonly CancellationToken None = CancellationToken.None;
    private static int passed;

    private static async Task Main(string[] args)
    {
        if (Array.IndexOf(args, "--live-smoke") >= 0)
        {
            using (var client = new QuickLoginClient())
            {
                try
                {
                    await client.StartAsync(None);
                    if (await client.GetStatusAsync(None) != "Created") throw new Exception("Unexpected status for an unapproved request.");
                    Console.WriteLine("Live Roblox Quick Login create/status check passed. No account was signed in.");
                }
                finally { await client.CleanupAsync(); }
            }
            return;
        }

        await ApprovedFlow(true);
        await ApprovedFlow(false);
        await RejectBeforeApproval();
        await RejectMissingCookie();
        await RejectExpiredCode();
        await DoNotRetryChallenge();
        await CancelPendingCode();
        Console.WriteLine(passed + " Quick Login approval, session and cleanup checks passed.");
    }

    private static HttpResponseMessage Json(string text, HttpStatusCode status = HttpStatusCode.OK)
        => new HttpResponseMessage(status) { Content = new StringContent(text) };

    private static string Created(DateTimeOffset? expires = null)
        => new JObject { ["code"] = "ABC123", ["privateKey"] = "dummy-private-key", ["expirationTime"] = (expires ?? DateTimeOffset.UtcNow.AddMinutes(5)).ToString("O") }.ToString();

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        passed++;
    }

    private static async Task Reject(Func<Task> action, string message)
    {
        bool rejected = false;
        try { await action(); } catch (InvalidOperationException) { rejected = true; }
        Check(rejected, message);
    }

    private static async Task ApprovedFlow(bool accept)
    {
        var cookies = new CookieContainer();
        int create = 0, status = 0, logins = 0, logout = 0, cancel = 0;
        var handler = new Fake(async request =>
        {
            string path = request.RequestUri.AbsolutePath;
            string body = request.Content == null ? "{}" : await request.Content.ReadAsStringAsync();
            if (path.EndsWith("/create"))
            {
                if (++create == 1)
                {
                    var retry = Json("{}", HttpStatusCode.Forbidden);
                    retry.Headers.Add("x-csrf-token", "dummy-csrf");
                    return retry;
                }
                Check(request.Headers.Contains("x-csrf-token"), "The CSRF handshake was not completed.");
                return Json(Created());
            }
            if (path.EndsWith("/status"))
            {
                JObject data = JObject.Parse(body);
                Check(data.Value<string>("code") == "ABC123" && data.Value<string>("privateKey") == "dummy-private-key", "Status request must belong to this client.");
                return Json(++status == 1 ? "{\"status\":\"UserLinked\"}" : "{\"status\":\"Validated\"}");
            }
            if (path == "/v2/login")
            {
                logins++;
                JObject data = JObject.Parse(body);
                Check(data.Value<string>("ctype") == "AuthToken" && data.Value<string>("cvalue") == "ABC123" && data.Value<string>("password") == "dummy-private-key", "Only the approved device request may be exchanged.");
                cookies.Add(new Cookie(".ROBLOSECURITY", "dummy-session", "/", ".roblox.com") { Secure = true, HttpOnly = true });
                return Json("{}");
            }
            if (path == "/v1/users/authenticated") return Json("{\"id\":42,\"name\":\"TestAccount\"}");
            if (path.EndsWith("/logout")) { logout++; return Json("{}"); }
            if (path.EndsWith("/cancel")) { cancel++; return Json("{}"); }
            throw new Exception("Unexpected request.");
        });
        using (var client = new QuickLoginClient(handler, cookies))
        {
            await client.StartAsync(None);
            Check(await client.GetStatusAsync(None) == "UserLinked", "Entering a code must still require approval.");
            await Reject(async () => { await client.CompleteAsync(None); }, "An entered but unapproved code must not sign in.");
            Check(logins == 0, "Unapproved requests must never reach the login endpoint.");
            Check(await client.GetStatusAsync(None) == "Validated", "Approval was not detected.");
            QuickLoginAccount account = await client.CompleteAsync(None);
            Check(account.UserId == 42 && account.Username == "TestAccount" && account.SecurityToken == "dummy-session", "The session identity must be verified before it can be saved.");
            if (accept) client.AcceptSession();
            await client.CleanupAsync();
            Check(logout == (accept ? 0 : 1) && cancel == 0, "Keep accepted sessions; revoke only the newly issued unsaved session.");
        }
    }

    private static async Task RejectBeforeApproval()
    {
        int loginRequests = 0;
        using (var client = new QuickLoginClient(new Fake(request =>
        {
            if (request.RequestUri.AbsolutePath == "/v2/login") loginRequests++;
            return Task.FromResult(Json(Created()));
        }), new CookieContainer()))
        {
            await client.StartAsync(None);
            await Reject(async () => { await client.CompleteAsync(None); }, "A fresh code must not authorize a login.");
            Check(loginRequests == 0, "No credentials may be exchanged before approval.");
        }
    }

    private static async Task RejectMissingCookie()
    {
        using (var client = new QuickLoginClient(new Fake(request => Task.FromResult(Json(
            request.RequestUri.AbsolutePath.EndsWith("/create") ? Created() : request.RequestUri.AbsolutePath.EndsWith("/status") ? "{\"status\":\"Validated\"}" : "{}"))), new CookieContainer()))
        {
            await client.StartAsync(None);
            await client.GetStatusAsync(None);
            await Reject(async () => { await client.CompleteAsync(None); }, "HTTP 200 without a session cookie must not create an account.");
        }
    }

    private static async Task RejectExpiredCode()
    {
        using (var client = new QuickLoginClient(new Fake(request => Task.FromResult(Json(Created(DateTimeOffset.UtcNow.AddMinutes(-1))))), new CookieContainer()))
            await Reject(() => client.StartAsync(None), "An expired server code must be rejected.");
    }

    private static async Task DoNotRetryChallenge()
    {
        int count = 0;
        using (var client = new QuickLoginClient(new Fake(request =>
        {
            count++;
            var response = Json("{\"privateKey\":\"do-not-display\"}", HttpStatusCode.Forbidden);
            response.Headers.Add("rblx-challenge-id", "dummy-challenge");
            response.Headers.Add("x-csrf-token", "dummy-csrf");
            return Task.FromResult(response);
        }), new CookieContainer()))
        {
            string error = "";
            try { await client.StartAsync(None); } catch (InvalidOperationException ex) { error = ex.Message; }
            Check(count == 1 && !error.Contains("do-not-display"), "Challenges must stop the request without retrying or displaying response secrets.");
        }
    }

    private static async Task CancelPendingCode()
    {
        bool cancelled = false;
        using (var client = new QuickLoginClient(new Fake(async request =>
        {
            if (request.RequestUri.AbsolutePath.EndsWith("/create")) return Json(Created());
            JObject body = JObject.Parse(await request.Content.ReadAsStringAsync());
            cancelled = request.RequestUri.AbsolutePath.EndsWith("/cancel") && body.Value<string>("code") == "ABC123" && body["privateKey"] == null;
            return Json("{}");
        }), new CookieContainer()))
        {
            await client.StartAsync(None);
            await client.CleanupAsync();
            Check(cancelled, "Closing an unapproved request must cancel that exact request.");
        }
    }

    private sealed class Fake : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> responder;
        public Fake(Func<HttpRequestMessage, Task<HttpResponseMessage>> responder) { this.responder = responder; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return responder(request);
        }
    }
}
