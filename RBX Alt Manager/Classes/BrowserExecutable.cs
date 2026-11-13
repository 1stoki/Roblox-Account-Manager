using System;
using System.Collections.Generic;
using System.IO;

namespace RBX_Alt_Manager.Classes
{
    internal static class BrowserExecutable
    {
        public static string Find(string configuredPath = null)
        {
            return Find(configuredPath, new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
            });
        }

        internal static string Find(string configuredPath, IEnumerable<string> installationRoots)
        {
            if (!string.IsNullOrWhiteSpace(configuredPath))
            {
                string path = Path.GetFullPath(Environment.ExpandEnvironmentVariables(configuredPath));
                if (!File.Exists(path))
                    throw new FileNotFoundException("The browser configured in BrowserConfig.json was not found. Update ExecutablePath or remove it to use Chrome or Edge.", path);
                return path;
            }

            foreach (string relativePath in new[] { @"Google\Chrome\Application\chrome.exe", @"Microsoft\Edge\Application\msedge.exe" })
                foreach (string root in installationRoots)
                {
                    if (string.IsNullOrWhiteSpace(root)) continue;
                    string path = Path.Combine(root, relativePath);
                    if (File.Exists(path)) return path;
                }

            return null;
        }
    }
}
