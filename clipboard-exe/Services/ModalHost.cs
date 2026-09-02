// Services/ModalHost.cs - 弹窗宿主（对齐 Web #modal-root：居中弹窗卡片，一次只挂一个）
//  实现：独立顶层 Window（透明背景、无全屏压暗遮罩），卡片自带阴影、尺寸由内容决定，
//  因此可超出主窗口边界完整显示；位置相对主窗口居中，并夹在屏幕工作区内保证完整可见。
//  非模态：主窗口保持可点击；弹窗失焦（点击空白/主窗口/其它程序）即自动关闭（SuppressDismiss 期间不关，保护内部子对话框）。
//  - Attach(owner)：MainWindow 构造时绑定主窗口
//  - Show(content)：清旧弹窗 → 包 ScrollViewer（限制最大尺寸避免超出屏幕）→ 居中于主窗口的非模态 Window；Close()：收 Window
//  - Confirm(msg, onOk, ...) ：确认框（对齐 askConfirm）
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ClipboardExe.Services;

public static class ModalHost
{
    private static Window? _owner;
    private static Window? _current;
    private static bool _closing;   // 防 Deactivated 重入 Close 触发 VerifyNotClosing（窗口关闭中再次 Close 会抛异常）
    private static bool _armed;      // Show 后短暂屏蔽 Deactivated 自动关闭，避开打开瞬间的激活抖动（Owner 切换导致的瞬时 Deactivated）

    /// <summary>内部子对话框（文件选择等）打开期间置 true，屏蔽失焦自动关闭，避免误关弹窗。</summary>
    public static bool SuppressDismiss { get; set; }

    /// <summary>绑定主窗口（MainWindow 构造时调用一次）。</summary>
    public static void Attach(Window owner) => _owner = owner;

    /// <summary>当前是否有弹窗（对齐 $(".mask") 判断——已有弹窗时不叠加）。</summary>
    public static bool IsOpen => _current != null;

    /// <summary>挂载弹窗：独立顶层 Window（透明背景、不压暗界面）+ 相对主窗口居中 + 非模态（点击空白关闭）。</summary>
    public static void Show(FrameworkElement content)
    {
        if (_owner == null) return;
        if (_current != null) Close();

        var wa = SystemParameters.WorkArea;
        var win = new Window
        {
            Owner = _owner,
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = null, // 透明：不压暗整个界面
            Foreground = (Brush)Application.Current.FindResource("TextBrush"), // 弹窗独立窗口默认前景为黑，强制浅色，避免黑底黑字
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
            SizeToContent = SizeToContent.WidthAndHeight,
        };

        // 内容居中；包 ScrollViewer 限制最大尺寸，避免内容过大时超出屏幕（仅弹窗自身可滚动）
        content.HorizontalAlignment = HorizontalAlignment.Center;
        content.VerticalAlignment = VerticalAlignment.Center;
        win.Content = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            MaxWidth = wa.Width - 24,
            MaxHeight = wa.Height - 24,
            Content = content,
        };

        // 先把窗口摆到主窗口附近（用估算尺寸），避免 Show 瞬间的 0,0 闪烁；Loaded 再用真实尺寸精修
        content.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var est = content.DesiredSize;
        (win.Left, win.Top) = PositionFor(est.Width, est.Height);

        _current = win;
        _closing = false;
        _armed = false;
        win.Loaded += (_, _) =>
        {
            // 用真实渲染尺寸再夹一次，确保完整可见
            (win.Left, win.Top) = PositionFor(win.ActualWidth, win.ActualHeight);
            KeyboardFocus(content);
            _armed = true; // 装载完成后再允许「点击空白关闭」，避开打开瞬间的激活抖动（一开就关）
        };
        // 非模态：主窗口保持可点击；失焦（点空白/主窗口/其它程序）即关闭。
        //  仅当本窗口仍是当前弹窗(_current==win)、已就绪(_armed)、非关闭中(_closing)、非子对话框期间(SuppressDismiss)才关，
        //  以防打开瞬间 Owner 切换产生的瞬时 Deactivated 把刚开的弹窗立刻关掉，以及关闭过程 WmActivate 重入导致 VerifyNotClosing 崩溃。
        win.Deactivated += (_, _) =>
        {
            if (SuppressDismiss || !_armed || _closing) return;
            if (_current == win) Close();
        };
        win.Show();
    }

    /// <summary>计算相对主窗口居中、并夹在屏幕工作区内的左上角坐标。</summary>
    private static (double left, double top) PositionFor(double w, double h)
    {
        var ow = _owner!;
        var wa = SystemParameters.WorkArea;
        var owW = ow.ActualWidth > 0 ? ow.ActualWidth : ow.Width;
        var owH = ow.ActualHeight > 0 ? ow.ActualHeight : ow.Height;

        var left = ow.Left + (owW - w) / 2;
        var top = ow.Top + (owH - h) / 2;

        left = System.Math.Clamp(left, wa.Left, System.Math.Max(wa.Left, wa.Right - w));
        top = System.Math.Clamp(top, wa.Top, System.Math.Max(wa.Top, wa.Bottom - h));
        return (left, top);
    }

    public static void Close()
    {
        var w = _current;
        if (w == null || _closing) return;       // 已无弹窗 / 正在关闭中：不重入，避免 VerifyNotClosing
        _closing = true;                          // 标记关闭中（关闭过程 WmActivate 重入 Deactivated 时不再重复 Close）
        _current = null;
        try { w.Close(); }
        finally { _closing = false; }
    }

    /// <summary>确认框（对齐 askConfirm：标题"确认操作" + 消息 + 确认/取消等宽按钮）。</summary>
    public static void Confirm(string msg, Action onOk, string okText = "确认", Action? onCancel = null)
    {
        if (_owner == null) return;

        var title = new TextBlock { Text = "确认操作", FontSize = 16, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 12) };
        var body = new TextBlock
        {
            Text = msg,
            FontSize = 13,
            Foreground = new SolidColorBrush(Color.FromRgb(0x84, 0x84, 0x84)),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 18),
        };
        var ok = new Button
        {
            Style = (Style)Application.Current.FindResource("BtnPrimary"),
            Content = okText,
            MinWidth = 130,
            Margin = new Thickness(0, 0, 10, 0),
        };
        var cancel = new Button
        {
            Style = (Style)Application.Current.FindResource("BtnClose"),
            Content = "取消",
            MinWidth = 130,
        };
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(ok, 0);
        Grid.SetColumn(cancel, 1);
        ok.Margin = new Thickness(0, 0, 10, 0);
        row.Children.Add(ok);
        row.Children.Add(cancel);

        var sp = new StackPanel { Children = { title, body, row } };
        var card = new Border
        {
            Style = (Style)Application.Current.FindResource("ModalCard"),
            Width = 360,
            Child = sp,
        };

        ok.Click += (_, _) => { Close(); onOk(); };
        cancel.Click += (_, _) => { Close(); onCancel?.Invoke(); };
        Show(card);
    }

    /// <summary>把焦点给弹窗内第一个可聚焦控件（对齐 Web 打开弹窗即聚焦输入）。</summary>
    private static void KeyboardFocus(DependencyObject root)
    {
        var first = FindFocusable(root);
        first?.Focus();
        if (first is TextBox tb) tb.CaretIndex = tb.Text.Length;
    }

    private static UIElement? FindFocusable(DependencyObject node)
    {
        if (node is TextBox or Button) return node as UIElement;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
        {
            var hit = FindFocusable(VisualTreeHelper.GetChild(node, i));
            if (hit != null) return hit;
        }
        return null;
    }
}
