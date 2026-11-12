using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace DshRemote
{
    public class MainForm : Form
    {
        private const string AppVersion = "1.0.0";

        // Layout is authored in 96-dpi units, then scaled by UiTheme.Factor.
        private const int DesignWidth = 680;
        private const int DesignHeight = 700;

        private const int WM_NCLBUTTONDOWN = 0xA1;
        private const int HTCAPTION = 0x2;
        private const int CS_DROPSHADOW = 0x00020000;
        private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        private const int DWMWCP_ROUND = 2;

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        [DllImport("user32.dll")]
        private static extern uint GetDpiForSystem();

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, int flags);

        [DllImport("shcore.dll")]
        private static extern int GetDpiForMonitor(IntPtr monitor, int dpiType, out uint dpiX, out uint dpiY);

        private readonly ISettingsStore store;
        private readonly Logger logger;
        private readonly TailscaleService tailscale;
        private readonly HostController host;

        private AppSettings settings;
        private TailscaleSnapshot snapshot;
        private string selfIp = "";
        private bool busy;

        private HeaderPanel header;
        private WindowButton btnMin;
        private WindowButton btnClose;

        private StatusDot dotStatus;
        private Label lblState;
        private Label lblDetail;
        private Chip chipNet;

        private Label lblDeviceHint;
        private FieldPanel fieldDevice;
        private TextBox txtDevice;
        private ModernButton btnRefresh;

        private ModernButton btnStart;
        private ModernButton btnStop;

        private ModernButton btnClear;
        private LogPanel logPanel;
        private TextBox txtLog;

        private Label lblNote;
        private Label lblVersion;

        private ToolTip tip;

        public MainForm()
        {
            store = new IniSettingsStore(AppPaths.SettingsFile);
            AppPaths.EnsureRuntimeDir();
            logger = new Logger(AppPaths.LogFile);
            tailscale = new TailscaleService();
            host = new HostController(logger);
            settings = new AppSettings();

            ConfigureScale();
            BuildUi();

            Load += MainForm_Load;
            FormClosing += MainForm_FormClosing;
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ClassStyle = cp.ClassStyle | CS_DROPSHADOW;
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);

            // Windows 11 rounded corners for the borderless window (no-op elsewhere).
            try
            {
                int preference = DWMWCP_ROUND;
                DwmSetWindowAttribute(Handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref preference, sizeof(int));
            }
            catch
            {
            }
        }

        private static void ConfigureScale()
        {
            float dpi = 0f;

            // Effective DPI of the primary monitor. GetDpiForSystem can report the
            // logon DPI (often 96) even when the display runs at 150/175 %, so it
            // is only a fallback.
            try
            {
                IntPtr monitor = MonitorFromWindow(IntPtr.Zero, 1); // MONITOR_DEFAULTTOPRIMARY
                if (monitor != IntPtr.Zero)
                {
                    uint dpiX;
                    uint dpiY;
                    if (GetDpiForMonitor(monitor, 0 /* MDT_EFFECTIVE_DPI */, out dpiX, out dpiY) == 0)
                    {
                        dpi = dpiX;
                    }
                }
            }
            catch
            {
            }

            if (dpi <= 0f)
            {
                try
                {
                    dpi = GetDpiForSystem();
                }
                catch
                {
                }
            }

            if (dpi <= 0f)
            {
                try
                {
                    using (Graphics g = Graphics.FromHwnd(IntPtr.Zero))
                    {
                        dpi = g.DpiX;
                    }
                }
                catch
                {
                }
            }

            Size work = new Size(1024, 768);
            try
            {
                Screen screen = Screen.PrimaryScreen;
                if (screen != null) work = screen.WorkingArea.Size;
            }
            catch
            {
            }

            UiTheme.Configure(dpi, work, DesignWidth, DesignHeight);
        }

        private static Rectangle B(int x, int y, int width, int height)
        {
            return new Rectangle(
                UiTheme.S(x),
                UiTheme.S(y),
                UiTheme.S(width),
                UiTheme.S(height));
        }

        private static Font F(float size, FontStyle style)
        {
            return UiTheme.MakeFont(size, style);
        }

        private void BuildUi()
        {
            SuspendLayout();

            AutoScaleMode = AutoScaleMode.None;

            Text = "DSH Remote";
            ClientSize = new Size(UiTheme.S(DesignWidth), UiTheme.S(DesignHeight));
            FormBorderStyle = FormBorderStyle.None;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowIcon = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = UiTheme.Surface;
            Font = F(9F, FontStyle.Regular);
            DoubleBuffered = true;

            tip = new ToolTip();
            tip.AutoPopDelay = 8000;
            tip.ReshowDelay = 200;

            BuildHeader();
            BuildStatusCard();
            BuildDeviceCard();
            BuildActions();
            BuildActivityCard();
            BuildFooter();

            ResumeLayout(false);
            PerformLayout();

            ApplyWindowShape();
        }

        /// <summary>
        /// DWM only rounds framed windows, so the borderless form gets an explicit
        /// rounded region. Radius is design-scaled like everything else.
        /// </summary>
        private void ApplyWindowShape()
        {
            if (Width <= 0 || Height <= 0) return;

            using (GraphicsPath path = UiTheme.Rounded(
                new Rectangle(0, 0, Width, Height), UiTheme.S(12)))
            {
                Region old = Region;
                Region = new Region(path);
                if (old != null) old.Dispose();
            }
        }

        private void BuildHeader()
        {
            header = new HeaderPanel();
            header.Bounds = B(0, 0, DesignWidth, 92);
            header.MouseDown += DragWindow;
            Controls.Add(header);

            btnClose = new WindowButton(WindowButton.Kind.Close);
            btnClose.Bounds = B(DesignWidth - 16 - 34, 14, 34, 30);
            btnClose.Click += delegate { Close(); };
            header.Controls.Add(btnClose);

            btnMin = new WindowButton(WindowButton.Kind.Minimize);
            btnMin.Bounds = B(DesignWidth - 16 - 34 - 6 - 34, 14, 34, 30);
            btnMin.Click += delegate { WindowState = FormWindowState.Minimized; };
            header.Controls.Add(btnMin);
        }

        private void BuildStatusCard()
        {
            Card card = new Card();
            card.Bounds = B(24, 104, 632, 104);
            Controls.Add(card);

            dotStatus = new StatusDot();
            dotStatus.Bounds = B(22, 46, 18, 18);
            dotStatus.BackColor = UiTheme.CardColor;
            card.Controls.Add(dotStatus);

            lblState = MakeLabel("Stopped", 52, 24, 400, 32, 15F, FontStyle.Bold,
                UiTheme.Ink, ContentAlignment.MiddleLeft);
            card.Controls.Add(lblState);

            lblDetail = MakeLabel("", 54, 64, 500, 20, 9F, FontStyle.Regular,
                UiTheme.Muted, ContentAlignment.MiddleLeft);
            card.Controls.Add(lblDetail);

            chipNet = new Chip();
            chipNet.Bounds = B(632 - 20 - 136, 28, 136, 26);
            chipNet.BackColor = UiTheme.CardColor;
            chipNet.Text = "TAILSCALE OFFLINE";
            card.Controls.Add(chipNet);
        }

        private void BuildDeviceCard()
        {
            Card card = new Card();
            card.Bounds = B(24, 220, 632, 128);
            Controls.Add(card);

            Label caption = MakeLabel("TAILDROP DEVICE", 20, 18, 220, 18, 8F, FontStyle.Bold,
                UiTheme.Faint, ContentAlignment.MiddleLeft);
            card.Controls.Add(caption);

            lblDeviceHint = MakeLabel("", 240, 15, 372, 22, 8.5F, FontStyle.Regular,
                UiTheme.Muted, ContentAlignment.MiddleRight);
            card.Controls.Add(lblDeviceHint);

            fieldDevice = new FieldPanel();
            fieldDevice.Bounds = B(20, 44, 464, 40);
            fieldDevice.BackColor = UiTheme.CardColor;
            fieldDevice.Resize += delegate { LayoutDeviceBox(); };
            card.Controls.Add(fieldDevice);

            txtDevice = new TextBox();
            txtDevice.BorderStyle = BorderStyle.None;
            txtDevice.Font = F(10.5F, FontStyle.Regular);
            txtDevice.BackColor = UiTheme.CardColor;
            txtDevice.ForeColor = UiTheme.Ink;
            txtDevice.Bounds = B(14, 9, 436, 24);
            txtDevice.TextChanged += delegate { UpdateDeviceHint(); };
            txtDevice.GotFocus += delegate { fieldDevice.Active = true; };
            txtDevice.LostFocus += delegate { fieldDevice.Active = false; };
            txtDevice.KeyDown += txtDevice_KeyDown;
            fieldDevice.Controls.Add(txtDevice);

            btnRefresh = new ModernButton();
            btnRefresh.Bounds = B(496, 44, 116, 40);
            btnRefresh.Text = "Refresh";
            btnRefresh.Fill = UiTheme.SlateSoft;
            btnRefresh.TextColor = UiTheme.Ink;
            btnRefresh.HoverFill = UiTheme.SlateHover;
            btnRefresh.PressedFill = Color.FromArgb(203, 213, 225);
            btnRefresh.Radius = 10;
            btnRefresh.Font = F(9.5F, FontStyle.Bold);
            btnRefresh.BackColor = UiTheme.CardColor;
            btnRefresh.Click += btnRefresh_Click;
            card.Controls.Add(btnRefresh);

            Label note = MakeLabel(
                "Detected from Tailscale. Type a name to override.",
                22, 94, 570, 20, 8.5F, FontStyle.Regular, UiTheme.Faint,
                ContentAlignment.MiddleLeft);
            card.Controls.Add(note);

            tip.SetToolTip(btnRefresh, "Reload Tailscale devices and host status");
            tip.SetToolTip(txtDevice, "Taildrop device name used when sending the link");
        }

        private void BuildActions()
        {
            btnStart = new ModernButton();
            btnStart.Bounds = B(24, 364, 306, 50);
            btnStart.Text = "Start DSH";
            btnStart.Fill = UiTheme.Green;
            btnStart.TextColor = Color.White;
            btnStart.Font = F(10.5F, FontStyle.Bold);
            btnStart.Click += btnStart_Click;
            Controls.Add(btnStart);

            btnStop = new ModernButton();
            btnStop.Bounds = B(350, 364, 306, 50);
            btnStop.Text = "Stop DSH";
            btnStop.Fill = UiTheme.CardColor;
            btnStop.Outline = UiTheme.RedBorder;
            btnStop.TextColor = UiTheme.Red;
            btnStop.HoverFill = UiTheme.RedSoft;
            btnStop.PressedFill = Color.FromArgb(254, 226, 226);
            btnStop.Font = F(10.5F, FontStyle.Bold);
            btnStop.Click += btnStop_Click;
            Controls.Add(btnStop);

            tip.SetToolTip(btnStart, "Start the DSH web host and send the pairing link");
            tip.SetToolTip(btnStop, "Stop the host, rotate the token and clear QR files");
        }

        private void BuildActivityCard()
        {
            Card card = new Card();
            card.Bounds = B(24, 430, 632, 222);
            Controls.Add(card);

            Label caption = MakeLabel("ACTIVITY", 20, 16, 120, 18, 8F, FontStyle.Bold,
                UiTheme.Faint, ContentAlignment.MiddleLeft);
            card.Controls.Add(caption);

            btnClear = new ModernButton();
            btnClear.Bounds = B(632 - 20 - 76, 10, 76, 28);
            btnClear.Text = "Clear";
            btnClear.Fill = UiTheme.SlateSoft;
            btnClear.TextColor = UiTheme.Muted;
            btnClear.HoverFill = UiTheme.SlateHover;
            btnClear.PressedFill = Color.FromArgb(203, 213, 225);
            btnClear.Radius = 8;
            btnClear.Font = F(8.5F, FontStyle.Bold);
            btnClear.BackColor = UiTheme.CardColor;
            btnClear.Click += delegate { txtLog.Clear(); };
            card.Controls.Add(btnClear);

            logPanel = new LogPanel();
            logPanel.Bounds = B(14, 44, 604, 164);
            card.Controls.Add(logPanel);

            txtLog = new TextBox();
            txtLog.BorderStyle = BorderStyle.None;
            txtLog.Multiline = true;
            txtLog.ReadOnly = true;
            txtLog.ScrollBars = ScrollBars.None;
            txtLog.BackColor = UiTheme.Console;
            txtLog.ForeColor = UiTheme.ConsoleText;
            txtLog.Font = UiTheme.MakeMonoFont(9F, FontStyle.Regular);
            txtLog.Bounds = B(14, 10, 576, 144);
            txtLog.TabStop = false;
            logPanel.Controls.Add(txtLog);

            tip.SetToolTip(btnClear, "Clear the activity log");
        }

        private void BuildFooter()
        {
            lblNote = MakeLabel(
                "Closing this window keeps DSH running. Use Stop DSH to stop the host.",
                26, 658, 540, 20, 8.5F, FontStyle.Regular, UiTheme.Muted,
                ContentAlignment.MiddleLeft);
            lblNote.BackColor = UiTheme.Surface;
            Controls.Add(lblNote);

            lblVersion = MakeLabel("v" + AppVersion, 556, 658, 100, 20, 8.5F,
                FontStyle.Regular, UiTheme.Faint, ContentAlignment.MiddleRight);
            lblVersion.BackColor = UiTheme.Surface;
            Controls.Add(lblVersion);
        }

        private static Label MakeLabel(
            string text,
            int x,
            int y,
            int width,
            int height,
            float size,
            FontStyle style,
            Color foreColor,
            ContentAlignment align)
        {
            Label label = new Label();
            label.Text = text;
            label.Bounds = B(x, y, width, height);
            label.Font = F(size, style);
            label.ForeColor = foreColor;
            label.BackColor = UiTheme.CardColor;
            label.TextAlign = align;
            label.AutoEllipsis = true;
            return label;
        }

        private void LayoutDeviceBox()
        {
            if (txtDevice == null || fieldDevice == null) return;

            int pad = UiTheme.Px(this, 14);
            txtDevice.Left = pad;
            txtDevice.Width = Math.Max(10, fieldDevice.ClientSize.Width - pad * 2);
            txtDevice.Top = Math.Max(0, (fieldDevice.ClientSize.Height - txtDevice.Height) / 2);
        }

        private void DragWindow(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;

            ReleaseCapture();
            SendMessage(Handle, WM_NCLBUTTONDOWN, (IntPtr)HTCAPTION, IntPtr.Zero);
        }

        private void txtDevice_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Enter) return;

            e.SuppressKeyPress = true;
            if (!busy) btnStart.PerformClick();
        }

        private async void MainForm_Load(object sender, EventArgs e)
        {
            try
            {
                LayoutDeviceBox();

                settings = store.Load();
                txtDevice.Text = settings.Device ?? "";

                bool runtimeWritable = true;
                try
                {
                    string probe = Path.Combine(AppPaths.RuntimeDir, ".write-test");
                    File.WriteAllText(probe, "ok");
                    File.Delete(probe);
                }
                catch
                {
                    runtimeWritable = false;
                }

                if (AppPaths.RuntimeSource != "default")
                {
                    AppendUiLine("Runtime folder: " + AppPaths.RuntimeDir + " (" + AppPaths.RuntimeSource + ")");
                }

                if (!runtimeWritable)
                {
                    AppendUiLine("Cannot write to " + AppPaths.RuntimeDir +
                                 ". Run DSH Remote from Explorer (outside the DSH sandbox).");
                }

                DshPaths paths = DshLocator.Find(settings.DshExe, settings.DshCli);
                if (!string.IsNullOrWhiteSpace(paths.Exe) || !string.IsNullOrWhiteSpace(paths.Cli))
                {
                    settings.DshExe = paths.Exe ?? "";
                    settings.DshCli = paths.Cli ?? "";
                }

                if (string.IsNullOrWhiteSpace(settings.DshCli))
                {
                    AppendLog("DSH not found automatically. Start may fail; set DshExe/DshCli in settings.ini.");
                }

                UpdateTailscaleUi();

                await RefreshTailscaleAsync(true);
                await RefreshHostStatusAsync();

                if (string.IsNullOrWhiteSpace(txtDevice.Text))
                {
                    AppendLog("No suitable mobile device found. Enter a Taildrop device name before sending a link.");
                }
            }
            catch (Exception ex)
            {
                AppendLog("Load failed: " + ex.Message);
                SetStatus("Stopped", UiTheme.Faint, null);
            }
        }

        private async Task RefreshTailscaleAsync(bool initial)
        {
            TailscaleSnapshot current = await Task.Run(() => tailscale.GetSnapshot());
            snapshot = current;
            selfIp = current.SelfIp ?? "";

            if (!string.IsNullOrWhiteSpace(current.Error))
            {
                AppendLog("Tailscale: " + current.Error);
            }

            string lastDevice = initial ? (settings.Device ?? "") : (txtDevice.Text ?? "").Trim();
            DeviceSelection selection = TailscaleService.PickDefaultDevice(current, lastDevice);

            txtDevice.Text = selection.Device ?? "";
            if (!string.IsNullOrWhiteSpace(selection.Message))
            {
                AppendLog(selection.Message);
            }

            if (string.IsNullOrWhiteSpace(selfIp))
            {
                AppendLog("No Tailscale IPv4 address found. Start will not work.");
            }

            UpdateTailscaleUi();
            UpdateDeviceHint();
        }

        private void UpdateTailscaleUi()
        {
            bool online = !string.IsNullOrWhiteSpace(selfIp);

            if (online)
            {
                chipNet.SetAppearance(UiTheme.GreenSoft, UiTheme.Green);
                chipNet.Text = "TAILSCALE ONLINE";
            }
            else
            {
                chipNet.SetAppearance(UiTheme.AmberSoft, UiTheme.Amber);
                chipNet.Text = "TAILSCALE OFFLINE";
            }
        }

        private void UpdateDeviceHint()
        {
            if (lblDeviceHint == null) return;

            string name = (txtDevice.Text ?? "").Trim();
            if (name.Length == 0)
            {
                lblDeviceHint.ForeColor = UiTheme.Muted;
                lblDeviceHint.Text = "No device selected";
                return;
            }

            DeviceInfo match = null;
            if (snapshot != null)
            {
                foreach (DeviceInfo device in snapshot.Devices)
                {
                    if (device.Matches(name))
                    {
                        match = device;
                        break;
                    }
                }
            }

            if (match == null)
            {
                lblDeviceHint.ForeColor = UiTheme.Faint;
                lblDeviceHint.Text = "Not found in Tailscale peers";
                return;
            }

            string os = string.IsNullOrWhiteSpace(match.OS) ? "" : "  \u00B7  " + match.OS;

            if (match.Online)
            {
                lblDeviceHint.ForeColor = UiTheme.Green;
                lblDeviceHint.Text = "\u25CF Online" + os;
            }
            else
            {
                lblDeviceHint.ForeColor = UiTheme.Amber;
                lblDeviceHint.Text = "\u25CF Offline" + os;
            }
        }

        private async Task RefreshHostStatusAsync()
        {
            int port = settings.Port;
            int pid = await Task.Run(() => host.GetPortOwner(port));
            UpdateHostStatus(pid);
        }

        private void UpdateHostStatus(int pid)
        {
            if (pid > 0)
            {
                string where = string.IsNullOrWhiteSpace(selfIp)
                    ? "port " + settings.Port
                    : "http://" + selfIp + ":" + settings.Port;
                SetStatus("Running", UiTheme.Green, where + "   \u00B7   PID " + pid);
            }
            else
            {
                SetStatus("Stopped", UiTheme.Faint, "Not listening on port " + settings.Port);
            }
        }

        private async void btnStart_Click(object sender, EventArgs e)
        {
            if (busy) return;

            if (string.IsNullOrWhiteSpace(selfIp))
            {
                await RefreshTailscaleAsync(false);
            }

            string device = (txtDevice.Text ?? "").Trim();

            if (string.IsNullOrWhiteSpace(selfIp))
            {
                MessageBox.Show(
                    this,
                    "Tailscale is not running or has no IPv4 address.\r\nStart Tailscale, then try again.",
                    "DSH Remote",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            SetBusy(true, "Starting...");
            try
            {
                OperationResult result = await host.StartAsync(settings, device, selfIp, settings.Port);
                AppendLog(result.Message);
                AppendOutput(result.Output);

                if (result.Success)
                {
                    settings.Device = device;
                    settings.LastAction = "start";
                    settings.LastUpdated = DateTime.Now.ToString("o");
                    try { store.Save(settings); } catch (Exception ex) { AppendLog("Cannot save settings: " + ex.Message); }

                    SetStatus("Running", UiTheme.Green,
                        "http://" + selfIp + ":" + settings.Port + "   \u00B7   PID " + result.Pid);
                }
                else
                {
                    SetStatus("Stopped", UiTheme.Faint, "Not listening on port " + settings.Port);
                    MessageBox.Show(
                        this,
                        result.Message + "\r\n\r\n" + Trim(result.Output, 1200),
                        "DSH Remote",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                }
            }
            catch (Exception ex)
            {
                AppendLog("Start failed: " + ex.Message);
                SetStatus("Stopped", UiTheme.Faint, "Not listening on port " + settings.Port);
                MessageBox.Show(this, ex.Message, "DSH Remote", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                SetBusy(false, null);
            }
        }

        private async void btnStop_Click(object sender, EventArgs e)
        {
            if (busy) return;

            int pid = host.GetPortOwner(settings.Port);
            if (pid == 0)
            {
                AppendLog("DSH is already stopped.");
                UpdateHostStatus(0);
                return;
            }

            SetBusy(true, "Stopping...");
            try
            {
                OperationResult result = await host.StopAsync(settings.Port);
                AppendLog(result.Message);
                AppendOutput(result.Output);

                if (result.Success)
                {
                    UpdateHostStatus(0);
                }
                else
                {
                    MessageBox.Show(
                        this,
                        result.Message + "\r\n\r\n" + Trim(result.Output, 1200),
                        "DSH Remote",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                    await RefreshHostStatusAsync();
                }
            }
            catch (Exception ex)
            {
                AppendLog("Stop failed: " + ex.Message);
                MessageBox.Show(this, ex.Message, "DSH Remote", MessageBoxButtons.OK, MessageBoxIcon.Error);
                UpdateHostStatus(host.GetPortOwner(settings.Port));
            }
            finally
            {
                SetBusy(false, null);
            }
        }

        private async void btnRefresh_Click(object sender, EventArgs e)
        {
            if (busy) return;

            SetBusy(true, "Refreshing...");
            try
            {
                await RefreshTailscaleAsync(false);
                await RefreshHostStatusAsync();
            }
            catch (Exception ex)
            {
                AppendLog("Refresh failed: " + ex.Message);
            }
            finally
            {
                SetBusy(false, null);
            }
        }

        private void SetBusy(bool value, string status)
        {
            busy = value;
            btnStart.Enabled = !value;
            btnStop.Enabled = !value;
            btnRefresh.Enabled = !value;
            txtDevice.Enabled = !value;
            UseWaitCursor = value;

            if (value)
            {
                SetStatus(status ?? "Working...", UiTheme.Amber, "port " + settings.Port);
            }
        }

        private void SetStatus(string state, Color color, string detail)
        {
            lblState.Text = state;
            lblDetail.Text = detail ?? "";
            dotStatus.DotColor = color;
        }

        private void AppendLog(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return;

            AppendUiLine(message);
            logger.Write(message);
        }

        private void AppendUiLine(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return;

            // Defense in depth: never render a pairing token on screen.
            message = SecretMask.Apply(message);

            if (txtLog.TextLength > 16000) txtLog.Clear();
            txtLog.AppendText(DateTime.Now.ToString("HH:mm:ss") + "  " + message + Environment.NewLine);
            txtLog.SelectionStart = txtLog.TextLength;
            txtLog.ScrollToCaret();
        }

        private void AppendOutput(string output)
        {
            if (string.IsNullOrWhiteSpace(output)) return;

            string text = output.Trim();
            if (text.Length > 2000) text = "..." + text.Substring(text.Length - 2000);
            AppendUiLine(text);
        }

        private static string Trim(string text, int max)
        {
            if (string.IsNullOrEmpty(text)) return "";
            if (text.Length <= max) return text;
            return "..." + text.Substring(text.Length - max);
        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (busy)
            {
                e.Cancel = true;
                MessageBox.Show(
                    this,
                    "An operation is still running. Please wait for it to finish.",
                    "DSH Remote",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
        }
    }
}
