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
    private string? _lastPrompted;      // 已就此剪贴板内容弹过窗，避免重复弹出（激活/剪贴板事件共用）

    // 过滤状态（对齐 state.filter）
    private string _q = "";
    private string _tagFilter = "";
    private string _typeFilter = "all";
    private bool _includeArchived; // M3b-1：归档按钮 toggle 状态（默认 false）

    private const double Gap = 16; // .list gap:16px

    // 批量编辑状态（M3b-3b：进入批量模式后维护；_batchSel 跨刷新持久，卡片按 id 重渲染选中态）
    private bool _batchMode;
    private readonly HashSet<string> _batchSel = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CardView> _cards = new(StringComparer.Ordinal);
    private List<string> _visibleIds = new();

    private readonly SyncController _sync; // M5c：WebDAV 同步编排（从 UI 层下沉，降低耦合）
    private readonly DispatcherTimer _autoTimer = new() { Interval = TimeSpan.FromMinutes(1) }; // M5c：定时自动同步

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

        // M5：启动定时自动同步（1 分钟轮询，到点才跑；编排逻辑在 SyncController）
        _sync = new SyncController(_storage, _fileStore, App.DataDir);
        _autoTimer.Tick += (_, _) => _ = _sync.Tick();
        _autoTimer.Start();

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
    //  剪贴板事件(watcher)与窗口激活(OnActivated)都会触发，统一走 TryAutoPrompt 并用 _lastPrompted 去重，
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

    private void TryAutoPrompt()
    {
        if (ModalHost.IsOpen) return; // 已开弹窗不覆盖
        string text;
        try { text = (Clipboard.GetText() ?? "").Trim(); }
        catch { return; }
        if (text.Length == 0) return;
        if (text == _lastPrompted) return; // 同一剪贴板内容不重复弹（剪贴板事件/激活/反复切回窗口都走这里去重）
        _lastPrompted = text;
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
                SuppressAndBump(cc);
                ToastService.Flash("已复制", x, y);
            }
        };
        card.OpenJsonRequested += cc =>
        {
            var dlg = new JsonDialog(cc);
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
                SuppressAndBump(cc);
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
                SuppressAndBump(cc);
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
            SuppressAndBump(c);
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

    /// <summary>复制成功后：来源抑制（避免本程序写剪贴板触发自动弹窗）+ 持久化复制计数（对齐 Web bumpCopyCount）。</summary>
    private void SuppressAndBump(ClipItem c)
    {
        if (_watcher != null) _watcher.Suppress(800);
        try { _svc.BumpCopyCount(c.Id); } catch { /* 计数失败不影响 */ }
    }

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

    // ---- 搜索 / 类型过滤 / 标签栏 ----

    private void Search_TextChanged(object sender, TextChangedEventArgs e)
    {
        _q = (SearchBox.Text ?? "").Trim();
        _searchTimer.Stop();
        _searchTimer.Start();
    }

    private void TypeRadio_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton rb || rb.IsChecked != true) return;
        _typeFilter = rb.Tag as string ?? "all";
        if (_svc == null) return; // InitializeComponent 期间首个 RadioButton 初始 Checked（服务尚未装配）
        RefreshWall();
    }

    /// <summary>完整标签栏 chips（M3b-1：聚合全部条目标签去重 + 当前选中金底 + 点击 toggle 过滤）。
    /// chips 用 ItemsControl/WrapPanel 风格横排，过多可水平滚动（外层 TagBar StackPanel → 改 ScrollViewer）。</summary>
    private void RenderTagBar()
    {
        TagBar.Children.Clear();
        // 全部（永远在最左）
        var all = new Button
        {
            Content = "全部",
            Tag = string.IsNullOrEmpty(_tagFilter) ? "on" : null,
            Style = (Style)FindResource("TagChip"),
        };
        all.Click += (_, _) => { _tagFilter = ""; RenderTagBar(); RefreshWall(); };
        TagBar.Children.Add(all);

        // 聚合标签（活跃区 + 归档——恢复条目带标签也要能筛，Search("",includeArchived=true)）
        var allTags = _svc.Search("", includeArchived: true)
            .SelectMany(c => c.Tags ?? new List<string>())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(x => x, StringComparer.Ordinal);
        foreach (var tag in allTags)
        {
            var chip = new Button
            {
                Content = tag,
                Tag = (_tagFilter == tag) ? "on" : null,
                Style = (Style)FindResource("TagChip"),
            };
            var captured = tag;
            chip.Click += (_, _) =>
            {
                _tagFilter = (_tagFilter == captured) ? "" : captured;
                RenderTagBar();
                RefreshWall();
            };
            TagBar.Children.Add(chip);
        }
    }

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
            SearchBox.CaretIndex = SearchBox.Text.Length; // 光标置于末尾，直接打字即从末尾续写
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

    /// <summary>列数偏好切换：0(自动) → 1 → 2 → 3 → 4 → 0 循环；立即持久化 + 刷新列宽。
    /// 按钮文案显示当前选择（"列数·自动" / "列数·2"）便于查看。</summary>
    private void ColsBtn_Click(object sender, RoutedEventArgs e)
    {
        _settings.MaxColumns = (_settings.MaxColumns + 1) % 5; // 0~4 循环
        _settings.Save();
        UpdateColsBtnText();
        UpdateColumnWidth();
    }

    private void UpdateColsBtnText()
        => ColsBtn.Content = _settings.MaxColumns == 0 ? "列数·自动" : "列数·" + _settings.MaxColumns;

    /// <summary>含归档 toggle：默认 false 只看活跃区；点开后看到归档卡（可 ↺ 恢复 + ✕ 删除）。
    /// 按钮文案 + 颜色双反馈："归档·关"暗色 / "归档·开"金色（与置顶选中态一致）。</summary>
    private void ArchBtn_Click(object sender, RoutedEventArgs e)
    {
        _includeArchived = !_includeArchived;
        ArchBtn.Content = _includeArchived ? "归档·开" : "归档·关";
        ArchBtn.Foreground = _includeArchived
            ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xD4, 0xAF, 0x37)) // 选中金（与 PinBtn 一致）
            : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x84, 0x84, 0x84)); // 暗灰（与 MutedBrush 视觉一致）
        RefreshWall();
    }

    // ---- 批量编辑（M3b-3b：对齐 Web 版 .batch-bar + batchDeleteClips / batchSetTags） ----

    /// <summary>进入/退出批量模式（编辑按钮 / 完成按钮）。进入时清空选择；重建卡片墙应用 BatchMode。</summary>
    private void SetBatchMode(bool on)
    {
        _batchMode = on;
        if (!on) _batchSel.Clear();
        BatchBar.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        RefreshWall(); // 重建以应用 BatchMode 覆盖层 + 恢复选中态
    }

    private void EditBtn_Click(object sender, RoutedEventArgs e) => SetBatchMode(true);

    /// <summary>数据管理弹窗：本地备份（导入/导出/清空）+ WebDAV 同步设置（同步配置入口在此，工具栏「同步」只负责同步）。</summary>
    private void DataBtn_Click(object sender, RoutedEventArgs e)
        => ModalHost.Show(new DataDialog(_svc, RefreshWall, _sync));
    private void BatchDoneBtn_Click(object sender, RoutedEventArgs e) => SetBatchMode(false);

    /// <summary>卡片 SelectionToggled → 切换选择集 → 同步该卡视觉 + 计数。</summary>
    private void OnCardSelectionToggled(string id)
    {
        if (!_batchMode) return;
        if (_batchSel.Contains(id)) _batchSel.Remove(id);
        else _batchSel.Add(id);
        if (_cards.TryGetValue(id, out var card)) card.SetSelected(_batchSel.Contains(id));
        SyncBatchUI();
    }

    /// <summary>全选/取消全选当前页（基于当前 Search 可见集，对齐 getVisibleClips；非全库）。</summary>
    private void BatchSelectAllBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_visibleIds.Count == 0) return;
        bool allSel = _visibleIds.All(id => _batchSel.Contains(id));
        if (allSel) foreach (var id in _visibleIds) _batchSel.Remove(id);
        else foreach (var id in _visibleIds) _batchSel.Add(id);
        foreach (var id in _visibleIds)
            if (_cards.TryGetValue(id, out var card)) card.SetSelected(_batchSel.Contains(id));
        SyncBatchUI();
    }

    private void BatchAddTagBtn_Click(object sender, RoutedEventArgs e) => OpenBatchTagModal(isAdd: true);
    private void BatchRemoveTagBtn_Click(object sender, RoutedEventArgs e) => OpenBatchTagModal(isAdd: false);

    /// <summary>批量标签弹窗：复用 TagPicker；add=全局标签（可新建）/ remove=已选条目标签并集。确认后走 ClipService.BatchSetTags。</summary>
    private void OpenBatchTagModal(bool isAdd)
    {
        if (_batchSel.Count == 0) { ToastService.Flash("请先选择内容"); return; }

        var title = new TextBlock
        {
            Text = isAdd ? "批量添加标签" : "批量移除标签",
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 4),
        };
        var hint = new TextBlock
        {
            Text = isAdd ? "选择要添加的标签（输入框回车可新建）" : "选择要移除的标签",
            FontSize = 12,
            Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x84, 0x84, 0x84)),
            Margin = new Thickness(0, 0, 0, 12),
        };
        var picker = new TagPicker();
        if (isAdd)
            picker.SetTags(new List<string>(), GetAllTagsIncludingArchive()); // 全局标签（可新建）
        else
            picker.SetTags(new List<string>(), UnionTagsOfSelection());       // 已选条目标签并集

        var wrap = new StackPanel { Children = { title, hint, picker } };

        var ok = new Button
        {
            Style = (Style)FindResource("BtnPrimary"),
            Content = isAdd ? "添加" : "移除",
            MinWidth = 130,
            Margin = new Thickness(0, 16, 10, 0),
        };
        var cancel = new Button
        {
            Style = (Style)FindResource("BtnClose"),
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

        var sp = new StackPanel { Children = { wrap, row } };
        var card = new Border
        {
            Style = (Style)FindResource("ModalCard"),
            Width = 420,
            Child = sp,
        };

        ok.Click += (_, _) =>
        {
            ModalHost.Close();
            try
            {
                var n = _svc.BatchSetTags(_batchSel, picker.Selected, isAdd);
                ToastService.Flash(isAdd ? $"已为 {n} 条添加标签" : $"已从 {n} 条移除标签");
                RefreshWall(); // 标签变化可能触发重排序；重建后保留批量选中态
            }
            catch (Exception ex) { ToastService.Error(ex.Message); }
        };
        cancel.Click += (_, _) => ModalHost.Close();
        ModalHost.Show(card);
    }

    /// <summary>已选条目标签并集（跨活跃+归档读取），供批量移除弹窗展示可选标签。</summary>
    private List<string> UnionTagsOfSelection()
    {
        var items = _storage.LoadClips().Concat(_storage.LoadArchive())
            .Where(c => _batchSel.Contains(c.Id));
        return items.SelectMany(c => c.Tags ?? new List<string>())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>系统已有标签（聚合全部条目去重，含归档；对齐 /api/tags 全量）。</summary>
    private List<string> GetAllTagsIncludingArchive()
        => _svc.Search("", includeArchived: true).SelectMany(c => c.Tags ?? new List<string>())
            .Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToList();

    /// <summary>批量删除：确认 → ClipService.BatchDelete（跨区+清文件+墓碑）→ 退出批量模式 → 刷新。</summary>
    private void BatchDelBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_batchSel.Count == 0) { ToastService.Flash("请先选择内容"); return; }
        ModalHost.Confirm($"删除选中的 {_batchSel.Count} 条内容？此操作不可撤销", () =>
        {
            try
            {
                var n = _svc.BatchDelete(_batchSel);
                ToastService.Flash($"已删除 {n} 条");
                SetBatchMode(false); // 选择集已删除，退出批量模式
            }
            catch (Exception ex) { ToastService.Error(ex.Message); }
        }, "删除");
    }

    /// <summary>同步批量条 UI：已选计数 + 全选按钮文案（全选→取消全选）。</summary>
    private void SyncBatchUI()
    {
        BatchCountText.Text = $"已选 {_batchSel.Count}";
        bool allSel = _visibleIds.Count > 0 && _visibleIds.All(id => _batchSel.Contains(id));
        BatchSelectAllBtn.Content = allSel ? "取消全选" : "全选当前页";
    }
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

    // ---------- M5c：WebDAV 同步（编排下沉 SyncController；工具栏「同步」只负责触发同步） ----------

    /// <summary>工具栏「同步」按钮：用已保存配置立即同步；未配置则提示去「数据管理」设置（不打开配置 UI）。</summary>
    private async void SyncBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_sync.Config == null)
        {
            ToastService.Error("尚未配置 WebDAV 同步，请到「数据管理」设置");
            return;
        }
        try
        {
            ToastService.Flash("同步中…");
            var r = await _sync.RunNow(_sync.Config);
            if (r.Ok) ToastService.Flash($"同步完成 · 共 {r.Clips} 条");
            else ToastService.Error("同步失败：" + (r.Error ?? "未知错误"));
        }
        catch (Exception ex) { ToastService.Error("同步失败：" + ex.Message); }
    }
}
