using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace DshRemote
{
    /// <summary>
    /// Shared palette + geometry helpers for the custom-drawn UI.
    /// Kept as plain static members so the whole app stays a single WinForms exe
    /// compiled with the in-box csc (C# 5, .NET Framework 4.8).
    /// </summary>
    internal static class UiTheme
    {
        public static readonly Color Surface = Color.FromArgb(241, 245, 249);
        public static readonly Color CardColor = Color.White;
        public static readonly Color Border = Color.FromArgb(226, 232, 240);
        public static readonly Color Ink = Color.FromArgb(15, 23, 42);
        public static readonly Color Muted = Color.FromArgb(100, 116, 139);
        public static readonly Color Faint = Color.FromArgb(148, 163, 184);
        public static readonly Color Accent = Color.FromArgb(37, 99, 235);

        public static readonly Color Green = Color.FromArgb(22, 163, 74);
        public static readonly Color GreenSoft = Color.FromArgb(220, 252, 231);
        public static readonly Color Red = Color.FromArgb(220, 38, 38);
        public static readonly Color RedSoft = Color.FromArgb(254, 242, 242);
        public static readonly Color RedBorder = Color.FromArgb(252, 165, 165);
        public static readonly Color Amber = Color.FromArgb(217, 119, 6);
        public static readonly Color AmberSoft = Color.FromArgb(254, 243, 199);
        public static readonly Color SlateSoft = Color.FromArgb(241, 245, 249);
        public static readonly Color SlateHover = Color.FromArgb(226, 232, 240);

        public static readonly Color HeaderTop = Color.FromArgb(15, 23, 42);
        public static readonly Color HeaderBottom = Color.FromArgb(26, 36, 54);
        public static readonly Color Console = Color.FromArgb(15, 23, 42);
        public static readonly Color ConsoleText = Color.FromArgb(203, 213, 225);

        // The layout is authored in 96-dpi units and mapped to real pixels by
        // Factor. Factor normally equals Dpi/96, but is capped so the window
        // always fits the work area (small / heavily scaled screens).
        public static float Dpi = 96f;
        public static float Factor = 1f;

        public static void Configure(float dpi, Size workArea, int designWidth, int designHeight)
        {
            Dpi = dpi <= 0f ? 96f : dpi;

            float fitWidth = (workArea.Width * 0.94f) / Math.Max(1, designWidth);
            float fitHeight = (workArea.Height * 0.94f) / Math.Max(1, designHeight);

            float factor = Math.Min(Dpi / 96f, Math.Min(fitWidth, fitHeight));
            if (factor < 0.75f) factor = 0.75f;

            Factor = factor;
        }

        /// <summary>Design units (96 dpi) to physical pixels.</summary>
        public static int S(int value)
        {
            return (int)Math.Round(value * Factor);
        }

        /// <summary>Scale a 96-dpi design value to the control's current DPI.</summary>
        public static int Px(Control control, int value)
        {
            return S(value);
        }

        /// <summary>Design point size to the point size that renders at the scaled size.</summary>
        public static float Pt(float designPoints)
        {
            float points = (float)(designPoints * Factor * 96.0 / Dpi);
            if (points < 4f) points = 4f;
            return points;
        }

        public static Font MakeFont(float designPoints, FontStyle style)
        {
            return new Font("Segoe UI", Pt(designPoints), style, GraphicsUnit.Point);
        }

        public static Font MakeMonoFont(float designPoints, FontStyle style)
        {
            return new Font("Consolas", Pt(designPoints), style, GraphicsUnit.Point);
        }

        public static Color Blend(Color from, Color to, double amount)
        {
            if (amount < 0.0) amount = 0.0;
            if (amount > 1.0) amount = 1.0;

            return Color.FromArgb(
                (int)Math.Round(from.R + (to.R - from.R) * amount),
                (int)Math.Round(from.G + (to.G - from.G) * amount),
                (int)Math.Round(from.B + (to.B - from.B) * amount));
        }

        public static GraphicsPath Rounded(Rectangle bounds, int radius)
        {
            GraphicsPath path = new GraphicsPath();

            if (bounds.Width <= 0 || bounds.Height <= 0)
            {
                path.Dispose();
                return new GraphicsPath();
            }

            int diameter = radius * 2;
            if (diameter > bounds.Width) diameter = bounds.Width;
            if (diameter > bounds.Height) diameter = bounds.Height;
            if (diameter <= 0)
            {
                path.AddRectangle(bounds);
                return path;
            }

            Rectangle arc = new Rectangle(bounds.X, bounds.Y, diameter, diameter);
            path.AddArc(arc, 180, 90);

            arc.X = bounds.Right - diameter;
            path.AddArc(arc, 270, 90);

            arc.Y = bounds.Bottom - diameter;
            path.AddArc(arc, 0, 90);

            arc.X = bounds.X;
            path.AddArc(arc, 90, 90);

            path.CloseFigure();
            return path;
        }
    }

    /// <summary>White card with a soft border and rounded corners.</summary>
    internal class Card : Panel
    {
        public int Radius = 14;

        public Card()
        {
            SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw,
                true);

            BackColor = UiTheme.Surface;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            Rectangle bounds = new Rectangle(0, 0, Width - 1, Height - 1);
            int radius = UiTheme.Px(this, Radius);

            using (GraphicsPath path = UiTheme.Rounded(bounds, radius))
            {
                using (SolidBrush brush = new SolidBrush(UiTheme.CardColor))
                {
                    g.FillPath(brush, path);
                }

                using (Pen pen = new Pen(UiTheme.Border, 1f))
                {
                    g.DrawPath(pen, path);
                }
            }

            base.OnPaint(e);
        }
    }

    /// <summary>Dark rounded container for the activity console.</summary>
    internal class LogPanel : Panel
    {
        public LogPanel()
        {
            SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw,
                true);

            BackColor = UiTheme.Console;
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);

            if (Width <= 0 || Height <= 0) return;

            using (GraphicsPath path = UiTheme.Rounded(
                new Rectangle(0, 0, Width, Height), UiTheme.Px(this, 12)))
            {
                Region old = Region;
                Region = new Region(path);
                if (old != null) old.Dispose();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            using (GraphicsPath path = UiTheme.Rounded(
                new Rectangle(0, 0, Width - 1, Height - 1), UiTheme.Px(this, 12)))
            using (Pen pen = new Pen(Color.FromArgb(30, 41, 59), 1f))
            {
                e.Graphics.DrawPath(pen, path);
            }
        }
    }

    /// <summary>Rounded input frame; hosts a borderless TextBox.</summary>
    internal class FieldPanel : Panel
    {
        private bool active;

        public FieldPanel()
        {
            SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw,
                true);

            BackColor = UiTheme.CardColor;
        }

        public bool Active
        {
            get { return active; }
            set
            {
                if (active == value) return;
                active = value;
                Invalidate();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            int radius = UiTheme.Px(this, 10);
            Rectangle bounds = new Rectangle(0, 0, Width - 1, Height - 1);

            using (GraphicsPath path = UiTheme.Rounded(bounds, radius))
            {
                using (SolidBrush brush = new SolidBrush(UiTheme.CardColor))
                {
                    g.FillPath(brush, path);
                }

                Color borderColor = active ? UiTheme.Accent : Color.FromArgb(203, 213, 225);
                float borderWidth = active ? 1.6f : 1f;

                using (Pen pen = new Pen(borderColor, borderWidth))
                {
                    g.DrawPath(pen, path);
                }
            }
        }
    }

    /// <summary>Flat rounded button with hover / pressed / disabled states.</summary>
    internal class ModernButton : Control
    {
        private bool hovered;
        private bool pressed;

        public Color Fill = UiTheme.Green;
        public Color HoverFill = Color.Empty;
        public Color PressedFill = Color.Empty;
        public Color Outline = Color.Empty;
        public Color TextColor = Color.White;
        public int Radius = 11;

        public ModernButton()
        {
            SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.Selectable,
                true);

            BackColor = UiTheme.Surface;
            Font = UiTheme.MakeFont(10F, FontStyle.Bold);
            Cursor = Cursors.Hand;
            Size = new Size(140, 46);
            TabStop = true;
        }

        public void PerformClick()
        {
            if (Enabled) OnClick(EventArgs.Empty);
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            hovered = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            hovered = false;
            pressed = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                pressed = true;
                Invalidate();
            }

            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (pressed)
            {
                pressed = false;
                Invalidate();
            }

            base.OnMouseUp(e);
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            Invalidate();
            base.OnEnabledChanged(e);
        }

        protected override void OnGotFocus(EventArgs e)
        {
            Invalidate();
            base.OnGotFocus(e);
        }

        protected override void OnLostFocus(EventArgs e)
        {
            Invalidate();
            base.OnLostFocus(e);
        }

        protected override bool IsInputKey(Keys keyData)
        {
            if (keyData == Keys.Space || keyData == Keys.Enter) return true;
            return base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter)
            {
                pressed = true;
                Invalidate();
                e.Handled = true;
            }

            base.OnKeyDown(e);
        }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter)
            {
                pressed = false;
                Invalidate();
                PerformClick();
                e.Handled = true;
            }

            base.OnKeyUp(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            Rectangle bounds = new Rectangle(0, 0, Width - 1, Height - 1);
            int radius = UiTheme.Px(this, Radius);

            Color fill = Fill;
            Color textColor = TextColor;
            bool showOutline = Outline != Color.Empty && Enabled;

            if (!Enabled)
            {
                fill = Color.FromArgb(226, 232, 240);
                textColor = Color.FromArgb(148, 163, 184);
            }
            else if (pressed)
            {
                fill = PressedFill != Color.Empty
                    ? PressedFill
                    : UiTheme.Blend(Fill, Color.Black, 0.12);
            }
            else if (hovered)
            {
                fill = HoverFill != Color.Empty
                    ? HoverFill
                    : UiTheme.Blend(Fill, Color.Black, 0.07);
            }

            using (GraphicsPath path = UiTheme.Rounded(bounds, radius))
            {
                using (SolidBrush brush = new SolidBrush(fill))
                {
                    g.FillPath(brush, path);
                }

                if (showOutline)
                {
                    using (Pen pen = new Pen(Outline, 1.2f))
                    {
                        g.DrawPath(pen, path);
                    }
                }
            }

            if (Focused && Enabled)
            {
                Rectangle inner = Rectangle.Inflate(bounds, -3, -3);

                using (GraphicsPath path = UiTheme.Rounded(inner, Math.Max(2, radius - 3)))
                using (Pen pen = new Pen(Color.FromArgb(110, textColor), 1f))
                {
                    pen.DashStyle = DashStyle.Dot;
                    g.DrawPath(pen, path);
                }
            }

            TextRenderer.DrawText(
                g,
                Text,
                Font,
                bounds,
                textColor,
                TextFormatFlags.HorizontalCenter |
                TextFormatFlags.VerticalCenter |
                TextFormatFlags.EndEllipsis |
                TextFormatFlags.NoPrefix);
        }
    }

    /// <summary>Small pill label (e.g. "TAILSCALE ONLINE").</summary>
    internal class Chip : Control
    {
        private Color fill = UiTheme.GreenSoft;
        private Color textColor = UiTheme.Green;

        public Chip()
        {
            SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw,
                true);

            BackColor = UiTheme.CardColor;
            Font = UiTheme.MakeFont(8F, FontStyle.Bold);
            Size = new Size(136, 26);
        }

        public void SetAppearance(Color fillColor, Color text)
        {
            fill = fillColor;
            textColor = text;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            Rectangle bounds = new Rectangle(0, 0, Width - 1, Height - 1);

            using (GraphicsPath path = UiTheme.Rounded(bounds, bounds.Height / 2))
            using (SolidBrush brush = new SolidBrush(fill))
            {
                g.FillPath(brush, path);
            }

            TextRenderer.DrawText(
                g,
                Text,
                Font,
                bounds,
                textColor,
                TextFormatFlags.HorizontalCenter |
                TextFormatFlags.VerticalCenter |
                TextFormatFlags.NoPrefix);
        }
    }

    /// <summary>Status dot with a soft halo.</summary>
    internal class StatusDot : Control
    {
        private Color dotColor = UiTheme.Faint;

        public StatusDot()
        {
            SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw,
                true);

            BackColor = UiTheme.CardColor;
            Size = new Size(18, 18);
        }

        public Color DotColor
        {
            get { return dotColor; }
            set
            {
                dotColor = value;
                Invalidate();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            int diameter = Math.Min(Width, Height) - 1;
            if (diameter <= 0) return;

            using (SolidBrush halo = new SolidBrush(Color.FromArgb(45, dotColor)))
            {
                g.FillEllipse(halo, 0, 0, diameter, diameter);
            }

            int core = Math.Max(6, (int)Math.Round(diameter * 0.55));
            int offset = (diameter - core) / 2;

            using (SolidBrush brush = new SolidBrush(dotColor))
            {
                g.FillEllipse(brush, offset, offset, core, core);
            }
        }
    }

    /// <summary>Window chrome button (minimize / close) drawn on the header.</summary>
    internal class WindowButton : Control
    {
        public enum Kind
        {
            Close,
            Minimize
        }

        private readonly Kind kind;
        private bool hovered;

        public WindowButton(Kind kind)
        {
            this.kind = kind;

            SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.SupportsTransparentBackColor,
                true);

            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
            Size = new Size(34, 30);
            TabStop = false;
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            hovered = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            hovered = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            Rectangle bounds = new Rectangle(0, 0, Width - 1, Height - 1);
            Color icon = Color.FromArgb(203, 213, 225);

            if (hovered)
            {
                Color fill = kind == Kind.Close
                    ? Color.FromArgb(220, 38, 38)
                    : Color.FromArgb(51, 65, 85);

                using (GraphicsPath path = UiTheme.Rounded(bounds, UiTheme.Px(this, 7)))
                using (SolidBrush brush = new SolidBrush(fill))
                {
                    g.FillPath(brush, path);
                }

                if (kind == Kind.Close) icon = Color.White;
            }

            int cx = Width / 2;
            int cy = Height / 2;
            int arm = UiTheme.Px(this, 5);

            using (Pen pen = new Pen(icon, 1.4f))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;

                if (kind == Kind.Close)
                {
                    g.DrawLine(pen, cx - arm, cy - arm, cx + arm, cy + arm);
                    g.DrawLine(pen, cx + arm, cy - arm, cx - arm, cy + arm);
                }
                else
                {
                    g.DrawLine(pen, cx - arm, cy, cx + arm, cy);
                }
            }
        }
    }

    /// <summary>Top bar: gradient, logo, title and the window buttons live here.</summary>
    internal class HeaderPanel : Panel
    {
        private readonly Font titleFont = UiTheme.MakeFont(15F, FontStyle.Bold);
        private readonly Font subtitleFont = UiTheme.MakeFont(9F, FontStyle.Regular);
        private readonly Font logoFont = UiTheme.MakeFont(16F, FontStyle.Bold);

        public HeaderPanel()
        {
            SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw,
                true);

            BackColor = UiTheme.HeaderTop;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;

            Rectangle all = ClientRectangle;
            if (all.Width <= 0 || all.Height <= 0) return;

            using (LinearGradientBrush brush = new LinearGradientBrush(
                all, UiTheme.HeaderTop, UiTheme.HeaderBottom, LinearGradientMode.Horizontal))
            {
                g.FillRectangle(brush, all);
            }

            int barHeight = UiTheme.Px(this, 3);
            Rectangle bar = new Rectangle(0, all.Height - barHeight, all.Width, barHeight);

            using (LinearGradientBrush brush = new LinearGradientBrush(
                bar, Color.FromArgb(37, 99, 235), Color.FromArgb(13, 148, 136), LinearGradientMode.Horizontal))
            {
                g.FillRectangle(brush, bar);
            }

            int logoSize = UiTheme.Px(this, 40);
            Rectangle logo = new Rectangle(
                UiTheme.Px(this, 24),
                (all.Height - logoSize - barHeight) / 2,
                logoSize,
                logoSize);

            using (GraphicsPath path = UiTheme.Rounded(logo, UiTheme.Px(this, 11)))
            using (LinearGradientBrush brush = new LinearGradientBrush(
                logo, Color.FromArgb(59, 130, 246), Color.FromArgb(14, 165, 233), LinearGradientMode.ForwardDiagonal))
            {
                g.FillPath(brush, path);
            }

            TextRenderer.DrawText(
                g,
                "D",
                logoFont,
                logo,
                Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

            int textLeft = logo.Right + UiTheme.Px(this, 16);
            int textWidth = Math.Max(10, all.Width - textLeft - UiTheme.Px(this, 130));

            Rectangle title = new Rectangle(
                textLeft, UiTheme.Px(this, 19), textWidth, UiTheme.Px(this, 28));

            TextRenderer.DrawText(
                g,
                "DSH Remote",
                titleFont,
                title,
                Color.White,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

            Rectangle subtitle = new Rectangle(
                textLeft + UiTheme.Px(this, 2),
                UiTheme.Px(this, 47),
                textWidth,
                UiTheme.Px(this, 20));

            TextRenderer.DrawText(
                g,
                "Tailscale host controller",
                subtitleFont,
                subtitle,
                Color.FromArgb(148, 163, 184),
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

            base.OnPaint(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                titleFont.Dispose();
                subtitleFont.Dispose();
                logoFont.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
