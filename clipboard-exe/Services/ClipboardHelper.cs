// Services/ClipboardHelper.cs - 健壮剪贴板写入
// 真实根因（已用 --selftest 探针复现 + 上游权威确认 dotnet/wpf #9901）：
//   System.Windows.Clipboard.SetText / SetDataObject(copy:true) 内部 OleSetClipboard→OleFlushClipboard(Flush)，
//   Flush 在剪贴板被外部进程（TeraCopy / Skype / PowerToys / TextInputHost 触摸键盘 / Win+V 历史 / RDP 剪贴板桥）
//   占用时抛 COMException 0x800401D0 (CLIPBRD_E_CANT_OPEN_CLIPBOARD)。WPF 自带重试是同线程 Thread.Sleep，
//   救不了「外部常驻占用」——故上一版「泵 dispatcher + 重试」仍然失败。
// 权威修复（dotnet/wpf #9901 + StackOverflow 多位 maintainer 验证）：
//   改用 System.Windows.Forms.Clipboard.SetDataObject(data, copy, retryTimes, retryDelay) ——
//   它自带重试循环(10×100ms) 且不受 WPF 的 Flush 路径影响，是报告中最稳定可用的写法；copy:true 保留数据直到进程退出
//   （剪贴板工具需要：复制后即使本程序关闭也应可粘贴）。兜底仍尝试 WPF 路径（极少数 WinForms.Clipboard 不可用环境）。
// 注：浏览器端无此问题（navigator.clipboard 走系统服务），桌面端必须处理。
using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SWF = System.Windows.Forms;

namespace ClipboardExe.Services;

public static class ClipboardHelper
{
    private const int RetryTimes = 10;
    private const int RetryDelayMs = 100;

    // ---- 诊断：定位剪贴板占用方（外部进程常驻占用即 0x800401D0 根因） ----
    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetOpenClipboardWindow();
    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder text, int count);
    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowTextLength(IntPtr hWnd);
    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    /// <summary>返回当前占用剪贴板的窗口/进程信息；无人占用返回提示。用于复制失败时定位根因。</summary>
    private static string GetClipboardOwnerInfo()
    {
        try
        {
            var hwnd = GetOpenClipboardWindow();
            if (hwnd == IntPtr.Zero) return "（无进程占用剪贴板）";
            var len = GetWindowTextLength(hwnd);
            var sb = new System.Text.StringBuilder(len + 1);
            GetWindowText(hwnd, sb, sb.Capacity);
            GetWindowThreadProcessId(hwnd, out var pid);
            var procName = "";
            try { procName = System.Diagnostics.Process.GetProcessById((int)pid)?.ProcessName ?? ""; }
            catch { /* 进程已退出等 */ }
            return $"占用方 hwnd=0x{hwnd:X} 窗口='{sb}' 进程={procName}(pid={pid})";
        }
        catch (Exception ex) { return "（诊断失败：" + ex.Message + "）"; }
    }

    /// <summary>复制纯文本（对齐 Web navigator.clipboard.writeText；同时写 UnicodeText+Text 保证中文/emoji 无损）。</summary>
    public static void SetText(string text)
    {
        var d = new SWF.DataObject();
        d.SetData(SWF.DataFormats.UnicodeText, text ?? "");
        d.SetData(SWF.DataFormats.Text, text ?? "");
        SetViaWinForms(d, copy: true);
    }

    /// <summary>复制图片（对齐 Web copyImageToClipboard）。BitmapSource → System.Drawing.Bitmap 后走 WinForms 路径（绕开 WPF Flush）。</summary>
    public static void SetImage(BitmapSource image)
    {
        if (image == null) throw new ArgumentNullException(nameof(image));
        using var bmp = BitmapSourceToBitmap(image);
        SetViaWinForms(new SWF.DataObject(SWF.DataFormats.Bitmap, bmp), copy: true);
    }

    /// <summary>复制任意 DataObject（富文本 CopyRich 传入的 HTML+Text 包）。</summary>
    public static void SetDataObject(DataObject data)
        => SetViaWinForms(ToWinForms(data), copy: true);

    // ---- 核心：WinForms.Clipboard（自带重试，绕开 WPF Flush） ----
    private static void SetViaWinForms(SWF.DataObject data, bool copy)
    {
        try
        {
            SWF.Clipboard.SetDataObject(data, copy, RetryTimes, RetryDelayMs);
            return;
        }
        catch (Exception ex)
        {
            AppLog.Info($"clipboard write (WinForms) failed: {ex.GetType().Name}(0x{unchecked((uint)ex.HResult):X8}): {ex.Message} | 占用方: {GetClipboardOwnerInfo()}");
        }
        // 兜底：WPF 路径（极少数环境 WinForms.Clipboard 不可用）
        try
        {
            var wpf = new DataObject();
            foreach (var fmt in data.GetFormats())
                wpf.SetData(fmt, data.GetData(fmt));
            Clipboard.SetDataObject(wpf, copy);
        }
        catch (Exception ex)
        {
            AppLog.Info($"clipboard write (WPF fallback) failed: {ex} | 占用方: {GetClipboardOwnerInfo()}");
            throw new InvalidOperationException(
                "系统剪贴板无法写入：可能被剪贴板管理器/系统历史占用，或当前进程无剪贴板访问权限。占用方=" + GetClipboardOwnerInfo(), ex);
        }
    }

    /// <summary>WPF DataObject → WinForms DataObject（逐格式搬运，保留 UnicodeText/Text/Html 等）。</summary>
    private static SWF.DataObject ToWinForms(DataObject wpf)
    {
        var wf = new SWF.DataObject();
        foreach (var fmt in wpf.GetFormats())
            wf.SetData(fmt, wpf.GetData(fmt));
        return wf;
    }

    /// <summary>BitmapSource → System.Drawing.Bitmap（先统一转 Pbgra32 再 BGRA 直拷，无 unsafe）。</summary>
    private static System.Drawing.Bitmap BitmapSourceToBitmap(BitmapSource source)
    {
        var converted = new FormatConvertedBitmap(source, PixelFormats.Pbgra32, null, 0);
        var bmp = new System.Drawing.Bitmap(
            converted.PixelWidth, converted.PixelHeight,
            System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        var rect = new System.Drawing.Rectangle(0, 0, converted.PixelWidth, converted.PixelHeight);
        var bd = bmp.LockBits(rect, System.Drawing.Imaging.ImageLockMode.WriteOnly,
            System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        converted.CopyPixels(new Int32Rect(0, 0, converted.PixelWidth, converted.PixelHeight),
            bd.Scan0, bd.Height * bd.Stride, bd.Stride);
        bmp.UnlockBits(bd);
        return bmp;
    }
}
