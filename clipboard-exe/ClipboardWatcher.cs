using System.Drawing.Imaging;
using System.Text.RegularExpressions;

namespace ClipboardExe;

/// <summary>
/// 剪贴板捕获：Win32 AddClipboardFormatListener 收到变更后读取内容并落存储。
///  - 类型识别：URL → link；纯文本 → text；图片 → file（fileMime=image/*，实体存 data/files/）
///  - 去重：与最近一条（同 type + 同内容）相同则跳过——外部重复复制不产生重复条目
///  - 自身回写抑制：点击卡片复制会把内容写回剪贴板 → 触发监听，用抑制窗口跳过（Web 版 v0.1.3 同款思路）
///  - 在 UI 线程运行（WndProc 转发），剪贴板访问需 STA；占用/异常一律静默
/// </summary>
public sealed class ClipboardWatcher
{
    private static readonly Regex UrlRe = new(@"^https?://\S+$", RegexOptions.IgnoreCase);
    private const long SuppressWindowMs = 800;

    private readonly Storage _storage;
    private readonly Action _onCaptured;
    private long _suppressUntilMs;

    /// <summary>
    /// 自动捕获开关：仅当主窗口处于激活（前台）状态时才捕获剪贴板。
    /// 由 MainForm 的 Activated/Deactivate 事件切换——窗口未激活/最小化到托盘时不读剪贴板（隐私优先，用户确认 2026-08-28）。
    /// 手动存入（CaptureNow）不受此开关限制（显式操作）。
    /// </summary>
    public bool CaptureEnabled { get; set; } = true;

    public ClipboardWatcher(Storage storage, Action onCaptured)
    {
        _storage = storage;
        _onCaptured = onCaptured;
    }

    public static bool Start(IntPtr hwnd) => NativeMethods.AddClipboardFormatListener(hwnd);
    public static bool Stop(IntPtr hwnd) => NativeMethods.RemoveClipboardFormatListener(hwnd);

    /// <summary>点击卡片复制前调用：抑制接下来一次剪贴板变更，避免自身回写被误捕获。</summary>
    public void SuppressNext() => _suppressUntilMs = DateTimeOffset.Now.ToUnixTimeMilliseconds() + SuppressWindowMs;

    /// <summary>由 MainForm WndProc 转发剪贴板变更消息（UI 线程）。仅前台激活时捕获。</summary>
    public void OnClipboardUpdate()
    {
        if (!CaptureEnabled) return; // 窗口未激活 → 不读剪贴板
        if (DateTimeOffset.Now.ToUnixTimeMilliseconds() < _suppressUntilMs) return;
        try
        {
            // 图片优先：浏览器/Excel 复制图片时剪贴板可能同时带文本
            if (Clipboard.ContainsImage())
            {
                CaptureImage();
                return;
            }
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

    /// <summary>手动存入（工具栏"存入"按钮）：立即读取当前剪贴板并捕获，不受抑制窗口影响。</summary>
    public void CaptureNow()
    {
        try
        {
            if (Clipboard.ContainsImage()) { CaptureImage(); return; }
            if (Clipboard.ContainsText())
            {
                var text = Clipboard.GetText().Trim();
                if (text.Length > 0) CaptureText(text);
            }
        }
        catch { /* 静默 */ }
    }

    // ---------------- 捕获实现 ----------------

    /// <summary>HTML 片段大小上限（对齐 Web 版 CONFIG.MAX_HTML = 512KB）。</summary>
    private const int MaxHtml = 512 * 1024;

    private void CaptureText(string text)
    {
        var now = DateTimeOffset.Now.ToUnixTimeMilliseconds();
        var type = UrlRe.IsMatch(text) ? "link" : "text";
        // link 对齐 Web 版语义：content 留空、url 存清理后的链接（去追踪参数）、title 取 url 前 60
        var url = type == "link" ? CleanUrl.Clean(text) : "";
        var content = type == "link" ? "" : text;
        var title = type == "link" ? (url.Length > 60 ? url[..60] : url) : "";

        // 富文本 html：仅 text 类型有意义（link 对齐 Web 版不存 html）；从剪贴板读 CF_HTML 片段
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

        var list = _storage.Load();
        // 去重：与最新一条同 type + 同内容（text 比 content / link 比 url）相同 → 跳过（刷新 updatedAt 置顶）
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
        _storage.Add(clip);
        AppLog.Info($"captured {type}: {(type == "link" ? url : text[..Math.Min(40, text.Length)])}...");
        _onCaptured();
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
        _storage.Add(clip);
        AppLog.Info($"captured image {bytes.Length} bytes");
        _onCaptured();
    }
}
