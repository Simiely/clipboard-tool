using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using ClipboardExe.Services;

namespace ClipboardExe;

/// <summary>
/// 入口装配（对齐 Web 版 server.mjs 入口职责）：
///  - 单实例互斥（已有实例则退出）
///  - 版本指纹（AppVersion/GitCommit，日志首行，实例身份可追溯）
///  - 全局异常兜底（记录不退出，对齐 Web 版进程级兜底语义）
///  - 数据目录：exe 同目录 data/（便携式）
/// </summary>
public partial class App : Application
{
    public const string AppName = "clipboard-tool";

    /// <summary>版本号（csproj Version=0.7.0 → 程序集版本三段）。</summary>
    public static string AppVersion { get; } = ReadVersion();

    /// <summary>Git commit（发布时替换为实际 commit；dev = 未发布）。</summary>
    public static string GitCommit { get; set; } = "dev";

    /// <summary>数据目录（exe 同目录 data/）。</summary>
    public static string DataDir { get; private set; } = ".";

    private const string MutexName = "ClipboardTool_SingleInstance_7e2c1a9f";
    private Mutex? _mutex;
    private bool _mutexOwned;
    private TrayIconService? _tray;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 强制软件渲染：WPF 单文件发布（PublishSingleFile）下，硬件渲染线程在含
        // DropShadowEffect 的视觉树里会静默崩溃（窗口客户区变白、内容不绘制）。
        // 剪贴板工具对渲染性能无要求，软件渲染稳定可靠。
        // 必须在任何视觉对象创建之前设置。
        RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;

        // 自检模式（--selftest）：跑数据层规则断言后退出，不启动主窗（发版前健康检查）。
        // 置于单实例检查之前：自检与主程序互不干扰，可随时并行跑（对齐 Web 版 node 测试脚本思路）。
        if (e.Args.Contains("--selftest"))
        {
            Environment.Exit(Services.SelfTest.Run());
            return;
        }

        // 单实例：已有实例则直接退出（不走 WPF 生命周期，避免 OnExit 释放未拥有 mutex 崩溃）
        _mutex = new Mutex(true, MutexName, out var createdNew);
        if (!createdNew)
        {
            Environment.Exit(0);
            return;
        }
        _mutexOwned = true;

        // 数据目录：exe 同目录 data/（便携式，整个文件夹拷走即迁移）
        var exeDir = Path.GetDirectoryName(Environment.ProcessPath) ?? ".";
        DataDir = Path.Combine(exeDir, "data");

        // 日志：首行版本指纹（对齐 Web 版"实例身份可追溯"约定）
        AppLog.Init(DataDir, $"clipboard v{AppVersion} ({GitCommit})");
        AppLog.Info("startup");

        // 全局异常兜底：记录不退出（数据全落盘 JSON 原子写，进程存活比崩溃重启更友好）
        DispatcherUnhandledException += (_, ex) => AppLog.Info("DispatcherUnhandledException: " + ex.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, ex) => AppLog.Info("UnhandledException: " + ex.ExceptionObject);

        // 装配主窗体 + 托盘
        var settings = Settings.Load(DataDir);
        MainWindow? main = null;
        _tray = new TrayIconService("剪贴板", () => { main?.ReallyExit(); return true; });
        main = new MainWindow(settings, _tray);
        main.Closed += (_, _) => _tray?.Dispose();
        main.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_mutexOwned)
        {
            try { _mutex?.ReleaseMutex(); } catch { /* 释放失败不影响退出 */ }
        }
        base.OnExit(e);
    }

    private static string ReadVersion()
    {
        try
        {
            var v = Assembly.GetExecutingAssembly().GetName().Version;
            return v == null ? "0.7.0" : $"{v.Major}.{v.Minor}.{v.Build}";
        }
        catch { return "0.7.0"; }
    }
}
