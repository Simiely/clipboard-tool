// Services/ModalHost.cs - 弹窗宿主（对齐 Web #modal-root：遮罩 + 居中弹窗卡片，一次只挂一个）
//  - Attach(Grid)：MainWindow 构造时绑定全窗口遮罩层（ModalLayer）
//  - Show(content)：清掉旧弹窗后挂新弹窗 + 显示遮罩；Close()：收遮罩
//  - Confirm(msg, onOk, okText, onCancel)：确认框（对齐 askConfirm：h3 确认操作 + msg + 确认/取消）
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ClipboardExe.Services;

public static class ModalHost
{
    private static Grid? _layer;
    private static FrameworkElement? _current;

    /// <summary>绑定遮罩层（MainWindow 构造时调用一次）。</summary>
    public static void Attach(Grid layer) => _layer = layer;

    /// <summary>当前是否有弹窗（对齐 $(".mask") 判断——已有弹窗时不叠加）。</summary>
    public static bool IsOpen => _current != null;

    /// <summary>挂载弹窗（居中）。</summary>
    public static void Show(FrameworkElement content)
    {
        if (_layer == null) return;
        if (_current != null) Close();
        _current = content;
        content.HorizontalAlignment = HorizontalAlignment.Center;
        content.VerticalAlignment = VerticalAlignment.Center;
        _layer.Children.Add(content);
        _layer.Visibility = Visibility.Visible;
        KeyboardFocus(content);
    }

    public static void Close()
    {
        if (_layer == null) return;
        if (_current != null)
        {
            _layer.Children.Remove(_current);
            _current = null;
        }
        _layer.Visibility = Visibility.Collapsed;
    }

    /// <summary>确认框（对齐 askConfirm：标题"确认操作" + 消息 + 确认/取消等宽按钮）。</summary>
    public static void Confirm(string msg, Action onOk, string okText = "确认", Action? onCancel = null)
    {
        if (_layer == null) return;

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
            Style = (Style)_layer.FindResource("BtnPrimary"),
            Content = okText,
            MinWidth = 130,
            Margin = new Thickness(0, 0, 10, 0),
        };
        var cancel = new Button
        {
            Style = (Style)_layer.FindResource("BtnClose"),
            Content = "取消",
            MinWidth = 130,
        };
        var row = new Grid { Margin = new Thickness(0, 0, 0, 0) };
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
            Style = (Style)_layer.FindResource("ModalCard"),
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
