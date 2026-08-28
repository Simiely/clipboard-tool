using System.Drawing;
using System.Windows.Forms;

namespace ClipboardExe;

/// <summary>
/// 编辑弹窗（双击卡片或点 ✎ 打开，对齐 Web openEditModal）：
/// 标题 / 内容（或链接 URL）/ 标签 chips / 「清除格式」一键转纯文本 / 归档-恢复 / 删除。
/// </summary>
public sealed class EditDialog : Form
{
    private readonly ClipItem _item;
    private readonly List<string> _allTags;
    private readonly HashSet<string> _selectedTags;
    private readonly Storage _storage;
    private readonly Action _refresh;

    private readonly TextBox _titleBox;
    private readonly TextBox _contentBox = null!;
    private readonly TextBox _urlBox = null!;
    private readonly TextBox _tagInput;
    private readonly FlowLayoutPanel _tagChips;
    private readonly Button _archiveBtn;
    private readonly Button _clearHtmlBtn = null!;

    private static readonly Color Bg = Color.FromArgb(0x1A, 0x1A, 0x1A);
    private static readonly Color InputBg = Color.FromArgb(0x26, 0x26, 0x26);
    private static readonly Color Gold = Color.FromArgb(0xC9, 0xA9, 0x6E);
    private static readonly Color TextColor = Color.FromArgb(0xDA, 0xDA, 0xDA);
    private static readonly Color Muted = Color.FromArgb(0x84, 0x84, 0x84);
    private static readonly Color Border = Color.FromArgb(0x2A, 0x2A, 0x2A);

    public EditDialog(ClipItem item, List<string> allTags, Storage storage, Action refresh)
    {
        _item = item;
        _allTags = allTags;
        _selectedTags = new HashSet<string>(item.Tags ?? new List<string>(), StringComparer.Ordinal);
        _storage = storage;
        _refresh = refresh;

        Text = "编辑条目";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        BackColor = Bg;
        ForeColor = TextColor;
        Font = new Font("Microsoft YaHei UI", 9f);
        ClientSize = new Size(460, 440);
        ShowInTaskbar = false;

        var y = 14;
        y = AddLabel(y, "标题");
        _titleBox = MakeTextBox();
        _titleBox.Text = _item.Title;
        y = PlaceControl(y, _titleBox, 26);

        if (_item.Type == "link")
        {
            y = AddLabel(y, "链接 URL");
            _urlBox = MakeTextBox();
            _urlBox.Text = _item.Url;
            y = PlaceControl(y, _urlBox, 26);
        }
        else if (_item.Type == "text")
        {
            y = AddLabel(y, "内容");
            _contentBox = MakeTextBox();
            _contentBox.Multiline = true;
            _contentBox.Height = 120;
            _contentBox.ScrollBars = ScrollBars.Vertical;
            _contentBox.Text = _item.Content;
            y = PlaceControl(y, _contentBox, 132);

            // 清除格式（对齐 Web：富文本一键转纯文本）
            if (!string.IsNullOrEmpty(_item.Html))
            {
                var hint = new Label
                {
                    Text = "✦ 此条含富文本格式",
                    Location = new Point(16, y),
                    AutoSize = true,
                    ForeColor = Gold,
                };
                Controls.Add(hint);
                _clearHtmlBtn = MakeButton("清除格式", 88);
                _clearHtmlBtn.Location = new Point(120, y - 2);
                _clearHtmlBtn.Click += (_, _) =>
                {
                    _item.Html = "";
                    _clearHtmlBtn.Enabled = false;
                    hint.Text = "已转为纯文本";
                    hint.ForeColor = Muted;
                };
                Controls.Add(_clearHtmlBtn);
                y += 26;
            }
        }
        else
        {
            y = AddLabel(y, "文件");
            var fileBox = MakeTextBox();
            fileBox.ReadOnly = true;
            fileBox.Text = $"{_item.FileName} · {_item.FileSize / 1024} KB · {_item.FileMime}";
            y = PlaceControl(y, fileBox, 26);
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

        // 归档 / 恢复
        _archiveBtn = MakeButton(_item.Archived ? "从归档恢复" : "移入归档", 100);
        _archiveBtn.Location = new Point(16, y);
        _archiveBtn.Click += (_, _) =>
        {
            if (_item.Archived) _storage.UnarchiveClip(_item.Id);
            else _storage.ArchiveClip(_item.Id);
            _refresh();
            DialogResult = DialogResult.OK;
            Close();
        };
        Controls.Add(_archiveBtn);

        var deleteBtn = MakeButton("删除", 70);
        deleteBtn.Location = new Point(122, y);
        deleteBtn.ForeColor = Color.FromArgb(0xE0, 0x8A, 0x7A);
        deleteBtn.Click += (_, _) =>
        {
            if (MessageBox.Show("删除这条记录？", "Clipboard", MessageBoxButtons.OKCancel,
                    MessageBoxIcon.Question) == DialogResult.OK)
            {
                _storage.Delete(_item.Id);
                _refresh();
                DialogResult = DialogResult.OK;
                Close();
            }
        };
        Controls.Add(deleteBtn);

        var cancelBtn = MakeButton("取消", 70);
        cancelBtn.Location = new Point(374, y);
        cancelBtn.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        Controls.Add(cancelBtn);

        var saveBtn = MakeButton("保存", 70);
        saveBtn.Location = new Point(298, y);
        saveBtn.BackColor = Color.FromArgb(0x3A, 0x33, 0x24);
        saveBtn.ForeColor = Gold;
        saveBtn.FlatAppearance.BorderColor = Gold;
        saveBtn.Click += (_, _) => { Save(); };
        Controls.Add(saveBtn);

        AcceptButton = saveBtn;
    }

    private void Save()
    {
        _item.Title = _titleBox.Text.Trim();
        _item.Tags = _selectedTags.ToList();
        if (_item.Type == "text")
        {
            var c = _contentBox.Text;
            if (c.Length == 0) { MessageBox.Show("内容不能为空", "Clipboard", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            _item.Content = c;
        }
        else if (_item.Type == "link")
        {
            var u = CleanUrl.Clean(_urlBox.Text.Trim());
            if (!u.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !u.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show("链接需以 http(s):// 开头", "Clipboard", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            _item.Url = u;
            if (string.IsNullOrEmpty(_item.Title)) _item.Title = u.Length > 60 ? u[..60] : u;
        }
        _item.UpdatedAt = DateTimeOffset.Now.ToUnixTimeMilliseconds();
        _storage.Save(_storage.Load().Select(c => c.Id == _item.Id ? _item : c).ToList());
        _refresh();
        DialogResult = DialogResult.OK;
        Close();
    }

    // ---------------- 标签 chips ----------------

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
        Controls.Add(new Label { Text = text, Location = new Point(16, y), AutoSize = true, ForeColor = Muted });
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
