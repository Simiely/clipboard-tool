using System.Drawing;
using System.Drawing.Drawing2D;

namespace ClipboardExe;

/// <summary>
/// 类型化卡片（对齐 Web 版 clipCard + makeCardBody + makeRichSplit + 按钮行）：
///  - 类型徽章：文本 T（灰）/ 链接 L（砖红）/ 图片 I（金）
///  - 内容区按类型：文本摘要（+JSON {} 按钮 + 富文本分栏 T|✦）、链接 host 徽章 + URL、图片缩略图
///  - 卡片按钮行：★置顶 / ✎编辑 / ↺归档恢复 / ✕删除
///  - 整卡点击 = 复制（文本/链接复制内容、图片复制为 PNG）
///  - 富文本分栏：内容区底部「T 普通文本 | ✦ 富文本」，点击分别复制纯文本/富文本（对齐 makeRichSplit）
/// 事件由 MainForm 订阅处理（复制/编辑/删除等），本控件只负责绘制与命中。
/// </summary>
public sealed class CardControl : UserControl
{
    public const int CardWidth = 280;
    public const int CardHeight = 142;

    public event EventHandler? CopyRequested;
    public event EventHandler? RichCopyRequested;
    public event EventHandler? JsonRequested;
    public event EventHandler? PinRequested;
    public event EventHandler? EditRequested;
    public event EventHandler? DeleteRequested;
    public event EventHandler? RestoreRequested;
    public event EventHandler? BatchToggleRequested;

    private static readonly Color Bg = Color.FromArgb(0x1F, 0x1F, 0x1F);        // --elev
    private static readonly Color Border = Color.FromArgb(0x2A, 0x2A, 0x2A);
    private static readonly Color BorderHover = Color.FromArgb(0xC9, 0xA9, 0x6E); // 悬停金边
    private static readonly Color TextColor = Color.FromArgb(0xDA, 0xDA, 0xDA);
    private static readonly Color Muted = Color.FromArgb(0x84, 0x84, 0x84);
    private static readonly Color Gold = Color.FromArgb(0xC9, 0xA9, 0x6E);
    private static readonly Color Accent2 = Color.FromArgb(0xAE, 0x4D, 0x4D);     // 链接砖红
    private static readonly Color Divider = Color.FromArgb(0x2E, 0x2E, 0x2E);

    private static readonly Font TitleFont = new("Microsoft YaHei UI", 10f, FontStyle.Bold);
    private static readonly Font BodyFont = new("Microsoft YaHei UI", 8.8f);
    private static readonly Font MetaFont = new("Microsoft YaHei UI", 8f);
    private static readonly Font SplitFont = new("Microsoft YaHei UI", 8.5f);

    private readonly ClipItem _item;
    private readonly Storage _storage;
    private readonly ToolTip _tip;
    private bool _hovered;
    private bool _richHovered;   // 富文本分栏右栏 hover

    /// <summary>批量编辑模式（勾选框显示，整卡点击=切换选中而非复制）。</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool BatchMode { get; set; }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool BatchChecked { get; set; }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public ClipItem Item => _item;

    public CardControl(ClipItem item, Storage storage)
    {
        _item = item;
        _storage = storage;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
               | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Size = new Size(CardWidth, CardHeight);
        Cursor = Cursors.Hand;
        _tip = new ToolTip { AutoPopDelay = 4000, InitialDelay = 400 };
    }

    protected override void OnMouseEnter(EventArgs e) { _hovered = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hovered = false; _richHovered = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        var rich = RichSplitRect().Contains(e.Location);
        if (rich != _richHovered) { _richHovered = rich; Invalidate(); }
        base.OnMouseMove(e);
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        if (BatchMode)
        {
            // 批量模式：整卡点击 = 切换选中（对齐 Web setBatchMode 勾选交互）
            BatchChecked = !BatchChecked;
            Invalidate();
            BatchToggleRequested?.Invoke(this, EventArgs.Empty);
            return;
        }
        var p = e.Location;
        if (RichSplitRect().Contains(p) && HasRich) { RichCopyRequested?.Invoke(this, EventArgs.Empty); return; }
        if (JsonBtnRect().Contains(p)) { JsonRequested?.Invoke(this, EventArgs.Empty); return; }
        if (PinBtnRect().Contains(p)) { PinRequested?.Invoke(this, EventArgs.Empty); return; }
        if (EditBtnRect().Contains(p)) { EditRequested?.Invoke(this, EventArgs.Empty); return; }
        if (RestoreBtnRect().Contains(p)) { RestoreRequested?.Invoke(this, EventArgs.Empty); return; }
        if (DeleteBtnRect().Contains(p)) { DeleteRequested?.Invoke(this, EventArgs.Empty); return; }
        CopyRequested?.Invoke(this, EventArgs.Empty); // 整卡点击 = 复制
    }

    private bool HasRich => !string.IsNullOrEmpty(_item.Html);
    private bool LooksJson => _item.Type == "text" && (_item.Content.TrimStart().StartsWith('{') || _item.Content.TrimStart().StartsWith('['));

    // ---------------- 绘制 ----------------

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        using var path = RoundedRect(rect, 12);
        using var bg = new SolidBrush(Bg);
        g.FillPath(bg, path);
        using var pen = new Pen(_hovered ? BorderHover : Border, _hovered ? 1.6f : 1f);
        g.DrawPath(pen, path);

        if (BatchMode) DrawBatchCheck(g);
        DrawBadge(g);
        DrawTitle(g);
        DrawBody(g);
        DrawButtons(g);
        DrawMeta(g);
    }

    private void DrawBatchCheck(Graphics g)
    {
        var r = new Rectangle(6, 8, 16, 16);
        using var brush = new SolidBrush(BatchChecked ? Gold : Color.FromArgb(0x1A, 0x1A, 0x1A));
        using var pen = new Pen(BatchChecked ? Gold : Muted);
        g.FillRectangle(brush, r);
        g.DrawRectangle(pen, r);
        if (BatchChecked)
        {
            using var f = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold);
            TextRenderer.DrawText(g, "✓", f, r, Color.FromArgb(0x1A, 0x1A, 0x1A),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }

    private void DrawBadge(Graphics g)
    {
        var (letter, color) = _item.Type switch
        {
            "link" => ("L", Accent2),
            "file" => ("I", Gold),
            _ => ("T", Muted),
        };
        var badge = new Rectangle(12, 12, 24, 24);
        using var path = RoundedRect(badge, 7);
        using var brush = new SolidBrush(Color.FromArgb(0x2A, 0x2A, 0x2A));
        using var pen = new Pen(color);
        g.FillPath(brush, path);
        g.DrawPath(pen, path);
        TextRenderer.DrawText(g, letter, new Font("Microsoft YaHei UI", 9f, FontStyle.Bold), badge, color,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }

    private void DrawTitle(Graphics g)
    {
        var title = string.IsNullOrEmpty(_item.Title)
            ? _item.Type switch { "link" => _item.Url, "file" => "图片", _ => _item.Content }
            : _item.Title;
        var area = new Rectangle(44, 12, Width - 110, 22);
        TextRenderer.DrawText(g, TruncateLines(g, title, TitleFont, area.Width, 1), TitleFont, area, TextColor,
            TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        // 右上：置顶★ / 复制次数
        if (_item.Pinned)
            TextRenderer.DrawText(g, "★", MetaFont, new Rectangle(Width - 30, 10, 22, 18), Gold,
                TextFormatFlags.Right | TextFormatFlags.Top);
        if (_item.CopyCount > 0)
            TextRenderer.DrawText(g, $"×{_item.CopyCount}", MetaFont, new Rectangle(Width - 30, 26, 26, 14), Muted,
                TextFormatFlags.Right | TextFormatFlags.Top);
    }

    private void DrawBody(Graphics g)
    {
        switch (_item.Type)
        {
            case "link": DrawLinkBody(g); break;
            case "file": DrawFileBody(g); break;
            default: DrawTextBody(g); break;
        }
    }

    private void DrawTextBody(Graphics g)
    {
        var area = new Rectangle(12, 40, Width - 24, HasRich ? 36 : 52);
        TextRenderer.DrawText(g, TruncateLines(g, _item.Content, BodyFont, area.Width, HasRich ? 2 : 3),
            BodyFont, area, Muted, TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.EndEllipsis);
        if (HasRich) DrawRichSplit(g);
    }

    private void DrawLinkBody(Graphics g)
    {
        // host 徽章（对齐 Web makeLinkBody：圆角小块 + host）
        var host = HostOf(_item.Url);
        var badgeSize = TextRenderer.MeasureText(host, SplitFont);
        var badgeRect = new Rectangle(12, 42, badgeSize.Width + 14, 20);
        using var path = RoundedRect(badgeRect, 10);
        using var brush = new SolidBrush(Color.FromArgb(0x2A, 0x22, 0x22));
        using var pen = new Pen(Accent2);
        g.FillPath(brush, path);
        g.DrawPath(pen, path);
        TextRenderer.DrawText(g, host, SplitFont, badgeRect, Accent2,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        // URL 一行
        var area = new Rectangle(12, 68, Width - 24, 20);
        TextRenderer.DrawText(g, TruncateLines(g, _item.Url, BodyFont, area.Width, 1), BodyFont, area, Muted,
            TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
    }

    private void DrawFileBody(Graphics g)
    {
        // 缩略图（左）+ 文件名/大小
        try
        {
            var bytes = _storage.LoadImage(_item.FileId);
            if (bytes != null)
            {
                using var ms = new MemoryStream(bytes);
                using var bmp = new Bitmap(ms);
                var thumb = new Rectangle(12, 42, 70, 50);
                g.DrawImage(bmp, thumb);
            }
        }
        catch { /* 缩略图失败显示文字 */ }
        var area = new Rectangle(90, 44, Width - 102, 34);
        TextRenderer.DrawText(g, TruncateLines(g, _item.FileName, BodyFont, area.Width, 2), BodyFont, area, Muted,
            TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.EndEllipsis);
        TextRenderer.DrawText(g, $"{_item.FileSize / 1024} KB · {(_item.FileMime.StartsWith("image/") ? "图片" : "文件")}",
            MetaFont, new Rectangle(90, 74, Width - 102, 14), Muted, TextFormatFlags.Left);
    }

    private void DrawRichSplit(Graphics g)
    {
        // 富文本分栏：T 普通文本 | ✦ 富文本（对齐 Web makeRichSplit：左纯文本右富文本）
        var r = RichSplitRect();
        using var pen = new Pen(Divider);
        g.DrawLine(pen, r.Left, r.Top, r.Right, r.Top);
        var left = new Rectangle(r.Left, r.Top, r.Width / 2, r.Height);
        var right = new Rectangle(r.Left + r.Width / 2, r.Top, r.Width - r.Width / 2, r.Height);
        using var leftBrush = new SolidBrush(Color.FromArgb(0x24, 0x24, 0x24));
        using var rightBrush = new SolidBrush(Color.FromArgb(0x2E, 0x28, 0x18));
        g.FillRectangle(leftBrush, left);
        g.FillRectangle(rightBrush, right);
        g.DrawLine(pen, left.Right, left.Top, left.Right, left.Bottom);
        TextRenderer.DrawText(g, "T 普通文本", SplitFont, left, TextColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        TextRenderer.DrawText(g, "✦ 富文本", SplitFont, right, Gold,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }

    private void DrawButtons(Graphics g)
    {
        // 底部按钮行：↺归档/恢复 {}JSON ✕删除 ✎编辑 ★置顶（右对齐）
        var btns = new (Rectangle r, string t, string txt)[]
        {
            (RestoreBtnRect(), "归档/恢复", _item.Archived ? "↺" : "▤"),
            (JsonBtnRect(), "JSON 预览", "{}"),
            (DeleteBtnRect(), "删除", "✕"),
            (EditBtnRect(), "编辑", "✎"),
            (PinBtnRect(), "置顶", _item.Pinned ? "★" : "☆"),
        };
        foreach (var (r, tip, txt) in btns)
        {
            if (txt == "{}" && !LooksJson) continue; // 非 JSON 不显示 {} 按钮
            if (txt == "↺" || txt == "▤") { /* 归档按钮恒显示 */ }
            var hover = _hovered && r.Contains(PointToClient(Cursor.Position));
            using var brush = new SolidBrush(hover ? Color.FromArgb(0x33, 0x33, 0x33) : Color.FromArgb(0x24, 0x24, 0x24));
            using var path = RoundedRect(r, 6);
            g.FillPath(brush, path);
            var color = txt == "★" ? Gold : (txt == "✕" ? Color.FromArgb(0xE0, 0x8A, 0x7A) : Muted);
            TextRenderer.DrawText(g, txt, MetaFont, r, color,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            _tip.SetToolTip(this, _tip.GetToolTip(this) ?? "");
        }
    }

    private void DrawMeta(Graphics g)
    {
        TextRenderer.DrawText(g, CardControl.FormatRelative(_item.UpdatedAt), MetaFont,
            new Rectangle(12, Height - 24, Width - 60, 16), Muted,
            TextFormatFlags.Left | TextFormatFlags.Bottom);
        if (_item.Archived)
            TextRenderer.DrawText(g, "已归档", MetaFont, new Rectangle(Width - 56, Height - 24, 48, 16), Gold,
                TextFormatFlags.Right | TextFormatFlags.Bottom);
    }

    // ---------------- 命中区域 ----------------

    private Rectangle RichSplitRect() => new(12, Height - 62, Width - 24, 22);
    private Rectangle JsonBtnRect() => new(Width - 118, Height - 28, 24, 20);
    private Rectangle RestoreBtnRect() => new(Width - 142, Height - 28, 24, 20);
    private Rectangle DeleteBtnRect() => new(Width - 94, Height - 28, 24, 20);
    private Rectangle EditBtnRect() => new(Width - 70, Height - 28, 24, 20);
    private Rectangle PinBtnRect() => new(Width - 46, Height - 28, 24, 20);

    // ---------------- 工具 ----------------

    private static string HostOf(string url)
    {
        try { return new Uri(url).Host.Replace("www.", ""); } catch { return url; }
    }

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
        var lines = new List<string>();
        var current = text;
        while (current.Length > 0 && lines.Count < maxLines)
        {
            int fit = 0, lo = 0, hi = current.Length;
            while (lo <= hi)
            {
                int mid = (lo + hi) / 2;
                if (TextRenderer.MeasureText(current[..mid], font).Width <= width) { fit = mid; lo = mid + 1; }
                else hi = mid - 1;
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
        return DateTimeOffset.FromUnixTimeMilliseconds(ms).ToLocalTime().ToString("MM-dd HH:mm");
    }
}
