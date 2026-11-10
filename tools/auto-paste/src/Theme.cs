using System;
using System.Drawing;

namespace AutoPaste
{
    static class Theme
    {
        public static readonly Color Back = Color.FromArgb(244, 245, 248);
        public static readonly Color Card = Color.White;
        public static readonly Color Border = Color.FromArgb(226, 229, 235);
        public static readonly Color Text = Color.FromArgb(29, 33, 41);
        public static readonly Color SubText = Color.FromArgb(102, 110, 124);
        public static readonly Color Hint = Color.FromArgb(138, 145, 158);
        public static readonly Color Faint = Color.FromArgb(176, 182, 192);
        public static readonly Color Field = Color.FromArgb(247, 248, 250);
        public static readonly Color Track = Color.FromArgb(238, 240, 244);
        public static readonly Color Hover = Color.FromArgb(232, 235, 241);
        public static readonly Color SwitchOff = Color.FromArgb(205, 210, 218);
        public static readonly Color Accent = Color.FromArgb(59, 108, 246);
        public static readonly Color AccentHover = Color.FromArgb(47, 94, 228);
        public static readonly Color AccentPressed = Color.FromArgb(37, 80, 206);
        public static readonly Color AccentSoft = Color.FromArgb(232, 239, 255);
        public static readonly Color Danger = Color.FromArgb(229, 72, 77);
        public static readonly Color DangerHover = Color.FromArgb(212, 60, 66);
        public static readonly Color DangerPressed = Color.FromArgb(190, 49, 55);
        public static readonly Color Success = Color.FromArgb(22, 163, 74);
        public static readonly Color Warning = Color.FromArgb(217, 119, 6);
        public static readonly Color HudBack = Color.FromArgb(30, 33, 40);

        public static readonly float Scale = ReadScale();

        static float ReadScale()
        {
            using (var g = Graphics.FromHwnd(IntPtr.Zero))
                return g.DpiX / 96f;
        }

        // Converts a 96-DPI length to device pixels.
        public static int S(float value)
        {
            return (int)Math.Round(value * Scale);
        }

        public static Font MakeFont(float points, FontStyle style)
        {
            return new Font("Microsoft YaHei UI", points, style, GraphicsUnit.Point);
        }

        public static Color Blend(Color from, Color to, float t)
        {
            t = Math.Max(0f, Math.Min(1f, t));
            return Color.FromArgb(
                (int)(from.R + (to.R - from.R) * t),
                (int)(from.G + (to.G - from.G) * t),
                (int)(from.B + (to.B - from.B) * t));
        }
    }
}
