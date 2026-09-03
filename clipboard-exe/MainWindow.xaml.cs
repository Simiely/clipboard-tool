using System.ComponentModel;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ClipboardExe.Controls;
using ClipboardExe.Models;
using ClipboardExe.Services;

namespace ClipboardExe;

/// <summary>
/// 主窗体（M3a 编排层）：
///  - ★ 置顶按钮（M1）：Window.Topmost 切换 + 状态持久化
///  - 剪贴板监听：WM_CLIPBOARDUPDATE → 防抖 → 去重 → 弹存入/编辑窗（对齐 Web clipboardchange）
///  - 卡片墙：搜索 100ms 防抖 + 类型 RadioButton 过滤 + 标签过滤（轻量 tagbar）+ 自适应列数（ColumnsFor）
///  - 弹窗编排：存入/编辑/JSON 预览/确认 → ModalHost；数据变更走 ClipService（规则净化对齐 Web）
/// </summary>
public partial class MainWindow : Window
{
    private readonly Settings _settings;
    private readonly TrayIconService? _tray;
    private readonly Storage _storage;
    private readonly FileStore _fileStore; // M3b-2a：文件实体（data/files/）
    private readonly ClipService _svc;
    private ClipboardWatcher? _watcher; // OnSourceInitialized 后创建（需窗口句柄就绪）
    private readonly DispatcherTimer _searchTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private bool _reallyExit;
    private bool _everActivated;        // 首次激活（启动）不自动弹窗，仅后续切回才检测
    private uint? _lastHandledSeq;      // 已处理过的剪贴板序列号（激活/剪贴板事件共用；null = 尚未处理过任何内容）

    private const double Gap = 16; // .list gap:16px（卡片右/下外边距，MakeCard 使用）

    public MainWindow(Settings settings, TrayIconService? tray)
    {
        InitializeComponent();
        _settings = settings;
        _tray = tray;
        if (_tray != null) _tray.ShowMainRequested += ShowMainFromTray;

        // 服务装配（数据目录 exe 同目录 data/）
        _storage = new Storage(App.DataDir);
        _fileStore = new FileStore(App.DataDir);
        _svc = new ClipService(_storage, _fileStore);

        // M5c：启动定时自动同步（1 分钟轮询，到点才跑；编排逻辑在 SyncController；接线见 MainWindow.SyncOps.InitAutoSync）
        _sync = new SyncController(_storage, _fileStore, App.DataDir);
        InitAutoSync();

        // 弹窗宿主 + Toast 初始化（独立顶层 Window，可超出主窗口边界）
        ModalHost.Attach(this);
        ToastService.Init();

        // 搜索防抖（对齐 Web 100ms 微防抖：只防极速输入时的 DOM 重建）
        _searchTimer.Tick += (_, _) => { _searchTimer.Stop(); RefreshWall(); };

        // 置顶状态恢复
        PinBtn.IsChecked = _settings.AlwaysOnTop;
        Topmost = _settings.AlwaysOnTop;
        // 列数/归档按钮初值（M3b-1）
        UpdateColsBtnText();
        ArchBtn.Content = "归档·关";
        ArchBtn.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x84, 0x84, 0x84));

        Loaded += (_, _) => RefreshWall();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        this.EnableImmersiveDarkTitleBar();
        // 剪贴板监听：句柄就绪后创建 + 订阅 + 注册（Paused 初始 true，等待首次激活——对齐 Web 前台语义）
        _watcher = new ClipboardWatcher(new System.Windows.Interop.WindowInteropHelper(this).Handle) { Paused = true };
        _watcher.ClipboardChanged += OnClipboardChanged;
        _watcher.Attach();
    }

    // ---- 剪贴板自动提示（对齐 Web clipboardchange + 激活检测）：有内容→存卡；已有内容→编辑弹窗 ----
    //  两条触发路径（watcher 剪贴板事件 / OnActivated 窗口激活）统一走 TryAutoPrompt，用剪贴板序列号去重，
    //  避免"一次复制 + 切回窗口/关弹窗再激活"被两条路径各弹一次（连续弹两次窗）。
    private void OnClipboardChanged()
    {
        if (_watcher == null) return;
        TryAutoPrompt();
    }

    private void PromptFromActivation()
    {
        TryAutoPrompt();
    }

    /// <summary>自上次处理后剪贴板是否又变了一次（序列号判定）。返回 false = 同一份内容被重复触发，不重复弹窗。
    /// 用序列号而非内容比对：实测相同内容再次复制，序列号照样递增——那是真实的新复制，不该被吞掉。</summary>
    private bool TryTakeClipboardSeq()
    {
        var seq = ClipboardNative.SequenceNumber;
        if (_lastHandledSeq == seq) return false;
        _lastHandledSeq = seq;
        return true;
    }

    private void TryAutoPrompt()
    {
        if (ModalHost.IsOpen) return; // 已开弹窗不覆盖
        string text;
        try { text = (Clipboard.GetText() ?? "").Trim(); }
        catch { return; }
        if (text.Length == 0) return;
        if (!TryTakeClipboardSeq()) return; // 剪贴板未再变化（激活/事件/反复切回窗口都在这里收敛）
        // 比对（对齐 findDuplicateClip：link 比 url / 其他比 content）
        var dup = ClipService.FindDuplicate(text, _svc.Search(""));
        if (dup != null)
        {
            if (!dup.Archived) OpenEditDialog(dup, dup: true);
            else ToastService.Flash("已有相同内容");
            return;
        }
        OpenPasteDialog();
    }

    // ---- 弹窗编排 ----

    private void OpenPasteDialog()
    {
        var dlg = new PasteDialog(_svc, _fileStore, () => _svc.Search(""), GetAllTags);
        dlg.DuplicateFound += c =>
        {
            if (!c.Archived) OpenEditDialog(c, dup: true);
            else ToastService.Flash("已有相同内容");
        };
        dlg.Saved += RefreshWall;
        ModalHost.Show(dlg);
    }

    private void OpenEditDialog(ClipItem c, bool dup)
    {
        var dlg = new EditDialog(_svc, c, GetAllTags, dup);
        dlg.Saved += RefreshWall;
        dlg.Archived += RefreshWall;
        ModalHost.Show(dlg);
    }

    /// <summary>系统已有标签（聚合全部条目去重，对齐 /api/tags）。</summary>
    private List<string> GetAllTags()
        => _svc.Search("").SelectMany(c => c.Tags ?? new List<string>())
            .Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToList();

    // ---- 卡片墙渲染（搜索/类型/标签过滤 + 空状态双文案 + 自适应列数） ----

    private void RefreshWall()
    {
        var clips = _svc.Search(_q, _tagFilter, _typeFilter, includeArchived: _includeArchived);
        WallPanel.Children.Clear();
        _cards.Clear();
        _visibleIds = clips.Select(c => c.Id).ToList();
        foreach (var c in clips)
        {
            var card = MakeCard(c);
            card.BatchMode = _batchMode; // 批量模式：卡片显示 .sel-chk 覆盖层 + 单击切换选择
            card.SetSelected(_batchMode && _batchSel.Contains(c.Id)); // 重建后恢复选中态
            card.SelectionToggled += OnCardSelectionToggled;
            _cards[c.Id] = card;
            WallPanel.Children.Add(card);
        }

        var hasFilter = _q.Length > 0 || _tagFilter.Length > 0 || _typeFilter != "all" || _includeArchived;
        EmptyHint.Text = hasFilter
            ? "没有匹配的内容 — 试试调整搜索词、标签或类型"
            : "还没有内容 — 顶部粘贴框 Ctrl+V 即存，或拖文件进来";
        EmptyHint.Visibility = clips.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        RenderTagBar();   // 标签集合随活跃+归档变化同步刷新
        UpdateColumnWidth(); // 重建后同步列宽
        SyncBatchUI();    // 批量计数/全选按钮文案随可见集变化
    }

    /// <summary>卡片装配 + 事件接线（对齐 clipCard 各按钮 → MainWindow 编排）。</summary>
    private CardView MakeCard(ClipItem c)
    {
        var card = new CardView(fileId =>
        {
            try { return _fileStore.ReadAllBytes(fileId); }
            catch { return Array.Empty<byte>(); }
        });
        card.SetClip(c);
        // 卡片间距：WrapPanel 无 gap 概念，Web .list{gap:16px} 靠右/下外边距实现。
        // ItemWidth 已在 UpdateColumnWidth 里减掉一个 Gap，此处补上对应的右/下 Margin，
        // 否则每张卡紧贴排列（左侧视觉连成一片），Gap 只变成行尾空白。
        card.Margin = new Thickness(0, 0, Gap, Gap);
        card.EditRequested += cc => OpenEditDialog(cc, dup: false);
        card.TogglePinRequested += cc =>
        {
            try
            {
                var pinned = _svc.TogglePin(cc.Id);
                ToastService.Flash(pinned ? "已置顶" : "已取消置顶");
                RefreshWall(); // 置顶重排（对齐 Web refreshList）
            }
            catch (Exception ex) { ToastService.Error(ex.Message); }
        };
        card.DeleteRequested += cc =>
        {
            ModalHost.Confirm("删除这条内容？", () =>
            {
                try
                {
                    _svc.Delete(cc.Id);
                    if (cc.Type == "file" && !string.IsNullOrEmpty(cc.FileId)) _fileStore.Delete(cc.FileId); // 联动清理文件实体（对齐 Web 路由层 deleteFile）
                    ToastService.Flash("已删除");
                    RefreshWall();
                }
                catch (Exception ex) { ToastService.Error(ex.Message); }
            }, "删除");
        };
        card.DownloadRequested += cc =>
        {
            try
            {
                var dlg = new Microsoft.Win32.SaveFileDialog { FileName = cc.FileName ?? "file", Filter = "所有文件 (*.*)|*.*" };
                ModalHost.SuppressDismiss = true; // 子对话框期间屏蔽失焦自动关闭
                var ok = dlg.ShowDialog(this) == true;
                ModalHost.SuppressDismiss = false;
                if (ok)
                {
                    File.WriteAllBytes(dlg.FileName, _fileStore.ReadAllBytes(cc.FileId)); // 对齐 downloadFile：attachment 原名落盘
                    ToastService.Flash("已下载");
                }
            }
            catch (Exception ex) { ToastService.Error("下载失败: " + ex.Message); }
        };
        card.CopyImageRequested += cc => CopyImageToClipboard(cc); // M3b-2b：图片卡单击复制到系统剪贴板
        card.CopyRequested += (cc, x, y) =>
        {
            var text = (cc.Type == "link" ? cc.Url : cc.Content) ?? "";
            if (string.IsNullOrEmpty(text)) { ToastService.Error("没有可复制的内容"); return; }
            if (TryCopy(() => ClipboardHelper.SetText(text)))
            {
                card.MarkCopied();
                MarkSelfWriteAndBump(cc);
                ToastService.Flash("已复制", x, y);
            }
        };
        card.OpenJsonRequested += cc =>
        {
            var dlg = new JsonDialog(cc);
            // 复制 JSON 也属本程序写剪贴板：记下序列号，关窗后激活不再误弹存入窗
            dlg.Copied += () => MarkSelfWrite();
            dlg.SaveRequested += (c, newContent) =>
            {
                try
                {
                    // v0.6.11：带 html 的条目覆盖保存同步重建 html，防 content/html 不一致；标题/标签原样保留
                    var newHtml = !string.IsNullOrEmpty(c.Html) && newContent != (c.Content ?? "")
                        ? RichText.TextToHtml(newContent) : null;
                    if (_svc.Update(c.Id, c.Title, c.Tags, null, newContent, null, newHtml) != null)
                    {
                        ToastService.Flash("已覆盖保存");
                        RefreshWall();
                        ModalHost.Close();
                    }
                    else ToastService.Error("条目不存在");
                }
                catch (Exception ex) { ToastService.Error(ex.Message); }
            };
            ModalHost.Show(dlg);
        };
        card.OpenLinkRequested += cc =>
        {
            try
            {
                Process.Start(new ProcessStartInfo(cc.Url) { UseShellExecute = true });
            }
            catch { ToastService.Error("无法打开链接"); }
        };
        card.CopyPlainRequested += (cc, x, y) =>
        {
            if (TryCopy(() => ClipboardHelper.SetText(cc.Content ?? "")))
            {
                card.MarkCopied();
                MarkSelfWriteAndBump(cc);
                ToastService.Flash("已复制", x, y);
            }
        };
        card.CopyRichRequested += (cc, x, y) =>
        {
            bool ok;
            try { ok = RichText.CopyRich(cc.Html, cc.Content); }
            catch (Exception ex) { AppLog.Info("rich copy failed: " + ex); ok = false; }
            if (ok)
            {
                card.MarkCopied();
                MarkSelfWriteAndBump(cc);
                ToastService.Flash("富文本已复制（含格式）", x, y);
            }
            else ToastService.Error("富文本复制失败，请用独立窗口重试");
        };
        card.TagFilterRequested += tag =>
        {
            _tagFilter = (_tagFilter == tag) ? "" : tag; // 同一标签再点 → 清除（对齐 Web .tagchip toggle）
            RenderTagBar();
            RefreshWall();
        };
        card.RestoreRequested += cc =>
        {
            try
            {
                if (_svc.Unarchive(cc.Id)) ToastService.Flash("已恢复");
                else ToastService.Flash("活跃区已存在同 id，未恢复");
                RefreshWall(); // 恢复后归档区减少 + 活跃区可能新增（refreshWall 包含归档过滤）
                RenderTagBar(); // 标签集合可能变化（恢复条目带新标签）
            }
            catch (Exception ex) { ToastService.Error(ex.Message); }
        };
        return card;
    }

    /// <summary>复制图片到系统剪贴板（M3b-2b，对齐 Web copyImageToClipboard：解码文件为 BitmapSource 后走 ClipboardHelper；
    /// 成功 flash + 计数 + 来源抑制；失败降级 errToast，不打开预览（WPF 已有 toast 反馈）。</summary>
    private void CopyImageToClipboard(ClipItem c)
    {
        if (string.IsNullOrEmpty(c.FileId)) { ToastService.Error("图片缺失"); return; }
        var bytes = _fileStore.ReadAllBytes(c.FileId);
        if (bytes == null || bytes.Length == 0) { ToastService.Error("图片为空"); return; }
        BitmapSource? bmp = null;
        try
        {
            using var ms = new MemoryStream(bytes);
            var bi = new BitmapImage();
            bi.BeginInit(); bi.CacheOption = BitmapCacheOption.OnLoad; bi.StreamSource = ms; bi.EndInit(); bi.Freeze();
            bmp = bi;
        }
        catch (Exception ex) { AppLog.Info("decode image failed: " + ex.Message); ToastService.Error("图片解码失败"); return; }
        if (bmp != null && TryCopy(() => ClipboardHelper.SetImage(bmp)))
        {
            MarkSelfWriteAndBump(c);
            ToastService.FlashAtMouse("图片已复制，可直接粘贴");
        }
    }

    /// <summary>统一复制写入：成功返回 true；失败记录异常（含剪贴板占用方诊断）并弹错误 toast（消除 5 处重复处理）。</summary>
    private static bool TryCopy(Action write, string? context = null)
    {
        try { write(); return true; }
        catch (Exception ex)
        {
            AppLog.Info($"copy failed{(context != null ? " (" + context + ")" : "")}: {ex.GetType().Name}(0x{unchecked((uint)ex.HResult):X8}): {ex.Message}");
            ToastService.Error("复制失败，请手动选择复制");
            return false;
        }
    }

    /// <summary>复制成功后：标记为"本程序自己写入"+ 持久化复制计数（对齐 Web bumpCopyCount）。
    /// 两条自动弹窗路径各自用确定性判定收敛，都不依赖时间窗：
    ///   - 剪贴板事件路径：watcher 在 WM 到达瞬间用属主 PID 判定属本进程 → 忽略；
    ///   - 激活路径（OnActivated → TryAutoPrompt）：用剪贴板序列号判重，写入后记下新序列号 → 切回不再误弹。</summary>
    private void MarkSelfWriteAndBump(ClipItem c)
    {
        MarkSelfWrite();
        try { _svc.BumpCopyCount(c.Id); } catch { /* 计数失败不影响 */ }
    }

    /// <summary>记录"本程序刚写入剪贴板"：把当前序列号记为已处理，激活路径据此不再弹窗（对齐 Web 语义）。</summary>
    private void MarkSelfWrite() => _lastHandledSeq = ClipboardNative.SequenceNumber;

    /// <summary>列宽自适应（对齐 CSS auto-fill minmax(280px,1fr)；仅改 ItemWidth 不重建卡片——拖动窗口不闪烁）。
    /// maxColumns 来自 Settings：0 = 自动 4 上限，1~4 = 用户锁定（M3b-1 接入）。</summary>
    private void UpdateColumnWidth()
    {
        var w = WallPanel.ActualWidth;
        if (w <= 0) return;
        var cols = LayoutRules.ColumnsFor(w, _settings.MaxColumns);
        // ItemWidth 只负责「卡片 + 右间隙」的槽位宽度，不能再减 Gap：
        // 卡片自身已有 Margin.Right=Gap（见 MakeCard），若这里再减一次，
        // 每行右端会白白空出 (cols+1)*Gap —— 看起来像「给滚动条留了一大条空位」。
        // Floor 防止 cols 个槽位因小数累加超出 w 而误换行。
        WallPanel.ItemWidth = Math.Floor(w / cols);
    }

    private void WallPanel_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateColumnWidth();

    // ---- 窗口生命周期：置顶 / 前台语义 / 退出 ----

    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        if (_watcher != null) _watcher.Paused = false; // 前台激活恢复捕获
        if (!_everActivated) { _everActivated = true; return; } // 首次启动不自动弹窗
        PromptFromActivation(); // 切回窗口时：剪贴板有内容则弹存卡/编辑窗
    }

    protected override void OnDeactivated(EventArgs e)
    {
        base.OnDeactivated(e);
        if (_watcher != null) _watcher.Paused = true; // 失活暂停（对齐 Web 前台语义）
    }

    // ---- 全局快捷键（仅在主窗口已激活、无弹窗、且焦点不在文本框内时生效） ----
    //  - Ctrl+V：快速打开存入编辑器并自动填入剪贴板内容（PasteDialog 打开即 autoFill）
    //  - 空格：把输入光标快速定位到搜索框（打字即可搜索）；空格本身不落入搜索框
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);

        // 已有弹窗时不拦截（交给弹窗自身处理，避免重复开窗或吞掉弹窗内的空格/粘贴）
        if (ModalHost.IsOpen) return;
        // 焦点已在文本框（搜索框/存入框等）：保留原生行为（空格正常输入、Ctrl+V 正常粘贴）
        if (Keyboard.FocusedElement is TextBox or PasswordBox or ComboBox) return;

        if (e.Key == Key.V && (Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            e.Handled = true;
            OpenPasteDialog(); // 打开即自动填入剪贴板内容
            return;
        }

        if (e.Key == Key.Space)
        {
            e.Handled = true; // 空格不落入搜索框
            SearchBox.Focus();
            SearchBox.SelectAll(); // 全选既有查询，直接输入即新检索（对齐 Web 平台版）
        }
    }

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        if (WindowState == WindowState.Minimized && _watcher != null) _watcher.Paused = true;
    }

    /// <summary>★ 置顶切换：金=开，状态持久化。</summary>
    private void PinBtn_Click(object sender, RoutedEventArgs e)
    {
        Topmost = PinBtn.IsChecked == true;
        _settings.AlwaysOnTop = Topmost;
        _settings.Save();
        AppLog.Info("always-on-top: " + Topmost);
    }

    /// <summary>宽屏自适应（规则在 LayoutRules.MaxWidthFor，纯函数可单测）。
    /// 阈值基于客户区宽度（ActualWidth ≈ Web viewport），而非窗口总宽 e.NewSize.Width（含 DWM 边框 ~16px），
    /// 避免临界 1280 早一档切换。兜底走 e.NewSize.Width - 16（边框经验值）。</summary>
    private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var w = ActualWidth > 0 ? ActualWidth : e.NewSize.Width - 16;
        if (w <= 0) return;
        ViewGrid.MaxWidth = LayoutRules.MaxWidthFor(w);
    }

    private void StoreBtn_Click(object sender, RoutedEventArgs e) => OpenPasteDialog();

    private void ExitBtn_Click(object sender, RoutedEventArgs e) => ReallyExit();

    public void ReallyExit()
    {
        _reallyExit = true;
        AppLog.Info("exit");
        Application.Current.Shutdown();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        // 点 X / 最小化 → 收进托盘（不退出、停止可见），托盘「退出」才真退
        if (!_reallyExit && _tray != null)
        {
            e.Cancel = true;
            Hide();
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        _watcher?.Dispose();
    }

    private void ShowMainFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    // 同步逻辑已抽到 MainWindow.SyncOps.cs（partial class）
}
