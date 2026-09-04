// Services/TrayIconService.cs - 托盘常驻（对齐 Web 版语义：点 X/最小化 → 托盘；托盘"退出"才真正退出）
// 右键菜单：固定暗色渲染（对齐主窗口暗色 UI，见 TrayDarkMenu.cs）——默认 WinForms ContextMenuStrip
// 是浅色白底，与主程序 #1A1A1A 暗色割裂；用自定义 ToolStripProfessionalRenderer 强制暗色，不随系统切。
using System.Drawing;
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
        var menu = new ContextMenuStrip
        {
            Renderer = new TrayDarkRenderer(),      // 固定暗色渲染（见 TrayDarkMenu.cs）
            Font = new Font("Segoe UI", 9f),
            ShowImageMargin = false,                // 无图标列：去掉左侧浅色渐变空列，整条菜单等宽
        };
        var showItem = new ToolStripMenuItem("显示主窗口");
        showItem.Click += (_, _) => ShowMain();
        menu.Items.Add(showItem);
        menu.Items.Add(new ToolStripSeparator());
        var exitItem = new ToolStripMenuItem("退出");
        exitItem.Click += (_, _) => RequestExit();
        menu.Items.Add(exitItem);
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
