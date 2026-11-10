using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace AutoPaste
{
    sealed class MainForm : Form
    {
        public static readonly int ShowMeMessage = (int)Native.RegisterWindowMessage("AutoPaste.ShowMe");

        const int HotkeyId = 1;
        // Longer clipboard contents are still typed in full; only the preview is cut.
        const int PreviewLimit = 200000;
        // Starting from the button needs time to click into the target window.
        const int ButtonCountdownMin = 3;

        static readonly string[] ModeHints =
        {
            "直接发送文字，兼容绝大多数软件，中文无需切换输入法",
            "模拟真实按键，适合虚拟机、远程桌面；中文仍按文字发送",
        };

        readonly Settings settings = Settings.Load();
        readonly Font bodyFont = Theme.MakeFont(9.75f, FontStyle.Regular);
        readonly Font smallFont = Theme.MakeFont(9f, FontStyle.Regular);
        readonly Font sectionFont = Theme.MakeFont(10.5f, FontStyle.Bold);
        readonly Font titleFont = Theme.MakeFont(15f, FontStyle.Bold);
        readonly Font buttonFont = Theme.MakeFont(10.5f, FontStyle.Bold);
        readonly Font textFont = Theme.MakeFont(10f, FontStyle.Regular);

        readonly LogoBox logo = new LogoBox();
        readonly ChipToggle pinChip = new ChipToggle("置顶");
        readonly Card contentCard = new Card();
        readonly Card settingsCard = new Card();
        readonly Segmented sourceSeg = new Segmented("剪贴板", "自定义");
        readonly Segmented modeSeg = new Segmented("Unicode", "按键模拟");
        readonly Segmented newlineSeg = new Segmented("回车", "Shift+回车", "忽略");
        readonly FieldBox textField = new FieldBox();
        readonly LinkLabel clearLink = new LinkLabel();
        readonly HotkeyBox hotkeyBox = new HotkeyBox();
        readonly NumberBox countdownBox = new NumberBox(0, 10);
        readonly NumberBox intervalBox = new NumberBox(0, 2000);
        readonly ToggleSwitch editorSwitch = new ToggleSwitch();
        readonly AccentButton startButton = new AccentButton();
        readonly StatusLine status = new StatusLine();
        readonly Hud hud = new Hud();
        readonly Timer idleTimer = new Timer();
        readonly Timer saveTimer = new Timer();
        readonly Label titleLabel, subtitleLabel, contentTitle, settingsTitle, statsLabel, placeholder;
        readonly Label modeHint, editorHint, countdownUnit, intervalUnit;
        readonly Label[] rowLabels;
        readonly bool ready;

        Typer typer;
        bool typerFromHotkey;
        bool hotkeyOk;
        bool settingText;
        string clipboardText = "";
        string customText = "";
        IntPtr escHook;
        Native.LowLevelKeyboardProc escHookProc;
        bool swallowEscUp;

        public MainForm()
        {
            Text = "自动粘贴";
            Font = bodyFont;
            BackColor = Theme.Back;
            AutoScaleMode = AutoScaleMode.None;
            StartPosition = FormStartPosition.Manual;
            DoubleBuffered = true;
            Icon = LoadAppIcon();
            ClientSize = new Size(Theme.S(780), Theme.S(536));
            MinimumSize = Size;

            Controls.Add(logo);
            titleLabel = AddLabel(this, "自动粘贴", titleFont, Theme.Text);
            subtitleLabel = AddLabel(this, "模拟键盘逐字输入，专治各种「禁止粘贴」", smallFont, Theme.SubText);
            Controls.Add(pinChip);

            Controls.Add(contentCard);
            contentTitle = AddLabel(contentCard, "要输入的内容", sectionFont, Theme.Text);
            contentCard.Controls.Add(sourceSeg);
            textField.Box.Font = textFont;
            contentCard.Controls.Add(textField);
            placeholder = AddLabel(textField, "", textFont, Theme.Hint);
            placeholder.BackColor = Theme.Field;
            placeholder.BringToFront();
            statsLabel = AddLabel(contentCard, "", smallFont, Theme.SubText);
            statsLabel.AutoSize = false;
            statsLabel.AutoEllipsis = true;
            statsLabel.TextAlign = ContentAlignment.MiddleLeft;
            clearLink.Text = "清空";
            clearLink.Font = smallFont;
            clearLink.AutoSize = true;
            clearLink.BackColor = Theme.Card;
            clearLink.LinkColor = Theme.Accent;
            clearLink.ActiveLinkColor = Theme.AccentPressed;
            clearLink.LinkBehavior = LinkBehavior.HoverUnderline;
            clearLink.TabStop = false;
            contentCard.Controls.Add(clearLink);

            Controls.Add(settingsCard);
            settingsTitle = AddLabel(settingsCard, "设置", sectionFont, Theme.Text);
            string[] rowNames = { "快捷键", "开始前倒计时", "每字间隔", "输入方式", "换行方式", "代码编辑器修正" };
            rowLabels = new Label[rowNames.Length];
            for (int i = 0; i < rowNames.Length; i++)
                rowLabels[i] = AddLabel(settingsCard, rowNames[i], bodyFont, Theme.Text);
            hotkeyBox.FocusAfter = startButton;
            settingsCard.Controls.Add(hotkeyBox);
            settingsCard.Controls.Add(countdownBox);
            countdownUnit = AddLabel(settingsCard, "秒", bodyFont, Theme.SubText);
            settingsCard.Controls.Add(intervalBox);
            intervalUnit = AddLabel(settingsCard, "毫秒", bodyFont, Theme.SubText);
            settingsCard.Controls.Add(modeSeg);
            modeHint = AddLabel(settingsCard, "", smallFont, Theme.Hint);
            settingsCard.Controls.Add(newlineSeg);
            settingsCard.Controls.Add(editorSwitch);
            editorHint = AddLabel(settingsCard, "头歌等在线编辑器：抵消自动补全的括号和缩进", smallFont, Theme.Hint);

            startButton.Text = "开始输入";
            startButton.Font = buttonFont;
            Controls.Add(status);
            Controls.Add(startButton);
            ActiveControl = startButton;

            ApplySettings();
            WireEvents();
            ready = true;
            LayoutUi();
        }

        static Label AddLabel(Control parent, string text, Font font, Color color)
        {
            var label = new Label();
            label.Text = text;
            label.Font = font;
            label.ForeColor = color;
            label.BackColor = parent is Form ? Theme.Back : Theme.Card;
            label.AutoSize = true;
            label.UseMnemonic = false;
            parent.Controls.Add(label);
            return label;
        }

        static Icon LoadAppIcon()
        {
            using (Stream stream = typeof(MainForm).Assembly.GetManifestResourceStream("AutoPaste.app.ico"))
                return stream != null ? new Icon(stream) : null;
        }

        public static string ReadClipboard()
        {
            for (int attempt = 0; attempt < 6; attempt++)
            {
                try
                {
                    return Clipboard.ContainsText() ? Clipboard.GetText() : "";
                }
                catch (ExternalException)
                {
                    System.Threading.Thread.Sleep(25);
                }
            }
            return null;
        }

        void ApplySettings()
        {
            hotkeyBox.Hotkey = settings.Hotkey;
            countdownBox.Value = settings.Countdown;
            intervalBox.Value = settings.Interval;
            sourceSeg.SelectedIndex = settings.Source;
            modeSeg.SelectedIndex = settings.Mode;
            newlineSeg.SelectedIndex = settings.Newline;
            editorSwitch.Checked = settings.EditorFix;
            pinChip.Checked = settings.TopMost;
            TopMost = settings.TopMost;
            modeHint.Text = ModeHints[settings.Mode];

            Rectangle b = settings.Bounds;
            if (b.Width >= MinimumSize.Width && b.Height >= MinimumSize.Height && IsOnScreen(b))
                Bounds = b;
            else
            {
                Rectangle area = Screen.PrimaryScreen.WorkingArea;
                Location = new Point(area.Left + (area.Width - Width) / 2, area.Top + (area.Height - Height) / 2);
            }
            ShowSource();
        }

        static bool IsOnScreen(Rectangle bounds)
        {
            var titleBar = new Rectangle(bounds.X + 40, bounds.Y, Math.Max(1, bounds.Width - 80), 30);
            foreach (Screen screen in Screen.AllScreens)
                if (screen.WorkingArea.IntersectsWith(titleBar))
                    return true;
            return false;
        }

        void WireEvents()
        {
            pinChip.CheckedChanged += delegate
            {
                TopMost = settings.TopMost = pinChip.Checked;
                SaveSoon();
            };
            sourceSeg.SelectedIndexChanged += delegate
            {
                settings.Source = sourceSeg.SelectedIndex;
                SaveSoon();
                ShowSource();
            };
            textField.Box.TextChanged += delegate
            {
                if (settingText || settings.Source != 1)
                    return;
                customText = textField.Box.Text;
                UpdatePlaceholder();
                UpdateStats();
            };
            placeholder.MouseDown += delegate { textField.Box.Focus(); };
            clearLink.LinkClicked += delegate
            {
                textField.Box.Clear();
                textField.Box.Focus();
            };
            hotkeyBox.CaptureStarted += delegate
            {
                UnregisterMainHotkey();
                status.Set(Theme.Accent, "请按下新的快捷键组合（需包含 Ctrl 或 Alt），Esc 取消");
            };
            hotkeyBox.CaptureEnded += delegate
            {
                RegisterMainHotkey();
                ShowIdleStatus();
            };
            hotkeyBox.HotkeyChanged += delegate
            {
                settings.Hotkey = hotkeyBox.Hotkey;
                SaveSoon();
            };
            hotkeyBox.Rejected += delegate { status.Set(Theme.Danger, hotkeyBox.RejectReason); };
            countdownBox.ValueChanged += delegate
            {
                settings.Countdown = countdownBox.Value;
                SaveSoon();
            };
            intervalBox.ValueChanged += delegate
            {
                settings.Interval = intervalBox.Value;
                SaveSoon();
                UpdateStats();
            };
            modeSeg.SelectedIndexChanged += delegate
            {
                settings.Mode = modeSeg.SelectedIndex;
                modeHint.Text = ModeHints[settings.Mode];
                SaveSoon();
                UpdateStats();
            };
            newlineSeg.SelectedIndexChanged += delegate
            {
                settings.Newline = newlineSeg.SelectedIndex;
                SaveSoon();
            };
            editorSwitch.CheckedChanged += delegate
            {
                settings.EditorFix = editorSwitch.Checked;
                SaveSoon();
                UpdateStats();
            };
            startButton.Click += delegate
            {
                if (typer != null)
                    StopTyping();
                else
                    StartTyping(false);
            };

            // Clicking empty space ends a hotkey capture, like clicking away from a text box.
            MouseDown += ClearFocus;
            contentCard.MouseDown += ClearFocus;
            settingsCard.MouseDown += ClearFocus;

            idleTimer.Interval = 6000;
            idleTimer.Tick += delegate
            {
                idleTimer.Stop();
                if (typer == null)
                    ShowIdleStatus();
            };
            saveTimer.Interval = 500;
            saveTimer.Tick += delegate
            {
                saveTimer.Stop();
                settings.Save();
            };
        }

        void ClearFocus(object sender, MouseEventArgs e)
        {
            ActiveControl = startButton;
        }

        void SaveSoon()
        {
            saveTimer.Stop();
            saveTimer.Start();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutUi();
        }

        void LayoutUi()
        {
            if (!ready)
                return;
            int pad = Theme.S(20), gap = Theme.S(14);
            int w = ClientSize.Width, h = ClientSize.Height;

            logo.Bounds = new Rectangle(pad, Theme.S(18), Theme.S(40), Theme.S(40));
            titleLabel.Location = new Point(logo.Right + Theme.S(12), Theme.S(14));
            subtitleLabel.Location = new Point(logo.Right + Theme.S(13), titleLabel.Bottom);
            pinChip.Bounds = new Rectangle(w - pad - Theme.S(76), Theme.S(24), Theme.S(76), Theme.S(28));

            int top = logo.Bottom + Theme.S(16);
            int barH = Theme.S(44);
            int barY = h - pad - barH;
            int settingsW = Theme.S(344);
            int cardH = barY - gap - top;
            settingsCard.Bounds = new Rectangle(w - pad - settingsW, top, settingsW, cardH);
            contentCard.Bounds = new Rectangle(pad, top, settingsCard.Left - gap - pad, cardH);
            startButton.Bounds = new Rectangle(w - pad - Theme.S(168), barY, Theme.S(168), barH);
            status.Bounds = new Rectangle(pad + Theme.S(2), barY, startButton.Left - pad - Theme.S(16), barH - Theme.S(2));

            LayoutContentCard();
            LayoutSettingsCard();
        }

        void LayoutContentCard()
        {
            int p = Theme.S(18), w = contentCard.Width, h = contentCard.Height;
            contentTitle.Location = new Point(p, Theme.S(17));
            sourceSeg.Bounds = new Rectangle(w - p - Theme.S(148), Theme.S(12), Theme.S(148), Theme.S(30));
            int statsH = Theme.S(22);
            int fieldTop = Theme.S(54);
            textField.Bounds = new Rectangle(p, fieldTop, w - p * 2, h - fieldTop - p - statsH - Theme.S(6));
            placeholder.Location = new Point(Theme.S(10), Theme.S(11));
            statsLabel.Bounds = new Rectangle(p, h - p - statsH + Theme.S(2), w - p * 2 - Theme.S(48), statsH);
            clearLink.Location = new Point(w - p - clearLink.Width, statsLabel.Top + (statsH - clearLink.Height) / 2);
        }

        void LayoutSettingsCard()
        {
            int p = Theme.S(18), w = settingsCard.Width;
            settingsTitle.Location = new Point(p, Theme.S(17));
            int x = p + Theme.S(100), cw = w - x - p, ch = Theme.S(32), rowH = Theme.S(46);
            int y = Theme.S(50);

            PlaceRowLabel(0, y, rowH);
            hotkeyBox.Bounds = InRow(x, y, cw, ch, rowH);
            y += rowH;

            PlaceRowLabel(1, y, rowH);
            countdownBox.Bounds = InRow(x, y, Theme.S(116), ch, rowH);
            countdownUnit.Location = new Point(countdownBox.Right + Theme.S(8), y + (rowH - countdownUnit.Height) / 2);
            y += rowH;

            PlaceRowLabel(2, y, rowH);
            intervalBox.Bounds = InRow(x, y, Theme.S(116), ch, rowH);
            intervalUnit.Location = new Point(intervalBox.Right + Theme.S(8), y + (rowH - intervalUnit.Height) / 2);
            y += rowH;

            PlaceRowLabel(3, y, rowH);
            modeSeg.Bounds = InRow(x, y, cw, ch, rowH);
            y += rowH;
            modeHint.Location = new Point(p, y - Theme.S(5));
            y += Theme.S(18);

            PlaceRowLabel(4, y, rowH);
            newlineSeg.Bounds = InRow(x, y, cw, ch, rowH);
            y += rowH;

            PlaceRowLabel(5, y, rowH);
            editorSwitch.Bounds = InRow(w - p - Theme.S(42), y, Theme.S(42), Theme.S(24), rowH);
            y += rowH;
            editorHint.Location = new Point(p, y - Theme.S(5));
        }

        void PlaceRowLabel(int row, int y, int rowH)
        {
            rowLabels[row].Location = new Point(Theme.S(18), y + (rowH - rowLabels[row].Height) / 2);
        }

        static Rectangle InRow(int x, int y, int width, int height, int rowH)
        {
            return new Rectangle(x, y + (rowH - height) / 2, width, height);
        }

        void ShowSource()
        {
            bool custom = settings.Source == 1;
            if (custom && customText.Length == 0)
                customText = clipboardText;
            textField.Box.ReadOnly = !custom;
            textField.Box.AcceptsTab = custom;
            clearLink.Visible = custom;
            if (custom)
                SetBoxText(customText, false);
            else
                RefreshClipboard();
            UpdatePlaceholder();
            UpdateStats();
        }

        void SetBoxText(string text, bool preview)
        {
            settingText = true;
            textField.Box.Text = preview && text.Length > PreviewLimit ? text.Substring(0, PreviewLimit) : text;
            settingText = false;
        }

        void RefreshClipboard()
        {
            string text = ReadClipboard();
            if (text == null)
                return;
            clipboardText = text;
            if (settings.Source != 0)
                return;
            SetBoxText(text, true);
            UpdatePlaceholder();
            UpdateStats();
        }

        void UpdatePlaceholder()
        {
            placeholder.Text = settings.Source == 0
                ? "剪贴板里还没有文字。\n复制一段文字，这里会自动显示预览。"
                : "在这里输入或粘贴要自动输入的文字…";
            placeholder.Visible = textField.Box.TextLength == 0;
        }

        void UpdateStats()
        {
            string text = settings.Source == 0 ? clipboardText : customText;
            int lines;
            int count = Typer.Count(text, out lines);
            if (count == 0)
            {
                statsLabel.Text = settings.Source == 0 ? "剪贴板为空" : "还没有内容";
                return;
            }
            long ms = (long)count * (settings.Interval + (settings.Mode == 1 ? 4 : 1));
            if (settings.EditorFix)
                ms += (long)(lines - 1) * 45;
            statsLabel.Text = string.Format("{0:N0} 个字符 · {1:N0} 行 · 预计约 {2}", count, lines, FormatDuration(ms));
        }

        static string FormatDuration(long ms)
        {
            long s = Math.Max(1, (ms + 999) / 1000);
            if (s < 60)
                return s + " 秒";
            if (s < 3600)
                return string.Format("{0} 分 {1} 秒", s / 60, s % 60);
            return string.Format("{0} 小时 {1} 分", s / 3600, s % 3600 / 60);
        }

        void RegisterMainHotkey()
        {
            UnregisterMainHotkey();
            Keys k = settings.Hotkey;
            uint mods = Native.MOD_NOREPEAT;
            if ((k & Keys.Control) != 0)
                mods |= Native.MOD_CONTROL;
            if ((k & Keys.Alt) != 0)
                mods |= Native.MOD_ALT;
            if ((k & Keys.Shift) != 0)
                mods |= Native.MOD_SHIFT;
            hotkeyOk = Native.RegisterHotKey(Handle, HotkeyId, mods, (uint)(k & Keys.KeyCode));
            hotkeyBox.Invalid = !hotkeyOk;
        }

        void UnregisterMainHotkey()
        {
            Native.UnregisterHotKey(Handle, HotkeyId);
            hotkeyOk = false;
        }

        void ShowIdleStatus()
        {
            if (hotkeyBox.Capturing)
                return;
            string hotkey = HotkeyBox.Format(settings.Hotkey);
            if (hotkeyOk)
                status.Set(Theme.Faint, "就绪 · 在目标输入框里按 " + hotkey + " 开始，Esc 随时停止");
            else
                status.Set(Theme.Danger, "快捷键 " + hotkey + " 已被其他程序占用，请在右侧换一个");
        }

        void OnHotkey()
        {
            if (typer != null)
            {
                StopTyping();
                return;
            }
            if ((settings.Hotkey & Keys.Alt) != 0)
                Typer.MaskModifierRelease();
            StartTyping(true);
        }

        void StartTyping(bool fromHotkey)
        {
            idleTimer.Stop();
            string text;
            if (settings.Source == 0)
            {
                text = ReadClipboard();
                if (text == null)
                {
                    Notify(fromHotkey, Theme.Danger, "剪贴板正被其他程序占用，请稍后再试");
                    return;
                }
            }
            else
                text = customText;
            int lines;
            if (Typer.Count(text, out lines) == 0)
            {
                Notify(fromHotkey, Theme.Danger, settings.Source == 0 ? "剪贴板里没有文字" : "还没有要输入的内容");
                return;
            }

            var options = new TypeOptions();
            options.Text = text;
            options.CountdownSeconds = fromHotkey ? settings.Countdown : Math.Max(settings.Countdown, ButtonCountdownMin);
            options.IntervalMs = settings.Interval;
            options.Mode = (InputMode)settings.Mode;
            options.Newline = (NewlineMode)settings.Newline;
            options.EditorFix = settings.EditorFix;

            var t = new Typer(options);
            t.Progress += p => Post(() => OnProgress(t, p));
            typer = t;
            typerFromHotkey = fromHotkey;
            InstallEscHook();
            startButton.Danger = true;
            startButton.Text = "取消";
            t.Start();
        }

        void StopTyping()
        {
            if (typer != null)
                typer.Cancel();
        }

        void Post(Action action)
        {
            try
            {
                if (IsHandleCreated && !IsDisposed)
                    BeginInvoke(action);
            }
            catch (InvalidOperationException)
            {
            }
        }

        void OnProgress(Typer source, TypeProgress p)
        {
            if (source != typer)
                return;
            switch (p.State)
            {
                case TypeState.WaitingKeys:
                    ShowProgress("请松开快捷键…", -1);
                    break;
                case TypeState.Countdown:
                    ShowProgress(string.Format(typerFromHotkey ? "{0} 秒后开始输入 · Esc 取消" : "{0} 秒后开始，请点一下要输入的位置 · Esc 取消", p.Seconds), -1);
                    break;
                case TypeState.Typing:
                    startButton.Text = "停止输入";
                    ShowProgress(string.Format("正在输入 {0:N0} / {1:N0} · Esc 停止", p.Done, p.Total), p.Total > 0 ? (float)p.Done / p.Total : 0f);
                    break;
                case TypeState.Finished:
                    Finish(Theme.Success, string.Format("输入完成，共 {0:N0} 个字符", p.Total));
                    break;
                case TypeState.Stopped:
                    Finish(Theme.Warning, p.Message ?? string.Format("已停止，已输入 {0:N0} / {1:N0}", p.Done, p.Total));
                    break;
                default:
                    Finish(Theme.Danger, p.Message);
                    break;
            }
        }

        void ShowProgress(string text, float progress)
        {
            status.Set(Theme.Accent, text);
            hud.ShowMessage(text, progress, 0);
        }

        void Finish(Color color, string text)
        {
            typer = null;
            RemoveEscHook();
            startButton.Danger = false;
            startButton.Text = "开始输入";
            status.Set(color, text);
            hud.ShowMessage(text, -1, 2200);
            idleTimer.Start();
        }

        // Errors raised before typing starts; from the hotkey the main window may be hidden, so use the banner too.
        void Notify(bool banner, Color color, string text)
        {
            status.Set(color, text);
            if (banner)
                hud.ShowMessage(text, -1, 2500);
            idleTimer.Start();
        }

        void InstallEscHook()
        {
            if (escHook != IntPtr.Zero)
                return;
            escHookProc = EscHookCallback;
            escHook = Native.SetWindowsHookEx(Native.WH_KEYBOARD_LL, escHookProc, Native.GetModuleHandle(null), 0);
        }

        void RemoveEscHook()
        {
            if (escHook == IntPtr.Zero)
                return;
            Native.UnhookWindowsHookEx(escHook);
            escHook = IntPtr.Zero;
        }

        // While typing, the user's Esc stops it and is swallowed; the Esc presses we inject ourselves pass through.
        IntPtr EscHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                var info = (Native.KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(Native.KBDLLHOOKSTRUCT));
                if (info.vkCode == Native.VK_ESCAPE && info.dwExtraInfo != Typer.Signature)
                {
                    int msg = wParam.ToInt32();
                    if (msg == Native.WM_KEYDOWN || msg == Native.WM_SYSKEYDOWN)
                    {
                        swallowEscUp = true;
                        StopTyping();
                        return (IntPtr)1;
                    }
                    if (swallowEscUp)
                    {
                        swallowEscUp = false;
                        return (IntPtr)1;
                    }
                }
            }
            return Native.CallNextHookEx(escHook, nCode, wParam, lParam);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Native.AddClipboardFormatListener(Handle);
            RegisterMainHotkey();
            int caption = Theme.Back.R | (Theme.Back.G << 8) | (Theme.Back.B << 16);
            Native.DwmSetWindowAttribute(Handle, Native.DWMWA_CAPTION_COLOR, ref caption, 4);
            ShowIdleStatus();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            ActiveControl = startButton;
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_HOTKEY && m.WParam.ToInt32() == HotkeyId)
            {
                OnHotkey();
                return;
            }
            if (m.Msg == Native.WM_CLIPBOARDUPDATE)
            {
                if (typer == null)
                    RefreshClipboard();
            }
            else if (m.Msg == ShowMeMessage && ShowMeMessage != 0)
            {
                if (WindowState == FormWindowState.Minimized)
                    WindowState = FormWindowState.Normal;
                Show();
                Activate();
                return;
            }
            base.WndProc(ref m);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            StopTyping();
            RemoveEscHook();
            Native.UnregisterHotKey(Handle, HotkeyId);
            Native.RemoveClipboardFormatListener(Handle);
            settings.Bounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
            saveTimer.Stop();
            settings.Save();
            hud.Close();
            base.OnFormClosing(e);
        }
    }
}
