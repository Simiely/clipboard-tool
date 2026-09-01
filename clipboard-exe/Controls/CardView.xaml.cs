// Controls/CardView.xaml.cs - 卡片装配与交互（对齐 app.js clipCard / handleCardClick / makeCardBody / bindImageHoverPreview）
// 只做「展示 + 用户手势 → 事件转发」，业务编排（ClipService 变更 / ClipboardWatcher 抑制 / 弹窗）全部抛给 MainWindow：
//   CopyBumped        复制成功（MainWindow: watcher.Suppress(800) + BumpCopyCount 持久化；本卡仅本地 +1 显示，不重排——对齐 Web bumpCopyCount）
//   EditRequested     双击编辑 / ✎（归档只读不触发）
//   TogglePinRequested / DeleteRequested / OpenJsonRequested / OpenLinkRequested / TagFilterRequested
//   DownloadRequested 文件卡：单击复制=下载（M3b-2a）
//   CopyImageRequested 图片卡：单击复制到系统剪贴板（M3b-2b，对齐 Web copyImageToClipboard：失败降级 toast）
//   ImageBytesRequested 图片卡：构造时未注入加载器时异步获取字节（M3b-2b，MainWindow 从 FileStore 读取）
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ClipboardExe.Models;
using ClipboardExe.Services;

namespace ClipboardExe.Controls;

public partial class CardView : UserControl
{
    private ClipItem? _clip;
    private long _copyCount;          // 本地显示计数（复制成功后 +1，不改存储对象——防与 BumpCopyCount 双写）
    private readonly Func<string, byte[]>? _imageLoader; // M3b-2b：fileId → 图片字节；MainWindow 注入 FileStore 读取
    private byte[]? _imageBytes;      // M3b-2b：当前图片卡字节（已读则缓存，避免 hover 预览再读一次）
    private Popup? _imgPreviewPopup;  // M3b-2b：hover 浮层 Popup（唯一改 IsOpen 出口）
    private DispatcherTimer? _imgPreviewTimer; // M3b-2b：260ms 延迟打开（对齐 Web setTimeout 防快速划过误弹）
    private double _imgPreviewScale = 1.0; // M3b-2b：当前缩放（默认 100%，每次开重置）
    private double _imgPreviewStep = 0.15; // M3b-2b：滚轮缩放步长（对齐 Web LS.get("zoomStep", 0.15)；未来从 Settings 读取）

    public event Action<ClipItem>? CopyBumped;
    public event Action<ClipItem>? EditRequested;
    public event Action<ClipItem>? TogglePinRequested;
    public event Action<ClipItem>? DeleteRequested;
    public event Action<ClipItem>? OpenJsonRequested;
    public event Action<ClipItem>? OpenLinkRequested;
    public event Action<ClipItem>? DownloadRequested; // 文件卡：单击复制=下载（M3b-2a）
    public event Action<ClipItem>? CopyImageRequested; // M3b-2b：图片卡：单击复制=复制图片到系统剪贴板
    public event Action<string>? TagFilterRequested;
    /// <summary>归档卡 ↺ 恢复（M3b-1：归档只读但可恢复到活跃区）。</summary>
    public event Action<ClipItem>? RestoreRequested;

    /// <summary>当前条目（供调用方读取）。</summary>
    public ClipItem? Item => _clip;

    public CardView() : this(null) { }

    /// <summary>
    /// 构造卡片。图片卡可注入 imageLoader 用于构造时同步读字节（避免 hover 预览再 IO）。
    /// 文件卡不依赖；MainWindow 注入 FileStore.ReadAllBytes(fileId)。
    /// </summary>
    public CardView(Func<string, byte[]>? imageLoader)
    {
        InitializeComponent();
        _imageLoader = imageLoader;
    }

    /// <summary>装配条目（对齐 clipCard：类型徽章 / 标题兜底 / 状态徽章 / body / meta / ops）。</summary>
    public void SetClip(ClipItem c)
    {
        _clip = c;
        _copyCount = c.CopyCount;
        _imageBytes = null; // 每次重装配清缓存（防换条目串图）
        CloseImagePreview(); // 关闭任何遗留浮层
        CardBorder.Tag = c.Pinned ? "pin" : null;

        // —— row1：类型徽章（.badge elev 底彩字：text 绿 / link 砖红 / file 金 / image 紫红） ——
        var isImage = c.Type == "file" && Format.IsImageMime(c.FileMime);
        TypeBadge.Style = c.Type switch
        {
            "link" => (Style)FindResource("BadgeLink"),
            "file" => (Style)FindResource(isImage ? "BadgeImage" : "BadgeFile"),
            _ => (Style)FindResource("BadgeText"),
        };
        TypeBadge.Content = c.Type == "link" ? "链接" : isImage ? "图片" : c.Type == "file" ? "文件" : "文本";

        // —— row1：标题（对齐 title || link→hostOf / file→fileName / text→content 前 30 字） ——
        var title = c.Title;
        if (string.IsNullOrEmpty(title))
        {
            title = c.Type == "link" ? Format.HostOf(c.Url)
                  : c.Type == "file" ? c.FileName
                  : Truncate(c.Content, 30);
        }
        TitleText.Text = title;
        TitleText.ToolTip = title;

        // —— row1：状态徽章（★ 置顶 / ⏳ 过期 / 归档，顺序对齐 app.js clipCard） ——
        StatusPanel.Children.Clear();
        if (c.Pinned) StatusPanel.Children.Add(MakeSt("★ 置顶", "StatusStPin"));
        if (c.ExpireAt.HasValue) StatusPanel.Children.Add(MakeSt("⏳ " + Format.ExpLabel(c.ExpireAt), "StatusStExp"));
        if (c.Archived) StatusPanel.Children.Add(MakeSt("归档", "StatusStArch"));

        // —— row1 右上：归档卡 ↺ 恢复 + ✕ 删除；非归档卡仅 ✕ 删除（对齐 Web .ops.top） ——
        TopOpsPanel.Children.Clear();
        if (c.Archived) TopOpsPanel.Children.Add(MakeOpBtn("↺", "恢复到活跃区", del: false, () => RestoreRequested?.Invoke(c)));
        TopOpsPanel.Children.Add(MakeOpBtn("✕", "删除", del: true, () => DeleteRequested?.Invoke(c)));

        // —— body：类型专属内容区（图片→imgwrap / 文件→图标卡 / 链接→URL+按钮 / 文本→摘要） ——
        if (isImage)
        {
            var body = BuildImageBody(c);
            if (body != null) BodyHost.Content = body;
            else BodyHost.Content = BuildFileBody(c); // 图片字节读取/解码失败 → 降级为文件卡
        }
        else BodyHost.Content = c.Type == "file" ? BuildFileBody(c) : c.Type == "link" ? BuildLinkBody(c) : BuildTextBody(c);

        // —— foot：meta（复制 N 次 / #tag / 时间）+ ops ——
        MetaPanel.Children.Clear();
        var cnt = new TextBlock { Text = "复制 " + c.CopyCount + " 次", FontSize = 11, Foreground = DimBrush, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        MetaPanel.Children.Add(cnt);
        foreach (var t in c.Tags ?? new List<string>())
        {
            var tag = new Button { Style = (Style)FindResource("MetaTag"), Content = "#" + t, Margin = new Thickness(0, 0, 8, 0), Tag = c };
            tag.Click += (_, _) => TagFilterRequested?.Invoke(t);
            MetaPanel.Children.Add(tag);
        }
        MetaPanel.Children.Add(new TextBlock { Text = Format.Time(c.UpdatedAt), FontSize = 11, Foreground = DimBrush, VerticalAlignment = VerticalAlignment.Center });

        OpsPanel.Children.Clear();
        if (!c.Archived)
        {
            var pin = MakeOpBtn(c.Pinned ? "★" : "☆", c.Pinned ? "取消置顶" : "置顶", del: false,
                () => TogglePinRequested?.Invoke(c));
            if (c.Pinned) pin.Foreground = AmberBrush; // .ops .b.on：置顶态金色
            OpsPanel.Children.Add(pin);
        }
        // 文件/图片共用 ↓ 下载（图片也允许下载，对齐 Web file → ↓ 下载无条件含归档）
        if (c.Type == "file")
            OpsPanel.Children.Add(MakeOpBtn("↓", "下载", del: false, () => DownloadRequested?.Invoke(c)));
        if (!c.Archived)
            OpsPanel.Children.Add(MakeOpBtn("✎", "编辑", del: false, () => EditRequested?.Invoke(c)));
        if (c.Type == "text" && Format.LooksLikeJson(c.Content))
            OpsPanel.Children.Add(MakeOpBtn("{}", "JSON 格式化预览", del: false, () => OpenJsonRequested?.Invoke(c)));

        OpsPanel.Visibility = OpsPanel.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // ---- body 构建 ----

    /// <summary>文本卡 body：.pv 内嵌滚动摘要（InsetPanel 底，muted 12px，line-height 1.6，可滚动——对齐 .pv overflow-y:auto）。</summary>
    private FrameworkElement BuildTextBody(ClipItem c)
    {
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var text = new TextBlock
        {
            Text = c.Content ?? "",
            FontSize = 12,
            Foreground = MutedBrush,
            TextWrapping = TextWrapping.Wrap, // 对齐 white-space:pre-wrap（保留换行 + 自动折行）
            LineHeight = 19.2,                // 12 * 1.6
        };
        scroll.Content = text;
        return new Border
        {
            Child = scroll,
            Style = (Style)FindResource("InsetPanel"),
            Padding = new Thickness(11, 9, 6, 9),
        };
    }

    /// <summary>图片卡 body（M3b-2b：imgwrap cover 撑满对齐 app.js makeCardBody 图片分支；图片字节读取/解码失败返回 null 由调用方降级为文件卡）。</summary>
    private FrameworkElement? BuildImageBody(ClipItem c)
    {
        if (string.IsNullOrEmpty(c.FileId) || _imageLoader == null) return null;
        byte[] bytes;
        try { bytes = _imageLoader(c.FileId); }
        catch { return null; }
        if (bytes == null || bytes.Length == 0) return null;

        BitmapImage bmp;
        try
        {
            using var ms = new MemoryStream(bytes);
            bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad; // 立即加载并释放流
            bmp.StreamSource = ms;
            bmp.EndInit();
            bmp.Freeze(); // 跨线程可用
        }
        catch { return null; }

        _imageBytes = bytes; // 缓存供 hover 预览 / 单击复制复用

        var img = new Image
        {
            Source = bmp,
            Stretch = Stretch.UniformToFill, // 对齐 app.js imgwrap cover 撑满内容区
            StretchDirection = StretchDirection.Both,
            ClipToBounds = true,
        };
        // imgwrap（InsetPanel 底 r-md 圆角 + ClipToBounds 圆角裁剪；对齐 .imgwrap 视觉）
        var wrap = new Border
        {
            Style = (Style)FindResource("InsetPanel"),
            CornerRadius = (CornerRadius)FindResource("RadiusMd"),
            Child = img,
            Padding = new Thickness(0),
            Margin = new Thickness(0, 2, 0, 0),
        };
        wrap.Name = "ImgWrap"; // hover 触发区标识（mouseenter 仅此处）
        // 绑定 mouseenter：仅图片区域触发（对齐 Web v0.6.13 触发区收窄）
        wrap.MouseEnter += ImgWrap_MouseEnter;
        wrap.MouseLeave += ImgWrap_MouseLeavePreview;
        return wrap;
    }

    /// <summary>文件卡 body（对齐 app.js makeFileIcon：.fic 图标卡 PDF 红边/ZIP 金边/FILE 中性 + 折叠角 + 内虚线 + fname·fsize·mime 首段）。</summary>
    private FrameworkElement BuildFileBody(ClipItem c)
    {
        var kind = Format.FileKindFor(c.FileName); // "pdf" | "zip" | "file"
        var fic = BuildFic(kind);
        var fname = new TextBlock
        {
            Text = c.FileName,
            FontSize = 13,
            FontWeight = FontWeights.Medium,
            Foreground = TextBrush,
            TextTrimming = TextTrimming.CharacterEllipsis, // 对齐 .finfo .fname：单行省略
        };
        var fsize = new TextBlock
        {
            Text = Format.Size(c.FileSize) + " · " + (c.FileMime ?? "").Split('/')[0], // 对齐 fmtSize + " · " + mime 首段
            FontSize = 11,
            Foreground = DimBrush,
            Margin = new Thickness(0, 3, 0, 0),
        };
        var finfo = new StackPanel { Children = { fname, fsize } };
        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center, // 对齐 .filebody：flex 1 + align-items center
            Margin = new Thickness(0, 2, 0, 0),
            Children = { fic, finfo },
        };
    }

    /// <summary>文件图标（对齐 .fic：46x56 r-11 inset 底 + 折叠角 + PDF/ZIP 彩边 + 内虚线框）。</summary>
    private FrameworkElement BuildFic(string kind)
    {
        var label = new TextBlock
        {
            Text = kind switch { "pdf" => "PDF", "zip" => "ZIP", _ => "FILE" },
            FontSize = 12,
            FontWeight = FontWeights.Medium,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        // 折叠角（.fic .fold：top -1 left 6 18x5 elev-hi 顶圆角 3 / 底 0）
        var fold = new Border
        {
            Width = 18,
            Height = 5,
            Background = ElevHiBrush,
            CornerRadius = (CornerRadius)FindResource("RadiusFold"),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(6, -1, 0, 0),
        };
        var grid = new Grid { Children = { fold, label } };

        var box = new Border
        {
            Width = 46,
            Height = 56,
            Background = InsetBrush,
            CornerRadius = (CornerRadius)FindResource("RadiusIconLg"),
            BorderThickness = new Thickness(1),
            Child = grid,
            Margin = new Thickness(0, 0, 14, 0), // gap 14（.filebody gap）
        };
        if (kind == "pdf")
        {
            box.BorderBrush = RedSoftBorder;   // rgba(red,.35)
            label.Foreground = RedBrush;
            grid.Children.Insert(0, MakeFicDash(RedSoftBorder)); // 内虚线框（pdf/zip 特有）
        }
        else if (kind == "zip")
        {
            box.BorderBrush = GoldSoftBorder;  // rgba(gold,.35)
            label.Foreground = AmberBrush;     // 对齐 .fic.zip color: var(--accent)（金）
            grid.Children.Insert(0, MakeFicDash(GoldSoftBorder));
        }
        else
        {
            box.BorderBrush = FileBoxBorderBrush; // 对齐 .fic：1px var(--border)
            label.Foreground = MutedBrush;
        }
        return box;
    }

    /// <summary>fic 内虚线框（.fic.pdf::after / .fic.zip::after：inset 4px dashed opacity .35）。</summary>
    private static FrameworkElement MakeFicDash(Brush stroke)
    {
        return new System.Windows.Shapes.Rectangle
        {
            Margin = new Thickness(4),
            RadiusX = 6,
            RadiusY = 6,
            Stroke = stroke,
            StrokeThickness = 1,
            StrokeDashArray = new DoubleCollection { 3, 2 },
            Opacity = 0.35,
            IsHitTestVisible = false,
        };
    }

    /// <summary>链接卡 body：砖红 URL（monospace 单行省略）+ ↗ 打开链接金色主按钮（.main-btn：accent 底深字，margin-top:auto）。</summary>
    private FrameworkElement BuildLinkBody(ClipItem c)
    {
        var url = new TextBlock
        {
            Text = c.Url ?? "",
            FontSize = 11.5,
            FontFamily = new FontFamily("Consolas, ui-monospace"),
            Foreground = BlueBrush,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        var urlBox = new Border
        {
            Child = url,
            Style = (Style)FindResource("InsetPanel"),
            Padding = new Thickness(11, 8, 6, 8),
        };
        var open = new Button
        {
            Content = "↗ 打开链接",
            Style = (Style)FindResource("MainLinkBtn"),
            Margin = new Thickness(0, 8, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        open.Click += (_, _) => OpenLinkRequested?.Invoke(c);
        return new StackPanel { Children = { urlBox, open } };
    }

    // ---- 图片 hover 预览（M3b-2b，对齐 app.js bindImageHoverPreview：260ms 延迟 / 仅 imgwrap mouseenter / 浮层 Popup / 50%~300% 缩放 / 视口钳制） ----

    private void ImgWrap_MouseEnter(object sender, MouseEventArgs e)
    {
        if (_imageBytes == null) return;
        if (_imgPreviewTimer != null) _imgPreviewTimer.Stop();
        _imgPreviewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(260) }; // 对齐 Web 260ms 延迟防快速划过误弹
        _imgPreviewTimer.Tick += (_, _) =>
        {
            _imgPreviewTimer?.Stop();
            OpenImagePreview();
        };
        _imgPreviewTimer.Start();
    }

    private void ImgWrap_MouseLeavePreview(object sender, MouseEventArgs e) => CloseImagePreview();

    private void OpenImagePreview()
    {
        if (_imageBytes == null || _clip == null) return;
        if (_imgPreviewPopup != null && _imgPreviewPopup.IsOpen) return; // 防重：浮层已开不再重建
        _imgPreviewScale = 1.0; // 每次重开重置 100%

        BitmapImage bmp;
        try
        {
            using var ms = new MemoryStream(_imageBytes);
            bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.StreamSource = ms;
            bmp.EndInit();
            bmp.Freeze();
        }
        catch { return; }

        // 浮层容器（.img-hover-preview：白底 / r-lg / sh-raised 阴影 / padding 10）
        var bg = new Border
        {
            Background = Brushes.White,
            CornerRadius = (CornerRadius)FindResource("RadiusLg"),
            Padding = new Thickness(10),
            Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = Colors.Black, BlurRadius = 12, ShadowDepth = 4, Opacity = 0.35 },
        };
        var inner = new StackPanel();
        // 图片（ScrollViewer 套住，超框可滚动；wheel 缩放绑定在外层）
        var img = new Image { Source = bmp, StretchDirection = StretchDirection.Both };
        var scroll = new ScrollViewer
        {
            Content = img,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            MaxWidth = 800,
            MaxHeight = 600,
        };
        img.Width = bmp.PixelWidth;
        img.Height = bmp.PixelHeight;
        // 标题（.img-cap：fname · fsize · 百分比；M3b-2b 简化）
        var cap = new TextBlock
        {
            Text = (_clip.FileName ?? "图片") + " · " + Format.Size(_clip.FileSize) + " · " + Math.Round(_imgPreviewScale * 100) + "%",
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44)),
            Margin = new Thickness(0, 6, 0, 0),
        };
        inner.Children.Add(scroll);
        inner.Children.Add(cap);
        bg.Child = inner;
        // 鼠标滚轮在背景上缩放（preventDefault 拦截，绑 Popup 整体而非 Image）
        bg.PreviewMouseWheel += ImgPreview_Wheel;
        // 浮层挂到 CardBorder（弹出层不影响布局；对齐 Web card.appendChild(box) 浮层挂卡片内部）
        _imgPreviewPopup = new Popup
        {
            PlacementTarget = CardBorder,
            Placement = PlacementMode.Custom,
            AllowsTransparency = true,
            StaysOpen = true, // mouseleave 由我们显式控制
            Child = bg,
        };
        _imgPreviewPopup.CustomPopupPlacementCallback = (popupSize, targetSize, offset) =>
            RepositionPreview(popupSize, targetSize);
        _imgPreviewPopup.IsOpen = true;
    }

    /// <summary>浮层定位（对齐 Web reposition：跟随卡片正中上方 4px，顶部空间不够时落底部；视口钳制 8px）。</summary>
    private CustomPopupPlacement[] RepositionPreview(Size popupSize, Size targetSize)
    {
        var cardRect = CardBorder.PointToScreen(new Point(0, 0)); // 卡片屏幕坐标
        var screenW = SystemParameters.PrimaryScreenWidth;
        var screenH = SystemParameters.PrimaryScreenHeight;
        var bw = popupSize.Width;
        var bh = popupSize.Height;
        // 水平：卡片居中，钳制 [8, screenW - bw - 8]
        var left = cardRect.X + (targetSize.Width - bw) / 2;
        left = Math.Max(8, Math.Min(left, screenW - bw - 8));
        // 垂直：优先卡片顶部 - 4px - bh（间隙 4px 贴卡片防"飘远"）；上方空间不够则卡片底部 + 4px
        var topAbove = cardRect.Y - bh - 4;
        var topBelow = cardRect.Y + targetSize.Height + 4;
        var top = (topAbove >= 8) ? topAbove : Math.Min(topBelow, screenH - bh - 8);
        // Popup 的 CustomPopupPlacement 返回相对 PlacementTarget 的偏移（Point(0,0) = PlacementTarget 左上）
        var relX = left - cardRect.X;
        var relY = top - cardRect.Y;
        return new[] { new CustomPopupPlacement(new Point(relX, relY), PopupPrimaryAxis.Horizontal) };
    }

    private void ImgPreview_Wheel(object sender, MouseWheelEventArgs e)
    {
        if (_imgPreviewPopup == null || !_imgPreviewPopup.IsOpen) return;
        e.Handled = true; // 拦截页面滚动
        var before = _imgPreviewScale;
        _imgPreviewScale = Math.Min(3.0, Math.Max(0.5, _imgPreviewScale + (e.Delta > 0 ? _imgPreviewStep : -_imgPreviewStep)));
        if (_imgPreviewScale != before) ApplyPreviewScale();
    }

    private void ApplyPreviewScale()
    {
        if (_imgPreviewPopup?.Child is not Border bg || bg.Child is not StackPanel sp) return;
        if (sp.Children.Count < 2 || sp.Children[0] is not ScrollViewer sv || sv.Content is not Image img) return;
        if (_imageBytes == null || _clip == null) return;
        // 用 bytes 重新构造 BitmapImage 以获取 PixelWidth/Height
        try
        {
            using var ms = new MemoryStream(_imageBytes);
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.StreamSource = ms;
            bmp.EndInit();
            img.Source = bmp;
            img.Width = bmp.PixelWidth * _imgPreviewScale;
            img.Height = bmp.PixelHeight * _imgPreviewScale;
            if (sp.Children[1] is TextBlock cap)
                cap.Text = (_clip.FileName ?? "图片") + " · " + Format.Size(_clip.FileSize) + " · " + Math.Round(_imgPreviewScale * 100) + "%";
            // 视口钳制：Popup.CustomPopupPlacementCallback 在下次 IsOpen=true 时才回调；显式调用重新定位
            _imgPreviewPopup.HorizontalOffset += 0.001; // 触发重新 placement
            _imgPreviewPopup.HorizontalOffset -= 0.001;
        }
        catch { /* 缩放失败不抛——预览仍可用 */ }
    }

    private void CloseImagePreview()
    {
        _imgPreviewTimer?.Stop();
        _imgPreviewTimer = null;
        if (_imgPreviewPopup != null)
        {
            _imgPreviewPopup.IsOpen = false;
            if (_imgPreviewPopup.Child is Border bg) bg.PreviewMouseWheel -= ImgPreview_Wheel;
            _imgPreviewPopup.Child = null;
            _imgPreviewPopup = null;
        }
    }

    // ---- 手势：单击复制 / 双击编辑（守卫排除按钮区与归档） ----

    private void Card_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_clip == null) return;
        if (e.OriginalSource is DependencyObject src && IsInsideInteractive(src)) return; // ops/标签/主按钮自己处理
        var pos = e.GetPosition(null); // 屏幕坐标（对齐 e.clientX/Y）
        Copy(_clip, pos.X, pos.Y);
    }

    private void Card_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_clip == null || _clip.Archived) return; // 归档只读（对齐 ondblclick 守卫）
        if (e.OriginalSource is DependencyObject src && IsInsideInteractive(src)) return;
        EditRequested?.Invoke(_clip);
    }

    /// <summary>复制内容（对齐 handleCardClick：text 复制 content / link 复制 url / file 非图片 → 下载；
    /// 图片 → 复制到系统剪贴板（失败 toast；对齐 Web copyImageToClipboard + 失败降级 errToast）。</summary>
    private void Copy(ClipItem c, double x, double y)
    {
        if (c.Type == "file")
        {
            if (Format.IsImageMime(c.FileMime))
            {
                CopyImageRequested?.Invoke(c); // M3b-2b：MainWindow 复制到系统剪贴板
                return;
            }
            DownloadRequested?.Invoke(c); // 对齐 handleCardClick：文件（非图片）单击 = 下载
            return;
        }
        var text = c.Type == "link" ? c.Url : c.Content;
        try { Clipboard.SetText(text); }
        catch { ToastService.Error("复制失败，请手动选择复制"); return; }
        ToastService.Flash("已复制", x, y);
        _copyCount++;
        UpdateCopyLabel();
        CopyBumped?.Invoke(c); // MainWindow: watcher.Suppress(800) + BumpCopyCount
    }

    private void UpdateCopyLabel()
    {
        if (MetaPanel.Children.Count == 0) return;
        if (MetaPanel.Children[0] is TextBlock t) t.Text = "复制 " + _copyCount + " 次";
    }

    /// <summary>是否点在内层交互件上（Button / 内容可点击区）——卡片单击/双击守卫（对齐 e.target.closest(".ops")）。</summary>
    private static bool IsInsideInteractive(DependencyObject? src)
    {
        while (src != null)
        {
            if (src is Button or ToggleButton) return true;
            src = VisualTreeHelper.GetParent(src);
        }
        return false;
    }

    // ---- 小构件 ----

    private ContentControl MakeSt(string text, string styleKey) => new()
    {
        Style = (Style)FindResource(styleKey),
        Content = text,
        Margin = new Thickness(0, 0, 5, 0),
    };

    private Button MakeOpBtn(string glyph, string tip, bool del, Action onClick)
    {
        var b = new Button
        {
            Content = glyph,
            ToolTip = tip,
            Style = (Style)FindResource(del ? "OpsIconBtnDel" : "OpsIconBtn"),
            Margin = new Thickness(0, 0, 5, 0),
        };
        b.Click += (_, _) => onClick();
        return b;
    }

    private static string Truncate(string? s, int max)
    {
        s ??= "";
        return s.Length > max ? s[..max] : s;
    }

    // ---- 画刷缓存（避免每次装配 new SolidColorBrush） ----
    private static readonly Brush DimBrush = BrushFrom(0x6E6E6E);
    private static readonly Brush MutedBrush = BrushFrom(0x848484);
    private static readonly Brush TextBrush = BrushFrom(0xDADADA);
    private static readonly Brush BlueBrush = BrushFrom(0xAE4D4D);
    private static readonly Brush AmberBrush = BrushFrom(0xD4AF37);
    private static readonly Brush RedBrush = BrushFrom(0xE08A7A);
    private static readonly Brush InsetBrush = BrushFrom(0x141414);
    private static readonly Brush ElevHiBrush = BrushFrom(0x2A2A2A);
    private static readonly Brush FileBoxBorderBrush = BrushFrom(0x3D3D3D);
    /// <summary>rgba(red,.35) 文件卡 PDF 边。</summary>
    private static readonly Brush RedSoftBorder = BrushFromAlpha(0xE08A7A, 0x59);
    /// <summary>rgba(gold,.35) 文件卡 ZIP 边。</summary>
    private static readonly Brush GoldSoftBorder = BrushFromAlpha(0xC9A96E, 0x59);

    private static Brush BrushFrom(int rgb)
        => new SolidColorBrush(Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));

    private static Brush BrushFromAlpha(int rgb, byte alpha)
        => new SolidColorBrush(Color.FromArgb(alpha, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));
}