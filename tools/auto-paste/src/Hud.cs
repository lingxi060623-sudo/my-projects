using System;
using System.Drawing;
using System.Windows.Forms;

namespace AutoPaste
{
    // Small dark banner at the top of the screen showing the countdown and progress.
    // It never takes focus and lets clicks through, so typing keeps going to the target.
    sealed class Hud : Form
    {
        const int WS_EX_TOPMOST = 0x00000008;
        const int WS_EX_TRANSPARENT = 0x00000020;
        const int WS_EX_TOOLWINDOW = 0x00000080;
        const int WS_EX_NOACTIVATE = 0x08000000;

        readonly Font font = Theme.MakeFont(10.5f, FontStyle.Regular);
        readonly Timer hideTimer = new Timer();
        string message = "";
        float progress = -1;

        public Hud()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            BackColor = Theme.HudBack;
            Opacity = 0.95;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            hideTimer.Tick += delegate
            {
                hideTimer.Stop();
                Hide();
            };
        }

        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= WS_EX_TOPMOST | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            int round = 2;
            Native.DwmSetWindowAttribute(Handle, Native.DWMWA_WINDOW_CORNER_PREFERENCE, ref round, 4);
        }

        // progress < 0 hides the bar; autoHideMs > 0 hides the banner after that long.
        public void ShowMessage(string text, float progress, int autoHideMs)
        {
            message = text;
            this.progress = progress;
            int width = Math.Max(Theme.S(260), TextRenderer.MeasureText(text, font).Width + Theme.S(56));
            if (Visible)
                width = Math.Max(width, Width);
            IntPtr fg = Native.GetForegroundWindow();
            Rectangle area = (fg != IntPtr.Zero ? Screen.FromHandle(fg) : Screen.PrimaryScreen).WorkingArea;
            var bounds = new Rectangle(area.Left + (area.Width - width) / 2, area.Top + Theme.S(28), width, Theme.S(46));
            if (Bounds != bounds)
                Bounds = bounds;

            hideTimer.Stop();
            if (autoHideMs > 0)
            {
                hideTimer.Interval = autoHideMs;
                hideTimer.Start();
            }
            if (!Visible)
                Show();
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(BackColor);
            int bar = progress >= 0 ? Theme.S(3) : 0;
            TextRenderer.DrawText(g, message, font, new Rectangle(0, 0, Width, Height - bar), Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
            if (bar > 0)
            {
                using (var track = new SolidBrush(Color.FromArgb(60, 255, 255, 255)))
                    g.FillRectangle(track, 0, Height - bar, Width, bar);
                using (var fill = new SolidBrush(Color.FromArgb(120, 160, 255)))
                    g.FillRectangle(fill, 0, Height - bar, Width * Math.Min(1f, progress), bar);
            }
        }
    }
}
