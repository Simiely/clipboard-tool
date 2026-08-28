using System.Drawing;
using System.Windows.Forms;

namespace ClipboardExe;

/// <summary>
/// 编辑弹窗（双击卡片打开）：标题 / 内容（或链接 URL）/ 标签 chips / 富文本指示 / 归档-恢复。
/// 标签编辑对齐用户偏好：可点击标签芯片（已有标签点选，选中金色边框），输入框即时新增。
/// 返回 DialogResult.OK + 各操作标志（Save/Archive/Unarchive），由 MainForm 落库。
/// </summary>
public sealed class EditDialog : Form
{
    private readonly ClipItem _item;
    private readonly List<string> _allTags;
    private readonly HashSet<string> _selectedTags;

    private readonly TextBox _titleBox;
    private readonly TextBox _contentBox;
    private readonly TextBox _urlBox;
    private readonly TextBox _tagInput;
    private readonly FlowLayoutPanel _tagChips;
    private readonly Button _archiveBtn;

    /// <summary>编辑后的值（保存时读取）。</summary>
    public string EditedTitle => _titleBox.Text.Trim();
    public string EditedContent => _contentBox.Text;
    public string EditedUrl => _urlBox.Text.Trim();
    public List<string> EditedTags => _selectedTags.ToList();

    /// <summary>操作标志。</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool SaveRequested { get; private set; }
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool ArchiveRequested { get; private set; }
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool UnarchiveRequested { get; private set; }

    private static readonly Color Bg = Color.FromArgb(0x1A, 0x1A, 0x1A);
    private static readonly Color Panel = Color.FromArgb(0x1F, 0x1F, 0x1F);
    private static readonly Color InputBg = Color.FromArgb(0x26, 0x26, 0x26);
    private static readonly Color Gold = Color.FromArgb(0xC9, 0xA9, 0x6E);
    private static readonly Color TextColor = Color.FromArgb(0xDA, 0xDA, 0xDA);
    private static readonly Color Muted = Color.FromArgb(0x84, 0x84, 0x84);
    private static readonly Color Border = Color.FromArgb(0x2A, 0x2A, 0x2A);

    public EditDialog(ClipItem item, List<string> allTags)
    {
        _item = item;
        _allTags = allTags;
        _selectedTags = new HashSet<string>(item.Tags ?? new List<string>(), StringComparer.Ordinal);

        Text = "编辑条目";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        BackColor = Bg;
        ForeColor = TextColor;
        Font = new Font("Microsoft YaHei UI", 9f);
        ClientSize = new Size(460, 400);
        ShowInTaskbar = false;

        var y = 14;

        y = AddLabel(y, "标题");
        _titleBox = MakeTextBox(360);
        _titleBox.Text = _item.Title;
        y = PlaceControl(y, _titleBox, 60);

        if (_item.Type == "link")
        {
            y = AddLabel(y, "链接 URL");
            _urlBox = MakeTextBox(360);
            _urlBox.Text = _item.Url;
            y = PlaceControl(y, _urlBox, 60);
        }
        else if (_item.Type == "text")
        {
            y = AddLabel(y, "内容");
            _contentBox = MakeTextBox(360);
            _contentBox.Multiline = true;
            _contentBox.Height = 130;
            _contentBox.ScrollBars = ScrollBars.Vertical;
            _contentBox.Text = _item.Content;
            y = PlaceControl(y, _contentBox, 160);
            if (!string.IsNullOrEmpty(_item.Html))
            {
                y = AddHint(y, "✦ 此条含富文本格式——复制时右键可选「复制富文本」");
            }
        }
        else
        {
            y = AddLabel(y, "文件");
            var fileBox = MakeTextBox(360);
            fileBox.ReadOnly = true;
            fileBox.Text = $"{_item.FileName} · {_item.FileSize / 1024} KB · {_item.FileMime}";
            y = PlaceControl(y, fileBox, 60);
        }

        y = AddLabel(y, "标签");
        _tagChips = new FlowLayoutPanel
        {
            Location = new Point(16, y),
            Size = new Size(428, 60),
            BackColor = Bg,
            AutoScroll = true,
            WrapContents = true,
            FlowDirection = FlowDirection.LeftToRight,
        };
        RenderChips();
        y += 66;

        // 新增标签输入
        _tagInput = MakeTextBox(300);
        _tagInput.PlaceholderText = "输入新标签，回车或点「添加」";
        _tagInput.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { AddNewTag(); e.Handled = true; e.SuppressKeyPress = true; } };
        _tagInput.Location = new Point(16, y);
        Controls.Add(_tagInput);

        var addBtn = MakeButton("添加", 64);
        addBtn.Location = new Point(322, y - 1);
        addBtn.Click += (_, _) => AddNewTag();
        Controls.Add(addBtn);
        y += 38;

        // 归档 / 恢复
        _archiveBtn = MakeButton(_item.Archived ? "从归档恢复" : "移入归档", 100);
        _archiveBtn.Location = new Point(16, y);
        _archiveBtn.Click += (_, _) =>
        {
            ArchiveRequested = !_item.Archived;
            UnarchiveRequested = _item.Archived;
            DialogResult = DialogResult.OK;
            Close();
        };
        Controls.Add(_archiveBtn);

        var cancelBtn = MakeButton("取消", 70);
        cancelBtn.Location = new Point(374, y);
        cancelBtn.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        Controls.Add(cancelBtn);

        var saveBtn = MakeButton("保存", 70);
        saveBtn.Location = new Point(298, y);
        saveBtn.BackColor = Color.FromArgb(0x3A, 0x33, 0x24);
        saveBtn.ForeColor = Gold;
        saveBtn.FlatAppearance.BorderColor = Gold;
        saveBtn.Click += (_, _) =>
        {
            SaveRequested = true;
            DialogResult = DialogResult.OK;
            Close();
        };
        Controls.Add(saveBtn);
    }

    // ---------------- 标签 chips ----------------

    private void RenderChips()
    {
        _tagChips.Controls.Clear();
        foreach (var tag in _allTags)
        {
            var chip = MakeChip(tag);
            _tagChips.Controls.Add(chip);
        }
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
            chip.BackColor = _selectedTags.Contains(tag) ? Color.FromArgb(0x2E, 0x28, 0x18) : InputBg;
            chip.ForeColor = _selectedTags.Contains(tag) ? Gold : TextColor;
            chip.FlatAppearance.BorderColor = _selectedTags.Contains(tag) ? Gold : Border;
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

    private int AddHint(int y, string text)
    {
        Controls.Add(new Label
        {
            Text = text,
            Location = new Point(16, y),
            AutoSize = true,
            ForeColor = Gold,
        });
        return y + 22;
    }

    private TextBox MakeTextBox(int width)
    {
        return new TextBox
        {
            Location = new Point(16, 0),
            Size = new Size(width, 26),
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
