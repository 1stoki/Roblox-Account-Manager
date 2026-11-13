# Browser sign-in fix

Click **Add Account** to request a Roblox **Quick Login** code. On a device where you are already signed in to the desired account, open Roblox **Settings > Quick Login**, enter the code shown by this manager and review the approval request. You can also use **Open approval page** to open Roblox's official confirmation page in your normal browser. That browser needs to be signed in to the desired account. Only approve a code you requested yourself.

The manager uses Roblox's device approval flow, verifies the resulting account identity and then saves it through its existing account storage. It does not control the approval browser, read its existing cookies, or ask you to paste a session cookie. Codes expire; pending requests are cancelled when the dialog closes. If a newly issued session cannot be saved, only that new session is logged out. Login codes, private keys and cookies are not written to diagnostic logs.

Quick Login needs an already signed-in device/browser and may not be available for every account. See [Roblox's Quick Login instructions](https://en.help.roblox.com/hc/en-us/articles/360056582012-Quick-Login). The API can still reject or require additional verification; the client stops and reports that response.

Choose **Sign in with password / browser instead** for an installed, up-to-date Chrome or Edge. The manager opens a separate temporary profile; complete CAPTCHA and two-step verification yourself. This is still a managed browser, so changing the browser engine does not establish that Roblox will accept its CAPTCHA verification. A working launch test is not a successful Roblox sign-in test.

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

## Quick Login checks

Build `tests/QuickLogin/QuickLogin.Tests.csproj`, then run `QuickLogin.Tests.exe` from its `net472` and `net8.0` output folders. The checks cover explicit device approval, CSRF handshakes, verified account identity, missing session cookies, expiration, challenge responses, request cancellation and revoking only unsaved new sessions. The optional `--live-smoke` check creates, polls and cancels an unapproved code without signing into an account or printing the code. Completing a real user approval remains a manual verification step.
