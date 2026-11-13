using System;
using System.Linq;
using System.Threading.Tasks;
using PuppeteerSharp;
using RBX_Alt_Manager.Classes;

internal static class Program
{
    private static async Task Main()
    {
        var executable = BrowserExecutable.Find();
        if (executable == null) throw new Exception("Chrome or Edge is required for the smoke test.");

        var browser = await Puppeteer.LaunchAsync(new LaunchOptions
        {
            ExecutablePath = executable,
            Headless = false,
            DefaultViewport = null
        });
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
