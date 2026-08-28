using System;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace ClipboardExe;

internal static class Program
{
    // 版本指纹：AppVersion 与 csproj <Version> 同步；GitCommit 发布时替换为实际 commit（dev = 未发布）
    public const string AppVersion = "0.8.0";
    public const string GitCommit = "dev";

    private const string MutexName = "ClipboardExe_SingleInstance_9f2c4d7b";
    private static NotifyIcon? _tray;
    private static MainForm? _mainForm;

    [STAThread]
    private static void Main()
    {
        // 全局异常兜底：写日志不静默（崩溃也能追溯根因）
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => AppLog.Info("ThreadException: " + e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => AppLog.Info("UnhandledException: " + e.ExceptionObject);

        try
        {
            // 单实例：已有实例则唤醒其主窗体后退出
            using var mutex = new Mutex(true, MutexName, out bool createdNew);
            if (!createdNew)
            {
                NativeMethods.PostMessage(new IntPtr(0xFFFF), NativeMethods.WM_SHOW_MAIN, IntPtr.Zero, IntPtr.Zero);
                return;
            }

            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            // 官方深色模式：菜单/滚动条/对话框等系统绘制部分整体深色（.NET 9 API，成熟方案非自绘）
            Application.SetColorMode(SystemColorMode.Dark);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            _mainForm = new MainForm();
            _tray = BuildTray(_mainForm);
            _mainForm.TrayIcon = _tray;

            Application.Run(_mainForm);
        }
        catch (Exception ex)
        {
            AppLog.Info("FATAL: " + ex);
            throw;
        }
    }

    private static NotifyIcon BuildTray(MainForm form)
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("显示主窗口", null, (_, _) => form.ShowFromTray());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => form.ExitApp());

        var tray = new NotifyIcon
        {
            Icon = IconFactory.Create(),
            Text = $"Clipboard v{AppVersion}",
            Visible = true,
            ContextMenuStrip = menu,
        };
        tray.DoubleClick += (_, _) => form.ShowFromTray();
        return tray;
    }
}

/// <summary>
/// 极简日志：追加写 data/clipboard-exe.log（与数据同目录，便于排查）。
/// </summary>
internal static class AppLog
{
    private static readonly object Lock = new();

    public static void Info(string message)
    {
        try
        {
            var exeDir = Path.GetDirectoryName(Application.ExecutablePath) ?? ".";
            var logPath = Path.Combine(exeDir, "data", "clipboard-exe.log");
            lock (Lock)
            {
                File.AppendAllText(logPath,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // 日志失败不阻塞程序
        }
    }
}
