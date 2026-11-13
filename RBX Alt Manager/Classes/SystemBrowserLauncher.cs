using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using PuppeteerSharp;

namespace RBX_Alt_Manager.Classes
{
    internal static class SystemBrowserLauncher
    {
        private static readonly string ProfileRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "RobloxAccountManager", "Browsers"));

        public static async Task<Browser> LaunchAsync(string executablePath, string[] arguments = null, int timeout = 30000)
        {
            if (!File.Exists(executablePath)) throw new FileNotFoundException("The selected browser executable was not found.", executablePath);
            var args = new List<string>(arguments ?? Array.Empty<string>());
            if (args.Any(arg => arg == null || arg.IndexOf('\0') >= 0))
                throw new ArgumentException("Browser arguments cannot contain null values or null characters.");
            if (args.Any(arg => arg.StartsWith("--user-data-dir", StringComparison.OrdinalIgnoreCase) || arg.StartsWith("--remote-debugging-", StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException("Remove --user-data-dir and --remote-debugging-* from BrowserConfig.json. The manager creates its own isolated sign-in profile and local connection.");

            string profile = Path.Combine(ProfileRoot, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(profile);
            args.AddRange(new[]
            {
                "--user-data-dir=" + profile,
                "--remote-debugging-port=0",
                "--remote-debugging-address=127.0.0.1",
                "--no-first-run",
                "--no-default-browser-check",
                "--enable-automation",
                "about:blank"
            });

            Process process = null;
            Browser browser = null;
            string endpoint = null;
            try
            {
                process = Process.Start(new ProcessStartInfo(executablePath, string.Join(" ", args.Select(QuoteArgument)))
                {
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
                if (process == null) throw new IOException("Windows did not start the selected browser.");

                // Edge can hand off to another process and exit before DevTools is ready.
                // Discover the endpoint in our unique profile instead of relying on stderr
                // or treating the original process exiting as a failed browser launch.
                var timer = Stopwatch.StartNew();
                while (timer.ElapsedMilliseconds < timeout)
                {
                    endpoint = ReadEndpoint(profile);
                    if (endpoint != null) break;
                    await Task.Delay(100).ConfigureAwait(false);
                }
                if (endpoint == null)
                    throw new TimeoutException("The browser opened, but its local connection was not available within 30 seconds. Close the sign-in window and try again. Check whether a browser policy blocks remote debugging.");

                browser = (Browser)await Puppeteer.ConnectAsync(new ConnectOptions
                {
                    BrowserWSEndpoint = endpoint,
                    DefaultViewport = null,
                    WebSocketFactory = async (url, options, cancellationToken) =>
                    {
                        var socket = new ClientWebSocket();
                        // This connection is local to our browser, never the website proxy.
                        socket.Options.Proxy = null;
                        try
                        {
                            using (var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                            {
                                connectTimeout.CancelAfter(TimeSpan.FromSeconds(20));
                                await socket.ConnectAsync(url, connectTimeout.Token).ConfigureAwait(false);
                            }
                            return socket;
                        }
                        catch { socket.Dispose(); throw; }
                    }
                }).ConfigureAwait(false);

                Process ownedProcess = process;
                int cleanedUp = 0;
                browser.Disconnected += (sender, e) =>
                {
                    if (Interlocked.Exchange(ref cleanedUp, 1) != 0) return;
                    ownedProcess.Dispose();
                    _ = DeleteProfileAsync(profile);
                };
                return browser;
            }
            catch
            {
                if (browser != null)
                {
                    try { await browser.CloseAsync().ConfigureAwait(false); } catch { }
                }
                // Close only the browser belonging to this freshly created profile.
                await TryCloseEndpointAsync(endpoint ?? ReadEndpoint(profile)).ConfigureAwait(false);
                try { if (process != null && !process.HasExited) process.Kill(); } catch { }
                process?.Dispose();
                await DeleteProfileAsync(profile).ConfigureAwait(false);
                throw;
            }
        }

        private static string ReadEndpoint(string profile)
        {
            try
            {
                string[] lines = File.ReadAllLines(Path.Combine(profile, "DevToolsActivePort"));
                if (lines.Length >= 2 && int.TryParse(lines[0], out int port) && port > 0 && port <= 65535 &&
                    lines[1].StartsWith("/devtools/browser/", StringComparison.Ordinal) &&
                    Uri.TryCreate("ws://127.0.0.1:" + port + lines[1], UriKind.Absolute, out Uri endpoint) &&
                    endpoint.Host == "127.0.0.1" && endpoint.Port == port && string.IsNullOrEmpty(endpoint.UserInfo))
                    return endpoint.AbsoluteUri;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return null;
        }

        internal static string QuoteArgument(string argument)
        {
            // Quote Windows process arguments, including trailing backslashes and quotes.
            var result = new StringBuilder("\"");
            int slashes = 0;
            foreach (char character in argument)
            {
                if (character == '\\') { slashes++; continue; }
                result.Append('\\', character == '"' ? slashes * 2 + 1 : slashes);
                result.Append(character);
                slashes = 0;
            }
            return result.Append('\\', slashes * 2).Append('"').ToString();
        }

        private static async Task TryCloseEndpointAsync(string endpoint)
        {
            if (endpoint == null) return;
            try
            {
                using (var socket = new ClientWebSocket())
                using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3)))
                {
                    socket.Options.Proxy = null;
                    await socket.ConnectAsync(new Uri(endpoint), timeout.Token).ConfigureAwait(false);
                    byte[] command = Encoding.UTF8.GetBytes("{\"id\":1,\"method\":\"Browser.close\"}");
                    await socket.SendAsync(new ArraySegment<byte>(command), WebSocketMessageType.Text, true, timeout.Token).ConfigureAwait(false);
                }
            }
            catch { }
        }

        private static async Task DeleteProfileAsync(string profile)
        {
            string fullPath = Path.GetFullPath(profile);
            if (!string.Equals(Path.GetDirectoryName(fullPath), ProfileRoot, StringComparison.OrdinalIgnoreCase) ||
                !Guid.TryParseExact(Path.GetFileName(fullPath), "N", out _)) return;

            for (int attempt = 0; attempt < 8; attempt++)
            {
                try
                {
                    if (Directory.Exists(fullPath)) Directory.Delete(fullPath, true);
                    return;
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
                await Task.Delay(250 * (attempt + 1)).ConfigureAwait(false);
            }
        }
    }
}
