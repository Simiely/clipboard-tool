using System;

namespace ClipboardExe;

/// <summary>
/// 剪贴板监听封装：Win32 AddClipboardFormatListener。
/// 不依赖窗口焦点——程序在后台/托盘也能收到复制事件（比浏览器 clipboardchange 强）。
/// 宿主窗口（MainForm）负责注册监听 HWND，并把 WM_CLIPBOARDUPDATE 转发到这里。
/// M2 骨架：仅记录日志；M4 落存储（文本/链接/图片自动捕获）。
/// </summary>
public static class ClipboardWatcher
{
    public const int WM_CLIPBOARDUPDATE = NativeMethods.WM_CLIPBOARDUPDATE;

    /// <summary>注册监听（宿主窗口 Handle 创建后调用）。返回是否成功。</summary>
    public static bool Start(IntPtr hwnd) => NativeMethods.AddClipboardFormatListener(hwnd);

    /// <summary>注销监听（宿主窗口 Handle 销毁前调用）。</summary>
    public static bool Stop(IntPtr hwnd) => NativeMethods.RemoveClipboardFormatListener(hwnd);

    /// <summary>由宿主窗口 WndProc 转发剪贴板变更消息。</summary>
    public static void OnClipboardUpdate()
    {
        // M2：先只记录；内容捕获/去重/落盘在 M4
        AppLog.Info("clipboard changed (capture in M4)");
    }
}
