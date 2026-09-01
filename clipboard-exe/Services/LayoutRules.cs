// Services/LayoutRules.cs - 布局规则纯函数（M1 抽离：把"窗口宽度 → 布局参数"模式立起来）
// 对齐 Web 版 .view：max-width 三档（<1280→960 / ≥1280→1440 / ≥1920→1920）。
// 纯函数无 UI 依赖，可单测；卡片墙列数（Web .list auto-fill minmax(280px,1fr)）同族规则，M3a 起在此加 ColumnsFor。
namespace ClipboardExe.Services;

public static class LayoutRules
{
    /// <summary>按客户区宽度取内容区最大宽度（三档，对齐 Web .view max-width）。</summary>
    public static double MaxWidthFor(double clientWidth) =>
        clientWidth >= 1920 ? 1920 : clientWidth >= 1280 ? 1440 : 960;

    /// <summary>
    /// 自适应列数（对齐 Web .list auto-fill minmax(280px,1fr) + data-cols 1~4 覆盖）：
    /// 基准卡片最小宽 280 + gap 16 → 每行能放几列；钳制 1~4（Web 列数选择器上限）。
    /// 取整用 (w+gap)/(280+gap) 对齐 CSS auto-fill 的取整语义（278px 宽仍 1 列，296px 才 2 列）。
    /// <para>maxColumns：用户列数偏好，0 或 负数 = 用 4 作上限（自动模式）；1~4 = 锁定上限（M3b-1 接入）。</para>
    /// </summary>
    public static int ColumnsFor(double contentWidth, int maxColumns = 0)
    {
        const double min = 280.0, gap = 16.0;
        var n = (int)Math.Floor((contentWidth + gap) / (min + gap));
        var upper = maxColumns > 0 ? Math.Min(maxColumns, 4) : 4;
        return Math.Clamp(n, 1, upper);
    }
}
