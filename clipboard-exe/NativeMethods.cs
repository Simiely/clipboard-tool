using System;
using System.Runtime.InteropServices;

namespace ClipboardExe;

/// <summary>
/// Win32 P/Invoke 集合：剪贴板监听 / 深色标题栏 / 单实例唤醒。
/// </summary>
internal static class NativeMethods
{
    // 单实例唤醒消息（RegisterWindowMessage 注册，避免广播打扰其他窗口）
    public static readonly int WM_SHOW_MAIN = RegisterWindowMessage("ClipboardExe_ShowMain");

    // 剪贴板监听
    public const int WM_CLIPBOARDUPDATE = 0x031D;

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool AddClipboardFormatListener(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern int RegisterWindowMessage(string lpString);

    // 沉浸式深色标题栏（Win10 1809+ / Win11）；attr = DWMWA_USE_IMMERSIVE_DARK_MODE = 20
    [DllImport("dwmapi.dll", PreserveSig = true)]
    public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    // 图标句柄释放（IconFactory 用）
    [DllImport("user32.dll")]
    public static extern bool DestroyIcon(IntPtr hIcon);
}
