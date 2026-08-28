using System.Drawing;
using System.Windows.Forms;

namespace ClipboardExe;

/// <summary>
/// 存入确认弹窗（对齐 Web 版 openPasteModal 交互：检测到复制内容自动弹出大窗口）。
/// 展示类型徽章 + 内容预览（可编辑）+ 标题 + 标签 chips，「存入」金色主按钮落库、「放弃」丢弃。
/// 图片实体已在捕获时写入 data/files/，放弃时由 MainForm 清理。
/// </summary>
public sealed class CaptureDialog : Form
{
    private readonly ClipItem _pending;
    private readonly List<string> _allTags;
    private readonly HashSet<string> _selectedTags;
    private readonly Storage _storage;

    private readonly TextBox _titleBox;
    private readonly TextBox _contentBox = null!;
    private readonly TextBox _urlBox = null!;
    private readonly TextBox _tagInput;
    private readonly FlowLayoutPanel _tagChips;

    private static readonly Color Bg = Color.FromArgb(0x1A, 0x1A, 0x1A);
    private static readonly Color InputBg = Color.FromArgb(0x26, 0x26, 0x26);
    private static readonly Color Gold = Color.FromArgb(0xC9, 0xA9, 0x6E);
    private static readonly Color TextColor = Color.FromArgb(0xDA, 0xDA, 0xDA);
    private static readonly Color Muted = Color.FromArgb(0x84, 0x84, 0x84);
    private static readonly Color Border = Color.FromArgb(0x2A, 0x2A, 0x2A);
    private static readonly Color Accent2 = Color.FromArgb(0xAE, 0x4D, 0x4D); // 砖红（链接徽章）

    public CaptureDialog(ClipItem pending, List<string> allTags, Storage storage)
    {
        _pending = pending;
        _allTags = allTags;
        _selectedTags = new HashSet<string>(pending.Tags ?? new List<string>(), StringComparer.Ordinal);
        _storage = storage;

        Text = "存入剪贴板";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        BackColor = Bg;
        ForeColor = TextColor;
        Font = new Font("Microsoft YaHei UI", 9f);
        ClientSize = new Size(460, 430);
        ShowInTaskbar = false;

        var y = 14;

        // 类型徽章
        var (badge, badgeColor) = pending.Type switch
        {
            "link" => ("🔗 捕获到链接", Accent2),
            "file" => ("🖼 捕获到图片", Gold),
            _ => ("📄 捕获到文本", Muted),
        };
        Controls.Add(new Label
        {
            Text = badge,
            Location = new Point(16, y),
            AutoSize = true,
            ForeColor = badgeColor,
            Font = new Font("Microsoft YaHei UI", 10.5f, FontStyle.Bold),
        });
        y += 28;
        if (pending.Type == "text" && !string.IsNullOrEmpty(pending.Html))
        {
            Controls.Add(new Label
            {
                Text = "✦ 含富文本格式（复制时点卡片右栏）",
                Location = new Point(16, y),
                AutoSize = true,
                ForeColor = Gold,
            });
            y += 22;
        }

        y = AddLabel(y, "标题（可留空）");
        _titleBox = MakeTextBox();
        _titleBox.Text = pending.Title;
        y = PlaceControl(y, _titleBox, 26);

        if (pending.Type == "link")
        {
            y = AddLabel(y, "链接 URL");
            _urlBox = MakeTextBox();
            _urlBox.Text = pending.Url;
            y = PlaceControl(y, _urlBox, 26);
        }
        else if (pending.Type == "text")
        {
            y = AddLabel(y, "内容");
            _contentBox = MakeTextBox();
            _contentBox.Multiline = true;
            _contentBox.Height = 96;
            _contentBox.ScrollBars = ScrollBars.Vertical;
            _contentBox.Text = pending.Content;
            y = PlaceControl(y, _contentBox, 106);
        }
        else
        {
            var pb = new PictureBox
            {
                Location = new Point(16, y),
                Size = new Size(428, 100),
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.FromArgb(0x12, 0x12, 0x12),
                BorderStyle = BorderStyle.FixedSingle,
            };
            try
            {
                var bytes = _storage.LoadImage(pending.FileId);
                if (bytes != null)
                {
                    using var ms = new MemoryStream(bytes);
                    pb.Image = new Bitmap(ms);
                }
            }
            catch { /* 预览失败不阻塞 */ }
            Controls.Add(pb);
            y += 110;
        }

        y = AddLabel(y, "标签");
        _tagChips = new FlowLayoutPanel
        {
            Location = new Point(16, y),
            Size = new Size(428, 56),
            BackColor = Bg,
            AutoScroll = true,
            WrapContents = true,
            FlowDirection = FlowDirection.LeftToRight,
        };
        RenderChips();
        y += 62;

        _tagInput = MakeTextBox();
        _tagInput.PlaceholderText = "输入新标签，回车或点「添加」";
        _tagInput.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { AddNewTag(); e.Handled = true; e.SuppressKeyPress = true; } };
        _tagInput.Location = new Point(16, y);
        Controls.Add(_tagInput);
        var addBtn = MakeButton("添加", 64);
        addBtn.Location = new Point(322, y - 1);
        addBtn.Click += (_, _) => AddNewTag();
        Controls.Add(addBtn);
        y += 38;

        var cancelBtn = MakeButton("放弃", 70);
        cancelBtn.Location = new Point(374, y);
        cancelBtn.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        Controls.Add(cancelBtn);

        var saveBtn = MakeButton("存入", 70);
        saveBtn.Location = new Point(298, y);
        saveBtn.BackColor = Color.FromArgb(0x3A, 0x33, 0x24);
        saveBtn.ForeColor = Gold;
        saveBtn.FlatAppearance.BorderColor = Gold;
        saveBtn.Click += (_, _) => { DialogResult = DialogResult.OK; Close(); };
        Controls.Add(saveBtn);

        AcceptButton = saveBtn;
    }

    /// <summary>确认后合并编辑值到 pending（图片实体已在捕获时写入 files/）。</summary>
    public ClipItem BuildItem()
    {
        _pending.Title = _titleBox.Text.Trim();
        _pending.Tags = _selectedTags.ToList();
        if (_pending.Type == "text")
        {
            var c = _contentBox.Text;
            if (c.Length > 0) _pending.Content = c;
        }
        else if (_pending.Type == "link")
        {
            var u = CleanUrl.Clean(_urlBox.Text.Trim());
            if (u.Length > 0) _pending.Url = u;
            if (string.IsNullOrEmpty(_pending.Title)) _pending.Title = u.Length > 60 ? u[..60] : u;
        }
        return _pending;
    }

    // ---------------- 标签 chips（与 Web 版标签点选同款交互） ----------------

    private void RenderChips()
    {
        _tagChips.Controls.Clear();
        foreach (var tag in _allTags) _tagChips.Controls.Add(MakeChip(tag));
    }

    private Button MakeChip(string tag)
    {
        var selected = _selectedTags.Contains(tag);
        var chip = new Button
        {
            Text = tag,
            AutoSize = true,
            MinimumSize = new Size(40, 24),
            Margin = new Padding(0, 0, 6, 6),
            Padding = new Padding(8, 0, 8, 0),
            FlatStyle = FlatStyle.Flat,
            BackColor = selected ? Color.FromArgb(0x2E, 0x28, 0x18) : InputBg,
            ForeColor = selected ? Gold : TextColor,
            Font = new Font("Microsoft YaHei UI", 8.5f),
            Cursor = Cursors.Hand,
        };
        chip.FlatAppearance.BorderColor = selected ? Gold : Border;
        chip.FlatAppearance.MouseOverBackColor = Color.FromArgb(0x33, 0x33, 0x33);
        chip.Click += (_, _) =>
        {
            if (!_selectedTags.Add(tag)) _selectedTags.Remove(tag);
            var on = _selectedTags.Contains(tag);
            chip.BackColor = on ? Color.FromArgb(0x2E, 0x28, 0x18) : InputBg;
            chip.ForeColor = on ? Gold : TextColor;
            chip.FlatAppearance.BorderColor = on ? Gold : Border;
        };
        return chip;
    }

    private void AddNewTag()
    {
        var t = _tagInput.Text.Trim();
        if (t.Length == 0) return;
        _selectedTags.Add(t);
        _tagInput.Clear();
        if (!_allTags.Contains(t, StringComparer.Ordinal)) _allTags.Add(t);
        RenderChips();
    }

    // ---------------- 布局工具 ----------------

    private int AddLabel(int y, string text)
    {
        Controls.Add(new Label
        {
            Text = text,
            Location = new Point(16, y),
            AutoSize = true,
            ForeColor = Muted,
        });
        return y + 22;
    }

    private TextBox MakeTextBox()
    {
        return new TextBox
        {
            Location = new Point(16, 0),
            Size = new Size(428, 26),
            BackColor = InputBg,
            ForeColor = TextColor,
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Microsoft YaHei UI", 9.5f),
        };
    }

    private int PlaceControl(int y, Control c, int height)
    {
        c.Location = new Point(16, y);
        if (height > 26) c.Height = height;
        Controls.Add(c);
        return y + height + 6;
    }

    private Button MakeButton(string text, int width)
    {
        return new Button
        {
            Text = text,
            Width = width,
            Height = 28,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(0x2A, 0x2A, 0x2A),
            ForeColor = TextColor,
            Font = new Font("Microsoft YaHei UI", 9f),
            Cursor = Cursors.Hand,
        };
    }
}
