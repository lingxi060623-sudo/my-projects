using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace AutoPaste
{
    enum InputMode { Unicode, KeyStroke }

    enum NewlineMode { Enter, ShiftEnter, Ignore }

    enum TypeState { WaitingKeys, Countdown, Typing, Finished, Stopped, Failed }

    sealed class TypeOptions
    {
        public string Text;
        public int CountdownSeconds;
        public int IntervalMs;
        public InputMode Mode;
        public NewlineMode Newline;
        public bool EditorFix;
    }

    sealed class TypeProgress
    {
        public TypeState State;
        public int Seconds;
        public int Done;
        public int Total;
        public string Message;
    }

    // Types text into the foreground window with SendInput, on a background thread.
    sealed class Typer
    {
        // Tags every event we inject so the Esc hook can tell them from the user's own keys.
        public static readonly IntPtr Signature = new IntPtr(0x41505354);

        // Online code editors (头歌/Monaco, CodeMirror) insert the closing half of these as you type.
        const string AutoClosed = "([{\"'`";
        const int OutdentPresses = 7;
        const int KeyHoldMs = 3;
        static readonly int InputSize = Marshal.SizeOf(typeof(Native.INPUT));
        static readonly int[] ModifierKeys = { Native.VK_SHIFT, Native.VK_CONTROL, Native.VK_MENU, Native.VK_LWIN, Native.VK_RWIN };

        readonly TypeOptions options;
        volatile bool cancelled;
        IntPtr layout;
        IntPtr targetRoot;
        int lastReport;

        public event Action<TypeProgress> Progress;

        public Typer(TypeOptions options)
        {
            this.options = options;
        }

        public void Start()
        {
            var thread = new Thread(Run);
            thread.IsBackground = true;
            thread.Name = "Typer";
            thread.Start();
        }

        public void Cancel()
        {
            cancelled = true;
        }

        // Splits text into what one keystroke produces: a character, a surrogate pair, "\n" or "\t".
        public static List<string> Split(string text)
        {
            var units = new List<string>();
            if (string.IsNullOrEmpty(text))
                return units;
            string s = Normalize(text);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (char.IsHighSurrogate(c) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
                {
                    units.Add(s.Substring(i, 2));
                    i++;
                }
                else if (IsTypeable(c))
                    units.Add(c.ToString());
            }
            return units;
        }

        // Same rules as Split, without allocating; lines counts the text's lines.
        public static int Count(string text, out int lines)
        {
            lines = 0;
            if (string.IsNullOrEmpty(text))
                return 0;
            string s = Normalize(text);
            int count = 0;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (char.IsHighSurrogate(c) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
                {
                    count++;
                    i++;
                }
                else if (IsTypeable(c))
                {
                    count++;
                    if (c == '\n')
                        lines++;
                }
            }
            if (count > 0)
                lines++;
            return count;
        }

        // A trailing line break is dropped: in chat boxes and forms it would send or submit.
        static string Normalize(string text)
        {
            return text.Replace("\r\n", "\n").Replace('\r', '\n').TrimEnd('\n');
        }

        static bool IsTypeable(char c)
        {
            return (c >= ' ' && c != '\x7f') || c == '\n' || c == '\t';
        }

        // Tapping an unassigned key while Alt is still held keeps the target from
        // opening its menu bar when the user lets go of Alt after the hotkey.
        public static void MaskModifierRelease()
        {
            var down = new Native.INPUT();
            down.type = Native.INPUT_KEYBOARD;
            down.u.ki.wVk = Native.VK_UNASSIGNED;
            down.u.ki.dwExtraInfo = Signature;
            Native.INPUT up = down;
            up.u.ki.dwFlags = Native.KEYEVENTF_KEYUP;
            Send(down, up);
        }

        void Run()
        {
            List<string> units = Split(options.Text);
            int total = units.Count;
            try
            {
                if (!WaitForModifiers())
                {
                    if (cancelled)
                        Report(TypeState.Stopped, 0, 0, total, "已取消");
                    else
                        Report(TypeState.Failed, 0, 0, total, "请先松开 Ctrl / Alt / Shift 键再开始");
                    return;
                }
                for (int s = options.CountdownSeconds; s > 0; s--)
                {
                    Report(TypeState.Countdown, s, 0, total, null);
                    if (!Pause(1000))
                    {
                        Report(TypeState.Stopped, 0, 0, total, "已取消");
                        return;
                    }
                }

                IntPtr target = Native.GetForegroundWindow();
                uint pid;
                uint thread = Native.GetWindowThreadProcessId(target, out pid);
                if (target == IntPtr.Zero || pid == (uint)Process.GetCurrentProcess().Id)
                {
                    Report(TypeState.Failed, 0, 0, total, "没有目标窗口：请先点一下要输入的位置，再开始");
                    return;
                }
                targetRoot = Native.GetAncestor(target, Native.GA_ROOTOWNER);
                layout = Native.GetKeyboardLayout(thread);

                IntPtr ime = options.Mode == InputMode.KeyStroke ? CloseIme(target) : IntPtr.Zero;
                Native.timeBeginPeriod(1);
                try
                {
                    TypeAll(units);
                }
                finally
                {
                    Native.timeEndPeriod(1);
                    if (ime != IntPtr.Zero)
                        SetImeOpen(ime, true);
                }
            }
            catch (Exception ex)
            {
                Report(TypeState.Failed, 0, 0, total, "出错了：" + ex.Message);
            }
        }

        void TypeAll(List<string> units)
        {
            int total = units.Count;
            Report(TypeState.Typing, 0, 0, total, null);
            for (int i = 0; i < total; i++)
            {
                if (cancelled)
                {
                    Report(TypeState.Stopped, 0, i, total, null);
                    return;
                }
                if (TargetChanged())
                {
                    Report(TypeState.Stopped, 0, i, total, string.Format("目标窗口已切换，已自动停止（{0:N0} / {1:N0}）", i, total));
                    return;
                }
                if (!TypeUnit(units[i]))
                {
                    Report(TypeState.Failed, 0, i, total, "按键被系统拦截：目标程序可能以管理员身份运行，请右键本工具「以管理员身份运行」");
                    return;
                }
                int now = Environment.TickCount;
                if (i + 1 == total || now - lastReport >= 40)
                {
                    lastReport = now;
                    Report(TypeState.Typing, 0, i + 1, total, null);
                }
                if (options.IntervalMs > 0 && i + 1 < total && !Pause(options.IntervalMs))
                {
                    Report(TypeState.Stopped, 0, i + 1, total, null);
                    return;
                }
            }
            Report(TypeState.Finished, 0, total, total, null);
        }

        bool TypeUnit(string unit)
        {
            char c = unit[0];
            if (c == '\n')
                return TypeNewline();
            if (c == '\t')
                return Tap(Native.VK_TAB, false);

            bool ok = options.Mode == InputMode.KeyStroke && unit.Length == 1 ? TypeKeyStroke(c) : SendText(unit);
            if (ok && options.EditorFix && AutoClosed.IndexOf(c) >= 0)
            {
                // Delete the closing bracket or quote the editor just added; the text brings its own.
                Thread.Sleep(8);
                ok = Tap(Native.VK_DELETE, true);
            }
            return ok;
        }

        bool TypeNewline()
        {
            if (options.Newline == NewlineMode.Ignore)
                return true;
            bool ok = true;
            if (options.EditorFix)
            {
                // Esc closes the completion popup, so Enter starts a new line instead of accepting a suggestion.
                ok = Tap(Native.VK_ESCAPE, false);
                Thread.Sleep(8);
            }
            ok = ok && (options.Newline == NewlineMode.ShiftEnter ? Chord(Native.VK_SHIFT, Native.VK_RETURN) : Tap(Native.VK_RETURN, false));
            if (options.EditorFix)
            {
                // Ctrl+[ outdents: undo the editor's auto-indent, the text's own leading spaces follow.
                Thread.Sleep(8);
                for (int i = 0; i < OutdentPresses && ok; i++)
                    ok = Chord(Native.VK_CONTROL, Native.VK_OEM_4);
                Thread.Sleep(8);
            }
            return ok;
        }

        // Presses the physical key for c (plus Shift etc.); characters the keyboard layout
        // has no key for, such as Chinese, are sent as Unicode instead.
        bool TypeKeyStroke(char c)
        {
            short scan = Native.VkKeyScanEx(c, layout);
            if (scan == -1)
                return SendText(c.ToString());
            int vk = scan & 0xFF;
            int mods = (scan >> 8) & 0xFF;
            if (vk == 0xFF || (mods & ~7) != 0)
                return SendText(c.ToString());

            bool shift = (mods & 1) != 0;
            if (char.IsLetter(c) && (Native.GetKeyState(Native.VK_CAPITAL) & 1) != 0)
                shift = !shift;
            var held = new List<int>();
            if (shift)
                held.Add(Native.VK_SHIFT);
            if ((mods & 2) != 0)
                held.Add(Native.VK_CONTROL);
            if ((mods & 4) != 0)
                held.Add(Native.VK_MENU);

            var down = new List<Native.INPUT>();
            foreach (int m in held)
                down.Add(KeyInput(m, false, false));
            down.Add(KeyInput(vk, false, false));
            if (!Send(down.ToArray()))
                return false;

            // Virtual machines and remote sessions drop keys that are released too quickly.
            Thread.Sleep(KeyHoldMs);
            var up = new List<Native.INPUT>();
            up.Add(KeyInput(vk, true, false));
            for (int i = held.Count - 1; i >= 0; i--)
                up.Add(KeyInput(held[i], true, false));
            return Send(up.ToArray());
        }

        bool Tap(int vk, bool extended)
        {
            return Send(KeyInput(vk, false, extended), KeyInput(vk, true, extended));
        }

        bool Chord(int modifier, int vk)
        {
            return Send(KeyInput(modifier, false, false), KeyInput(vk, false, false), KeyInput(vk, true, false), KeyInput(modifier, true, false));
        }

        static bool SendText(string text)
        {
            var inputs = new Native.INPUT[text.Length * 2];
            for (int i = 0; i < text.Length; i++)
            {
                inputs[i * 2] = CharInput(text[i], false);
                inputs[i * 2 + 1] = CharInput(text[i], true);
            }
            return Send(inputs);
        }

        Native.INPUT KeyInput(int vk, bool up, bool extended)
        {
            var input = new Native.INPUT();
            input.type = Native.INPUT_KEYBOARD;
            input.u.ki.wVk = (ushort)vk;
            input.u.ki.wScan = (ushort)Native.MapVirtualKeyEx((uint)vk, Native.MAPVK_VK_TO_VSC, layout);
            input.u.ki.dwFlags = (up ? Native.KEYEVENTF_KEYUP : 0)
                | (extended ? Native.KEYEVENTF_EXTENDEDKEY : 0)
                | (options.Mode == InputMode.KeyStroke ? Native.KEYEVENTF_SCANCODE : 0);
            input.u.ki.dwExtraInfo = Signature;
            return input;
        }

        static Native.INPUT CharInput(char c, bool up)
        {
            var input = new Native.INPUT();
            input.type = Native.INPUT_KEYBOARD;
            input.u.ki.wScan = c;
            input.u.ki.dwFlags = Native.KEYEVENTF_UNICODE | (up ? Native.KEYEVENTF_KEYUP : 0);
            input.u.ki.dwExtraInfo = Signature;
            return input;
        }

        static bool Send(params Native.INPUT[] inputs)
        {
            return Native.SendInput((uint)inputs.Length, inputs, InputSize) == inputs.Length;
        }

        // Turns the target's IME off so key presses are not composed into Chinese;
        // returns the IME window to switch back on afterwards, or zero if it was already off.
        static IntPtr CloseIme(IntPtr window)
        {
            IntPtr ime = Native.ImmGetDefaultIMEWnd(window);
            if (ime == IntPtr.Zero)
                return IntPtr.Zero;
            IntPtr open;
            if (Native.SendMessageTimeout(ime, Native.WM_IME_CONTROL, (IntPtr)Native.IMC_GETOPENSTATUS, IntPtr.Zero, Native.SMTO_ABORTIFHUNG, 200, out open) == IntPtr.Zero || open == IntPtr.Zero)
                return IntPtr.Zero;
            SetImeOpen(ime, false);
            return ime;
        }

        static void SetImeOpen(IntPtr ime, bool open)
        {
            IntPtr result;
            Native.SendMessageTimeout(ime, Native.WM_IME_CONTROL, (IntPtr)Native.IMC_SETOPENSTATUS, (IntPtr)(open ? 1 : 0), Native.SMTO_ABORTIFHUNG, 200, out result);
        }

        bool WaitForModifiers()
        {
            var watch = Stopwatch.StartNew();
            bool reported = false;
            while (AnyModifierDown())
            {
                if (cancelled || watch.ElapsedMilliseconds > 5000)
                    return false;
                if (!reported && watch.ElapsedMilliseconds > 300)
                {
                    Report(TypeState.WaitingKeys, 0, 0, 0, null);
                    reported = true;
                }
                Thread.Sleep(15);
            }
            return !cancelled;
        }

        static bool AnyModifierDown()
        {
            foreach (int vk in ModifierKeys)
                if ((Native.GetAsyncKeyState(vk) & 0x8000) != 0)
                    return true;
            return false;
        }

        bool TargetChanged()
        {
            IntPtr fg = Native.GetForegroundWindow();
            return fg != IntPtr.Zero && Native.GetAncestor(fg, Native.GA_ROOTOWNER) != targetRoot;
        }

        // Sleeps in short slices so Cancel takes effect quickly; false when cancelled.
        bool Pause(int ms)
        {
            var watch = Stopwatch.StartNew();
            while (!cancelled)
            {
                long left = ms - watch.ElapsedMilliseconds;
                if (left <= 0)
                    return true;
                Thread.Sleep((int)Math.Min(left, 50));
            }
            return false;
        }

        void Report(TypeState state, int seconds, int done, int total, string message)
        {
            Action<TypeProgress> handler = Progress;
            if (handler == null)
                return;
            var p = new TypeProgress();
            p.State = state;
            p.Seconds = seconds;
            p.Done = done;
            p.Total = total;
            p.Message = message;
            handler(p);
        }
    }
}
