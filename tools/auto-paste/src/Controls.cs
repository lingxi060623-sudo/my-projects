using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace AutoPaste
{
    // Base for owner-drawn controls: double-buffered, cleared to BackColor, anti-aliased.
    abstract class PaintedControl : Control
    {
        protected const TextFormatFlags CenterFlags =
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;

        protected PaintedControl()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.StandardDoubleClick, false);
            BackColor = Theme.Card;
        }

        protected Graphics BeginPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            return e.Graphics;
        }

        protected RectangleF Outline
        {
            get { return new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f); }
        }

        protected static void DrawText(Graphics g, string text, Font font, Rectangle bounds, Color color)
        {
            TextRenderer.DrawText(g, text, font, bounds, color, CenterFlags);
        }

        protected override void OnGotFocus(EventArgs e)
        {
            base.OnGotFocus(e);
            Invalidate();
        }

        protected override void OnLostFocus(EventArgs e)
        {
            base.OnLostFocus(e);
            Invalidate();
        }
    }

    // White rounded panel that groups related controls.
    sealed class Card : Panel
    {
        public Card()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Back;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
            Shapes.Fill(g, Theme.Card, r, Theme.S(12));
            Shapes.Stroke(g, Theme.Border, r, Theme.S(12), 1f);
        }
    }

    // RichTextBox that only accepts plain text and has a Chinese context menu.
    sealed class PlainTextBox : RichTextBox
    {
        readonly ToolStripMenuItem cutItem, copyItem, pasteItem;

        public PlainTextBox()
        {
            BorderStyle = BorderStyle.None;
            DetectUrls = false;
            ScrollBars = RichTextBoxScrollBars.Vertical;
            ShortcutsEnabled = false;
            MaxLength = int.MaxValue;
            LanguageOption = RichTextBoxLanguageOptions.AutoFont;

            cutItem = new ToolStripMenuItem("剪切", null, delegate { if (!ReadOnly) Cut(); });
            copyItem = new ToolStripMenuItem("复制", null, delegate { Copy(); });
            pasteItem = new ToolStripMenuItem("粘贴", null, delegate { PastePlain(); });
            var menu = new ContextMenuStrip();
            menu.Items.AddRange(new ToolStripItem[]
            {
                cutItem, copyItem, pasteItem, new ToolStripSeparator(),
                new ToolStripMenuItem("全选", null, delegate { SelectAll(); }),
            });
            menu.Opening += delegate
            {
                cutItem.Enabled = !ReadOnly && SelectionLength > 0;
                copyItem.Enabled = SelectionLength > 0;
                pasteItem.Enabled = !ReadOnly;
            };
            ContextMenuStrip = menu;
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            switch (keyData)
            {
                case Keys.Control | Keys.A:
                    SelectAll();
                    return true;
                case Keys.Control | Keys.C:
                case Keys.Control | Keys.Insert:
                    Copy();
                    return true;
                case Keys.Control | Keys.X:
                case Keys.Shift | Keys.Delete:
                    if (!ReadOnly)
                        Cut();
                    return true;
                case Keys.Control | Keys.V:
                case Keys.Shift | Keys.Insert:
                    PastePlain();
                    return true;
                case Keys.Control | Keys.Z:
                    if (!ReadOnly)
                        Undo();
                    return true;
                case Keys.Control | Keys.Y:
                    if (!ReadOnly)
                        Redo();
                    return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        void PastePlain()
        {
            if (ReadOnly)
                return;
            string text = MainForm.ReadClipboard();
            if (!string.IsNullOrEmpty(text))
                SelectedText = text;
        }
    }

    // Rounded, light-grey field hosting a PlainTextBox.
    sealed class FieldBox : Panel
    {
        public readonly PlainTextBox Box = new PlainTextBox();

        public FieldBox()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Card;
            Box.BackColor = Theme.Field;
            Box.ForeColor = Theme.Text;
            Box.GotFocus += delegate { Invalidate(); };
            Box.LostFocus += delegate { Invalidate(); };
            Controls.Add(Box);
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            int x = Theme.S(12), y = Theme.S(10);
            Box.Bounds = new Rectangle(x, y, Math.Max(1, Width - x - Theme.S(4)), Math.Max(1, Height - y - Theme.S(6)));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
            Shapes.Fill(g, Theme.Field, r, Theme.S(10));
            Shapes.Stroke(g, Box.Focused ? Theme.Accent : Theme.Border, r, Theme.S(10), 1f);
        }
    }

    // iOS-style segmented picker.
    sealed class Segmented : PaintedControl
    {
        readonly string[] items;
        int selected;
        int hover = -1;
        Font boldFont, boldSource;

        public event EventHandler SelectedIndexChanged;

        public Segmented(params string[] items)
        {
            this.items = items;
            SetStyle(ControlStyles.Selectable, true);
            TabStop = true;
            Cursor = Cursors.Hand;
        }

        public int SelectedIndex
        {
            get { return selected; }
            set
            {
                selected = Math.Max(0, Math.Min(items.Length - 1, value));
                Invalidate();
            }
        }

        Font Bold
        {
            get
            {
                if (boldSource != Font)
                {
                    if (boldFont != null)
                        boldFont.Dispose();
                    boldFont = new Font(Font, FontStyle.Bold);
                    boldSource = Font;
                }
                return boldFont;
            }
        }

        // Segment widths follow their labels so "Shift+回车" gets more room than "回车".
        RectangleF[] Segments()
        {
            float pad = Theme.S(3);
            var widths = new float[items.Length];
            float sum = 0;
            for (int i = 0; i < items.Length; i++)
            {
                widths[i] = TextRenderer.MeasureText(items[i], Bold, Size.Empty, TextFormatFlags.NoPadding).Width;
                sum += widths[i];
            }
            float extra = (Width - pad * 2 - sum) / items.Length;
            var rects = new RectangleF[items.Length];
            float x = pad;
            for (int i = 0; i < items.Length; i++)
            {
                float w = widths[i] + extra;
                rects[i] = new RectangleF(x, pad, w, Height - pad * 2);
                x += w;
            }
            return rects;
        }

        int HitTest(Point p)
        {
            RectangleF[] segments = Segments();
            for (int i = 0; i < segments.Length; i++)
                if (p.X >= segments[i].Left && p.X < segments[i].Right)
                    return i;
            return -1;
        }

        void Choose(int index)
        {
            if (index < 0 || index >= items.Length || index == selected)
                return;
            selected = index;
            Invalidate();
            if (SelectedIndexChanged != null)
                SelectedIndexChanged(this, EventArgs.Empty);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int h = HitTest(e.Location);
            if (h != hover)
            {
                hover = h;
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            hover = -1;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left)
                Choose(HitTest(e.Location));
        }

        protected override bool IsInputKey(Keys keyData)
        {
            return keyData == Keys.Left || keyData == Keys.Right || base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Left)
                Choose(selected - 1);
            else if (e.KeyCode == Keys.Right)
                Choose(selected + 1);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = BeginPaint(e);
            Shapes.Fill(g, Theme.Track, Outline, Theme.S(8));
            RectangleF[] segments = Segments();
            RectangleF sel = segments[selected];
            Shapes.Fill(g, Color.FromArgb(16, 0, 0, 0), new RectangleF(sel.X, sel.Y + 1f, sel.Width, sel.Height), Theme.S(6));
            Shapes.Fill(g, Theme.Card, sel, Theme.S(6));
            Shapes.Stroke(g, Theme.Border, sel, Theme.S(6), 1f);
            for (int i = 0; i < items.Length; i++)
            {
                Color color = i == selected || i == hover ? Theme.Text : Theme.SubText;
                DrawText(g, items[i], i == selected ? Bold : Font, Rectangle.Round(segments[i]), color);
            }
            if (Focused && ShowFocusCues)
                Shapes.Stroke(g, Theme.Accent, Outline, Theme.S(8), 1.5f);
        }
    }

    sealed class ToggleSwitch : PaintedControl
    {
        readonly Timer timer = new Timer();
        bool on;
        float knob;

        public event EventHandler CheckedChanged;

        public ToggleSwitch()
        {
            SetStyle(ControlStyles.Selectable, true);
            TabStop = true;
            Cursor = Cursors.Hand;
            timer.Interval = 15;
            timer.Tick += delegate
            {
                float target = on ? 1f : 0f;
                knob += Math.Sign(target - knob) * 0.2f;
                if (Math.Abs(target - knob) < 0.2f)
                {
                    knob = target;
                    timer.Stop();
                }
                Invalidate();
            };
        }

        public bool Checked
        {
            get { return on; }
            set
            {
                on = value;
                knob = on ? 1f : 0f;
                Invalidate();
            }
        }

        void Toggle()
        {
            on = !on;
            timer.Start();
            if (CheckedChanged != null)
                CheckedChanged(this, EventArgs.Empty);
        }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            Toggle();
        }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            base.OnKeyUp(e);
            if (e.KeyCode == Keys.Space)
                Toggle();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = BeginPaint(e);
            float radius = (Height - 1f) / 2f;
            Shapes.Fill(g, Theme.Blend(Theme.SwitchOff, Theme.Accent, knob), Outline, radius);
            float inset = Theme.S(3);
            float d = Height - inset * 2;
            float x = inset + knob * (Width - inset * 2 - d);
            using (var shadow = new SolidBrush(Color.FromArgb(36, 0, 0, 0)))
                g.FillEllipse(shadow, x, inset + 1f, d, d);
            g.FillEllipse(Brushes.White, x, inset, d, d);
            if (Focused && ShowFocusCues)
                Shapes.Stroke(g, Theme.AccentPressed, Outline, radius, 1.5f);
        }
    }

    // Number field with − / + buttons; hold a button to repeat, or scroll the wheel.
    sealed class NumberBox : PaintedControl
    {
        readonly TextBox box = new TextBox();
        readonly Timer repeat = new Timer();
        readonly int min, max;
        int current, direction, repeats, hoverZone;

        public event EventHandler ValueChanged;

        public NumberBox(int min, int max)
        {
            this.min = min;
            this.max = max;
            box.BorderStyle = BorderStyle.None;
            box.TextAlign = HorizontalAlignment.Center;
            box.BackColor = Theme.Field;
            box.ForeColor = Theme.Text;
            box.Text = "0";
            box.KeyPress += (s, e) =>
            {
                if (!char.IsDigit(e.KeyChar) && !char.IsControl(e.KeyChar))
                    e.Handled = true;
            };
            box.KeyDown += OnBoxKeyDown;
            box.Leave += delegate { Commit(); };
            box.GotFocus += delegate { Invalidate(); };
            box.LostFocus += delegate { Invalidate(); };
            box.MouseWheel += (s, e) => Step(e.Delta > 0 ? 1 : -1);
            Controls.Add(box);
            repeat.Tick += delegate
            {
                repeat.Interval = 50;
                repeats++;
                Step(direction);
            };
        }

        public int Value
        {
            get { return current; }
            set { SetValue(value, false); }
        }

        void SetValue(int value, bool notify)
        {
            value = Math.Max(min, Math.Min(max, value));
            bool changed = value != current;
            current = value;
            box.Text = value.ToString();
            Invalidate();
            if (changed && notify && ValueChanged != null)
                ValueChanged(this, EventArgs.Empty);
        }

        void Commit()
        {
            int typed;
            SetValue(int.TryParse(box.Text, out typed) ? typed : current, true);
        }

        void Step(int delta)
        {
            Commit();
            int step = repeats > 30 ? 10 : repeats > 12 ? 5 : 1;
            SetValue(current + delta * step, true);
        }

        void OnBoxKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                Commit();
                box.SelectAll();
                e.SuppressKeyPress = true;
            }
            else if (e.KeyCode == Keys.Up || e.KeyCode == Keys.Down)
            {
                Step(e.KeyCode == Keys.Up ? 1 : -1);
                e.Handled = true;
            }
        }

        int Zone(Point p)
        {
            return p.X < Height ? -1 : p.X >= Width - Height ? 1 : 0;
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            box.Bounds = new Rectangle(Height, (Height - box.Height) / 2, Math.Max(1, Width - Height * 2), box.Height);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            int zone = Zone(e.Location);
            if (e.Button != MouseButtons.Left || zone == 0)
                return;
            direction = zone;
            repeats = 0;
            Step(direction);
            repeat.Interval = 400;
            repeat.Start();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            repeat.Stop();
            repeats = 0;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int zone = Zone(e.Location);
            Cursor = zone == 0 ? Cursors.Default : Cursors.Hand;
            if (zone != hoverZone)
            {
                hoverZone = zone;
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            repeat.Stop();
            repeats = 0;
            hoverZone = 0;
            Invalidate();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            Step(e.Delta > 0 ? 1 : -1);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = BeginPaint(e);
            float radius = Theme.S(8);
            Shapes.Fill(g, Theme.Field, Outline, radius);
            Shapes.Stroke(g, box.Focused ? Theme.Accent : Theme.Border, Outline, radius, 1f);
            DrawSign(g, Height / 2f, false, current > min, hoverZone == -1);
            DrawSign(g, Width - Height / 2f, true, current < max, hoverZone == 1);
        }

        void DrawSign(Graphics g, float cx, bool plus, bool enabled, bool hot)
        {
            float cy = Height / 2f, half = Theme.S(5);
            if (hot && enabled)
            {
                float r = Theme.S(11);
                using (var brush = new SolidBrush(Theme.Hover))
                    g.FillEllipse(brush, cx - r, cy - r, r * 2, r * 2);
            }
            Color color = !enabled ? Theme.Faint : hot ? Theme.Accent : Theme.SubText;
            using (var pen = new Pen(color, Math.Max(1.5f, Theme.Scale * 1.6f)))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                g.DrawLine(pen, cx - half, cy, cx + half, cy);
                if (plus)
                    g.DrawLine(pen, cx, cy - half, cx, cy + half);
            }
        }
    }

    // Shows the global hotkey as key caps; click it and press a new combination to change it.
    sealed class HotkeyBox : PaintedControl
    {
        readonly Font capFont = Theme.MakeFont(9f, FontStyle.Regular);
        Keys hotkey, pending;
        bool capturing, invalid;

        public event EventHandler HotkeyChanged;
        public event EventHandler CaptureStarted;
        public event EventHandler CaptureEnded;
        public event EventHandler Rejected;

        public HotkeyBox()
        {
            SetStyle(ControlStyles.Selectable, true);
            TabStop = true;
            Cursor = Cursors.Hand;
        }

        public Keys Hotkey
        {
            get { return hotkey; }
            set
            {
                hotkey = value;
                Invalidate();
            }
        }

        public bool Capturing
        {
            get { return capturing; }
        }

        public bool Invalid
        {
            get { return invalid; }
            set
            {
                invalid = value;
                Invalidate();
            }
        }

        public string RejectReason { get; private set; }

        // Where focus goes once a combination has been recorded or the capture is cancelled.
        public Control FocusAfter { get; set; }

        public static string Format(Keys keys)
        {
            return string.Join(" + ", Names(keys, true).ToArray());
        }

        // Null when keys can be a hotkey; otherwise the reason it cannot.
        public static string Validate(Keys keys)
        {
            Keys key = keys & Keys.KeyCode, mods = keys & Keys.Modifiers;
            if (key == Keys.None || IsModifierKey(key))
                return "请再按一个普通键";
            bool functionKey = key >= Keys.F1 && key <= Keys.F24;
            if (!functionKey && (mods & (Keys.Control | Keys.Alt)) == 0)
                return "快捷键需要包含 Ctrl 或 Alt（F1–F12 可以单独使用）";
            if (mods == Keys.Control && "ACVXZYS".IndexOf((char)key) >= 0)
                return "Ctrl + " + KeyName(key) + " 是常用快捷键，请换一个";
            return null;
        }

        static bool IsModifierKey(Keys key)
        {
            switch (key)
            {
                case Keys.ShiftKey:
                case Keys.LShiftKey:
                case Keys.RShiftKey:
                case Keys.ControlKey:
                case Keys.LControlKey:
                case Keys.RControlKey:
                case Keys.Menu:
                case Keys.LMenu:
                case Keys.RMenu:
                case Keys.LWin:
                case Keys.RWin:
                    return true;
            }
            return false;
        }

        static List<string> Names(Keys keys, bool includeKey)
        {
            var names = new List<string>();
            if ((keys & Keys.Control) != 0)
                names.Add("Ctrl");
            if ((keys & Keys.Alt) != 0)
                names.Add("Alt");
            if ((keys & Keys.Shift) != 0)
                names.Add("Shift");
            Keys key = keys & Keys.KeyCode;
            if (includeKey && key != Keys.None && !IsModifierKey(key))
                names.Add(KeyName(key));
            return names;
        }

        static string KeyName(Keys key)
        {
            if (key >= Keys.D0 && key <= Keys.D9)
                return ((int)(key - Keys.D0)).ToString();
            if (key >= Keys.NumPad0 && key <= Keys.NumPad9)
                return "Num " + (int)(key - Keys.NumPad0);
            if ((key >= Keys.A && key <= Keys.Z) || (key >= Keys.F1 && key <= Keys.F24))
                return key.ToString();
            switch (key)
            {
                case Keys.Space: return "Space";
                case Keys.Return: return "Enter";
                case Keys.Back: return "Backspace";
                case Keys.Insert: return "Insert";
                case Keys.Delete: return "Delete";
                case Keys.Home: return "Home";
                case Keys.End: return "End";
                case Keys.Prior: return "PgUp";
                case Keys.Next: return "PgDn";
                case Keys.Up: return "↑";
                case Keys.Down: return "↓";
                case Keys.Left: return "←";
                case Keys.Right: return "→";
            }
            uint ch = Native.MapVirtualKey((uint)key, Native.MAPVK_VK_TO_CHAR) & 0x7FFF;
            return ch > ' ' ? ((char)ch).ToString() : key.ToString();
        }

        void Raise(EventHandler handler)
        {
            if (handler != null)
                handler(this, EventArgs.Empty);
        }

        void EndCapture()
        {
            if (FocusAfter != null)
                FocusAfter.Focus();
        }

        protected override void OnGotFocus(EventArgs e)
        {
            base.OnGotFocus(e);
            capturing = true;
            pending = Keys.None;
            Raise(CaptureStarted);
        }

        protected override void OnLostFocus(EventArgs e)
        {
            base.OnLostFocus(e);
            capturing = false;
            Raise(CaptureEnded);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (!capturing)
                return base.ProcessCmdKey(ref msg, keyData);
            Keys key = keyData & Keys.KeyCode, mods = keyData & Keys.Modifiers;
            if (IsModifierKey(key))
            {
                pending = mods;
                Invalidate();
                return true;
            }
            if (mods == Keys.None && key == Keys.Tab)
                return base.ProcessCmdKey(ref msg, keyData);
            if (mods == Keys.None && (key == Keys.Escape || key == Keys.Back || key == Keys.Delete))
            {
                EndCapture();
                return true;
            }
            string reason = Validate(keyData);
            if (reason != null)
            {
                RejectReason = reason;
                pending = Keys.None;
                Invalidate();
                Raise(Rejected);
                return true;
            }
            hotkey = keyData;
            Raise(HotkeyChanged);
            EndCapture();
            return true;
        }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            base.OnKeyUp(e);
            if (capturing)
            {
                pending = Control.ModifierKeys;
                Invalidate();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = BeginPaint(e);
            float radius = Theme.S(8);
            Shapes.Fill(g, capturing ? Theme.AccentSoft : Theme.Field, Outline, radius);
            Shapes.Stroke(g, capturing ? Theme.Accent : invalid ? Theme.Danger : Theme.Border, Outline, radius, 1f);

            List<string> names = Names(capturing ? pending : hotkey, !capturing);
            if (capturing && names.Count == 0)
            {
                DrawText(g, "请按下新的快捷键…", Font, ClientRectangle, Theme.Accent);
                return;
            }
            if (capturing)
                names.Add("…");

            int capH = Height - Theme.S(12), padX = Theme.S(7), plusW = Theme.S(16);
            var widths = new int[names.Count];
            int total = 0;
            for (int i = 0; i < names.Count; i++)
            {
                widths[i] = TextRenderer.MeasureText(names[i], capFont, Size.Empty, TextFormatFlags.NoPadding).Width + padX * 2;
                total += widths[i] + (i > 0 ? plusW : 0);
            }
            int x = (Width - total) / 2, y = (Height - capH) / 2;
            float capRadius = Theme.S(5);
            for (int i = 0; i < names.Count; i++)
            {
                if (i > 0)
                {
                    DrawText(g, "+", capFont, new Rectangle(x, 0, plusW, Height), Theme.Hint);
                    x += plusW;
                }
                bool ellipsis = capturing && i == names.Count - 1;
                if (!ellipsis)
                {
                    var cap = new RectangleF(x + 0.5f, y + 0.5f, widths[i] - 1f, capH - 1f);
                    Shapes.Fill(g, Color.FromArgb(18, 0, 0, 0), new RectangleF(cap.X, cap.Y + 1.5f, cap.Width, cap.Height), capRadius);
                    Shapes.Fill(g, Theme.Card, cap, capRadius);
                    Shapes.Stroke(g, Theme.Border, cap, capRadius, 1f);
                }
                DrawText(g, names[i], capFont, new Rectangle(x, y, widths[i], capH), ellipsis ? Theme.Accent : Theme.Text);
                x += widths[i];
            }
        }
    }

    sealed class AccentButton : PaintedControl
    {
        bool hover, down, danger;

        public AccentButton()
        {
            SetStyle(ControlStyles.Selectable, true);
            TabStop = true;
            BackColor = Theme.Back;
            ForeColor = Color.White;
            Cursor = Cursors.Hand;
        }

        // Red "stop" look while typing is in progress.
        public bool Danger
        {
            get { return danger; }
            set
            {
                danger = value;
                Invalidate();
            }
        }

        protected override void OnTextChanged(EventArgs e)
        {
            base.OnTextChanged(e);
            Invalidate();
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            hover = true;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            hover = down = false;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            down = e.Button == MouseButtons.Left;
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            down = false;
            Invalidate();
        }

        protected override bool IsInputKey(Keys keyData)
        {
            return keyData == Keys.Enter || base.IsInputKey(keyData);
        }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            base.OnKeyUp(e);
            if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter)
                OnClick(EventArgs.Empty);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = BeginPaint(e);
            Color fill = danger
                ? (down ? Theme.DangerPressed : hover ? Theme.DangerHover : Theme.Danger)
                : (down ? Theme.AccentPressed : hover ? Theme.AccentHover : Theme.Accent);
            float radius = Theme.S(10);
            float lift = Theme.S(2);
            Shapes.Fill(g, Color.FromArgb(50, fill), new RectangleF(1f, lift, Width - 2f, Height - lift), radius);
            var face = new RectangleF(0.5f, 0.5f, Width - 1f, Height - lift - 0.5f);
            Shapes.Fill(g, fill, face, radius);
            DrawText(g, Text, Font, new Rectangle(0, 0, Width, (int)face.Bottom), ForeColor);
            if (Focused && ShowFocusCues)
            {
                float inset = Theme.S(3);
                Shapes.Stroke(g, Color.FromArgb(160, Color.White), RectangleF.Inflate(face, -inset, -inset), radius - inset, 1.5f);
            }
        }
    }

    // Pill-shaped on/off button with an optional pin glyph (used for "置顶").
    sealed class ChipToggle : PaintedControl
    {
        static readonly Font IconFont = FindIconFont();
        bool on, hover;

        public event EventHandler CheckedChanged;

        public ChipToggle(string text)
        {
            Text = text;
            SetStyle(ControlStyles.Selectable, true);
            TabStop = true;
            BackColor = Theme.Back;
            Cursor = Cursors.Hand;
        }

        static Font FindIconFont()
        {
            foreach (string name in new[] { "Segoe Fluent Icons", "Segoe MDL2 Assets" })
            {
                var font = new Font(name, 9.5f);
                if (font.Name == name)
                    return font;
                font.Dispose();
            }
            return null;
        }

        public bool Checked
        {
            get { return on; }
            set
            {
                on = value;
                Invalidate();
            }
        }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            on = !on;
            Invalidate();
            if (CheckedChanged != null)
                CheckedChanged(this, EventArgs.Empty);
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            hover = true;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            hover = false;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = BeginPaint(e);
            float radius = (Height - 1f) / 2f;
            Shapes.Fill(g, on ? Theme.AccentSoft : hover ? Theme.Hover : Theme.Card, Outline, radius);
            Shapes.Stroke(g, on ? Color.FromArgb(150, Theme.Accent) : Theme.Border, Outline, radius, 1f);
            Color fore = on ? Theme.Accent : Theme.SubText;

            const TextFormatFlags flags = TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;
            string glyph = on ? "" : "";
            Size glyphSize = IconFont != null ? TextRenderer.MeasureText(glyph, IconFont, Size.Empty, flags) : Size.Empty;
            Size textSize = TextRenderer.MeasureText(Text, Font, Size.Empty, flags);
            int gap = IconFont != null ? Theme.S(5) : 0;
            int x = (Width - glyphSize.Width - gap - textSize.Width) / 2;
            if (IconFont != null)
                TextRenderer.DrawText(g, glyph, IconFont, new Rectangle(x, 0, glyphSize.Width, Height), fore, flags);
            TextRenderer.DrawText(g, Text, Font, new Rectangle(x + glyphSize.Width + gap, 0, textSize.Width + 2, Height), fore, flags);
        }
    }

    sealed class StatusLine : PaintedControl
    {
        Color dot = Theme.Faint;

        public StatusLine()
        {
            BackColor = Theme.Back;
            ForeColor = Theme.Text;
        }

        public void Set(Color dotColor, string text)
        {
            dot = dotColor;
            Text = text;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = BeginPaint(e);
            float d = Theme.S(8), ring = Theme.S(3);
            using (var halo = new SolidBrush(Color.FromArgb(40, dot)))
                g.FillEllipse(halo, 0, (Height - d) / 2f - ring, d + ring * 2, d + ring * 2);
            using (var brush = new SolidBrush(dot))
                g.FillEllipse(brush, ring, (Height - d) / 2f, d, d);
            TextRenderer.DrawText(g, Text, Font, new Rectangle(Theme.S(20), 0, Width - Theme.S(20), Height), ForeColor,
                TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
        }
    }

    sealed class LogoBox : PaintedControl
    {
        public LogoBox()
        {
            BackColor = Theme.Back;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = BeginPaint(e);
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            Logo.Draw(g, new RectangleF(0, 0, Width, Height));
        }
    }
}
