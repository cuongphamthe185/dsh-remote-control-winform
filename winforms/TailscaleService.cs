using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace DshRemote
{
    public class TailscaleService
    {
        private readonly string exe;

        public TailscaleService()
        {
            exe = FindExe();
        }

        public string ExecutablePath
        {
            get { return exe; }
        }

        public TailscaleSnapshot GetSnapshot()
        {
            TailscaleSnapshot snapshot = new TailscaleSnapshot();

            if (exe.Length == 0)
            {
                snapshot.Error = "tailscale.exe not found (install Tailscale or add it to PATH)";
                return snapshot;
            }

            ProcessResult result = Run(exe, "status --json", 8000);
            if (result.TimedOut)
            {
                snapshot.Error = "tailscale status timed out";
                return snapshot;
            }
            if (result.ExitCode != 0)
            {
                snapshot.Error = FirstLine(result.StdErr);
                if (snapshot.Error.Length == 0) snapshot.Error = "tailscale status failed";
                return snapshot;
            }
            if (string.IsNullOrWhiteSpace(result.StdOut))
            {
                snapshot.Error = "tailscale status returned no data";
                return snapshot;
            }

            try
            {
                Parse(result.StdOut, snapshot);
            }
            catch (Exception ex)
            {
                snapshot.Error = "cannot parse tailscale status: " + ex.Message;
            }

            return snapshot;
        }

        public static DeviceSelection PickDefaultDevice(TailscaleSnapshot snapshot, string lastDevice)
        {
            DeviceSelection selection = new DeviceSelection();

            if (snapshot == null)
            {
                selection.Message = "Tailscale snapshot unavailable.";
                return selection;
            }

            if (!string.IsNullOrWhiteSpace(snapshot.Error))
            {
                if (!string.IsNullOrWhiteSpace(lastDevice))
                {
                    selection.Device = lastDevice.Trim();
                    selection.Message = "Tailscale error: " + snapshot.Error + " - keeping last device.";
                }
                else
                {
                    selection.Message = "Tailscale error: " + snapshot.Error;
                }
                return selection;
            }

            List<DeviceInfo> mobiles = snapshot.Devices.Where(d => d.IsMobile).ToList();
            List<DeviceInfo> online = mobiles
                .Where(d => d.Online)
                .OrderByDescending(d => d.LastSeen)
                .ToList();

            if (!string.IsNullOrWhiteSpace(lastDevice))
            {
                DeviceInfo match = mobiles.FirstOrDefault(d => d.Matches(lastDevice));
                if (match != null && match.Online)
                {
                    selection.Device = match.Name;
                    return selection;
                }

                if (online.Count > 0)
                {
                    selection.Device = online[0].Name;
                    selection.Message = "Last device is offline; using " + online[0].Name + ".";
                    return selection;
                }

                selection.Message = "No suitable mobile device found (last device is offline).";
                return selection;
            }

            if (online.Count > 0)
            {
                selection.Device = online[0].Name;
                return selection;
            }

            selection.Message = "No suitable mobile device found (no online iPhone/iPad/Android).";
            return selection;
        }

        private static void Parse(string json, TailscaleSnapshot snapshot)
        {
            JavaScriptSerializer serializer = new JavaScriptSerializer();
            serializer.MaxJsonLength = int.MaxValue;

            Dictionary<string, object> root =
                serializer.Deserialize<Dictionary<string, object>>(json);

            object selfObject;
            if (root.TryGetValue("Self", out selfObject))
            {
                Dictionary<string, object> self = selfObject as Dictionary<string, object>;
                if (self != null) snapshot.SelfIp = PickIpv4(self);
            }

            object peersObject;
            if (root.TryGetValue("Peer", out peersObject))
            {
                Dictionary<string, object> peers = peersObject as Dictionary<string, object>;
                if (peers != null)
                {
                    foreach (KeyValuePair<string, object> entry in peers)
                    {
                        Dictionary<string, object> peer = entry.Value as Dictionary<string, object>;
                        if (peer == null) continue;

                        DeviceInfo device = new DeviceInfo();
                        string dns = GetString(peer, "DNSName").TrimEnd('.');
                        string host = GetString(peer, "HostName");

                        device.DnsName = dns;
                        if (dns.Length > 0)
                        {
                            int dot = dns.IndexOf('.');
                            device.Name = dot > 0 ? dns.Substring(0, dot) : dns;
                        }
                        else
                        {
                            device.Name = host;
                        }

                        device.OS = GetString(peer, "OS");
                        device.Online = GetBool(peer, "Online");
                        device.LastSeen = ParseDate(GetString(peer, "LastSeen"));

                        if (!string.IsNullOrEmpty(device.Name))
                        {
                            snapshot.Devices.Add(device);
                        }
                    }
                }
            }
        }

        private static string PickIpv4(Dictionary<string, object> value)
        {
            object ipsObject;
            if (!value.TryGetValue("TailscaleIPs", out ipsObject)) return "";
            if (ipsObject == null || ipsObject is string) return "";

            IEnumerable ips = ipsObject as IEnumerable;
            if (ips == null) return "";

            foreach (object item in ips)
            {
                string ip = item == null ? "" : item.ToString();
                if (ip.Contains(".") && !ip.Contains(":")) return ip;
            }

            foreach (object item in ips)
            {
                string ip = item == null ? "" : item.ToString();
                if (ip.Length > 0) return ip;
            }

            return "";
        }

        private static string GetString(Dictionary<string, object> value, string key)
        {
            object item;
            if (!value.TryGetValue(key, out item) || item == null) return "";
            return item.ToString();
        }

        private static bool GetBool(Dictionary<string, object> value, string key)
        {
            object item;
            if (!value.TryGetValue(key, out item) || item == null) return false;
            if (item is bool) return (bool)item;

            bool parsed;
            return bool.TryParse(item.ToString(), out parsed) && parsed;
        }

        private static DateTime ParseDate(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return DateTime.MinValue;

            DateTime parsed;
            if (DateTime.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
                out parsed))
            {
                return parsed;
            }

            return DateTime.MinValue;
        }

        private static string FirstLine(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "";
            string[] lines = value.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            return lines.Length > 0 ? lines[0].Trim() : "";
        }

        private static string FindExe()
        {
            try
            {
                string path = Environment.GetEnvironmentVariable("PATH") ?? "";
                foreach (string dir in path.Split(';'))
                {
                    if (string.IsNullOrWhiteSpace(dir)) continue;
                    string candidate = Path.Combine(dir.Trim(), "tailscale.exe");
                    if (File.Exists(candidate)) return candidate;
                }
            }
            catch { }

            foreach (Environment.SpecialFolder folder in new Environment.SpecialFolder[]
            {
                Environment.SpecialFolder.ProgramFiles,
                Environment.SpecialFolder.ProgramFilesX86
            })
            {
                try
                {
                    string baseDir = Environment.GetFolderPath(folder);
                    if (string.IsNullOrEmpty(baseDir)) continue;
                    string candidate = Path.Combine(baseDir, "Tailscale", "tailscale.exe");
                    if (File.Exists(candidate)) return candidate;
                }
                catch { }
            }

            return "";
        }

        private static ProcessResult Run(string fileName, string arguments, int timeoutMs)
        {
            ProcessResult result = new ProcessResult();

            try
            {
                ProcessStartInfo startInfo = new ProcessStartInfo();
                startInfo.FileName = fileName;
                startInfo.Arguments = arguments;
                startInfo.UseShellExecute = false;
                startInfo.CreateNoWindow = true;
                startInfo.WindowStyle = ProcessWindowStyle.Hidden;
                startInfo.RedirectStandardOutput = true;
                startInfo.RedirectStandardError = true;
                startInfo.StandardOutputEncoding = new UTF8Encoding(false);
                startInfo.StandardErrorEncoding = new UTF8Encoding(false);

                using (Process process = new Process())
                {
                    process.StartInfo = startInfo;
                    process.Start();

                    Task<string> stdout = process.StandardOutput.ReadToEndAsync();
                    Task<string> stderr = process.StandardError.ReadToEndAsync();

                    bool exited = process.WaitForExit(timeoutMs);
                    if (!exited)
                    {
                        result.TimedOut = true;
                        try { process.Kill(); } catch { }
                        process.WaitForExit(3000);
                    }

                    result.StdOut = SafeResult(stdout);
                    result.StdErr = SafeResult(stderr);
                    result.ExitCode = exited ? process.ExitCode : -1;
                }
            }
            catch (Exception ex)
            {
                result.ExitCode = -1;
                result.StdErr = ex.Message;
            }

            return result;
        }

        private static string SafeResult(Task<string> task)
        {
            try
            {
                return task.Wait(3000) ? task.Result : "";
            }
            catch
            {
                return "";
            }
        }
    }
}
