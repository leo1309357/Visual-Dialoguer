/*
 * Copyright (c) 2026 Leo Jeang. All Rights Reserved.
 *
 * This is proprietary software. Unauthorized copying, modification,
 * or distribution is strictly prohibited.
 */

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using VisualDialoguer.Core.Model;

namespace VisualDialoguer.App.Views;

/// <summary>
/// 「稿台 Paperstage」主题的代码侧读取入口。
///
/// XAML 里可以直接写 <c>{ThemeResource PsInk}</c>，但流程图是在代码里
/// 一笔笔画出来的，画刷只能查出来。查询走 <see cref="Application"/>,
/// 它会按当前日/夜主题解析 <c>ThemeDictionaries</c>，所以同一份绘图代码
/// 在浅色下是暖纸、深色下是夜读案头。
///
/// 所有查询都带兜底：万一某个键缺失，返回一个可见的替代值而不是崩溃——
/// 界面少个颜色是瑕疵，抛异常是事故。
/// </summary>
public static class Theme
{
    /// <summary>
    /// 日/夜主题发生切换时触发。流程图是在代码里一笔笔画出来的，
    /// 画刷在重画那一刻才取；主题换了必须重画，否则卡片还是旧配色。
    /// XAML 部分用 ThemeResource 会自动跟着变，不需要这个事件。
    /// </summary>
    public static event Action? Changed;

    /// <summary>通知所有订阅者主题变了（由 App 在切完主题后调用）。</summary>
    public static void RaiseChanged() => Changed?.Invoke();

    /// <summary>按键取画刷；取不到时返回 <paramref name="fallback"/>。</summary>
    public static Brush Brush(string key, Brush fallback)
    {
        if (Application.Current?.Resources.TryGetValue(key, out object? value) == true
            && value is Brush brush)
        {
            return brush;
        }
        return fallback;
    }

    /// <summary>按键取颜色。</summary>
    public static Windows.UI.Color Color(string key, Windows.UI.Color fallback)
    {
        if (Application.Current?.Resources.TryGetValue(key, out object? value) == true
            && value is Windows.UI.Color color)
        {
            return color;
        }
        return fallback;
    }

    /// <summary>
    /// 从一个主题画刷里取色值。
    ///
    /// 主题字典里的 <c>Ps*Color</c>（Color 类型）在运行时查不到——只有画刷
    /// 能被 <see cref="Application.Resources"/> 解析出来；而 AppWindow 的
    /// 标题栏要的是 Color。所以这里先取画刷，再读它的 Color。
    /// </summary>
    public static Windows.UI.Color Swatch(string brushKey, Windows.UI.Color fallback) =>
        Brush(brushKey, new SolidColorBrush(fallback)) is SolidColorBrush solid ? solid.Color : fallback;

    /// <summary>
    /// 按键取数值令牌（字号、间距、尺寸都是 <c>x:Double</c>）。
    /// 代码里画的流程图节点也要吃同一套字号阶梯，不然同一屏里会出现两套字级。
    /// </summary>
    public static double Number(string key, double fallback)
    {
        if (Application.Current?.Resources.TryGetValue(key, out object? value) == true
            && value is double number)
        {
            return number;
        }
        return fallback;
    }

    /// <summary>
    /// 指令类型对应的语义色画刷。列表徽章、流程图节点书脊都用它，
    /// 保证同一个类型在不同视图里是同一种颜色。
    /// </summary>
    public static Brush KindBrush(InstKind kind) => Brush(
        KindBrushKey(kind),
        new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x80, 0x80, 0x80)));

    /// <summary>指令类型语义色的资源键。</summary>
    public static string KindBrushKey(InstKind kind) => kind switch
    {
        InstKind.Speaker => "PsKindSpeaker",
        InstKind.Line => "PsKindLine",
        InstKind.Choice => "PsKindChoice",
        InstKind.Goto => "PsKindGoto",
        InstKind.Set => "PsKindSet",
        InstKind.Clear => "PsKindClear",
        InstKind.Emit => "PsKindEmit",
        InstKind.End => "PsKindEnd",
        _ => "PsKindEnd",
    };
}
