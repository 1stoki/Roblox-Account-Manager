# Browser sign-in fix

Click **Add** to sign in with an installed, up-to-date Google Chrome or Microsoft Edge. The manager opens a separate temporary browser profile; it does not read your everyday browser profile. Complete CAPTCHA and two-step verification yourself in that window.

The fork no longer forces a Chrome 111 User-Agent, disables web security, ignores certificate errors, or injects stealth scripts into the browser. Manual sign-in also skips the local `extension` folder, configured page scripts and automatic CAPTCHA frame handling. The legacy CefSharp option still applies to account browsing, but **Add** always uses the installed browser.

Chrome is preferred over Edge. Standard per-user and system installation locations are checked. For a portable browser or a custom location, add `ExecutablePath` to `BrowserConfig.json` beside the manager executable. Existing configuration fields can stay in that file:

```json
{
  "ExecutablePath": "C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe"
}
```

If no supported browser is found, the manager shows an actionable error instead of downloading the old bundled Chromium for sign-in. If an explicitly configured path is invalid, fix or remove it. An installed browser still needs to be updated by its normal updater.

If Roblox continues to show **Try again**, verify sign-in in the same updated browser outside the manager. Network reputation, enabled proxies, browser launch arguments and Roblox-side challenge failures can still affect the result. This change repairs browser compatibility; successful live sign-in must be checked on the affected computer.

## Build and checks

Build with Visual Studio 2022 / MSBuild and the .NET Framework 4.7.2 targeting pack. Restore `RBX Alt Manager.sln` with NuGet, then build Release / Any CPU. `CefSharp.Common` is aligned to 109.1.110 to match the existing WinForms reference and project imports.

Run `pwsh -File tests/BrowserExecutable.Tests.ps1` for browser selection checks. Build the regression checks with `dotnet build tests/BrowserSmoke/BrowserSmoke.csproj -c Release`. Run `BrowserSmoke.exe chrome` and `BrowserSmoke.exe edge` from both the `net472` and `net8.0` output folders. These checks use the exact launcher source used by the application, including a launcher process that exits immediately after handing off to a child browser. They check browser launch, page interaction and a dummy cookie without signing in to Roblox. The **Build Windows** workflow runs these checks, restores packages, builds the application and uploads the Release folder as an Actions artifact.

## Edge opens about:blank and the manager reports a launch failure

The system-browser launcher now waits for the `DevToolsActivePort` file inside a unique temporary profile, then connects to the loopback endpoint recorded there. It does not depend on the original process staying alive or printing a DevTools address to stderr. This handles browser process handoffs. The local WebSocket connection bypasses the system proxy; website traffic continues to use the browser's configured proxy. Closing the browser cleans up its temporary profile. Existing browser windows and everyday profiles are not used.
