using System;
using System.IO;
using System.Text;

namespace DshRemote
{
    public class Logger
    {
        private readonly string path;
        private readonly object gate = new object();

        public string LastError = "";

        public Logger(string path)
        {
            this.path = path;
        }

        public void Write(string message)
        {
            try
            {
                lock (gate)
                {
                    string dir = Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                    if (File.Exists(path) && new FileInfo(path).Length > 1024 * 1024)
                    {
                        string old = path + ".old";
                        if (File.Exists(old)) File.Delete(old);
                        File.Move(path, old);
                    }

                    string line = "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "] " + SecretMask.Apply(message) + Environment.NewLine;
                    File.AppendAllText(path, line, new UTF8Encoding(false));
                }
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
            }
        }
    }
}
