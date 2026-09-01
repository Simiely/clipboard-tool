// Controls/JsonDialog.xaml.cs - JSON 格式化预览（对齐 app.js openJsonPreview，M3a 只读）
//  - PrettyJson（LF 统一，互导字节一致）；复制美化结果 → flash"已复制美化 JSON"
//  - 覆盖保存（M3b：带 html 条目同步重建）不在 M3a 范围
using System.Windows;
using System.Windows.Controls;
using ClipboardExe.Models;
using ClipboardExe.Services;

namespace ClipboardExe.Controls;

public partial class JsonDialog : UserControl
{
    private readonly ClipItem _clip;
    private readonly string _formatted;

    public JsonDialog(ClipItem clip)
    {
        InitializeComponent();
        _clip = clip;
        HeadTitle.Text = "JSON 预览 · " + (string.IsNullOrEmpty(clip.Title) ? "未命名" : clip.Title);
        _formatted = Format.PrettyJson(clip.Content);
        JsonBox.Text = _formatted;
        JsonBox.CaretIndex = 0;
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(_formatted);
            ToastService.Flash("已复制美化 JSON");
        }
        catch { ToastService.Error("复制失败"); }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => ModalHost.Close();
}
