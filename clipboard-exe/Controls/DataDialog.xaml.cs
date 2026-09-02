// Controls/DataDialog.xaml.cs - 数据管理 / 本地备份（对齐 Web 版 openDataModal 备份区）
//  - 导出：BuildExport → 写 JSON 文件（无 BOM，camelCase，与 Web 互导）
//  - 导入：读 JSON → 校验 {clips[]} → ImportClips 合并 → 刷新
//  - 清空：Confirm → ClearAll → 刷新
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using ClipboardExe.Models;
using ClipboardExe.Services;

namespace ClipboardExe.Controls;

public partial class DataDialog : UserControl
{
    private readonly ClipService _svc;
    private readonly Action _onChanged;

    public DataDialog(ClipService svc, Action onChanged)
    {
        InitializeComponent();
        _svc = svc;
        _onChanged = onChanged;
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog
        {
            Title = "导出剪贴板备份",
            Filter = "JSON 文件|*.json|所有文件|*.*",
            FileName = "clipboard-" + System.DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json",
            DefaultExt = ".json",
            AddExtension = true,
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            var doc = _svc.BuildExport();
            File.WriteAllText(dlg.FileName, Storage.Serialize(doc), new System.Text.UTF8Encoding(false));
            ModalHost.Close();
            ToastService.Flash($"已导出 {doc.Clips.Count} 条（含归档）");
        }
        catch (Exception ex) { ToastService.Error("导出失败：" + ex.Message); }
    }

    private void Import_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "导入剪贴板备份",
            Filter = "JSON 文件|*.json|所有文件|*.*",
            Multiselect = false,
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            var doc = Storage.Deserialize<ExportDoc>(File.ReadAllText(dlg.FileName));
            if (doc == null || doc.Clips == null) throw new InvalidOperationException("不是剪贴板备份文件");
            var res = _svc.ImportClips(doc.Clips);
            ModalHost.Close();
            _onChanged();
            ToastService.Flash($"导入完成：新增 {res.Added} / 更新 {res.Updated} / 跳过 {res.Skipped}（共 {res.Total} 条）");
        }
        catch (Exception ex) { ToastService.Error("导入失败：" + ex.Message); }
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        ModalHost.Confirm("确定清空全部内容（含归档）？此操作不可撤销。",
            () =>
            {
                var n = _svc.ClearAll();
                _onChanged();
                ToastService.Flash($"已清空 {n} 条内容");
            }, "全部清空");
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => ModalHost.Close();
}
