// Services/TrayIconService.cs - 托盘常驻（对齐 Web 版语义：点 X/最小化 → 托盘；托盘"退出"才真正退出）
using System.Windows.Forms;

namespace ClipboardExe.Services;

public sealed class TrayIconService : IDisposable
{
    private readonly NotifyIcon _tray;
    private readonly Func<bool> _exitHandler; // 返回 true 才真正退出

    public TrayIconService(string tooltip, Func<bool> exitHandler)
    {
        _exitHandler = exitHandler;
        _tray = new NotifyIcon
        {
            Text = tooltip,
            Icon = System.Drawing.SystemIcons.Application,
            Visible = true,
        };
        var menu = new ContextMenuStrip();
        menu.Items.Add("显示主窗口", null, (_, _) => ShowMain());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => RequestExit());
        _tray.ContextMenuStrip = menu;
        _tray.DoubleClick += (_, _) => ShowMain();
    }

    public event Action? ShowMainRequested;

    private void ShowMain() => ShowMainRequested?.Invoke();

    private void RequestExit()
    {
        if (_exitHandler()) _tray.Visible = false;
    }

    public void Dispose()
    {
        _tray.Visible = false;
        _tray.Dispose();
    }
}
