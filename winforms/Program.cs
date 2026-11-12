using System;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

[assembly: AssemblyTitle("DSH Remote")]
[assembly: AssemblyDescription("Start and stop the DSH web host")]
[assembly: AssemblyProduct("DSH Remote")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

namespace DshRemote
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            bool created;
            using (Mutex mutex = new Mutex(true, "DSHRemote.SingleInstance", out created))
            {
                if (!created)
                {
                    MessageBox.Show(
                        "DSH Remote is already running.",
                        "DSH Remote",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                    return;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new MainForm());
            }
        }
    }
}
