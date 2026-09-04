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
    private bool _autoFilling; // autoFill 程序赋值期间置 true：不触发用户输入级重复检测（对齐 Web：程序 set value 不派发 input 事件）

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
        // 注:不能同时设 DisplayMemberPath + ItemTemplate(WPF 抛 InvalidOperationException)。
        //   DarkCombo 自定义 Template 不应用 DisplayMemberPath,只能靠 XAML 的 ItemTemplate 绑 Label。
        ExpireBox.SelectedIndex = 0;

        TagPick.SetTags(Array.Empty<string>(), getTags());

        _dupTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _dupTimer.Tick += (_, _) => CheckDuplicate();

        // 生命周期契约：本控件离开视觉树（保存/取消/失焦自动关/被其它弹窗顶替）即取消迟到的重复检测——
        // 否则用户 300ms 内保存后，定时器到点会拿"刚入库的文本"再查一次必然命中自身，
        // 自动 DuplicateFound → MainWindow 再弹编辑窗 = "存完/取消完又蹦出第二扇窗"。
        // （DispatcherTimer 在控件卸载后仍会持续触发，官方模式即 Unloaded 中 Stop。）
        Unloaded += (_, _) => CancelPendingDuplicate();

        // 粘贴拦截：资源管理器复制文件 Ctrl+V → FileDrop → 选文件（对齐 ta paste ②图片/文件优先）
        InputBox.AddHandler(DataObject.PastingEvent, new DataObjectPastingEventHandler(OnPasting), true);

        // 打开即自动填入剪贴板文本（对齐 autoFillPasteModal：文本优先）
        // 程序赋值不触发 Web input 事件 → 也不该启动重复检测；WPF 赋值会触发 TextChanged，
        // 用 _autoFilling 抑制，仅保留徽章刷新（UpdateBadge 在赋值后显式调用）。
        try
        {
            var t = Clipboard.GetText();
            if (!string.IsNullOrEmpty(t) && string.IsNullOrEmpty(InputBox.Text))
            {
                _autoFilling = true;
                try { InputBox.Text = t; }
                finally { _autoFilling = false; }
                UpdateBadge();
                ToastService.Flash("已填入剪贴板内容");
            }
            // 纯图片剪贴板（截图 Win+Shift+S / 右键复制图片 / 微信QQ复制图片：无文本但有 Bitmap/DIB/PNG）
            // → 打开即自动接收成图片 chip——此前只认文本，图片复制打开弹窗后是空窗（图片识别断点②）。
            // 文本优先级不变：已有文本（富文本复制）不抢，仍可手动 Ctrl+V 覆盖。
            else if (ClipboardHelper.IsImageOnlyClipboard())
            {
                var png = ClipboardHelper.ReadImageOnlyAsPng();
                if (png != null) PickBytes(png, "clipboard-image.png", "image/png");
            }
        }
        catch { /* 剪贴板不可读则留空 */ }
        UpdateBadge();

        // 对齐 Web v0.6.6（app.js: autoFillPasteModal(...).then(() => checkDuplicate())——
        // "自动填入(直接赋值不触发 input)后立即补一次重复检测"）：
        // TextBox.Text 程序赋值会触发 TextChanged（MS Learn），但被 _autoFilling 抑制（不算用户输入、不启动检测）；
        // 若此处不补查，则「打开存入窗即自动填入」的场景（Ctrl+V / ＋存入 等无预查入口）永远不做去重，
        // 库里已有相同内容也会被存成多张卡。无文本/选文件时 CheckDuplicate 内部会自行跳过。
        _dupTimer.Stop();
        _dupTimer.Start();
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
        // M3b-2b：纯图片剪贴板（截图/复制图片，有图且无文本）→ 转 PNG 字节 → 走 PickBytes。
        //   判定用 IsImageOnly（而不是 GetDataPresent(Bitmap)）：富文本复制（Word/网页）常带 CF_BITMAP/
        //   DIB 位图预览 + 文本，若见位图就当图片会把文本误存成图（对齐 Web paste ②图片优先 + 防误伤）。
        if (ClipboardHelper.IsImageOnly(data))
        {
            e.CancelCommand();
            var bytes = ClipboardHelper.ReadImageOnlyAsPng(data);
            if (bytes != null) PickBytes(bytes, "clipboard-image.png", "image/png");
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
            var bytes = ClipboardHelper.EncodePng(bmp);
            if (bytes != null) PickBytes(bytes, "paste-image.png", "image/png");
        }
        e.Handled = true;
    }

    private void PickFileBtn_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Title = "选择文件", Filter = "所有文件 (*.*)|*.*" };
        ModalHost.SuppressDismiss = true; // 子对话框期间屏蔽失焦自动关闭，避免误关本弹窗
        var ok = dlg.ShowDialog() == true;
        ModalHost.SuppressDismiss = false;
        if (ok) PickFile(dlg.FileName);
    }

    // ---- 输入：徽章刷新 + 300ms 重复检测（对齐 checkDuplicate：命中切编辑窗，一次只触发一次） ----
    private void Input_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateBadge();
        if (_autoFilling || _dupJumped) return; // autoFill 程序赋值不算用户输入；已跳转不再重复检测
        _dupTimer.Stop();
        _dupTimer.Start();
    }

    private void Input_KeyDown(object sender, KeyEventArgs e)
    {
        var ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        // Ctrl+V 纯图片兜底：剪贴板只有图片无文本时，TextBox 粘贴命令 CanPaste=false → DataObject.Pasting
        // 事件不触发（OnPasting 收不到，图片识别断点③）→ 在 PreviewKeyDown 层直接读图收下。
        //   有文本时不抢（富文本复制按文本走，用户可按 Ctrl 粘贴文本）；FileDrop 仍走 OnPasting。
        if (ctrl && e.Key == Key.V)
        {
            var png = ClipboardHelper.ReadImageOnlyAsPng();
            if (png != null)
            {
                e.Handled = true; // 吞掉本次粘贴，避免 TextBox 对无文本剪贴板无操作后事件继续冒泡
                PickBytes(png, "clipboard-image.png", "image/png");
                return;
            }
        }
        if (e.Key == Key.Enter && ctrl)
        {
            e.Handled = true;
            Save();
        }
    }

    private void CheckDuplicate()
    {
        // 已跳转 / 已不在台上（被关闭或被其它弹窗顶替）：迟到的 tick 一律不作数，避免关错窗/乱开窗。
        if (_dupJumped || !IsVisible) return;
        if (_pickedFile != null) return; // 已选文件（含图片）走文件线：不按文本查重复（对齐 Web checkDuplicate：pickedFile 不查）
        var content = (InputBox.Text ?? "").Trim();
        if (content.Length == 0) return;
        var dup = ClipService.FindDuplicate(content, _getClips());
        if (dup == null) return;
        JumpToDuplicate(dup);
    }

    /// <summary>命中重复：标记已跳转、关闭本窗、通知 MainWindow 打开该条目编辑窗（dup=true，带「已有相同内容」提示）。
    /// 输入级 CheckDuplicate 与保存兜底共用（对齐 Web：命中 → m.remove + openEditModal(dup, true)）。</summary>
    private void JumpToDuplicate(ClipItem dup)
    {
        _dupJumped = true;
        ModalHost.Close();
        DuplicateFound?.Invoke(dup);
    }

    /// <summary>取消一切"进行中/迟到"的重复检测。窗口生命周期出口（保存/取消/失焦自动关/被顶替）统一调用。</summary>
    private void CancelPendingDuplicate()
    {
        _dupTimer.Stop();
        _dupJumped = true;
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
        // 保存兜底去重（对齐 Web savePasteContent：v0.6.6 存入前 findDuplicateClip 拦截，命中关窗切编辑窗）——
        // 覆盖输入级检测被绕过的路径：autoFill 后 300ms 内直接 Ctrl+Enter 存入、用户手动改回已有文本等。
        // 文件线（_pickedFile != null）不查（对齐 Web：pickedFile 跳过；文件本就允许多份）。
        if (_pickedFile == null && ClipService.FindDuplicate(content, _getClips()) is { } dup)
        {
            JumpToDuplicate(dup);
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
