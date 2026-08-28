using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace ClipboardExe;

/// <summary>
/// 主窗体：黑金深色 UI + 剪贴板监听宿主 + 卡片墙。
/// M4 MVP：搜索 / 点击复制 / 右键菜单（置顶、删除）/ 导入导出（Web 格式互导）/ 手动存入。
/// </summary>
public partial class MainForm : Form
{
    private readonly string _dataDir;
    private readonly Storage _storage;
    private readonly ClipboardWatcher _watcher;
    private readonly List<ClipItem> _all = new();   // 全量缓存（刷新时重载，保持与磁盘一致）

    private TextBox _searchBox = null!;
    private FlowLayoutPanel _wall = null!;
    private Label _statusLabel = null!;
    private Label _emptyHint = null!;

    private bool _exiting; // 托盘"退出"才真正退出，点 X 只是最小化到托盘

    /// <summary>托盘图标（由 Program 注入，最小化时使用）。</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public NotifyIcon? TrayIcon { get; set; }

    public MainForm()
    {
        // 数据目录：exe 同目录 data/（决策 2026-08-27：便携式，整个文件夹拷走即迁移）
        var exeDir = Path.GetDirectoryName(Application.ExecutablePath) ?? ".";
        _dataDir = Path.Combine(exeDir, "data");
        try { Directory.CreateDirectory(_dataDir); } catch { /* 只读目录等极端情况，M2 不阻塞启动 */ }

        _storage = new Storage(_dataDir);
        _watcher = new ClipboardWatcher(_storage, RefreshCards);

        InitUi();
        RefreshCards();
        WriteStartupLog();
    }

    // ---------------- 剪贴板监听（宿主窗口） ----------------

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyDarkTitleBar();
        if (!ClipboardWatcher.Start(Handle))
        {
            AppLog.Info("AddClipboardFormatListener failed");
        }
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        ClipboardWatcher.Stop(Handle);
        base.OnHandleDestroyed(e);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == NativeMethods.WM_CLIPBOARDUPDATE)
        {
            _watcher.OnClipboardUpdate();
        }
        else if (m.Msg == NativeMethods.WM_SHOW_MAIN)
        {
            ShowFromTray();
        }
        base.WndProc(ref m);
    }

    private void ApplyDarkTitleBar()
    {
        // DWMWA_USE_IMMERSIVE_DARK_MODE = 20（Win10 1809+ / Win11）
        int useDark = 1;
        NativeMethods.DwmSetWindowAttribute(Handle, 20, ref useDark, sizeof(int));
    }

    // ---------------- 卡片墙渲染 ----------------

    /// <summary>全量重载 + 按搜索词过滤 + 排序 + 渲染卡片（Web 版排序同款：星标→次数→更新）。</summary>
    private void RefreshCards()
    {
        _all.Clear();
        _all.AddRange(_storage.Load());

        var keyword = _searchBox.Text.Trim();
        IEnumerable<ClipItem> view = _all.Where(c => !c.Archived);
        if (keyword.Length > 0)
        {
            view = view.Where(c =>
                c.Title.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                c.Content.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                c.Url.Contains(keyword, StringComparison.OrdinalIgnoreCase));
        }
        var sorted = Storage.Sort(view.ToList());

        _wall.SuspendLayout();
        _wall.Controls.Clear();
        foreach (var item in sorted)
        {
            _wall.Controls.Add(BuildCard(item));
        }
        _wall.ResumeLayout();

        _wall.Visible = sorted.Count > 0; // 空态隐藏卡片墙，露出空态提示
        _emptyHint.Visible = sorted.Count == 0;
        _statusLabel.Text = $"{_all.Count(c => !c.Archived)} 条{(keyword.Length > 0 ? $" · 筛选出 {sorted.Count} 条" : "")}";
    }

    private Control BuildCard(ClipItem item)
    {
        var card = new CardControl(item);
        card.Click += (_, _) => CopyItem(item);
        card.MouseUp += (s, e) =>
        {
            if (e.Button == MouseButtons.Right) ShowCardMenu(card, item);
        };
        return card;
    }

    // ---------------- 卡片操作 ----------------

    private void CopyItem(ClipItem item)
    {
        try
        {
            _watcher.SuppressNext(); // 自身回写剪贴板 → 抑制监听，避免误捕获
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
                // copy:true 让剪贴板持有图像副本，Bitmap 可安全释放
                Clipboard.SetDataObject(bmp, true);
            }
            else
            {
                var text = item.Type == "link" ? item.Url : item.Content;
                Clipboard.SetText(text);
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

    private void ShowCardMenu(CardControl card, ClipItem item)
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("复制", null, (_, _) => CopyItem(item));
        menu.Items.Add("置顶 / 取消置顶", null, (_, _) =>
        {
            item.Pinned = !item.Pinned;
            _storage.Save(_storage.Load().Select(c => c.Id == item.Id ? item : c).ToList());
            RefreshCards();
        });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("删除", null, (_, _) =>
        {
            if (MessageBox.Show("删除这条记录？", "Clipboard", MessageBoxButtons.OKCancel,
                    MessageBoxIcon.Question) == DialogResult.OK)
            {
                _storage.Delete(item.Id);
                RefreshCards();
            }
        });
        menu.Show(card, Cursor.Position - (Size)card.Location);
    }

    // ---------------- 工具栏动作 ----------------

    private void CaptureNow() => _watcher.CaptureNow();

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
            var json = File.ReadAllText(dlg.FileName);
            var (imported, skipped) = _storage.ImportFromWeb(json);
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
            Hide();
            TrayIcon?.ShowBalloonTip(1500, "Clipboard", "已最小化到托盘（双击图标恢复）", ToolTipIcon.Info);
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_exiting)
        {
            // 点 X = 最小化到托盘；托盘菜单"退出"才真正退出
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

    // ---------------- 启动日志（版本指纹） ----------------

    private void WriteStartupLog()
    {
        // 与 Web 版 server.mjs 版本指纹同精神：实例身份可追溯
        AppLog.Info($"startup v{Program.AppVersion} ({Program.GitCommit}) data={_dataDir}");
    }
}
