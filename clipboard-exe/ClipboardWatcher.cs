using System.Drawing.Imaging;
using System.Text.RegularExpressions;

namespace ClipboardExe;

/// <summary>
/// 剪贴板捕获（对齐 Web 版交互：检测到复制 → 交给 UI 弹存入确认窗）。
///  - 类型识别：URL → link；纯文本 → text；图片 → file（PNG 实体先写 data/files/，确认后保留/放弃清理）
///  - 去重：与最近一条同 type+同内容相同 → 不弹窗（静默刷新 updatedAt 置顶，不产生重复条目）
///  - 前台开关：仅主窗口激活时自动捕获（用户确认，隐私优先）；手动存入（CaptureNow）不受限
///  - 自身回写抑制：点击卡片复制写回剪贴板 → 抑制窗口跳过（Web v0.1.3 同款思路）
///  - 防叠加：确认弹窗已打开时忽略新的捕获事件
/// </summary>
public sealed class ClipboardWatcher
{
    private static readonly Regex UrlRe = new(@"^https?://\S+$", RegexOptions.IgnoreCase);
    private const long SuppressWindowMs = 800;
    private const int MaxHtml = 512 * 1024;

    private readonly Storage _storage;
    private readonly Action<ClipItem> _onPendingCapture;
    private long _suppressUntilMs;

    /// <summary>自动捕获开关：仅主窗口激活（前台）时生效，由 MainForm 的 Activated/Deactivate 切换。</summary>
    public bool CaptureEnabled { get; set; } = true;

    /// <summary>确认弹窗已打开时置 true：忽略新的捕获事件（防叠加）。</summary>
    public bool DialogOpen { get; set; }

    public ClipboardWatcher(Storage storage, Action<ClipItem> onPendingCapture)
    {
        _storage = storage;
        _onPendingCapture = onPendingCapture;
    }

    public static bool Start(IntPtr hwnd) => NativeMethods.AddClipboardFormatListener(hwnd);
    public static bool Stop(IntPtr hwnd) => NativeMethods.RemoveClipboardFormatListener(hwnd);

    /// <summary>点击卡片复制前调用：抑制接下来一次剪贴板变更，避免自身回写被误捕获。</summary>
    public void SuppressNext() => _suppressUntilMs = DateTimeOffset.Now.ToUnixTimeMilliseconds() + SuppressWindowMs;

    /// <summary>由 MainForm WndProc 转发剪贴板变更消息（UI 线程）。仅前台激活时捕获。</summary>
    public void OnClipboardUpdate()
    {
        if (!CaptureEnabled) return; // 窗口未激活 → 不读剪贴板（隐私优先）
        if (DialogOpen) return;      // 已有确认窗 → 忽略（防叠加）
        if (DateTimeOffset.Now.ToUnixTimeMilliseconds() < _suppressUntilMs) return;
        TryReadClipboard();
    }

    /// <summary>手动存入（工具栏「存入」/ 空格快捷键）：立即读取当前剪贴板，不受前台开关限制。</summary>
    public void CaptureNow()
    {
        if (DialogOpen) return;
        TryReadClipboard();
    }

    private void TryReadClipboard()
    {
        try
        {
            // 图片优先：浏览器/Excel 复制图片时剪贴板可能同时带文本
            if (Clipboard.ContainsImage()) { CaptureImage(); return; }
            if (Clipboard.ContainsText())
            {
                var text = Clipboard.GetText().Trim();
                if (text.Length > 0) CaptureText(text);
            }
        }
        catch
        {
            // 剪贴板被其他程序锁定（OLE 占用）等：静默，下个事件再试
        }
    }

    // ---------------- 捕获实现（构建 pending，交 UI 确认） ----------------

    private void CaptureText(string text)
    {
        var now = DateTimeOffset.Now.ToUnixTimeMilliseconds();
        var type = UrlRe.IsMatch(text) ? "link" : "text";
        // link 对齐 Web 版语义：content 留空、url 存清理后的链接（去追踪参数）、title 取 url 前 60
        var url = type == "link" ? CleanUrl.Clean(text) : "";
        var content = type == "link" ? "" : text;
        var title = type == "link" ? (url.Length > 60 ? url[..60] : url) : "";

        // 富文本 html：仅 text 类型（link 对齐 Web 版不存 html）；从剪贴板读 CF_HTML 片段
        var html = "";
        if (type == "text")
        {
            try
            {
                if (Clipboard.ContainsText(TextDataFormat.Html))
                {
                    html = Clipboard.GetText(TextDataFormat.Html);
                    if (html.Length > MaxHtml) html = html[..MaxHtml];
                }
            }
            catch { /* CF_HTML 读取失败不影响纯文本捕获 */ }
        }

        // 去重：与最新一条同 type + 同内容（text 比 content / link 比 url）相同 → 静默刷新置顶，不弹窗
        var list = _storage.Load();
        var latest = list.Where(c => !c.Archived)
                         .OrderByDescending(c => c.UpdatedAt)
                         .FirstOrDefault();
        var sameContent = type == "link"
            ? latest != null && latest.Type == "link" && latest.Url == url
            : latest != null && latest.Type == "text" && latest.Content == text;
        if (sameContent)
        {
            latest!.UpdatedAt = now;
            _storage.Save(list);
            return;
        }

        var clip = new ClipItem
        {
            Id = Guid.NewGuid().ToString(),
            Type = type,
            Content = content,
            Html = html,
            Title = title,
            Url = url,
            CreatedAt = now,
            UpdatedAt = now,
        };
        AppLog.Info($"pending {type}: {(type == "link" ? url : text[..Math.Min(40, text.Length)])}...");
        _onPendingCapture(clip);
    }

    private void CaptureImage()
    {
        var now = DateTimeOffset.Now.ToUnixTimeMilliseconds();
        using var img = Clipboard.GetImage();
        if (img == null) return;

        byte[] bytes;
        using (var ms = new MemoryStream())
        {
            img.Save(ms, ImageFormat.Png);
            bytes = ms.ToArray();
        }
        if (bytes.Length == 0) return;

        // 先写实体：确认后保留，放弃由 UI 清理
        var fileId = _storage.SaveImage(bytes, "image/png", ".png");
        var clip = new ClipItem
        {
            Id = Guid.NewGuid().ToString(),
            Type = "file",
            Content = "图片",
            Title = "图片",
            FileId = fileId,
            FileName = "image-" + now + ".png",
            FileSize = bytes.Length,
            FileMime = "image/png",
            CreatedAt = now,
            UpdatedAt = now,
        };
        AppLog.Info($"pending image {bytes.Length} bytes");
        _onPendingCapture(clip);
    }
}
