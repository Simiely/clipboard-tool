using System.Drawing;
using System.Windows.Forms;

namespace ClipboardExe;

// MainForm 布局部分（对齐 Web 版主页面结构：工具栏行 / 标签栏 / 卡片墙 / 状态栏）
public partial class MainForm
{
    private static readonly Color UiBg = Color.FromArgb(0x1A, 0x1A, 0x1A);      // --bg
    private static readonly Color UiPanel = Color.FromArgb(0x1F, 0x1F, 0x1F);   // --elev
    private static readonly Color UiGold = Color.FromArgb(0xC9, 0xA9, 0x6E);    // --accent 金
    private static readonly Color UiAccent2 = Color.FromArgb(0xAE, 0x4D, 0x4D); // --accent2 砖红
    private static readonly Color UiText = Color.FromArgb(0xDA, 0xDA, 0xDA);    // --text
    private static readonly Color UiMuted = Color.FromArgb(0x84, 0x84, 0x84);   // --muted
    private static readonly Color UiBorder = Color.FromArgb(0x2A, 0x2A, 0x2A);
    private static readonly Color UiInputBg = Color.FromArgb(0x26, 0x26, 0x26);
    private static readonly Color UiSelectedBg = Color.FromArgb(0x2E, 0x28, 0x18); // 选中态（金底偏暗）

    private void InitUi()
    {
        Text = $"Clipboard v{Program.AppVersion}";
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(1100, 720);
        MinimumSize = new Size(700, 500);
        BackColor = UiBg;
        ForeColor = UiText;
        Icon = IconFactory.Create();
        KeyPreview = true; // 空格/Esc 快捷键走 ProcessCmdKey

        // 工具栏行：搜索框 + 类型过滤 + 按钮
        var top = new Panel { Dock = DockStyle.Top, Height = 52, BackColor = UiPanel, Padding = new Padding(12, 10, 12, 8) };
        _searchBox = new TextBox
        {
            Dock = DockStyle.Fill,
            BackColor = UiInputBg,
            ForeColor = UiText,
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Microsoft YaHei UI", 10f),
            PlaceholderText = "搜索 标题 / 内容 / 链接（支持拼音首字母）…",
        };
        _searchBox.TextChanged += (_, _) => { _q = _searchBox.Text.Trim(); RefreshCards(); };

        _typeFilter = new ComboBox
        {
            Dock = DockStyle.Right,
            Width = 90,
            DropDownStyle = ComboBoxStyle.DropDownList,
            BackColor = UiInputBg,
            ForeColor = UiText,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Microsoft YaHei UI", 9f),
            Items = { "全部", "文本", "链接", "图片/文件" },
            SelectedIndex = 0,
        };
        _typeFilter.SelectedIndexChanged += (_, _) =>
        {
            _type = _typeFilter.SelectedIndex switch { 1 => "text", 2 => "link", 3 => "file", _ => "all" };
            RefreshCards();
        };

        var btns = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, WrapContents = false };
        _editModeBtn = MakeButton("编辑");
        _editModeBtn.Click += (_, _) => ToggleBatchMode();
        btns.Controls.Add(_editModeBtn);
        _archiveToggleBtn = MakeButton("含归档");
        _archiveToggleBtn.Click += (_, _) => ToggleArchiveView();
        btns.Controls.Add(_archiveToggleBtn);
        btns.Controls.Add(MakeButton("存入", () => _watcher.CaptureNow()));
        btns.Controls.Add(MakeButton("导出", ExportAll));
        btns.Controls.Add(MakeButton("导入", ImportFromWeb));

        top.Controls.Add(_searchBox);
        top.Controls.Add(_typeFilter);
        top.Controls.Add(btns);
        _searchBox.Padding = new Padding(0, 0, 8, 0);

        // 标签栏：全部 + 各标签 chips（点击过滤）
        _tagBar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 36,
            BackColor = UiBg,
            Padding = new Padding(12, 4, 12, 4),
            AutoScroll = true,
            WrapContents = false,
            FlowDirection = FlowDirection.LeftToRight,
        };

        // 状态栏
        var statusBar = new Panel { Dock = DockStyle.Bottom, Height = 26, BackColor = UiPanel };
        _statusLabel = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(12, 0, 12, 0),
            ForeColor = UiMuted,
            Font = new Font("Microsoft YaHei UI", 8.5f),
            Text = "",
        };
        statusBar.Controls.Add(_statusLabel);

        // 批量编辑条（对齐 Web renderBatchBar：底部悬浮，选中时出现）
        _batchBar = new Panel { Dock = DockStyle.Bottom, Height = 40, BackColor = Color.FromArgb(0x24, 0x22, 0x1A), Visible = false };
        _batchCountLabel = new Label
        {
            Text = "已选 0 项",
            Location = new Point(14, 10),
            AutoSize = true,
            ForeColor = UiGold,
            Font = new Font("Microsoft YaHei UI", 9f),
        };
        _batchBar.Controls.Add(_batchCountLabel);
        var batchFlow = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, WrapContents = false, Padding = new Padding(0, 5, 10, 0) };
        batchFlow.Controls.Add(MakeButton("全选当前页", BatchSelectAllVisible));
        batchFlow.Controls.Add(MakeButton("＋加标签", BatchAddTag));
        batchFlow.Controls.Add(MakeButton("－减标签", BatchRemoveTag));
        batchFlow.Controls.Add(MakeButton("删除", BatchDelete));
        _batchBar.Controls.Add(batchFlow);

        // 卡片墙
        _wall = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            WrapContents = true,
            FlowDirection = FlowDirection.LeftToRight,
            BackColor = UiBg,
            Padding = new Padding(12),
        };

        // 空态提示
        _emptyHint = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = UiMuted,
            Font = new Font("Microsoft YaHei UI", 11f),
            Text = "还没有内容\n\n复制任意文本 / 链接 / 图片，即自动弹出存入窗口\n也可点工具栏「存入」或按空格手动保存当前剪贴板",
            Visible = false,
        };

        // z-order：先 Add 的在底层。hint 在最底，wall 盖住它；空态时隐藏 wall 露出 hint
        Controls.Add(_emptyHint);
        Controls.Add(_wall);
        Controls.Add(statusBar);
        Controls.Add(_batchBar);
        Controls.Add(_tagBar);
        Controls.Add(top);
    }

    /// <summary>更新批量条已选计数（对齐 Web syncBatchUI）。</summary>
    private void UpdateBatchBar()
    {
        if (_batchCountLabel != null)
        {
            _batchCountLabel.Text = $"已选 {_batchSel.Count} 项";
            var allSel = _batchMode && _batchSel.Count > 0 && GetVisibleIds().All(_batchSel.Contains);
            // 全选按钮文字反馈：全选时显示「取消全选」
            // （按钮引用在 batchFlow 内，简化处理：文字不变，靠计数反馈）
        }
    }

    // ---------------- 标签栏 ----------------

    private void RenderTagBar()
    {
        _tagBar.SuspendLayout();
        _tagBar.Controls.Clear();
        _tagBar.Controls.Add(MakeTagChip("全部", _tag.Length == 0));
        foreach (var tag in _storage.GetAllTags())
        {
            _tagBar.Controls.Add(MakeTagChip(tag, tag == _tag));
        }
        _tagBar.ResumeLayout();
    }

    private Control MakeTagChip(string tag, bool selected)
    {
        var chip = new Button
        {
            Text = tag,
            AutoSize = true,
            MinimumSize = new Size(40, 24),
            Margin = new Padding(0, 2, 6, 2),
            Padding = new Padding(10, 0, 10, 0),
            FlatStyle = FlatStyle.Flat,
            BackColor = selected ? UiSelectedBg : UiInputBg,
            ForeColor = selected ? UiGold : UiText,
            Font = new Font("Microsoft YaHei UI", 8.5f),
            Cursor = Cursors.Hand,
        };
        chip.FlatAppearance.BorderColor = selected ? UiGold : UiBorder;
        chip.FlatAppearance.MouseOverBackColor = Color.FromArgb(0x33, 0x33, 0x33);
        chip.Click += (_, _) =>
        {
            _tag = tag == "全部" ? "" : tag;
            RefreshCards();
        };
        return chip;
    }

    // ---------------- 工具按钮 ----------------

    private Button MakeButton(string text, Action? onClick = null)
    {
        var btn = new Button
        {
            Text = text,
            Height = 30,
            Width = text.Length * 14 + 20,
            Margin = new Padding(6, 0, 0, 0),
            FlatStyle = FlatStyle.Flat,
            BackColor = UiInputBg,
            ForeColor = UiText,
            Font = new Font("Microsoft YaHei UI", 9f),
            Cursor = Cursors.Hand,
        };
        btn.FlatAppearance.BorderColor = UiBorder;
        btn.FlatAppearance.MouseOverBackColor = Color.FromArgb(0x33, 0x33, 0x33);
        if (onClick != null) btn.Click += (_, _) => onClick();
        return btn;
    }

    private void UpdateToggleBtn(Button btn, bool on)
    {
        btn.BackColor = on ? UiSelectedBg : UiInputBg;
        btn.ForeColor = on ? UiGold : UiText;
        btn.FlatAppearance.BorderColor = on ? UiGold : UiBorder;
    }
}
