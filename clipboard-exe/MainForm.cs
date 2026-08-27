using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace ClipboardExe;

/// <summary>
/// 主窗体：黑金深色 UI（对齐 Web 版配色令牌）+ 剪贴板监听宿主 + 数据目录初始化。
/// M2 骨架：中央占位区；M3 起为卡片墙 + 搜索条 + 标签栏。
/// </summary>
public class MainForm : Form
{
    private readonly string _dataDir;
    private readonly string _logPath;
    private bool _exiting; // 托盘"退出"才真正退出，点 X 只是最小化到托盘

    /// <summary>托盘图标（由 Program 注入，最小化时使用）。</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public NotifyIcon? TrayIcon { get; set; }

    public MainForm()
    {
        // 数据目录：exe 同目录 data/（决策 2026-08-27：便携式，整个文件夹拷走即迁移）
        var exeDir = Path.GetDirectoryName(Application.ExecutablePath) ?? ".";
        _dataDir = Path.Combine(exeDir, "data");
        try { Directory.CreateDirectory(_dataDir); } catch { /* 只读目录等极端情况，M2 不阻塞启动 */ }
        _logPath = Path.Combine(_dataDir, "clipboard-exe.log");

        InitUi();
        WriteStartupLog();
    }

    // ---------------- UI ----------------

    private void InitUi()
    {
        Text = $"Clipboard v{Program.AppVersion}";
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(1000, 680);
        MinimumSize = new Size(640, 480);
        BackColor = Color.FromArgb(0x1A, 0x1A, 0x1A);   // --bg
        ForeColor = Color.FromArgb(0xDA, 0xDA, 0xDA);   // --text
        Icon = IconFactory.Create();

        // 中央占位：版本 + 状态（M3 替换为卡片墙）
        var placeholder = new Label
        {
            Text = "Clipboard 剪贴板工具\n\nM2 骨架 — 剪贴板监听已就绪\n数据目录: " + _dataDir + "\n版本: v" + Program.AppVersion + " (" + Program.GitCommit + ")",
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleCenter,
            Dock = DockStyle.Fill,
            Font = new Font("Microsoft YaHei UI", 12f),
            ForeColor = Color.FromArgb(0x84, 0x84, 0x84), // --muted
            Padding = new Padding(24),
        };
        Controls.Add(placeholder);
    }

    // ---------------- 剪贴板监听（宿主窗口） ----------------

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyDarkTitleBar();
        if (!ClipboardWatcher.Start(Handle))
        {
            AppLog.Info("AddClipboardFormatListener failed");
        }
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        ClipboardWatcher.Stop(Handle);
        base.OnHandleDestroyed(e);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == NativeMethods.WM_CLIPBOARDUPDATE)
        {
            ClipboardWatcher.OnClipboardUpdate();
        }
        else if (m.Msg == NativeMethods.WM_SHOW_MAIN)
        {
            ShowFromTray();
        }
        base.WndProc(ref m);
    }

    private void ApplyDarkTitleBar()
    {
        // DWMWA_USE_IMMERSIVE_DARK_MODE = 20（Win10 1809+ / Win11）
        int useDark = 1;
        NativeMethods.DwmSetWindowAttribute(Handle, 20, ref useDark, sizeof(int));
    }

    // ---------------- 托盘交互 ----------------

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (WindowState == FormWindowState.Minimized)
        {
            Hide();
            TrayIcon?.ShowBalloonTip(1500, "Clipboard", "已最小化到托盘（双击图标恢复）", ToolTipIcon.Info);
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_exiting)
        {
            // 点 X = 最小化到托盘；托盘菜单"退出"才真正退出
            e.Cancel = true;
            WindowState = FormWindowState.Minimized;
            Hide();
            return;
        }
        base.OnFormClosing(e);
    }

    public void ShowFromTray()
    {
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
    }

    public void ExitApp()
    {
        _exiting = true;
        Application.Exit();
    }

    // ---------------- 启动日志（版本指纹） ----------------

    private void WriteStartupLog()
    {
        // 与 Web 版 server.mjs 版本指纹同精神：实例身份可追溯
        AppLog.Info($"startup v{Program.AppVersion} ({Program.GitCommit}) data={_dataDir}");
    }
}
