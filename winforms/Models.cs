using System;
using System.Collections.Generic;

namespace DshRemote
{
    public class DeviceInfo
    {
        public string Name = "";
        public string DnsName = "";
        public string OS = "";
        public bool Online;
        public DateTime LastSeen = DateTime.MinValue;

        public bool IsMobile
        {
            get
            {
                string os = (OS ?? "").ToLowerInvariant();
                if (os.Length > 0)
                {
                    return os.Contains("ios") || os.Contains("android");
                }

                string n = (Name ?? "").ToLowerInvariant();
                return n.Contains("iphone") || n.Contains("ipad") ||
                       n.Contains("android") || n.Contains("phone");
            }
        }

        public bool Matches(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            string v = Normalize(value);
            return v == Normalize(Name) || v == Normalize(DnsName);
        }

        public static string Normalize(string s)
        {
            if (s == null) return "";
            return s.Trim().TrimEnd('.').ToLowerInvariant();
        }
    }

    public class TailscaleSnapshot
    {
        public string SelfIp = "";
        public string Error = "";
        public List<DeviceInfo> Devices = new List<DeviceInfo>();
    }

    public class DeviceSelection
    {
        public string Device = "";
        public string Message = "";
    }

    public class AppSettings
    {
        public string Device = "";
        public string LastAction = "";
        public string LastUpdated = "";
        public string DshExe = "";
        public string DshCli = "";
        public string ListenHost = "auto";
        public int Port = 8443;
        public string Profile = "web";
    }

    public class OperationResult
    {
        public bool Success;
        public string Message = "";
        public string Output = "";
        public int Pid;
    }

    public class DshPaths
    {
        public string Exe = "";
        public string Cli = "";
    }

    public class ProcessResult
    {
        public int ExitCode;
        public bool TimedOut;
        public string StdOut = "";
        public string StdErr = "";
    }
}
