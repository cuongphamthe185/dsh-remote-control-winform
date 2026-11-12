using System;
using System.IO;

namespace DshRemote
{
    public static class AppPaths
    {
        public const string RuntimeDirEnvVar = "DSH_REMOTE_DIR";
        public const string PortableDirName = "data";

        private static readonly object gate = new object();
        private static string runtimeDir;
        private static string runtimeSource;

        public static string BaseDir
        {
            get { return AppDomain.CurrentDomain.BaseDirectory; }
        }

        public static string DefaultRuntimeDir
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".dsh", "remote");
            }
        }

        /// <summary>
        /// Folder for runtime files (settings, logs, pairing URL, QR).
        /// Order: %DSH_REMOTE_DIR% > "data" folder next to the exe (portable)
        /// > %USERPROFILE%\.dsh\remote. Keep in sync with the PowerShell scripts.
        /// </summary>
        public static string RuntimeDir
        {
            get
            {
                EnsureResolved();
                return runtimeDir;
            }
        }

        /// <summary>Where RuntimeDir came from: "default", "DSH_REMOTE_DIR" or "portable".</summary>
        public static string RuntimeSource
        {
            get
            {
                EnsureResolved();
                return runtimeSource;
            }
        }

        public static string SettingsFile
        {
            get { return Path.Combine(RuntimeDir, "gui-settings.ini"); }
        }

        public static string LogFile
        {
            get { return Path.Combine(RuntimeDir, "gui.log"); }
        }

        public static string StartScript
        {
            get { return FindScript("dsh-web-start.ps1"); }
        }

        public static string StopScript
        {
            get { return FindScript("dsh-web-stop.ps1"); }
        }

        public static void EnsureRuntimeDir()
        {
            Directory.CreateDirectory(RuntimeDir);
        }

        private static void EnsureResolved()
        {
            lock (gate)
            {
                if (runtimeDir != null) return;

                string dir = null;
                string source = "default";

                try
                {
                    string fromEnv = Environment.GetEnvironmentVariable(RuntimeDirEnvVar);
                    if (!string.IsNullOrWhiteSpace(fromEnv))
                    {
                        dir = Normalize(fromEnv);
                        source = RuntimeDirEnvVar;
                    }
                    else
                    {
                        string portable = Path.Combine(BaseDir, PortableDirName);
                        if (Directory.Exists(portable))
                        {
                            dir = Normalize(portable);
                            source = "portable";
                        }
                    }
                }
                catch
                {
                    dir = null;
                }

                if (string.IsNullOrEmpty(dir) ||
                    string.Equals(dir, Path.GetFullPath(DefaultRuntimeDir), StringComparison.OrdinalIgnoreCase))
                {
                    dir = DefaultRuntimeDir;
                    source = "default";
                }

                runtimeDir = dir;
                runtimeSource = source;
            }
        }

        private static string Normalize(string value)
        {
            string dir = Environment.ExpandEnvironmentVariables(value.Trim().Trim('"'));
            if (!Path.IsPathRooted(dir)) dir = Path.Combine(BaseDir, dir);
            dir = Path.GetFullPath(dir);

            // A trailing backslash would escape the closing quote when the path
            // is passed to PowerShell as an argument.
            if (dir.Length > 3) dir = dir.TrimEnd('\\', '/');
            return dir;
        }

        private static string FindScript(string name)
        {
            string besideExe = Path.Combine(BaseDir, name);
            if (File.Exists(besideExe)) return besideExe;

            string inScripts = Path.Combine(BaseDir, "scripts", name);
            if (File.Exists(inScripts)) return inScripts;

            return besideExe;
        }
    }
}
