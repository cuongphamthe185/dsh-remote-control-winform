using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace DshRemote
{
    public class HostController
    {
        private readonly Logger logger;

        public HostController(Logger logger)
        {
            this.logger = logger;
        }

        public Task<OperationResult> StartAsync(AppSettings settings, string device, string host, int port)
        {
            return Task.Run(() => Start(settings, device, host, port));
        }

        public Task<OperationResult> StopAsync(int port)
        {
            return Task.Run(() => Stop(port));
        }

        public int GetPortOwner(int port)
        {
            try
            {
                ProcessStartInfo startInfo = new ProcessStartInfo();
                startInfo.FileName = "netstat.exe";
                startInfo.Arguments = "-ano";
                startInfo.UseShellExecute = false;
                startInfo.CreateNoWindow = true;
                startInfo.RedirectStandardOutput = true;

                using (Process process = new Process())
                {
                    process.StartInfo = startInfo;
                    process.Start();
                    string output = process.StandardOutput.ReadToEnd();
                    process.WaitForExit(5000);

                    Regex regex = new Regex(
                        // Match on the foreign address, not on "LISTENING":
                        // netstat localizes the state column on non-English Windows.
                        @"^\s*TCP\s+\S+:" + port + @"\s+(?:0\.0\.0\.0:0|\[::\]:0)\s+.+?\s+(\d+)\s*$",
                        RegexOptions.IgnoreCase | RegexOptions.Multiline);

                    Match match = regex.Match(output ?? "");
                    if (match.Success)
                    {
                        int pid;
                        if (int.TryParse(match.Groups[1].Value, out pid)) return pid;
                    }
                }
            }
            catch { }

            return 0;
        }

        private OperationResult Start(AppSettings settings, string device, string host, int port)
        {
            OperationResult result = new OperationResult();
            bool wasRunning = GetPortOwner(port) > 0;

            string script = AppPaths.StartScript;
            if (!File.Exists(script))
            {
                result.Message = "Start script not found: " + script;
                return result;
            }

            string args = BuildStartArguments(script, settings, device, host, port);
            logger.Write("START " + args);

            ProcessResult process = RunProcess(FindPwsh(), args, 150000);
            result.Output = Combine(process.StdOut, process.StdErr);

            if (process.TimedOut)
            {
                result.Message = "Start timed out.";
                logger.Write("START timed out");
                return result;
            }

            if (process.ExitCode != 0)
            {
                result.Message = "Start failed (exit " + process.ExitCode + ").";
                logger.Write("START exit " + process.ExitCode);
                return result;
            }

            if (!wasRunning)
            {
                for (int i = 0; i < 120; i++)
                {
                    if (GetPortOwner(port) > 0) break;
                    Thread.Sleep(500);
                }
            }

            int pid = GetPortOwner(port);
            if (pid == 0)
            {
                result.Message = "DSH did not start listening on port " + port + ".";
                logger.Write("START no listener on " + port);
                return result;
            }

            result.Success = true;
            result.Pid = pid;
            result.Message = wasRunning
                ? "Link sent (DSH was already running)."
                : "DSH started on port " + port + ".";

            logger.Write("START ok pid=" + pid + " runningBefore=" + wasRunning);
            return result;
        }

        private OperationResult Stop(int port)
        {
            OperationResult result = new OperationResult();

            int pid = GetPortOwner(port);
            if (pid == 0)
            {
                result.Success = true;
                result.Message = "DSH is already stopped.";
                return result;
            }

            string script = AppPaths.StopScript;
            if (!File.Exists(script))
            {
                result.Message = "Stop script not found: " + script;
                return result;
            }

            // Always pass the folder explicitly so the script uses the same one as
            // the GUI, even when the scripts live in a "scripts" subfolder.
            string args = "-NoProfile -ExecutionPolicy Bypass -File " + Quote(script) +
                          " -Port " + port +
                          " -RuntimeDir " + Quote(AppPaths.RuntimeDir);

            logger.Write("STOP " + args);
            ProcessResult process = RunProcess(FindPwsh(), args, 45000);
            result.Output = Combine(process.StdOut, process.StdErr);

            if (process.TimedOut)
            {
                result.Message = "Stop timed out.";
                logger.Write("STOP timed out");
                return result;
            }

            for (int i = 0; i < 40; i++)
            {
                if (GetPortOwner(port) == 0) break;
                Thread.Sleep(500);
            }

            if (GetPortOwner(port) != 0)
            {
                result.Message = "DSH is still listening on port " + port + ".";
                logger.Write("STOP still listening on " + port);
                return result;
            }

            result.Success = true;
            result.Message = "DSH stopped and token rotated.";
            logger.Write("STOP ok");
            return result;
        }

        private static string BuildStartArguments(string script, AppSettings settings, string device, string host, int port)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("-NoProfile -ExecutionPolicy Bypass -File ").Append(Quote(script));
            sb.Append(" -ListenHost ").Append(Quote(host));
            sb.Append(" -Port ").Append(port);
            sb.Append(" -Profile ").Append(Quote(settings.Profile));

            if (!string.IsNullOrWhiteSpace(settings.DshExe))
            {
                sb.Append(" -DshExe ").Append(Quote(settings.DshExe));
            }
            if (!string.IsNullOrWhiteSpace(settings.DshCli))
            {
                sb.Append(" -DshCli ").Append(Quote(settings.DshCli));
            }
            if (!string.IsNullOrWhiteSpace(device))
            {
                sb.Append(" -Taildrop ").Append(Quote(device));
            }
            sb.Append(" -RuntimeDir ").Append(Quote(AppPaths.RuntimeDir));

            return sb.ToString();
        }

        private static ProcessResult RunProcess(string fileName, string arguments, int timeoutMs)
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
                        process.WaitForExit(5000);
                    }

                    result.StdOut = SafeTask(stdout);
                    result.StdErr = SafeTask(stderr);
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

        private static string SafeTask(Task<string> task)
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

        private static string Quote(string value)
        {
            return "\"" + (value ?? "").Replace("\"", "\\\"") + "\"";
        }

        private static string Combine(string stdout, string stderr)
        {
            // Script output contains the pairing URL with its token: mask it here,
            // at the source, so no caller can display or log the raw value.
            string output = ((stdout ?? "") + "\n" + (stderr ?? "")).Trim();
            return SecretMask.Apply(output);
        }

        private static string FindPwsh()
        {
            try
            {
                string path = Environment.GetEnvironmentVariable("PATH") ?? "";
                foreach (string dir in path.Split(';'))
                {
                    if (string.IsNullOrWhiteSpace(dir)) continue;
                    string candidate = Path.Combine(dir.Trim(), "pwsh.exe");
                    if (File.Exists(candidate)) return candidate;
                }
            }
            catch { }

            try
            {
                string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                if (!string.IsNullOrEmpty(pf))
                {
                    string candidate = Path.Combine(pf, "PowerShell", "7", "pwsh.exe");
                    if (File.Exists(candidate)) return candidate;
                }
            }
            catch { }

            return "powershell.exe";
        }
    }
}
