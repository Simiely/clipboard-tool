using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
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

        // 单实例：已有实例 → 把它的主窗口拉到前台后退出（不静默退出——静默会让用户
        // 双击 exe 毫无反应，误判为"打不开/崩溃"。参考微软 Technet《Create a single
        // instance desktop application》：遍历同名进程对 MainWindowHandle 做
        // ShowWindowAsync(SW_RESTORE) + SetForegroundWindow）。
        _mutex = new Mutex(true, MutexName, out var createdNew);
        if (!createdNew)
        {
            ActivateExistingInstance();
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

#if WPFMCP_INSPECTOR
        // WpfVisualTreeMcp Inspector (self-hosted, DEBUG-only "AI 眼睛")：让外部 MCP/CLI
        // 经 named pipe 读取真实 visual tree / 依赖属性 / 模拟点击，做自动视觉验收。
        // 仅在装了 WpfVisualTreeMcp 的 Debug 构建启用（见 csproj），Release 产物不含此段；
        // 初始化失败只记日志，绝不影响主流程（AI 眼睛是辅助，app 本身不依赖它）。
        try
        {
            WpfVisualTreeMcp.Inspector.InspectorService.Initialize(Environment.ProcessId);
            AppLog.Info("inspector-ready");
        }
        catch (Exception inspectorEx) { AppLog.Info("inspector-init-failed: " + inspectorEx); }
#endif

        // 全局异常兜底：记录且不让进程退出（数据全落盘 JSON 原子写，进程存活比崩溃重启更友好）。
        // 必须设置 e.Handled=true，否则 WPF 仍视异常为未处理而关闭整个应用（"闪退"）。
        DispatcherUnhandledException += (_, ex) =>
        {
            AppLog.Info("DispatcherUnhandledException: " + ex.Exception);
            ex.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, ex) => AppLog.Info("UnhandledException: " + ex.ExceptionObject);

        // 装配主窗体 + 托盘。启动期同步异常必须在装配段就地兜住并显式弹错：
        // DispatcherUnhandledException 只对消息循环内的异常生效，OnStartup 里抛出的
        // 异常会直接走 AppDomain 未处理路径——不弹错的话用户只看到进程消失，
        // 再次误判"打不开/闪退"。
        try
        {
            var settings = Settings.Load(DataDir);
            MainWindow? main = null;
            _tray = new TrayIconService("剪贴板", () => { main?.ReallyExit(); return true; });
            main = new MainWindow(settings, _tray);
            main.Closed += (_, _) => _tray?.Dispose();
            main.Show();
        }
        catch (Exception startupEx)
        {
            AppLog.Info("startup-failed: " + startupEx);
            try { _tray?.Dispose(); } catch { /* 托盘释放失败不掩盖主错误 */ }
            try
            {
                MessageBox.Show(
                    "clipboard-tool 启动失败：\n\n" + startupEx.Message +
                    "\n\n详细信息已写入 " + Path.Combine(DataDir, "clipboard-exe.log"),
                    "clipboard-tool", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch { /* 弹窗失败也不能再抛 */ }
            Shutdown(-1);
            return;
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_mutexOwned)
        {
            try { _mutex?.ReleaseMutex(); } catch { /* 释放失败不影响退出 */ }
        }
        base.OnExit(e);
    }

    [DllImport("user32.dll")]
    private static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    private const int SwRestore = 9;

    /// <summary>已有实例在跑：把它的主窗口恢复并置前（若主窗收进托盘则无可见句柄，尽力而为）。</summary>
    private static void ActivateExistingInstance()
    {
        try
        {
            var me = Environment.ProcessId;
            foreach (var p in Process.GetProcessesByName("Clipboard"))
            {
                if (p.Id == me) continue;
                var h = p.MainWindowHandle;
                if (h == IntPtr.Zero) continue; // 窗口已 Hide 到托盘，无法从外部 Show
                ShowWindowAsync(h, SwRestore);
                SetForegroundWindow(h);
                break;
            }
        }
        catch { /* 激活失败不影响退出 */ }
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
