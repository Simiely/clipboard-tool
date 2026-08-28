using System.Drawing;
using System.Windows.Forms;

namespace ClipboardExe;

// MainForm 布局部分（拆分参考 WindowTinter 同款：逻辑与 UI 分离）
public partial class MainForm
{
    // 配色令牌（对齐 Web 版 index.html :root 黑金）
    private static readonly Color UiBg = Color.FromArgb(0x1A, 0x1A, 0x1A);      // --bg
    private static readonly Color UiPanel = Color.FromArgb(0x1F, 0x1F, 0x1F);   // --elev
    private static readonly Color UiGold = Color.FromArgb(0xC9, 0xA9, 0x6E);    // --gold
    private static readonly Color UiText = Color.FromArgb(0xDA, 0xDA, 0xDA);    // --text
    private static readonly Color UiMuted = Color.FromArgb(0x84, 0x84, 0x84);   // --muted
    private static readonly Color UiBorder = Color.FromArgb(0x2A, 0x2A, 0x2A);

    private void InitUi()
    {
        Text = $"Clipboard v{Program.AppVersion}";
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(1000, 680);
        MinimumSize = new Size(640, 480);
        BackColor = UiBg;
        ForeColor = UiText;
        Icon = IconFactory.Create();

        // 顶部工具栏：搜索框 + 按钮
        var top = new Panel { Dock = DockStyle.Top, Height = 52, BackColor = UiPanel, Padding = new Padding(12, 10, 12, 8) };
        _searchBox = new TextBox
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(0x26, 0x26, 0x26),
            ForeColor = UiText,
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Microsoft YaHei UI", 10f),
            PlaceholderText = "搜索 标题 / 内容 / 链接…",
        };
        _searchBox.TextChanged += (_, _) => RefreshCards();
        _searchBox.KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) { _searchBox.Clear(); e.Handled = true; } };

        var btns = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, WrapContents = false };
        _archiveToggleBtn = MakeButton("含归档", ToggleArchiveView);
        _archiveToggleBtn.Width = 70;
        btns.Controls.Add(_archiveToggleBtn);
        btns.Controls.Add(MakeButton("存入", CaptureNow));
        btns.Controls.Add(MakeButton("导出", ExportAll));
        btns.Controls.Add(MakeButton("导入", ImportFromWeb));
        top.Controls.Add(_searchBox);
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

        // 空态提示（置于 wall 底层，wall 隐藏时露出）
        _emptyHint = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = UiMuted,
            Font = new Font("Microsoft YaHei UI", 11f),
            Text = "还没有内容\n\n复制任意文本 / 链接 / 图片，即自动存入这里\n也可点工具栏「存入」手动保存当前剪贴板",
            Visible = false,
        };

        // z-order：先 Add 的在底层。hint 在最底，wall 盖住它；空态时隐藏 wall 露出 hint
        Controls.Add(_emptyHint);
        Controls.Add(_wall);
        Controls.Add(statusBar);
        Controls.Add(_tagBar);
        Controls.Add(top);
    }

    private Button MakeButton(string text, Action onClick)
    {
        var btn = new Button
        {
            Text = text,
            Height = 30,
            Width = text == "存入" ? 64 : 58,
            Margin = new Padding(8, 0, 0, 0),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(0x2A, 0x2A, 0x2A),
            ForeColor = UiText,
            Font = new Font("Microsoft YaHei UI", 9f),
            Cursor = Cursors.Hand,
        };
        btn.FlatAppearance.BorderColor = UiBorder;
        btn.FlatAppearance.MouseOverBackColor = Color.FromArgb(0x33, 0x33, 0x33);
        btn.Click += (_, _) => onClick();
        return btn;
    }
}
