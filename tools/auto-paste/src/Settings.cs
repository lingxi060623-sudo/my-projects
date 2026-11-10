using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace AutoPaste
{
    // User preferences, kept as key=value lines in %APPDATA%\自动粘贴\settings.ini.
    sealed class Settings
    {
        public Keys Hotkey = Keys.Control | Keys.Alt | Keys.V;
        public int Countdown = 1;
        public int Interval = 10;
        public int Source;
        public int Mode;
        public int Newline;
        public bool EditorFix;
        public bool TopMost;
        public Rectangle Bounds;

        static string FilePath
        {
            get
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "自动粘贴");
                return Path.Combine(dir, "settings.ini");
            }
        }

        public static Settings Load()
        {
            var s = new Settings();
            try
            {
                if (!File.Exists(FilePath))
                    return s;
                var values = new Dictionary<string, string>();
                foreach (string line in File.ReadAllLines(FilePath, Encoding.UTF8))
                {
                    int eq = line.IndexOf('=');
                    if (eq > 0)
                        values[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
                }
                var hotkey = (Keys)Int(values, "hotkey", (int)s.Hotkey);
                if (HotkeyBox.Validate(hotkey) == null)
                    s.Hotkey = hotkey;
                s.Countdown = Clamp(Int(values, "countdown", s.Countdown), 0, 10);
                s.Interval = Clamp(Int(values, "interval", s.Interval), 0, 2000);
                s.Source = Clamp(Int(values, "source", 0), 0, 1);
                s.Mode = Clamp(Int(values, "mode", 0), 0, 1);
                s.Newline = Clamp(Int(values, "newline", 0), 0, 2);
                s.EditorFix = Int(values, "editorFix", 0) != 0;
                s.TopMost = Int(values, "topMost", 0) != 0;
                s.Bounds = new Rectangle(Int(values, "x", 0), Int(values, "y", 0), Int(values, "width", 0), Int(values, "height", 0));
            }
            catch (Exception)
            {
            }
            return s;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                string[] lines =
                {
                    "hotkey=" + (int)Hotkey,
                    "countdown=" + Countdown,
                    "interval=" + Interval,
                    "source=" + Source,
                    "mode=" + Mode,
                    "newline=" + Newline,
                    "editorFix=" + (EditorFix ? 1 : 0),
                    "topMost=" + (TopMost ? 1 : 0),
                    "x=" + Bounds.X,
                    "y=" + Bounds.Y,
                    "width=" + Bounds.Width,
                    "height=" + Bounds.Height,
                };
                File.WriteAllLines(FilePath, lines, Encoding.UTF8);
            }
            catch (Exception)
            {
            }
        }

        static int Int(Dictionary<string, string> values, string key, int fallback)
        {
            string text;
            int n;
            return values.TryGetValue(key, out text) && int.TryParse(text, out n) ? n : fallback;
        }

        static int Clamp(int value, int min, int max)
        {
            return Math.Max(min, Math.Min(max, value));
        }
    }
}
