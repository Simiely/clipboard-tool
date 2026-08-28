using System.Drawing;
using System.Windows.Forms;

namespace ClipboardExe;

/// <summary>简单单行输入弹窗（批量加/减标签等场景复用）。</summary>
public sealed class InputDialog : Form
{
    private readonly TextBox _input;

    public string Value => _input.Text.Trim();

    private InputDialog(string title, string prompt)
    {
        Text = title;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        BackColor = Color.FromArgb(0x1A, 0x1A, 0x1A);
        ForeColor = Color.FromArgb(0xDA, 0xDA, 0xDA);
        Font = new Font("Microsoft YaHei UI", 9f);
        ClientSize = new Size(360, 110);
        ShowInTaskbar = false;

        var label = new Label
        {
            Text = prompt,
            Location = new Point(16, 14),
            AutoSize = true,
            ForeColor = Color.FromArgb(0x84, 0x84, 0x84),
        };
        Controls.Add(label);

        _input = new TextBox
        {
            Location = new Point(16, 40),
            Size = new Size(328, 26),
            BackColor = Color.FromArgb(0x26, 0x26, 0x26),
            ForeColor = Color.FromArgb(0xDA, 0xDA, 0xDA),
            BorderStyle = BorderStyle.FixedSingle,
        };
        _input.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { DialogResult = DialogResult.OK; Close(); } };
        Controls.Add(_input);

        var cancel = new Button
        {
            Text = "取消",
            Width = 70, Height = 28,
            Location = new Point(274, 72),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(0x2A, 0x2A, 0x2A),
            ForeColor = Color.FromArgb(0xDA, 0xDA, 0xDA),
        };
        cancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        Controls.Add(cancel);

        var ok = new Button
        {
            Text = "确定",
            Width = 70, Height = 28,
            Location = new Point(198, 72),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(0x3A, 0x33, 0x24),
            ForeColor = Color.FromArgb(0xC9, 0xA9, 0x6E),
        };
        ok.Click += (_, _) => { DialogResult = DialogResult.OK; Close(); };
        Controls.Add(ok);

        AcceptButton = ok;
    }

    /// <summary>弹出输入框，返回输入值（取消返回 null）。</summary>
    public static string? Ask(string title, string prompt, IWin32Window owner)
    {
        using var dlg = new InputDialog(title, prompt);
        return dlg.ShowDialog(owner) == DialogResult.OK ? dlg.Value : null;
    }
}
