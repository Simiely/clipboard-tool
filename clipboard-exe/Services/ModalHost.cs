// Services/ModalHost.cs - 弹窗宿主（对齐 Web #modal-root：遮罩 + 居中弹窗卡片，一次只挂一个）
//  实现：独立顶层 Window（全屏半透明遮罩 + 居中卡片）。挂到 MainWindow.Owner 之上，
//  因此弹窗尺寸由内容决定、可超出主窗口边界完整显示在屏幕上（主窗口很小时不再被裁剪）。
//  - Attach(owner)：MainWindow 构造时绑定主窗口
//  - Show(content)：清旧弹窗 → 包 ScrollViewer → 显示遮罩 Window（模态 ShowDialog）；Close()：收 Window
//  - Confirm(msg, onOk, ...) ：确认框（对齐 askConfirm）
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ClipboardExe.Services;

public static class ModalHost
{
    private static Window? _owner;
    private static Window? _current;

    /// <summary>绑定主窗口（MainWindow 构造时调用一次）。</summary>
    public static void Attach(Window owner) => _owner = owner;

    /// <summary>当前是否有弹窗（对齐 $(".mask") 判断——已有弹窗时不叠加）。</summary>
    public static bool IsOpen => _current != null;

    /// <summary>挂载弹窗：独立顶层 Window + 全屏半透明遮罩 + 居中内容（可超出主窗口边界）。</summary>
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
            Background = new SolidColorBrush(Color.FromArgb(0xA8, 0x0A, 0x0C, 0x10)), // 对齐 .mask rgba(10,12,16,.66)
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Width = wa.Width,
            Height = wa.Height,
            Left = wa.Left,
            Top = wa.Top,
        };

        // 内容居中；包 ScrollViewer 以防内容超出屏幕时可滚动
        content.HorizontalAlignment = HorizontalAlignment.Center;
        content.VerticalAlignment = VerticalAlignment.Center;
        win.Content = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = content,
        };

        _current = win;
        win.Loaded += (_, _) => KeyboardFocus(content);
        win.ShowDialog();
    }

    public static void Close()
    {
        if (_current != null)
        {
            _current.Close();
            _current = null;
        }
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
