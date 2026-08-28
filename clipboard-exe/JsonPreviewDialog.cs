using System.Drawing;
using System.Text.Json;
using System.Windows.Forms;

namespace ClipboardExe;

/// <summary>
/// JSON 格式化预览（对齐 Web openJsonPreview）：文本条目内容可解析为 JSON 时，
/// 卡片 `{}` 按钮打开——美化展示 / 复制美化结果 / 覆盖保存回条目。
/// </summary>
public sealed class JsonPreviewDialog : Form
{
    private readonly ClipItem _item;
    private readonly Storage _storage;
    private readonly Action _refresh;
    private readonly TextBox _view;

    private static readonly Color Bg = Color.FromArgb(0x1A, 0x1A, 0x1A);
    private static readonly Color InputBg = Color.FromArgb(0x26, 0x26, 0x26);
    private static readonly Color Gold = Color.FromArgb(0xC9, 0xA9, 0x6E);
    private static readonly Color TextColor = Color.FromArgb(0xDA, 0xDA, 0xDA);

    public JsonPreviewDialog(ClipItem item, Storage storage, Action refresh)
    {
        _item = item;
        _storage = storage;
        _refresh = refresh;

        Text = "JSON 预览";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        BackColor = Bg;
        ForeColor = TextColor;
        Font = new Font("Microsoft YaHei UI", 9f);
        ClientSize = new Size(520, 420);
        ShowInTaskbar = false;

        _view = new TextBox
        {
            Location = new Point(14, 14),
            Size = new Size(492, 340),
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Both,
            BackColor = InputBg,
            ForeColor = TextColor,
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Consolas", 10f),
        };
        _view.Text = FormatJson(item.Content);
        Controls.Add(_view);

        var copyBtn = MakeButton("复制美化结果", 110);
        copyBtn.Location = new Point(14, 366);
        copyBtn.Click += (_, _) =>
        {
            try { Clipboard.SetText(_view.Text); } catch { /* 忽略 */ }
        };
        Controls.Add(copyBtn);

        var saveBtn = MakeButton("覆盖保存回条目", 120);
        saveBtn.Location = new Point(132, 366);
        saveBtn.BackColor = Color.FromArgb(0x3A, 0x33, 0x24);
        saveBtn.ForeColor = Gold;
        saveBtn.Click += (_, _) =>
        {
            _item.Content = _view.Text;
            _item.UpdatedAt = DateTimeOffset.Now.ToUnixTimeMilliseconds();
            _storage.Save(_storage.Load().Select(c => c.Id == _item.Id ? _item : c).ToList());
            _refresh();
            DialogResult = DialogResult.OK;
            Close();
        };
        Controls.Add(saveBtn);

        var closeBtn = MakeButton("关闭", 70);
        closeBtn.Location = new Point(436, 366);
        closeBtn.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        Controls.Add(closeBtn);
    }

    /// <summary>2 空格缩进美化（对齐 Web JSON 格式化预览）。</summary>
    private static string FormatJson(string s)
    {
        try
        {
            using var doc = JsonDocument.Parse(s);
            return JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = true });
        }
        catch { return s; }
    }

    private Button MakeButton(string text, int width)
    {
        return new Button
        {
            Text = text,
            Width = width,
            Height = 30,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(0x2A, 0x2A, 0x2A),
            ForeColor = TextColor,
            Font = new Font("Microsoft YaHei UI", 9f),
            Cursor = Cursors.Hand,
        };
    }
}
