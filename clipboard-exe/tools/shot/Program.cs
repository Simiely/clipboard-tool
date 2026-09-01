using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Automation;

class Shot
{
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc lpEnumFunc, IntPtr lParam);
    [DllImport("user32.dll")] static extern int GetWindowThreadProcessId(IntPtr h, out int pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder sb, int n);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr ins, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] static extern bool MoveWindow(IntPtr h, int x, int y, int cx, int cy, bool repaint);
    [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] static extern bool ClientToScreen(IntPtr h, ref POINT p);
    [DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] static extern void mouse_event(uint flags, uint dx, uint dy, uint data, IntPtr extra);
    [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(POINT p);
    [DllImport("user32.dll")] static extern int GetClassName(IntPtr h, StringBuilder sb, int n);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowTextW(IntPtr h, StringBuilder sb, int n);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] static extern IntPtr ChildWindowFromPointEx(IntPtr h, POINT p, uint flags);

    const uint SWP_NOZORDER = 0x0004;
    const uint SWP_SHOWWINDOW = 0x0040;
    const uint SWP_NOMOVE = 0x0002;
    const uint SWP_NOSIZE = 0x0001;
    const int SW_RESTORE = 9, SW_SHOW = 5;
    const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    const uint MOUSEEVENTF_LEFTUP = 0x0004;
    const uint CWP_SKIPINVISIBLE = 0x0001;
    const uint CWP_SKIPDISABLED = 0x0002;
    const uint CWP_SKIPTRANSPARENT = 0x0004;
    static readonly IntPtr HWND_TOP = new IntPtr(0);
    static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);

    [StructLayout(LayoutKind.Sequential)] struct RECT { public int L, T, R, B; }
    [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }

    delegate bool EnumProc(IntPtr h, IntPtr lp);

    /// <summary>找到 procName 进程的主可见窗口（hwnd + 客户区 clientRect）。</summary>
    static IntPtr FindWindow(string procName, out System.Diagnostics.Process proc)
    {
        proc = null!;
        var procs = System.Diagnostics.Process.GetProcessesByName(procName);
        if (procs.Length == 0) return IntPtr.Zero;
        proc = procs[0];
        int pid = proc.Id;
        IntPtr target = IntPtr.Zero;
        var sb = new StringBuilder(256);
        EnumWindows((h, _) =>
        {
            GetWindowThreadProcessId(h, out int wp);
            if (wp != pid) return true;
            GetWindowText(h, sb, sb.Capacity);
            GetWindowRect(h, out var r);
            if (IsWindowVisible(h) && (r.R - r.L) > 200) target = h;
            return true;
        }, IntPtr.Zero);
        return target;
    }

    static int Main(string[] args)
    {
        // 子命令: click <procName> <x> <y>  — 客户区相对坐标点击（开发期冒烟验证用）
        if (args.Length > 0 && args[0] == "click") return DoClick(args);
        // 子命令: probe <procName> <x> <y>  — 输出该坐标处控件类名+标题（调试用）
        if (args.Length > 0 && args[0] == "probe") return DoProbe(args);
        // 子命令: clickby <procName> <name>  — UIAutomation 按 Name 找按钮 Invoke（推荐：WPF 不暴露 Win32 子窗口）
        if (args.Length > 0 && args[0] == "clickby") return DoClickByName(args);
        // 子命令: list <procName> [filter]  — 枚举窗口内所有控件 Name（UI 状态精确验证）
        if (args.Length > 0 && args[0] == "list") return DoList(args);

        // 默认: 截图。args: [procName] [outPath] [targetW?]
        var procName = args.Length > 0 ? args[0] : "Clipboard";
        var outPath = args.Length > 1 ? args[1] : "shot.png";
        int targetW = args.Length > 2 ? int.Parse(args[2]) : 0;

        var proc = System.Diagnostics.Process.GetProcessesByName(procName);
        if (proc.Length == 0) { Console.WriteLine("ERR no proc"); return 1; }
        var target = FindWindow(procName, out _);
        if (target == IntPtr.Zero) { Console.WriteLine("ERR no window"); return 1; }

        GetClientRect(target, out var cr);
        Console.WriteLine("before client: " + cr.L + "," + cr.T + " " + (cr.R-cr.L) + "x" + (cr.B-cr.T));

        if (targetW > 0)
        {
            GetWindowRect(target, out var wr);
            var nw = targetW;
            var nh = wr.B - wr.T;
            MoveWindow(target, wr.L, wr.T, nw, nh, true);
            Thread.Sleep(800);
            GetClientRect(target, out cr);
            Console.WriteLine("after client: " + cr.L + "," + cr.T + " " + (cr.R-cr.L) + "x" + (cr.B-cr.T));
        }

        ShowWindow(target, SW_RESTORE);
        ShowWindow(target, SW_SHOW);
        Thread.Sleep(300);

        GetClientRect(target, out var rect);
        int w = rect.R - rect.L, h = rect.B - rect.T;
        using var bmp = new Bitmap(w, h);
        using (var g = Graphics.FromImage(bmp))
        {
            var hdc = g.GetHdc();
            var ok = PrintWindow(target, hdc, 2);
            Console.WriteLine("PrintWindow=" + ok + " size=" + w + "x" + h);
            g.ReleaseHdc(hdc);
        }
        bmp.Save(outPath, ImageFormat.Png);
        var bitbltPath = outPath.Replace(".png", ".bitblt.png");
        bmp.Save(bitbltPath, ImageFormat.Png);
        Console.WriteLine("OK -> " + outPath);
        return 0;
    }

    /// <summary>客户区相对坐标点击（临时置顶避免被遮挡）。</summary>
    static int DoClick(string[] args)
    {
        if (args.Length < 4) { Console.WriteLine("用法: shot click <procName> <x> <y>"); return 1; }
        var procName = args[1];
        if (!int.TryParse(args[2], out int x) || !int.TryParse(args[3], out int y))
        { Console.WriteLine("ERR: x/y must be int"); return 1; }
        var target = FindWindow(procName, out _);
        if (target == IntPtr.Zero) { Console.WriteLine("ERR no window"); return 1; }
        SetWindowPos(target, HWND_TOP, 0, 0, 0, 0, SWP_NOZORDER | SWP_SHOWWINDOW);
        SetForegroundWindow(target);
        Thread.Sleep(150);
        var pt = new POINT { X = x, Y = y };
        ClientToScreen(target, ref pt);
        SetCursorPos(pt.X, pt.Y);
        Thread.Sleep(80);
        mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, IntPtr.Zero);
        Thread.Sleep(40);
        mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, IntPtr.Zero);
        SetWindowPos(target, HWND_NOTOPMOST, 0, 0, 0, 0, SWP_NOZORDER | SWP_NOMOVE | SWP_NOSIZE);
        Console.WriteLine($"clicked ({x},{y}) -> screen ({pt.X},{pt.Y})");
        return 0;
    }

    /// <summary>探测 (x,y) 处最深子窗口信息（调试用）。</summary>
    static int DoProbe(string[] args)
    {
        if (args.Length < 4) { Console.WriteLine("用法: shot probe <procName> <x> <y>"); return 1; }
        var procName = args[1];
        if (!int.TryParse(args[2], out int x) || !int.TryParse(args[3], out int y))
        { Console.WriteLine("ERR: x/y must be int"); return 1; }
        var target = FindWindow(procName, out _);
        if (target == IntPtr.Zero) { Console.WriteLine("ERR no window"); return 1; }
        var pt = new POINT { X = x, Y = y };
        ClientToScreen(target, ref pt);
        var hwnd = target;
        for (int depth = 0; depth < 12; depth++)
        {
            var child = ChildWindowFromPointEx(hwnd, pt, CWP_SKIPINVISIBLE | CWP_SKIPDISABLED | CWP_SKIPTRANSPARENT);
            if (child == IntPtr.Zero || child == hwnd) break;
            hwnd = child;
        }
        var cls = new StringBuilder(64); GetClassName(hwnd, cls, 64);
        var txt = new StringBuilder(256); GetWindowTextW(hwnd, txt, 256);
        Console.WriteLine($"screen ({pt.X},{pt.Y}) hwnd={hwnd:X} class={cls} text='{txt}'");
        return 0;
    }

    /// <summary>UIAutomation 按 Name 找按钮并 Invoke（推荐：WPF 不暴露 Win32 子窗口，坐标点击不可靠）。
    /// 用法: shot clickby &lt;procName&gt; &lt;buttonName&gt;</summary>
    static int DoClickByName(string[] args)
    {
        if (args.Length < 3) { Console.WriteLine("用法: shot clickby <procName> <buttonName>"); return 1; }
        var procName = args[1];
        var name = args[2];
        var target = FindWindow(procName, out _);
        if (target == IntPtr.Zero) { Console.WriteLine("ERR no window"); return 1; }
        var element = AutomationElement.FromHandle(target);
        if (element == null) { Console.WriteLine("ERR no uia element"); return 1; }
        var cond = new System.Windows.Automation.PropertyCondition(AutomationElement.NameProperty, name);
        var btn = element.FindFirst(TreeScope.Descendants, cond);
        if (btn == null) { Console.WriteLine($"ERR no button named '{name}'"); return 1; }
        SetWindowPos(target, HWND_TOP, 0, 0, 0, 0, SWP_NOZORDER | SWP_SHOWWINDOW);
        SetForegroundWindow(target);
        Thread.Sleep(200);
        if (btn.TryGetCurrentPattern(InvokePattern.Pattern, out var p))
        {
            ((InvokePattern)p).Invoke();
            Console.WriteLine($"invoked '{name}'");
            return 0;
        }
        Console.WriteLine($"ERR '{name}' has no InvokePattern"); return 1;
    }

    /// <summary>枚举窗口内所有控件的 Name + ControlType（UI 状态精确验证）。用法: shot list <procName> [filter]</summary>
    static int DoList(string[] args)
    {
        if (args.Length < 2) { Console.WriteLine("用法: shot list <procName> [filter]"); return 1; }
        var procName = args[1];
        var filter = args.Length > 2 ? args[2] : null;
        var target = FindWindow(procName, out _);
        if (target == IntPtr.Zero) { Console.WriteLine("ERR no window"); return 1; }
        var element = AutomationElement.FromHandle(target);
        if (element == null) { Console.WriteLine("ERR no uia element"); return 1; }
        var all = element.FindAll(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button));
        int count = 0, shown = 0;
        foreach (AutomationElement ae in all)
        {
            var name = ae.Current.Name ?? "";
            if (filter != null && !name.Contains(filter)) continue;
            count++;
            if (shown < 60)
            {
                Console.WriteLine($"btn: '{name}'");
                shown++;
            }
        }
        Console.WriteLine($"--- {count} buttons{(filter != null ? " (filter=" + filter + ")" : "")}");
        return 0;
    }
}
