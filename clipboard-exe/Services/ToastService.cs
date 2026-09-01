// Services/ToastService.cs - 轻提示（对齐 Web 版 #flash .copied-flash 与 .toast-err）
//  - Flash(msg)：金色胶囊（accent 底 + 深字 600），默认底部居中；Flash(msg, x, y) 跟随鼠标上方（at-pos 语义 translate(-50%,-130%)），1400ms
//  - Error(msg)：红底白字，顶部居中，2600ms
// 实现：Popup（无窗口句柄，Absolute 屏幕坐标），内容先 Measure 再精确定位；复用单个 Popup + DispatcherTimer。
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace ClipboardExe.Services;

public static class ToastService
{
    private static Popup? _popup;
    private static Border? _body;
    private static TextBlock? _text;
    private static readonly DispatcherTimer Timer = new() { Interval = TimeSpan.FromMilliseconds(1400) };

    private static bool _isError;

    /// <summary>初始化（MainWindow 构造时调用一次；Popup 依赖 Application 存在）。</summary>
    public static void Init()
    {
        if (_popup != null) return;
        _text = new TextBlock { FontSize = 13, FontWeight = FontWeights.SemiBold, TextAlignment = TextAlignment.Center };
        _body = new Border
        {
            Child = _text,
            Padding = new Thickness(18, 8, 18, 8),
            CornerRadius = new CornerRadius(99),
            Effect = CreateShadow(),
        };
        _popup = new Popup
        {
            Child = _body,
            Placement = PlacementMode.Absolute,
            AllowsTransparency = true,
            IsHitTestVisible = false,
            StaysOpen = true,
        };
        Timer.Tick += (_, _) => Close();
    }

    /// <summary>金色提示：默认底部居中；x/y 提供时跟随鼠标上方（at-pos）。1400ms 自动关闭。</summary>
    public static void Flash(string msg, double? x = null, double? y = null)
    {
        Show(msg, x, y, isError: false, 1400);
    }

    /// <summary>红色错误提示：顶部居中。2600ms 自动关闭。</summary>
    public static void Error(string msg) => Show(msg, null, null, isError: true, 2600);

    private static void Show(string msg, double? x, double? y, bool isError, int ms)
    {
        if (_popup == null || _body == null || _text == null) return;
        _isError = isError;
        _text.Text = msg;
        _body.Background = isError ? new SolidColorBrush(Color.FromRgb(0xE0, 0x8A, 0x7A)) // --red
                                   : new SolidColorBrush(Color.FromRgb(0xC9, 0xA9, 0x6E)); // --accent 金
        _text.Foreground = isError ? Brushes.White : new SolidColorBrush(Color.FromRgb(0x10, 0x10, 0x14));

        // 先测量内容，Popup 无窗口不能直接拿 ActualWidth
        _body.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var w = _body.DesiredSize.Width;
        var h = _body.DesiredSize.Height;
        var wa = SystemParameters.WorkArea;

        double ox, oy;
        if (x.HasValue && y.HasValue)
        {
            // at-pos：鼠标上方（translate(-50%,-130%)），钳制在屏幕内
            ox = x.Value - w / 2;
            oy = y.Value - h * 1.3;
            ox = Math.Max(8, Math.Min(ox, wa.Width - w - 8));
            oy = Math.Max(8, oy);
        }
        else if (isError)
        {
            // toast-err：顶部居中
            ox = (wa.Width - w) / 2;
            oy = 16;
        }
        else
        {
            // copied-flash 默认：底部居中（bottom 32px）
            ox = (wa.Width - w) / 2;
            oy = wa.Height - h - 32;
        }

        _popup.HorizontalOffset = ox;
        _popup.VerticalOffset = oy;
        _body.Opacity = 0;
        _popup.IsOpen = true;
        // 淡入（对齐 .copied-flash transition opacity .25s；软件渲染下动画仍安全）
        _body.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)) { EasingFunction = new QuadraticEase() });
        Timer.Interval = TimeSpan.FromMilliseconds(ms);
        Timer.Stop();
        Timer.Start();
        AppLog.Info((isError ? "toast-err: " : "flash: ") + msg);
    }

    private static void Close()
    {
        if (_popup == null || _body == null) return;
        var fade = new DoubleAnimation(_body.Opacity, 0, TimeSpan.FromMilliseconds(140));
        fade.Completed += (_, _) => _popup.IsOpen = false;
        _body.BeginAnimation(UIElement.OpacityProperty, fade);
    }

    private static System.Windows.Media.Effects.DropShadowEffect CreateShadow() => new()
    {
        Color = Colors.Black, BlurRadius = 14, ShadowDepth = 2, Opacity = 0.5, Direction = 270,
    };
}
