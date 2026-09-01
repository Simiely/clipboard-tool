// Controls/EditDialog.xaml.cs - 编辑弹窗（对齐 app.js openEditModal，M3a：文本/链接）
//  - dup=true → 「已有相同内容」常驻提示（.edit-dup）
//  - 内容区：link → URL textarea（提示"保存后按链接类型存储，点击卡片可复制此地址"）；text → content textarea
//  - 元数据：别名 + 过期 select（按剩余时长回显 1h/1d/7d/30d）+ 标签选择器（预选条目自身标签）
//  - 操作：归档（非归档条目）→ Archive → Archived 事件；保存 → Update → Saved；取消
//  - 焦点：打开聚焦别名框（对齐 U-4）；别名框 Enter 且无内容框时快捷保存（对齐 title.onkeydown）
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ClipboardExe.Models;
using ClipboardExe.Services;

namespace ClipboardExe.Controls;

public partial class EditDialog : UserControl
{
    private readonly ClipService _svc;
    private readonly ClipItem _clip;
    private readonly Func<List<string>> _getTags;

    /// <summary>保存成功 → MainWindow 刷新列表。</summary>
    public event Action? Saved;

    /// <summary>归档成功 → MainWindow 刷新列表。</summary>
    public event Action? Archived;

    public EditDialog(ClipService svc, ClipItem clip, Func<List<string>> getTags, bool dup = false)
    {
        InitializeComponent();
        _svc = svc;
        _clip = clip;
        _getTags = getTags;

        var isLink = clip.Type == "link";
        var icon = isLink ? "🔗" : (clip.Html.Length > 0 ? "✦" : "📝");
        HeadIcon.Text = icon;
        HeadTitle.Text = "编辑" + (string.IsNullOrEmpty(clip.Title) ? "" : " · " + clip.Title);
        TypeBadge.Style = (Style)FindResource(isLink ? "TypeBadgeLink" : clip.Html.Length > 0 ? "TypeBadgeRich" : "TypeBadgeText");
        TypeBadge.Content = isLink ? "链接" : clip.Html.Length > 0 ? "格式文本" : "文本";

        if (dup) DupTip.Visibility = Visibility.Visible;

        // ① 内容区
        Sec1Title.Text = isLink ? "链接" : "内容";
        Sec1Hint.Text = isLink ? "与文本一致，直接编辑" : "纯文本";
        if (isLink)
        {
            ContentBox.Text = clip.Url;
            ContentBox.MinHeight = 90;
        }
        else
        {
            ContentBox.Text = clip.Content;
            ContentBox.MinHeight = 110;
        }
        ContentBox.Tag = isLink ? "保存后按链接类型存储，点击卡片可复制此地址" : null;

        // ② 元数据
        TitleBox.Text = clip.Title;
        ExpireBox.ItemsSource = new[]
        {
            new ExpOpt("", "永久"),
            new ExpOpt("1h", "1 小时后"),
            new ExpOpt("1d", "1 天后"),
            new ExpOpt("7d", "7 天后"),
            new ExpOpt("30d", "30 天后"),
        };
        ExpireBox.DisplayMemberPath = nameof(ExpOpt.Label);
        ExpireBox.SelectedIndex = ExpireIndex(clip.ExpireAt);

        TagPick.SetTags(clip.Tags ?? new List<string>(), getTags());

        ArchiveBtn.Visibility = clip.Archived ? Visibility.Collapsed : Visibility.Visible;

        // 焦点：别名框（对齐 U-4）；别名框 Enter 快捷保存（仅链接——文本 Tab 内容区可换行不拦截，对齐 title.onkeydown）
        Loaded += (_, _) =>
        {
            TitleBox.Focus();
            TitleBox.SelectAll();
        };
        TitleBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && _clip.Type == "link") Save();
        };
    }

    /// <summary>过期回显（对齐 expSel.value：按剩余时长 <2h→1h / <48h→1d / <7d→7d / 否则 30d）。</summary>
    private static int ExpireIndex(long? expireAt)
    {
        if (!expireAt.HasValue || expireAt == 0) return 0;
        var left = expireAt.Value - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (left < 7200000) return 1;        // 2h
        if (left < 172800000) return 2;      // 48h
        if (left < 604800000) return 3;      // 7d
        return 4;
    }

    private void Save_Click(object sender, RoutedEventArgs e) => Save();

    private void Cancel_Click(object sender, RoutedEventArgs e) => ModalHost.Close();

    private void Archive_Click(object sender, RoutedEventArgs e)
    {
        ModalHost.Confirm("将该条目移入归档？归档后可「含归档」查看，可随时恢复。", () =>
        {
            try
            {
                _svc.Archive(_clip.Id);
                ModalHost.Close();
                ToastService.Flash("已归档");
                Archived?.Invoke();
            }
            catch (Exception ex) { ToastService.Error(ex.Message); }
        }, "归档");
    }

    /// <summary>保存（对齐 openEditModal ok.onclick：title/tags/expire + content|url；Update 规则净化）。</summary>
    private void Save()
    {
        var title = TitleBox.Text ?? "";
        var tags = TagPick.Selected.ToList();
        var expire = (ExpireBox.SelectedItem as ExpOpt)?.Value ?? "";
        try
        {
            if (_clip.Type == "link")
                _svc.Update(_clip.Id, title, tags, expire, url: ContentBox.Text);
            else
                _svc.Update(_clip.Id, title, tags, expire, content: ContentBox.Text);
            ModalHost.Close();
            ToastService.Flash("已保存");
            Saved?.Invoke();
        }
        catch (Exception ex)
        {
            ToastService.Error(ex.Message);
        }
    }

    /// <summary>过期选项（对齐 resolveExpire 的 '1h'|'1d'|'7d'|'30d'|''）。</summary>
    private sealed class ExpOpt(string value, string label)
    {
        public string Value { get; } = value;
        public string Label { get; } = label;
    }
}
