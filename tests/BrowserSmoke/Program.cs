using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using PuppeteerSharp;
using RBX_Alt_Manager.Classes;

internal static class Program
{
    private static async Task Main(string[] args)
    {
        if (args.Length >= 2 && args[0] == "--handoff-browser")
        {
            // Reproduce a browser launcher handing off to another process and exiting.
            using (Process.Start(new ProcessStartInfo(args[1], string.Join(" ", args.Skip(2).Select(SystemBrowserLauncher.QuoteArgument)))
            {
                UseShellExecute = false,
                CreateNoWindow = true
            })) { }
            return;
        }

        string browserName = args.FirstOrDefault() ?? "chrome";
        string relativePath = browserName == "edge" ? @"Microsoft\Edge\Application\msedge.exe" : @"Google\Chrome\Application\chrome.exe";
        string executable = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
        }.Where(root => !string.IsNullOrEmpty(root)).Select(root => Path.Combine(root, relativePath)).FirstOrDefault(File.Exists);
        if (executable == null) throw new Exception(browserName + " is required for the smoke test.");

        bool packaged = args.Contains("--packaged");
        await CheckBrowser(executable, Array.Empty<string>(), packaged);
        string wrapper = Path.ChangeExtension(Assembly.GetExecutingAssembly().Location, ".exe");
        await CheckBrowser(wrapper, new[] { "--handoff-browser", executable }, packaged);
        Console.WriteLine(browserName + ": direct launch and exited-launcher handoff passed.");
    }

    private static async Task CheckBrowser(string executable, string[] arguments, bool packaged)
    {
        Browser browser;
        if (packaged)
        {
            // Exercise the launcher from the exact executable we ship, including weaving.
            var assembly = Assembly.LoadFrom(Path.GetFullPath("build/Release/Roblox Account Manager.exe"));
            var launcher = assembly.GetType("RBX_Alt_Manager.Classes.SystemBrowserLauncher", true)
                .GetMethod("LaunchAsync", BindingFlags.Public | BindingFlags.Static);
            browser = await (Task<Browser>)launcher.Invoke(null, new object[] { executable, arguments, 30000 });
        }
        else browser = await SystemBrowserLauncher.LaunchAsync(executable, arguments);
        try
        {
            var page = (await browser.PagesAsync())[0];
            await page.SetContentAsync("<form><input id='username'></form>");
            await page.TypeAsync("#username", "browser-smoke-test");
            var text = await page.EvaluateExpressionAsync<string>("document.querySelector('#username').value");
            if (text != "browser-smoke-test") throw new Exception("Page interaction failed.");

            await page.SetCookieAsync(new CookieParam
            {
                Name = "ram_smoke_test",
                Value = "not-a-real-session",
                Domain = "example.test",
                Url = "https://example.test/",
                HttpOnly = true,
                Secure = true
            });
            var cookies = await page.GetCookiesAsync("https://example.test/");
            if (!cookies.Any(cookie => cookie.Name == "ram_smoke_test"))
                throw new Exception("Browser cookie access failed.");

            Console.WriteLine(await browser.GetVersionAsync());
            Console.WriteLine("Installed browser launch, page interaction and cookie checks passed.");
        }
        finally
        {
            await browser.CloseAsync();
        }
    }
}
