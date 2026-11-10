using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace AutoPaste
{
    static class Shapes
    {
        public static GraphicsPath RoundRect(RectangleF r, float radius)
        {
            var path = new GraphicsPath();
            float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            if (d < 1f)
            {
                path.AddRectangle(r);
                return path;
            }
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static void Fill(Graphics g, Color color, RectangleF r, float radius)
        {
            using (var path = RoundRect(r, radius))
            using (var brush = new SolidBrush(color))
                g.FillPath(brush, path);
        }

        public static void Stroke(Graphics g, Color color, RectangleF r, float radius, float width)
        {
            using (var path = RoundRect(r, radius))
            using (var pen = new Pen(color, width))
                g.DrawPath(pen, path);
        }
    }

    // The app mark: a blue tile with three lines of text and a yellow typing caret.
    static class Logo
    {
        public static void Draw(Graphics g, RectangleF r)
        {
            SmoothingMode oldMode = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float s = r.Width;
            using (var tile = Shapes.RoundRect(r, s * 0.24f))
            using (var brush = new LinearGradientBrush(r, Color.FromArgb(104, 146, 255), Color.FromArgb(52, 84, 230), 45f))
                g.FillPath(brush, tile);

            using (var pen = new Pen(Color.White, Math.Max(s * 0.095f, 1.5f)))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                float x = r.X + s * 0.26f;
                g.DrawLine(pen, x, r.Y + s * 0.32f, r.X + s * 0.74f, r.Y + s * 0.32f);
                g.DrawLine(pen, x, r.Y + s * 0.50f, r.X + s * 0.62f, r.Y + s * 0.50f);
                g.DrawLine(pen, x, r.Y + s * 0.68f, r.X + s * 0.42f, r.Y + s * 0.68f);
                pen.Color = Color.FromArgb(255, 210, 74);
                g.DrawLine(pen, r.X + s * 0.56f, r.Y + s * 0.60f, r.X + s * 0.56f, r.Y + s * 0.76f);
            }
            g.SmoothingMode = oldMode;
        }
    }
}
