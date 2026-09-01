// Services/ClipboardWatcher.cs - 原生剪贴板监听（对齐 Web 版 navigator.clipboard "clipboardchange" 语义）
//  - AddClipboardFormatListener + HwndSource.AddHook 处理 WM_CLIPBOARDUPDATE(0x031D)
//  - 100ms 防抖：一次真实复制可能连发多条 WM，去重后只触发一次（DispatcherTimer 重启式防抖）
//  - 800ms 来源抑制：卡片复制/写剪贴板后调用 Suppress，窗口期内不触发（对齐 suppressAutoPasteUntil）
//  - Paused：窗口失活/最小化时暂停捕获（对齐 Web 版"仅前台页面派发"语义）
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Threading;

namespace ClipboardExe.Services;

public sealed class ClipboardWatcher : IDisposable
{
    private const int WmClipboardUpdate = 0x031D;

    [DllImport("user32.dll")]
    private static extern bool AddClipboardFormatListener(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

    private readonly IntPtr _hwnd;
    private HwndSource? _source;
    private readonly DispatcherTimer _debounce;
    private long _suppressUntil;   // 来源抑制截止（UtcNow ms），对齐 Date.now() 语义
    private bool _disposed;

    /// <summary>窗口失活/最小化时置 true → 暂停捕获（对齐 Web 前台语义）。</summary>
    public bool Paused { get; set; }

    /// <summary>剪贴板有新内容（已过防抖/抑制/暂停检查）。</summary>
    public event Action? ClipboardChanged;

    public ClipboardWatcher(IntPtr hwnd)
    {
        _hwnd = hwnd;
        _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            if (Paused || IsSuppressed) return;
            ClipboardChanged?.Invoke();
        };
    }

    /// <summary>注册监听（OnSourceInitialized 后调用）。</summary>
    public void Attach()
    {
        AddClipboardFormatListener(_hwnd);
        _source = HwndSource.FromHwnd(_hwnd);
        _source?.AddHook(WndProc);
        AppLog.Info("clipboard-watcher: attached");
    }

    /// <summary>来源抑制：本次写剪贴板引起的 WM 不触发自动弹窗（对齐 suppressAutoPasteUntil = now + 800）。</summary>
    public void Suppress(int ms = 800)
        => _suppressUntil = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + ms;

    public bool IsSuppressed
        => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() < _suppressUntil;

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmClipboardUpdate)
        {
            // 防抖：重启计时器，100ms 内无新消息才触发（一次真实复制连发多条 WM 只处理一次）
            _debounce.Stop();
            _debounce.Start();
            handled = false; // 不吞消息，让其他监听者也可见
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { RemoveClipboardFormatListener(_hwnd); } catch { /* 注销失败不影响退出 */ }
        try { _source?.RemoveHook(WndProc); } catch { /* 同上 */ }
        _debounce.Stop();
        AppLog.Info("clipboard-watcher: disposed");
    }
}
