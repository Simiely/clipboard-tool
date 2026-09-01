// Controls/CardView.xaml.cs - 卡片装配与交互（对齐 app.js clipCard / handleCardClick / makeCardBody）
// 只做「展示 + 用户手势 → 事件转发」，业务编排（ClipService 变更 / ClipboardWatcher 抑制 / 弹窗）全部抛给 MainWindow：
//   CopyBumped     复制成功（MainWindow: watcher.Suppress(800) + BumpCopyCount 持久化；本卡仅本地 +1 显示，不重排——对齐 Web bumpCopyCount）
//   EditRequested  双击编辑 / ✎（归档只读不触发）
//   TogglePinRequested / DeleteRequested / OpenJsonRequested / OpenLinkRequested / TagFilterRequested
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using ClipboardExe.Models;
using ClipboardExe.Services;

namespace ClipboardExe.Controls;

public partial class CardView : UserControl
{
    private ClipItem? _clip;
    private long _copyCount;          // 本地显示计数（复制成功后 +1，不改存储对象——防与 BumpCopyCount 双写）

    public event Action<ClipItem>? CopyBumped;
    public event Action<ClipItem>? EditRequested;
    public event Action<ClipItem>? TogglePinRequested;
    public event Action<ClipItem>? DeleteRequested;
    public event Action<ClipItem>? OpenJsonRequested;
    public event Action<ClipItem>? OpenLinkRequested;
    public event Action<ClipItem>? DownloadRequested; // 文件卡：单击复制=下载（M3b-2a）
    public event Action<string>? TagFilterRequested;
    /// <summary>归档卡 ↺ 恢复（M3b-1：归档只读但可恢复到活跃区）。</summary>
    public event Action<ClipItem>? RestoreRequested;

    /// <summary>当前条目（供调用方读取）。</summary>
    public ClipItem? Item => _clip;

    public CardView()
    {
        InitializeComponent();
    }

    /// <summary>装配条目（对齐 clipCard：类型徽章 / 标题兜底 / 状态徽章 / body / meta / ops）。</summary>
    public void SetClip(ClipItem c)
    {
        _clip = c;
        _copyCount = c.CopyCount;
        CardBorder.Tag = c.Pinned ? "pin" : null;

        // —— row1：类型徽章（.badge elev 底彩字：text 绿 / link 砖红 / file 金） ——
        TypeBadge.Style = c.Type switch
        {
            "link" => (Style)FindResource("BadgeLink"),
            "file" => (Style)FindResource("BadgeFile"),
            _ => (Style)FindResource("BadgeText"),
        };
        TypeBadge.Content = c.Type == "link" ? "链接" : c.Type == "file" ? "文件" : "文本";

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

        // —— body：类型专属内容区（文件→图标卡 / 链接→URL+按钮 / 文本→摘要；图片 2b 再接入） ——
        BodyHost.Content = c.Type == "file" ? BuildFileBody(c) : c.Type == "link" ? BuildLinkBody(c) : BuildTextBody(c);

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
        if (c.Type == "file")
            OpsPanel.Children.Add(MakeOpBtn("↓", "下载", del: false, () => DownloadRequested?.Invoke(c))); // 对齐 Web：file → ↓ 下载（含归档）
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
            Padding = new Thickness(11, 9, 11, 9),
        };
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
            Padding = new Thickness(11, 8, 11, 8),
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
    /// 成功后计数 +1 + 本地显示，不重排）。</summary>
    private void Copy(ClipItem c, double x, double y)
    {
        if (c.Type == "file")
        {
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
