// Controls/WindowExtensions.cs - 窗口级平台行为扩展（M1 抽离：防 MainWindow 膨胀，M2 弹窗可复用）
// 沉浸式深色标题栏（Win10 1809+，DWM）——从 MainWindow.xaml.cs 等价搬移，行为不变。
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace ClipboardExe.Controls;

public static class WindowExtensions
{
    /// <summary>开启沉浸式深色标题栏（Win10 1809+）。旧系统静默忽略。</summary>
    public static void EnableImmersiveDarkTitleBar(this Window window)
    {
        try
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            var useDark = 1;
            _ = DwmSetWindowAttribute(hwnd, 20, ref useDark, sizeof(int));
        }
        catch { /* 旧系统忽略 */ }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);
}
