using System.Drawing.Drawing2D;

namespace ClipboardExe;

/// <summary>
/// 卡片控件：自绘黑金风格（对齐 Web 版卡片墙）。
///  - 圆角背景 + 细边框；悬停边框金色高亮（新拟态用阴影/边框表达层次，不用 transform 类效果）
///  - 类型徽标：左上角圆角小块 + 字母（T=文本 / L=链接 / I=图片）
///  - 标题粗体一行 + 内容摘要两行截断 + 右下相对时间
///  - 置顶 ★ / 复制次数 徽标右上
///  - 点击复制由 MainForm 订阅 Click 处理；右键菜单也由 MainForm 挂
/// 配色令牌对齐 Web 版 index.html :root：bg=#1A1A1A elev=#1F1F1F gold=#C9A96E text=#DADADA muted=#848484
/// </summary>
public sealed class CardControl : UserControl
{
    public const int CardWidth = 280;
    public const int CardHeight = 112;

    private static readonly Color Bg = Color.FromArgb(0x1F, 0x1F, 0x1F);      // --elev
    private static readonly Color Border = Color.FromArgb(0x2A, 0x2A, 0x2A);
    private static readonly Color BorderHover = Color.FromArgb(0xC9, 0xA9, 0x6E); // --gold
    private static readonly Color TextColor = Color.FromArgb(0xDA, 0xDA, 0xDA);   // --text
    private static readonly Color Muted = Color.FromArgb(0x84, 0x84, 0x84);       // --muted
    private static readonly Color Gold = Color.FromArgb(0xC9, 0xA9, 0x6E);
    private static readonly Color LinkBlue = Color.FromArgb(0x7A, 0xA8, 0xE0);

    private static readonly Font TitleFont = new("Microsoft YaHei UI", 10.5f, FontStyle.Bold);
    private static readonly Font BodyFont = new("Microsoft YaHei UI", 9f);
    private static readonly Font MetaFont = new("Microsoft YaHei UI", 8f);
    private static readonly Font BadgeFont = new("Microsoft YaHei UI", 8.5f, FontStyle.Bold);

    /// <summary>卡片承载的条目。</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public ClipItem? Item { get; private set; }

    private bool _hovered;

    public CardControl(ClipItem item)
    {
        Item = item;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
               | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Size = new Size(CardWidth, CardHeight);
        Cursor = Cursors.Hand;
    }

    protected override void OnMouseEnter(EventArgs e) { _hovered = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hovered = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        using var path = RoundedRect(rect, 10);
        using var bgBrush = new SolidBrush(Bg);
        g.FillPath(bgBrush, path);
        using var pen = new Pen(_hovered ? BorderHover : Border, _hovered ? 1.6f : 1f);
        g.DrawPath(pen, path);

        if (Item == null) return;
        DrawBadge(g);
        DrawTitle(g);
        DrawSummary(g);
        DrawMeta(g);
        DrawFlags(g);
    }

    // ---------------- 绘制细节 ----------------

    private void DrawBadge(Graphics g)
    {
        var (letter, color) = Item!.Type switch
        {
            "link" => ("L", LinkBlue),
            "file" => ("I", Gold),
            _ => ("T", Muted),
        };
        using var brush = new SolidBrush(Color.FromArgb(0x2A, 0x2A, 0x2A));
        using var pen = new Pen(color);
        var badge = new Rectangle(12, 12, 24, 24);
        using var path = RoundedRect(badge, 6);
        g.FillPath(brush, path);
        g.DrawPath(pen, path);
        TextRenderer.DrawText(g, letter, BadgeFont, badge, color,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }

    private void DrawTitle(Graphics g)
    {
        var title = string.IsNullOrEmpty(Item!.Title) ? Item.Content : Item.Title;
        if (string.IsNullOrEmpty(title)) title = Item.Type switch
        {
            "link" => Item.Url,
            "file" => "图片",
            _ => "(空)",
        };
        var area = new Rectangle(44, 12, Width - 56, 22);
        TextRenderer.DrawText(g, TruncateLines(g, title, TitleFont, area.Width, 1), TitleFont, area, TextColor,
            TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
    }

    private void DrawSummary(Graphics g)
    {
        var text = Item!.Type switch
        {
            "link" => Item.Url,
            "file" => Item.FileMime == "image/png" ? $"PNG 图片 · {Item.FileSize / 1024} KB" : Item.FileName,
            _ => Item.Content,
        };
        var area = new Rectangle(12, 40, Width - 24, 38);
        TextRenderer.DrawText(g, TruncateLines(g, text, BodyFont, area.Width, 2), BodyFont, area, Muted,
            TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.EndEllipsis);
    }

    private void DrawMeta(Graphics g)
    {
        var time = FormatRelative(Item!.UpdatedAt);
        var area = new Rectangle(12, Height - 26, Width - 60, 18);
        TextRenderer.DrawText(g, time, MetaFont, area, Muted,
            TextFormatFlags.Left | TextFormatFlags.Bottom);
    }

    private void DrawFlags(Graphics g)
    {
        var x = Width - 12;
        if (Item!.Pinned)
        {
            var area = new Rectangle(x - 30, 12, 30, 18);
            TextRenderer.DrawText(g, "★", MetaFont, area, Gold,
                TextFormatFlags.Right | TextFormatFlags.Top);
        }
        if (Item.CopyCount > 0)
        {
            var area = new Rectangle(12, Height - 26, Width - 60, 18);
            var s = $"复制 {Item.CopyCount} 次";
            var sz = TextRenderer.MeasureText(s, MetaFont);
            var r = new Rectangle(Width - 12 - sz.Width, Height - 26, sz.Width, 18);
            TextRenderer.DrawText(g, s, MetaFont, r, Muted, TextFormatFlags.Right | TextFormatFlags.Bottom);
        }
    }

    // ---------------- 工具 ----------------

    private static GraphicsPath RoundedRect(Rectangle r, int radius)
    {
        var d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static string TruncateLines(Graphics g, string text, Font font, int width, int maxLines)
    {
        if (string.IsNullOrEmpty(text)) return "";
        // 逐行按宽度切分，最多 maxLines 行，末行加省略号
        var lines = new List<string>();
        var current = text;
        while (current.Length > 0 && lines.Count < maxLines)
        {
            int fit = 0, lo = 0, hi = current.Length;
            while (lo <= hi)
            {
                int mid = (lo + hi) / 2;
                var sz = TextRenderer.MeasureText(current[..mid], font);
                if (sz.Width <= width) { fit = mid; lo = mid + 1; } else hi = mid - 1;
            }
            if (fit <= 0) fit = 1;
            var line = current[..fit];
            if (lines.Count == maxLines - 1 && fit < current.Length) line = line[..Math.Max(1, line.Length - 1)] + "…";
            lines.Add(line);
            current = current[fit..];
            if (line.EndsWith("…")) break;
        }
        return string.Join(Environment.NewLine, lines);
    }

    public static string FormatRelative(long ms)
    {
        var diff = DateTimeOffset.Now.ToUnixTimeMilliseconds() - ms;
        if (diff < 60_000) return "刚刚";
        if (diff < 3_600_000) return $"{diff / 60_000} 分钟前";
        if (diff < 86_400_000) return $"{diff / 3_600_000} 小时前";
        var dt = DateTimeOffset.FromUnixTimeMilliseconds(ms).ToLocalTime();
        return dt.ToString("MM-dd HH:mm");
    }
}
