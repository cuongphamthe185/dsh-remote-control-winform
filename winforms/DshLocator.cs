using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Win32;

namespace DshRemote
{
    public static class DshLocator
    {
        public static DshPaths Find(string configuredExe, string configuredCli)
        {
            DshPaths result = new DshPaths();
            result.Exe = Existing(configuredExe);
            result.Cli = Existing(configuredCli);

            if (result.Cli.Length == 0 && result.Exe.Length > 0)
            {
                result.Cli = CliBesideExe(result.Exe);
            }
            if (result.Exe.Length == 0 && result.Cli.Length > 0)
            {
                result.Exe = ExeFromCli(result.Cli);
            }

            if (result.Exe.Length == 0)
            {
                string exe = FindExe();
                if (exe.Length > 0)
                {
                    result.Exe = exe;
                    if (result.Cli.Length == 0) result.Cli = CliBesideExe(exe);
                }
            }

            if (result.Cli.Length == 0)
            {
                string cli = FindOnPath("dsh.cmd");
                if (cli.Length > 0)
                {
                    result.Cli = cli;
                    if (result.Exe.Length == 0) result.Exe = ExeFromCli(cli);
                }
            }

            return result;
        }

        private static string Existing(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return "";
            try
            {
                return File.Exists(path) ? path : "";
            }
            catch
            {
                return "";
            }
        }

        private static string CliBesideExe(string exe)
        {
            try
            {
                string dir = Path.GetDirectoryName(exe);
                if (string.IsNullOrEmpty(dir)) return "";
                string cli = Path.Combine(dir, "resources", "runtime", "cli", "bin", "dsh.cmd");
                return File.Exists(cli) ? cli : "";
            }
            catch
            {
                return "";
            }
        }

        private static string ExeFromCli(string cli)
        {
            try
            {
                string root = cli;
                for (int i = 0; i < 5; i++) root = Path.GetDirectoryName(root);
                if (string.IsNullOrEmpty(root)) return "";
                string exe = Path.Combine(root, "DeepSeek Harness.exe");
                return File.Exists(exe) ? exe : "";
            }
            catch
            {
                return "";
            }
        }

        private static string FindExe()
        {
            List<string> candidates = new List<string>();

            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!string.IsNullOrEmpty(local))
            {
                candidates.Add(Path.Combine(local, "Programs", "DeepSeek Harness", "DeepSeek Harness.exe"));
            }

            string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            if (!string.IsNullOrEmpty(pf))
            {
                candidates.Add(Path.Combine(pf, "DeepSeek Harness", "DeepSeek Harness.exe"));
            }

            string pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            if (!string.IsNullOrEmpty(pf86))
            {
                candidates.Add(Path.Combine(pf86, "DeepSeek Harness", "DeepSeek Harness.exe"));
            }

            foreach (string c in candidates)
            {
                try
                {
                    if (File.Exists(c)) return c;
                }
                catch { }
            }

            string appPath = ReadAppPath(Registry.CurrentUser);
            if (appPath.Length > 0) return appPath;

            appPath = ReadAppPath(Registry.LocalMachine);
            if (appPath.Length > 0) return appPath;

            return "";
        }

        private static string ReadAppPath(RegistryKey root)
        {
            try
            {
                using (RegistryKey key = root.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\DeepSeek Harness.exe"))
                {
                    if (key == null) return "";
                    object value = key.GetValue(null);
                    if (value == null) return "";
                    string path = value.ToString().Trim('"');
                    return File.Exists(path) ? path : "";
                }
            }
            catch
            {
                return "";
            }
        }

        private static string FindOnPath(string fileName)
        {
            try
            {
                string path = Environment.GetEnvironmentVariable("PATH") ?? "";
                foreach (string dir in path.Split(';'))
                {
                    if (string.IsNullOrWhiteSpace(dir)) continue;
                    string candidate = Path.Combine(dir.Trim(), fileName);
                    if (File.Exists(candidate)) return candidate;
                }
            }
            catch { }
            return "";
        }
    }
}
