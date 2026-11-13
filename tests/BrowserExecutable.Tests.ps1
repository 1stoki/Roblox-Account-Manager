$ErrorActionPreference = 'Stop'
$source = Join-Path $PSScriptRoot '..\RBX Alt Manager\Classes\BrowserExecutable.cs'
$testSource = @'
using System;
using System.IO;
using RBX_Alt_Manager.Classes;

public static class BrowserExecutableTests
{
    public static int Run()
    {
        string root = Path.Combine(Path.GetTempPath(), "ram-browser-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        int passed = 0;
        Action<bool, string> check = (condition, name) => { if (!condition) throw new Exception(name); passed++; };
        try
        {
            check(BrowserExecutable.Find(null, new[] { "", root }) == null, "No browser must return null, including empty installation roots");
            string edge = Path.Combine(root, @"Microsoft\Edge\Application\msedge.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(edge));
            File.WriteAllText(edge, "");
            check(BrowserExecutable.Find(null, new[] { root }) == edge, "Edge must work without Chrome");
            string chrome = Path.Combine(root, @"Google\Chrome\Application\chrome.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(chrome));
            File.WriteAllText(chrome, "");
            check(BrowserExecutable.Find(null, new[] { root }) == chrome, "Chrome must take precedence when both are installed");
            check(BrowserExecutable.Find(edge, new[] { root }) == edge, "An explicit executable must take precedence");
            Environment.SetEnvironmentVariable("RAM_BROWSER_TEST_ROOT", root);
            check(BrowserExecutable.Find(@"%RAM_BROWSER_TEST_ROOT%\Microsoft\Edge\Application\msedge.exe", new[] { root }) == edge, "Environment variables in configured paths must expand");
            bool missingRejected = false;
            try { BrowserExecutable.Find(Path.Combine(root, "missing.exe"), new[] { root }); }
            catch (FileNotFoundException) { missingRejected = true; }
            check(missingRejected, "Invalid explicit paths must fail instead of silently selecting another browser");
            check(BrowserExecutable.Find(null, new[] { Path.Combine(root, "absent"), root }) == chrome, "Search must continue past a missing installation root");
        }
        finally
        {
            Environment.SetEnvironmentVariable("RAM_BROWSER_TEST_ROOT", null);
            string expectedParent = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!string.Equals(Path.GetDirectoryName(Path.GetFullPath(root)), expectedParent, StringComparison.OrdinalIgnoreCase))
                throw new Exception("Refusing to remove a test folder outside the temporary directory.");
            Directory.Delete(root, true);
        }
        return passed;
    }
}
'@
Add-Type -TypeDefinition ("using RBX_Alt_Manager.Classes;`n" + (Get-Content -LiteralPath $source -Raw) + "`n" + $testSource.Replace('using System;', '').Replace('using System.IO;', '').Replace('using RBX_Alt_Manager.Classes;', ''))
$passed = [BrowserExecutableTests]::Run()
Write-Output "$passed browser selection checks passed."
