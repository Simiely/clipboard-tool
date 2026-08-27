using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace ClipboardExe;

/// <summary>
/// 程序化生成托盘/窗口图标（黑底 + 金色剪贴板图形），避免依赖外部 .ico 文件。
/// 配色与 Web 版对齐：底 #1F1F1F、金 #C9A96E。
/// </summary>
internal static class IconFactory
{
    public static Icon Create()
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            using var bg = new SolidBrush(Color.FromArgb(0x1F, 0x1F, 0x1F));
            using var gold = new SolidBrush(Color.FromArgb(0xC9, 0xA9, 0x6E));

            // 剪贴板底板（圆角矩形）
            var path = new GraphicsPath();
            AddRoundedRect(path, 5f, 5f, 22f, 23f, 3f);
            g.FillPath(bg, path);

            // 顶夹
            g.FillRectangle(gold, 12f, 2f, 8f, 4f);

            // 三条内容线
            using var line = new Pen(gold, 2f);
            g.DrawLine(line, 10f, 12f, 22f, 12f);
            g.DrawLine(line, 10f, 17f, 22f, 17f);
            g.DrawLine(line, 10f, 22f, 18f, 22f);
        }

        var hIcon = bmp.GetHicon();
        try
        {
            // Clone 出独立副本后释放原始句柄，避免 Icon 生命周期泄漏
            return (Icon)Icon.FromHandle(hIcon).Clone();
        }
        finally
        {
            NativeMethods.DestroyIcon(hIcon);
        }
    }

    private static void AddRoundedRect(GraphicsPath path, float x, float y, float w, float h, float r)
    {
        float d = r * 2f;
        path.AddArc(x, y, d, d, 180, 90);
        path.AddArc(x + w - d, y, d, d, 270, 90);
        path.AddArc(x + w - d, y + h - d, d, d, 0, 90);
        path.AddArc(x, y + h - d, d, d, 90, 90);
        path.CloseFigure();
    }
}
