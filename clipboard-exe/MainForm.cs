using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace ClipboardExe;

/// <summary>
/// 主窗体（对齐 Web 版主页面逻辑）：
///  - 过滤状态对齐 state.filter：搜索词 q / 标签 tag / 类型 type / 含归档 archived
///  - 捕获回调 → 弹出存入确认窗（防叠加）；确认落库 / 放弃清理图片实体
///  - 卡片点击复制 / 富文本分栏复制；托盘常驻；前台捕获开关（隐私优先）
/// </summary>
public partial class MainForm : Form
{
    private readonly string _dataDir;
    private readonly Storage _storage;
    private readonly ClipboardWatcher _watcher;
    private readonly List<ClipItem> _all = new();

    private TextBox _searchBox = null!;
    private ComboBox _typeFilter = null!;
    private FlowLayoutPanel _tagBar = null!;
    private FlowLayoutPanel _wall = null!;
    private Label _statusLabel = null!;
    private Label _emptyHint = null!;
    private Button _archiveToggleBtn = null!;
    private Button _editModeBtn = null!;
    private Panel _batchBar = null!;
    private Label _batchCountLabel = null!;

    // 过滤状态（对齐 Web 版 state.filter）
    private string _q = "";
    private string _tag = "";        // "" = 全部
    private string _type = "all";    // all | text | link | file
    private bool _showArchived;

    private bool _captureDialogOpen; // 防弹窗叠加
    private bool _exiting;           // 托盘"退出"才真正退出

    // 批量编辑（对齐 Web batchSel / setBatchMode / renderBatchBar）
    private readonly HashSet<string> _batchSel = new();
    private bool _batchMode;

    /// <summary>托盘图标（由 Program 注入）。</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public NotifyIcon? TrayIcon { get; set; }

    public MainForm()
    {
        // 数据目录：exe 同目录 data/（便携式，整个文件夹拷走即迁移）
        var exeDir = Path.GetDirectoryName(Application.ExecutablePath) ?? ".";
        _dataDir = Path.Combine(exeDir, "data");
        try { Directory.CreateDirectory(_dataDir); } catch { /* 只读目录等极端情况不阻塞启动 */ }

        _storage = new Storage(_dataDir);
        _watcher = new ClipboardWatcher(_storage, ShowCaptureDialog);

        InitUi();
        RefreshCards();
        WriteStartupLog();
    }

    // ---------------- 剪贴板监听（宿主窗口） ----------------

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyDarkTitleBar();
        if (!ClipboardWatcher.Start(Handle)) AppLog.Info("AddClipboardFormatListener failed");
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        ClipboardWatcher.Stop(Handle);
        base.OnHandleDestroyed(e);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == NativeMethods.WM_CLIPBOARDUPDATE) _watcher.OnClipboardUpdate();
        else if (m.Msg == NativeMethods.WM_SHOW_MAIN) ShowFromTray();
        base.WndProc(ref m);
    }

    private void ApplyDarkTitleBar()
    {
        int useDark = 1;
        NativeMethods.DwmSetWindowAttribute(Handle, 20, ref useDark, sizeof(int));
    }

    // ---------------- 前台捕获开关（用户确认：仅前台激活时捕获，隐私优先） ----------------

    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        _watcher.CaptureEnabled = true;
    }

    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        _watcher.CaptureEnabled = false;
    }

    // ---------------- 快捷键（对齐 Web：空格=存入、Esc=关闭/清搜索） ----------------

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        // 空格 = 打开存入弹窗（无模态弹窗时）；Esc = 清空搜索
        if (keyData == Keys.Space)
        {
            _watcher.CaptureNow();
            return true;
        }
        if (keyData == Keys.Escape)
        {
            if (_searchBox.Text.Length > 0) { _searchBox.Clear(); return true; }
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    // ---------------- 存入确认弹窗（对齐 Web openPasteModal 流程） ----------------

    private void ShowCaptureDialog(ClipItem pending)
    {
        if (_captureDialogOpen)
        {
            // 已有弹窗 → 放弃本次（清理已写图片实体）
            if (pending.Type == "file") _storage.DeleteFile(pending.FileId);
            return;
        }
        _captureDialogOpen = true;
        _watcher.DialogOpen = true;
        try
        {
            using var dlg = new CaptureDialog(pending, _storage.GetAllTags(), _storage);
            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                _storage.Add(dlg.BuildItem());
                RefreshCards();
            }
            else if (pending.Type == "file")
            {
                _storage.DeleteFile(pending.FileId); // 放弃 → 清理图片实体
            }
        }
        finally
        {
            _captureDialogOpen = false;
            _watcher.DialogOpen = false;
        }
    }

    // ---------------- 列表渲染（对齐 Web renderList + getVisibleClips） ----------------

    private void RefreshCards()
    {
        _all.Clear();
        _all.AddRange(_storage.Load());

        IEnumerable<ClipItem> view = _showArchived ? _all : _all.Where(c => !c.Archived);
        if (_tag.Length > 0)
            view = view.Where(c => c.Tags != null && c.Tags.Contains(_tag, StringComparer.Ordinal));
        if (_type != "all")
            view = view.Where(c => c.Type == _type);
        if (_q.Length > 0)
        {
            // 对齐 Web getVisibleClips：标题/内容/URL 子串 + 拼音首字母
            view = view.Where(c =>
                c.Title.Contains(_q, StringComparison.OrdinalIgnoreCase) ||
                c.Content.Contains(_q, StringComparison.OrdinalIgnoreCase) ||
                c.Url.Contains(_q, StringComparison.OrdinalIgnoreCase) ||
                Pinyin.Match(c.Title + c.Content + c.Url, _q));
        }
        var sorted = Storage.Sort(view.ToList());

        _wall.SuspendLayout();
        _wall.Controls.Clear();
        foreach (var item in sorted) _wall.Controls.Add(BuildCard(item));
        _wall.ResumeLayout();

        _wall.Visible = sorted.Count > 0;
        _emptyHint.Visible = sorted.Count == 0;
        _statusLabel.Text = $"{_all.Count(c => !c.Archived)} 条" +
            (_showArchived ? "（含归档）" : "") +
            (_q.Length > 0 || _tag.Length > 0 || _type != "all" ? $" · 筛选出 {sorted.Count} 条" : "");
        RenderTagBar();
    }

    private Control BuildCard(ClipItem item)
    {
        var card = new CardControl(item, _storage)
        {
            BatchMode = _batchMode,
            BatchChecked = _batchSel.Contains(item.Id),
        };
        card.CopyRequested += (_, _) => CopyItem(item);
        card.RichCopyRequested += (_, _) => CopyRich(item);
        card.JsonRequested += (_, _) => OpenJsonPreview(item);
        card.BatchToggleRequested += (_, _) =>
        {
            if (!_batchSel.Add(item.Id)) _batchSel.Remove(item.Id);
            UpdateBatchBar();
        };
        card.PinRequested += (_, _) =>
        {
            item.Pinned = !item.Pinned;
            _storage.Save(_storage.Load().Select(c => c.Id == item.Id ? item : c).ToList());
            RefreshCards();
        };
        card.EditRequested += (_, _) => OpenEdit(item);
        card.DeleteRequested += (_, _) =>
        {
            if (MessageBox.Show("删除这条记录？", "Clipboard", MessageBoxButtons.OKCancel,
                    MessageBoxIcon.Question) == DialogResult.OK)
            {
                _storage.Delete(item.Id);
                RefreshCards();
            }
        };
        card.RestoreRequested += (_, _) =>
        {
            if (item.Archived) _storage.UnarchiveClip(item.Id);
            else _storage.ArchiveClip(item.Id);
            RefreshCards();
        };
        return card;
    }

    // ---------------- 复制（对齐 Web handleCardClick / copyText / copyRich） ----------------

    private void CopyItem(ClipItem item)
    {
        try
        {
            _watcher.SuppressNext(); // 自身回写 → 抑制监听
            if (item.Type == "file" && item.FileMime.StartsWith("image/"))
            {
                var bytes = _storage.LoadImage(item.FileId);
                if (bytes == null || bytes.Length == 0)
                {
                    MessageBox.Show("图片数据缺失", "Clipboard", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                using var ms = new MemoryStream(bytes);
                using var bmp = new Bitmap(ms);
                Clipboard.SetDataObject(bmp, true); // copy:true 让剪贴板持有副本，Bitmap 可安全释放
            }
            else
            {
                Clipboard.SetText(item.Type == "link" ? item.Url : item.Content);
            }
            item.CopyCount++;
            item.UpdatedAt = DateTimeOffset.Now.ToUnixTimeMilliseconds();
            _storage.Save(_storage.Load().Select(c => c.Id == item.Id ? item : c).ToList());
            RefreshCards();
        }
        catch (Exception ex)
        {
            AppLog.Info("copy failed: " + ex.Message);
        }
    }

    /// <summary>复制富文本（CF_HTML 原文回写，保留格式）。</summary>
    private void CopyRich(ClipItem item)
    {
        try
        {
            _watcher.SuppressNext();
            Clipboard.SetText(item.Html, TextDataFormat.Html);
        }
        catch (Exception ex)
        {
            AppLog.Info("rich copy failed: " + ex.Message);
        }
    }

    // ---------------- 编辑 / JSON 预览（P2 补齐细节，先留入口） ----------------

    private void OpenEdit(ClipItem item)
    {
        // 编辑弹窗在 P2 实现（对齐 Web openEditModal：标题/内容/标签/清除格式/归档/删除）
        new EditDialog(item, _storage.GetAllTags(), _storage, RefreshCards).ShowDialog(this);
    }

    private void OpenJsonPreview(ClipItem item)
    {
        // JSON 格式化预览（对齐 Web openJsonPreview：美化/复制/覆盖保存）
        new JsonPreviewDialog(item, _storage, RefreshCards).ShowDialog(this);
    }

    // ---------------- 批量编辑（对齐 Web setBatchMode / renderBatchBar） ----------------

    private void ToggleBatchMode()
    {
        _batchMode = !_batchMode;
        if (!_batchMode)
        {
            _batchSel.Clear();
            _batchBar.Visible = false;
            _editModeBtn.Text = "编辑";
            UpdateToggleBtn(_editModeBtn, false);
        }
        else
        {
            _editModeBtn.Text = "完成";
            UpdateToggleBtn(_editModeBtn, true);
            _batchBar.Visible = true;
        }
        UpdateBatchBar();
        RefreshCards();
    }

    /// <summary>全选当前页（对齐 Web selectAllVisible：当前过滤可见集全选）。</summary>
    private void BatchSelectAllVisible()
    {
        var visible = GetVisibleIds();
        var allSelected = visible.All(_batchSel.Contains);
        foreach (var id in visible)
        {
            if (allSelected) _batchSel.Remove(id); else _batchSel.Add(id);
        }
        UpdateBatchBar();
        RefreshCards();
    }

    private IEnumerable<string> GetVisibleIds()
    {
        IEnumerable<ClipItem> view = _showArchived ? _all : _all.Where(c => !c.Archived);
        if (_tag.Length > 0) view = view.Where(c => c.Tags != null && c.Tags.Contains(_tag, StringComparer.Ordinal));
        if (_type != "all") view = view.Where(c => c.Type == _type);
        if (_q.Length > 0)
            view = view.Where(c =>
                c.Title.Contains(_q, StringComparison.OrdinalIgnoreCase) ||
                c.Content.Contains(_q, StringComparison.OrdinalIgnoreCase) ||
                c.Url.Contains(_q, StringComparison.OrdinalIgnoreCase) ||
                Pinyin.Match(c.Title + c.Content + c.Url, _q));
        return Storage.Sort(view.ToList()).Select(c => c.Id);
    }

    private void BatchAddTag()
    {
        var tag = InputDialog.Ask("批量加标签", "输入要添加的标签：", this);
        if (string.IsNullOrWhiteSpace(tag)) return;
        tag = tag.Trim();
        var list = _storage.Load();
        var touched = 0;
        foreach (var c in list.Where(c => _batchSel.Contains(c.Id)))
        {
            if (c.Tags == null) c.Tags = new List<string>();
            if (!c.Tags.Contains(tag)) { c.Tags.Add(tag); c.UpdatedAt = DateTimeOffset.Now.ToUnixTimeMilliseconds(); touched++; }
        }
        if (touched > 0) { _storage.Save(list); RefreshCards(); }
    }

    private void BatchRemoveTag()
    {
        var tag = InputDialog.Ask("批量减标签", "输入要移除的标签：", this);
        if (string.IsNullOrWhiteSpace(tag)) return;
        tag = tag.Trim();
        var list = _storage.Load();
        var touched = 0;
        foreach (var c in list.Where(c => _batchSel.Contains(c.Id)))
        {
            if (c.Tags != null && c.Tags.Remove(tag)) { c.UpdatedAt = DateTimeOffset.Now.ToUnixTimeMilliseconds(); touched++; }
        }
        if (touched > 0) { _storage.Save(list); RefreshCards(); }
    }

    private void BatchDelete()
    {
        if (MessageBox.Show($"删除选中的 {_batchSel.Count} 条记录？", "Clipboard", MessageBoxButtons.OKCancel,
                MessageBoxIcon.Question) != DialogResult.OK) return;
        foreach (var id in _batchSel.ToList()) _storage.Delete(id);
        _batchSel.Clear();
        UpdateBatchBar();
        RefreshCards();
    }

    // ---------------- 工具栏动作 ----------------

    private void ToggleArchiveView()
    {
        _showArchived = !_showArchived;
        UpdateToggleBtn(_archiveToggleBtn, _showArchived);
        RefreshCards();
    }

    private void ExportAll()
    {
        using var dlg = new SaveFileDialog
        {
            Title = "导出剪贴板数据（Web 版可导入）",
            Filter = "JSON 文件 (*.json)|*.json",
            FileName = $"clipboard-export-{DateTime.Now:yyyyMMdd-HHmmss}.json",
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            File.WriteAllText(dlg.FileName, _storage.ExportJson());
            MessageBox.Show($"已导出 {_all.Count} 条到{Environment.NewLine}{dlg.FileName}",
                "Clipboard", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show("导出失败: " + ex.Message, "Clipboard", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ImportFromWeb()
    {
        using var dlg = new OpenFileDialog
        {
            Title = "导入 Web 版导出 JSON",
            Filter = "JSON 文件 (*.json)|*.json",
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var (imported, skipped) = _storage.ImportFromWeb(File.ReadAllText(dlg.FileName));
            RefreshCards();
            MessageBox.Show($"导入完成：新增/更新 {imported} 条，跳过重复 {skipped} 条",
                "Clipboard", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show("导入失败: " + ex.Message, "Clipboard", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // ---------------- 托盘交互 ----------------

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (WindowState == FormWindowState.Minimized)
        {
            _watcher.CaptureEnabled = false; // 最小化到托盘 → 停止自动捕获
            Hide();
            TrayIcon?.ShowBalloonTip(1500, "Clipboard", "已最小化到托盘（双击图标恢复）", ToolTipIcon.Info);
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_exiting)
        {
            e.Cancel = true;
            WindowState = FormWindowState.Minimized;
            Hide();
            return;
        }
        base.OnFormClosing(e);
    }

    public void ShowFromTray()
    {
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
    }

    public void ExitApp()
    {
        _exiting = true;
        Application.Exit();
    }

    private void WriteStartupLog()
    {
        AppLog.Info($"startup v{Program.AppVersion} ({Program.GitCommit}) data={_dataDir}");
    }
}
