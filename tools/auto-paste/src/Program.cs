using System;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

[assembly: AssemblyTitle("自动粘贴")]
[assembly: AssemblyProduct("自动粘贴")]
[assembly: AssemblyDescription("模拟键盘逐字输入文字，用于无法直接粘贴的地方")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

namespace AutoPaste
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            bool firstInstance;
            using (var mutex = new Mutex(true, "AutoPaste.SingleInstance.3f9c2a", out firstInstance))
            {
                if (!firstInstance)
                {
                    // Already running: bring that window forward instead of starting a second copy.
                    Native.PostMessage((IntPtr)0xFFFF, MainForm.ShowMeMessage, IntPtr.Zero, IntPtr.Zero);
                    return;
                }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.ThreadException += (s, e) => ShowError(e.Exception);
                AppDomain.CurrentDomain.UnhandledException += (s, e) => ShowError(e.ExceptionObject as Exception);
                Application.Run(new MainForm());
            }
        }

        static void ShowError(Exception ex)
        {
            MessageBox.Show(ex != null ? ex.ToString() : "未知错误", "自动粘贴出错了", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
