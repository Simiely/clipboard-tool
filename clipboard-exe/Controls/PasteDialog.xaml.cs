// Controls/PasteDialog.xaml.cs - 存入弹窗（对齐 app.js openPasteModal / savePasteContent）
//  - 类型徽章实时识别（文件 / 图片 / 链接 / 文本，对齐 updateBadge：pickedFile 优先「将存为：文件」；图片也走 file 通道不切徽章）
//  - Ctrl+Enter 快速存入；300ms 防抖重复检测 → 命中关自己 + DuplicateFound（MainWindow 打开该条目编辑窗带提示）
//  - 打开时自动填入剪贴板文本（autoFillPasteModal 对齐：readText → textarea，flash "已填入剪贴板内容"）
//  - 文件线（M3b-2a）+ 图片线（M3b-2b）：粘贴/拖放/📁 选择文件 → 10MB 拒收（对齐 pick：>10MB errToast）→ chip 显示
//    （对齐 .file-chip：📎 + fname + fsize + ✕ 取消；选中后隐藏 textarea + 徽章切「将存为：文件」）
//    → 存入走 FileStore.Save（实体落 data/files/）+ svc.Create(type=file)
//    M3b-2b：图片（MimeFromPath/剪贴板 Bitmap）也是文件，直接走同一路径（Web 版图也是 file）
//  - Save：文件/链接/文本三分支（对齐 savePasteContent）；文件分支 title 用别名 || 文件名
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ClipboardExe.Models;
using ClipboardExe.Services;
using Microsoft.Win32;

namespace ClipboardExe.Controls;

public partial class PasteDialog : UserControl
{
    private readonly ClipService _svc;
    private readonly FileStore _fileStore;
    private readonly Func<List<ClipItem>> _getClips;
    private readonly Func<List<string>> _getTags;
    private readonly DispatcherTimer _dupTimer;
    private bool _dupJumped;

    /// <summary>已选文件（含图片：图片在 M3b-2b 也走 file 通道）。</summary>
    private PickedFile? _pickedFile;

    /// <summary>输入命中重复 → MainWindow 关闭本窗并打开该条目编辑弹窗（dup=true）。</summary>
    public event Action<ClipItem>? DuplicateFound;

    /// <summary>存入成功 → MainWindow 刷新列表。</summary>
    public event Action? Saved;

    public PasteDialog(ClipService svc, FileStore fileStore, Func<List<ClipItem>> getClips, Func<List<string>> getTags)
    {
        InitializeComponent();
        _svc = svc;
        _fileStore = fileStore;
        _getClips = getClips;
        _getTags = getTags;

        ExpireBox.ItemsSource = new[]
        {
            new ExpOpt("", "永久"),
            new ExpOpt("1h", "1 小时后过期"),
            new ExpOpt("1d", "1 天后过期"),
            new ExpOpt("7d", "7 天后过期"),
            new ExpOpt("30d", "30 天后过期"),
        };
        ExpireBox.DisplayMemberPath = nameof(ExpOpt.Label);
        ExpireBox.SelectedIndex = 0;

        TagPick.SetTags(Array.Empty<string>(), getTags());

        _dupTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _dupTimer.Tick += (_, _) => CheckDuplicate();

        // 粘贴拦截：资源管理器复制文件 Ctrl+V → FileDrop → 选文件（对齐 ta paste ②图片/文件优先）
        InputBox.AddHandler(DataObject.PastingEvent, new DataObjectPastingEventHandler(OnPasting), true);

        // 打开即自动填入剪贴板文本（对齐 autoFillPasteModal：文本优先）
        try
        {
            var t = Clipboard.GetText();
            if (!string.IsNullOrEmpty(t) && string.IsNullOrEmpty(InputBox.Text))
            {
                InputBox.Text = t;
                UpdateBadge();
                ToastService.Flash("已填入剪贴板内容");
            }
        }
        catch { /* 剪贴板不可读则留空 */ }
        UpdateBadge();
    }

    // ---- 类型徽章实时识别（对齐 updateBadge：文件 > 链接 > 文本） ----
    private void UpdateBadge()
    {
        if (_pickedFile != null)
        {
            TypeBadge.Style = (Style)FindResource("TypeBadgeFile");
            TypeBadge.Content = "将存为：文件";
            return;
        }
        var content = (InputBox.Text ?? "").Trim();
        if (content.Length == 0 || !LooksLikeUrl(content))
        {
            TypeBadge.Style = (Style)FindResource("TypeBadgeText");
            TypeBadge.Content = "将存为：文本";
        }
        else
        {
            TypeBadge.Style = (Style)FindResource("TypeBadgeLink");
            TypeBadge.Content = "将存为：链接";
        }
    }

    private static bool LooksLikeUrl(string s) => System.Text.RegularExpressions.Regex.IsMatch(s, @"^https?://\S+$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    // ---- 文件拾取（对齐 pick：>10MB 拒收；chip 显示；隐藏 textarea；徽章切文件；M3b-2b 图片也走此路径） ----
    private void PickFile(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists) return;
            if (info.Length > FileStore.MaxFileSize)
            {
                ToastService.Error("文件超过 10MB 上限");
                return;
            }
            var mime = FileStore.MimeFromPath(path);
            var isImage = mime.StartsWith("image/", StringComparison.OrdinalIgnoreCase);
            _pickedFile = new PickedFile { Name = info.Name, Bytes = File.ReadAllBytes(path), Mime = mime, Size = info.Length };
            RenderChip();
            InputBox.Visibility = Visibility.Collapsed; // 对齐 syncTextareaVisibility：选文件后隐藏文本区
            UpdateBadge();
            ToastService.Flash(isImage ? "已接收图片，点存入即可" : "已接收文件，点存入即可"); // 对齐 Web flash
        }
        catch (Exception ex)
        {
            ToastService.Error("读取文件失败: " + ex.Message);
        }
    }

    /// <summary>M3b-2b：直接接收 (bytes/name/mime)——用于剪贴板截图/拖放图片等非 FileDrop 路径。</summary>
    private void PickBytes(byte[] bytes, string name, string mime)
    {
        try
        {
            if (bytes == null || bytes.Length == 0) { ToastService.Error("文件为空"); return; }
            if (bytes.Length > FileStore.MaxFileSize)
            {
                ToastService.Error("文件超过 10MB 上限");
                return;
            }
            _pickedFile = new PickedFile { Name = name, Bytes = bytes, Mime = mime, Size = bytes.Length };
            RenderChip();
            InputBox.Visibility = Visibility.Collapsed;
            UpdateBadge();
            ToastService.Flash(mime.StartsWith("image/", StringComparison.OrdinalIgnoreCase) ? "已接收图片，点存入即可" : "已接收文件，点存入即可");
        }
        catch (Exception ex)
        {
            ToastService.Error("接收文件失败: " + ex.Message);
        }
    }

    /// <summary>文件 chip（对齐 .file-chip：📎 fname(省略) · fsize + ✕ 取消）。</summary>
    private void RenderChip()
    {
        ChipBox.Children.Clear();
        if (_pickedFile == null) return;

        var chip = new Border
        {
            Background = (Brush)FindResource("InsetBrush"),
            CornerRadius = (CornerRadius)FindResource("RadiusBtn"),
            Padding = new Thickness(11, 14, 11, 14),
            Margin = new Thickness(0, 10, 0, 0),
        };
        var row = new DockPanel();
        // ✕ 取消（右，对齐 .file-chip .rm：elev 底 r-8 hover 红）
        var rm = new Button
        {
            Content = "✕",
            FontSize = 11,
            Style = (Style)FindResource("OpsIconBtnDel"),
            Width = 24,
            Height = 24,
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = Cursors.Hand,
        };
        DockPanel.SetDock(rm, Dock.Right);
        rm.Click += (_, _) => ClearPicked();
        // fsize（右，dim 11px）
        var fsize = new TextBlock
        {
            Text = Format.Size(_pickedFile.Size),
            FontSize = 11,
            Foreground = (Brush)FindResource("DimBrush"),
            Margin = new Thickness(10, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        DockPanel.SetDock(fsize, Dock.Right);
        // fname（填充省略）
        var fname = new TextBlock
        {
            Text = _pickedFile.Name,
            FontSize = 13,
            Foreground = (Brush)FindResource("TextBrush"),
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };
        row.Children.Add(fname);
        row.Children.Add(fsize);
        row.Children.Add(rm);
        chip.Child = row;
        ChipBox.Children.Add(chip);
    }

    private void ClearPicked()
    {
        _pickedFile = null;
        ChipBox.Children.Clear();
        InputBox.Visibility = Visibility.Visible;
        UpdateBadge();
    }

    // ---- 粘贴 / 拖放 / 选择文件 ----
    private void OnPasting(object sender, DataObjectPastingEventArgs e)
    {
        var data = e.DataObject;
        // M3b-2b：剪贴板截图（Bitmap）→ 转 PNG 字节 → 走 PickBytes（对齐 Web paste ②图片优先）
        if (data.GetDataPresent(DataFormats.Bitmap))
        {
            e.CancelCommand();
            if (data.GetData(DataFormats.Bitmap) is System.Windows.Media.Imaging.BitmapSource bmp)
            {
                var bytes = EncodePng(bmp);
                if (bytes != null) PickBytes(bytes, "paste-image.png", "image/png");
            }
            return;
        }
        // 文件粘贴：FileDrop → PickFile（对齐 Web paste ②文件优先 + preventDefault）
        if (data.GetDataPresent(DataFormats.FileDrop))
        {
            e.CancelCommand();
            if (data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0) PickFile(files[0]);
        }
    }

    private void Root_DragOver(object sender, DragEventArgs e)
    {
        // 文件或图片均可拖入
        var ok = e.Data.GetDataPresent(DataFormats.FileDrop) || e.Data.GetDataPresent(DataFormats.Bitmap);
        e.Effects = ok ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void Root_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop) && e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
        {
            PickFile(files[0]);
        }
        else if (e.Data.GetDataPresent(DataFormats.Bitmap) && e.Data.GetData(DataFormats.Bitmap) is System.Windows.Media.Imaging.BitmapSource bmp)
        {
            var bytes = EncodePng(bmp);
            if (bytes != null) PickBytes(bytes, "paste-image.png", "image/png");
        }
        e.Handled = true;
    }

    /// <summary>M3b-2b：BitmapSource → PNG 字节（剪贴板/拖放截图 → 实体存储）。对齐 Web blobToPng canvas.toBlob("image/png")。</summary>
    private static byte[]? EncodePng(System.Windows.Media.Imaging.BitmapSource bmp)
    {
        try
        {
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
            using var ms = new MemoryStream();
            encoder.Save(ms);
            return ms.ToArray();
        }
        catch { return null; }
    }

    private void PickFileBtn_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Title = "选择文件", Filter = "所有文件 (*.*)|*.*" };
        if (dlg.ShowDialog() == true) PickFile(dlg.FileName);
    }

    // ---- 输入：徽章刷新 + 300ms 重复检测（对齐 checkDuplicate：命中切编辑窗，一次只触发一次） ----
    private void Input_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateBadge();
        if (_dupJumped) return;
        _dupTimer.Stop();
        _dupTimer.Start();
    }

    private void Input_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            e.Handled = true;
            Save();
        }
    }

    private void CheckDuplicate()
    {
        if (_dupJumped) return;
        var content = (InputBox.Text ?? "").Trim();
        if (content.Length == 0) return;
        var dup = ClipService.FindDuplicate(content, _getClips());
        if (dup == null) return;
        _dupJumped = true;
        ModalHost.Close();
        DuplicateFound?.Invoke(dup);
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        InputBox.Text = "";
        ClearPicked();
        ToastService.Flash("已清空");
        InputBox.Focus();
    }

    private void Save_Click(object sender, RoutedEventArgs e) => Save();

    private void Cancel_Click(object sender, RoutedEventArgs e) => ModalHost.Close();

    /// <summary>存入（对齐 savePasteContent：文件/链接/文本三分支；空内容提示）。</summary>
    private void Save()
    {
        var content = (InputBox.Text ?? "").Trim();
        if (content.Length == 0 && _pickedFile == null)
        {
            ToastService.Error("先粘贴内容或选择文件");
            return;
        }
        var title = TitleBox.Text ?? "";
        var tags = TagPick.Selected.ToList();
        var expire = (ExpireBox.SelectedItem as ExpOpt)?.Value ?? "";
        try
        {
            if (_pickedFile != null)
            {
                var meta = _fileStore.Save(_pickedFile.Bytes, _pickedFile.Name, _pickedFile.Mime);
                _svc.Create("file", title.Length > 0 ? title : _pickedFile.Name, null, null, null, tags, expire,
                    meta.FileId, meta.FileName, meta.FileSize, meta.FileMime); // 对齐：文件标题 = 别名 || 文件名
            }
            else if (LooksLikeUrl(content))
            {
                _svc.Create("link", title, null, null, content, tags, expire);
            }
            else
            {
                _svc.Create("text", title, content, null, null, tags, expire);
            }
            ModalHost.Close();
            ToastService.Flash("已存入");
            Saved?.Invoke();
        }
        catch (Exception ex)
        {
            ToastService.Error(ex.Message);
        }
    }

    /// <summary>已选文件（字节 + 元信息；含图片——M3b-2b 图片也走 file 通道）。</summary>
    private sealed class PickedFile
    {
        public string Name { get; init; } = "";
        public byte[] Bytes { get; init; } = Array.Empty<byte>();
        public string Mime { get; init; } = "";
        public long Size { get; init; }
    }

    /// <summary>过期选项（对齐 resolveExpire 的 '1h'|'1d'|'7d'|'30d'|''）。</summary>
    private sealed class ExpOpt(string value, string label)
    {
        public string Value { get; } = value;
        public string Label { get; } = label;
    }
}
