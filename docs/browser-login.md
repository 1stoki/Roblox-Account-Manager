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

Run `pwsh -File tests/BrowserExecutable.Tests.ps1` for browser selection checks. The **Build Windows** workflow runs these checks, restores packages, builds the application and uploads the Release folder as an Actions artifact.
