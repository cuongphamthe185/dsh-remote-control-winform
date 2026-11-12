using System;
using System.IO;
using System.Text;

namespace DshRemote
{
    public interface ISettingsStore
    {
        AppSettings Load();
        void Save(AppSettings settings);
    }

    public class IniSettingsStore : ISettingsStore
    {
        private readonly string path;

        public IniSettingsStore(string path)
        {
            this.path = path;
        }

        public AppSettings Load()
        {
            AppSettings settings = new AppSettings();
            try
            {
                if (!File.Exists(path)) return settings;

                foreach (string raw in File.ReadAllLines(path))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#") || line.StartsWith(";")) continue;

                    int idx = line.IndexOf('=');
                    if (idx <= 0) continue;

                    string key = line.Substring(0, idx).Trim();
                    string value = line.Substring(idx + 1).Trim();

                    if (key.Equals("Device", StringComparison.OrdinalIgnoreCase)) settings.Device = value;
                    else if (key.Equals("LastAction", StringComparison.OrdinalIgnoreCase)) settings.LastAction = value;
                    else if (key.Equals("LastUpdated", StringComparison.OrdinalIgnoreCase)) settings.LastUpdated = value;
                    else if (key.Equals("DshExe", StringComparison.OrdinalIgnoreCase)) settings.DshExe = value;
                    else if (key.Equals("DshCli", StringComparison.OrdinalIgnoreCase)) settings.DshCli = value;
                    else if (key.Equals("ListenHost", StringComparison.OrdinalIgnoreCase)) settings.ListenHost = value;
                    else if (key.Equals("Profile", StringComparison.OrdinalIgnoreCase)) settings.Profile = value;
                    else if (key.Equals("Port", StringComparison.OrdinalIgnoreCase))
                    {
                        int port;
                        if (int.TryParse(value, out port)) settings.Port = port;
                    }
                }
            }
            catch
            {
                // A broken settings file must never stop the app.
            }

            if (settings.Port <= 0 || settings.Port > 65535) settings.Port = 8443;
            if (string.IsNullOrWhiteSpace(settings.Profile)) settings.Profile = "web";
            if (string.IsNullOrWhiteSpace(settings.ListenHost)) settings.ListenHost = "auto";
            if (settings.Device == null) settings.Device = "";
            if (settings.LastAction == null) settings.LastAction = "";
            if (settings.LastUpdated == null) settings.LastUpdated = "";
            if (settings.DshExe == null) settings.DshExe = "";
            if (settings.DshCli == null) settings.DshCli = "";
            return settings;
        }

        public void Save(AppSettings settings)
        {
            AppPaths.EnsureRuntimeDir();

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Device=" + settings.Device);
            sb.AppendLine("LastAction=" + settings.LastAction);
            sb.AppendLine("LastUpdated=" + settings.LastUpdated);
            sb.AppendLine("DshExe=" + settings.DshExe);
            sb.AppendLine("DshCli=" + settings.DshCli);
            sb.AppendLine("ListenHost=" + settings.ListenHost);
            sb.AppendLine("Port=" + settings.Port);
            sb.AppendLine("Profile=" + settings.Profile);

            string tmp = path + ".tmp";
            File.WriteAllText(tmp, sb.ToString(), new UTF8Encoding(false));

            if (File.Exists(path))
            {
                string backup = path + ".bak";
                try
                {
                    if (File.Exists(backup)) File.Delete(backup);
                    File.Replace(tmp, path, backup);
                }
                catch
                {
                    File.Copy(tmp, path, true);
                    File.Delete(tmp);
                }
            }
            else
            {
                File.Move(tmp, path);
            }
        }
    }
}
