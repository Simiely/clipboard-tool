using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
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

    // 过滤状态（对齐 state.filter）
    private string _q = "";
    private string _tagFilter = "";
    private string _typeFilter = "all";
    private bool _includeArchived; // M3b-1：归档按钮 toggle 状态（默认 false）

    private const double Gap = 16; // .list gap:16px

    public MainWindow(Settings settings, TrayIconService? tray)
    {
        InitializeComponent();
        _settings = settings;
        _tray = tray;
        if (_tray != null) _tray.ShowMainRequested += ShowMainFromTray;

        // 服务装配（数据目录 exe 同目录 data/）
        _storage = new Storage(App.DataDir);
        _fileStore = new FileStore(App.DataDir);
        _svc = new ClipService(_storage);

        // 弹窗宿主 + Toast 初始化
        ModalHost.Attach(ModalLayer);
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

    // ---- 剪贴板监听触发（对齐 Web clipboardchange：已开弹窗不覆盖 / 来源抑制由 watcher 处理） ----
    private void OnClipboardChanged()
    {
        if (_watcher == null) return;
        if (ModalHost.IsOpen) return; // 对齐 $(".mask") 已开不弹
        string text;
        try { text = (Clipboard.GetText() ?? "").Trim(); }
        catch { return; }
        if (text.Length == 0) return;
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
        foreach (var c in clips) WallPanel.Children.Add(MakeCard(c));

        var hasFilter = _q.Length > 0 || _tagFilter.Length > 0 || _typeFilter != "all" || _includeArchived;
        EmptyHint.Text = hasFilter
            ? "没有匹配的内容 — 试试调整搜索词、标签或类型"
            : "还没有内容 — 顶部粘贴框 Ctrl+V 即存，或拖文件进来";
        EmptyHint.Visibility = clips.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        RenderTagBar();   // 标签集合随活跃+归档变化同步刷新
        UpdateColumnWidth(); // 重建后同步列宽
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
        card.CopyBumped += _ =>
        {
            if (_watcher != null) _watcher.Suppress(800); // 来源抑制：本次复制不触发自动弹窗
            try { _svc.BumpCopyCount(c.Id); } catch { /* 计数失败不影响 */ }
            // 不重排（对齐 Web bumpCopyCount 仅本地 +1，卡片已更新显示）
        };
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
                if (dlg.ShowDialog(this) == true)
                {
                    File.WriteAllBytes(dlg.FileName, _fileStore.ReadAllBytes(cc.FileId)); // 对齐 downloadFile：attachment 原名落盘
                    ToastService.Flash("已下载");
                }
            }
            catch (Exception ex) { ToastService.Error("下载失败: " + ex.Message); }
        };
        card.CopyImageRequested += cc => CopyImageToClipboard(cc, _watcher); // M3b-2b：图片卡单击复制到系统剪贴板
        card.OpenJsonRequested += cc =>
        {
            var dlg = new JsonDialog(cc);
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

    /// <summary>复制图片到系统剪贴板（M3b-2b，对齐 Web copyImageToClipboard：直接 System.Windows.Clipboard.SetImage(BitmapSource)，
    /// 成功 flash + 计数 + 来源抑制；失败降级 errToast，不打开预览（WPF 已有 toast 反馈）。</summary>
    private void CopyImageToClipboard(ClipItem c, ClipboardWatcher? watcher)
    {
        if (string.IsNullOrEmpty(c.FileId)) { ToastService.Error("图片缺失"); return; }
        try
        {
            var bytes = _fileStore.ReadAllBytes(c.FileId);
            if (bytes == null || bytes.Length == 0) { ToastService.Error("图片为空"); return; }
            using var ms = new MemoryStream(bytes);
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.StreamSource = ms;
            bmp.EndInit();
            bmp.Freeze();
            Clipboard.SetImage(bmp);
            if (watcher != null) watcher.Suppress(800); // 来源抑制：避免本程序写剪贴板触发自动弹窗
            try { _svc.BumpCopyCount(c.Id); } catch { /* 计数失败不影响 */ }
            ToastService.Flash("图片已复制，可直接粘贴");
        }
        catch (Exception ex)
        {
            AppLog.Info("copy image failed: " + ex.Message);
            ToastService.Error("复制图片失败: " + ex.Message);
        }
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
    }

    protected override void OnDeactivated(EventArgs e)
    {
        base.OnDeactivated(e);
        if (_watcher != null) _watcher.Paused = true; // 失活暂停（对齐 Web 前台语义）
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

    /// <summary>真正退出（托盘退出 / 顶栏退出按钮）。</summary>
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
}
