/*
 * Copyright (c) 2026 Leo Jeang. All Rights Reserved.
 *
 * This is proprietary software. Unauthorized copying, modification,
 * or distribution is strictly prohibited.
 */

using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace VisualDialoguer.App.Views;

/// <summary>
/// 去掉下拉框聚焦时那圈画在框外的黑边。
///
/// WinUI 3 的 ComboBox 模板里有个叫 <c>HighlightBackground</c> 的 Border：
/// 它的 Margin 是 -4（在控件外侧 4px），聚焦时框架把它的边框刷成
/// <c>#E5000000</c>，于是控件外面浮出一圈 3px 宽的黑边。
///
/// 这个颜色是写死在框架模板里的，不是主题资源，改不掉——所以只能
/// 找到那个元素，把它的边框粗细抹成 0。框架的状态机只改它的颜色、
/// 不改粗细，抹一次就够，悬停/聚焦来回切也不会长回来。
///
/// 抹掉之后，下拉框和输入框是同一套语言了：聚焦只让底色从凹陷转成
/// 凸起（ComboBoxBackgroundFocused），不套任何外扩的环。
/// </summary>
public static class ComboBoxFocusRing
{
    /// <summary>把整棵可视树里的 ComboBox 都处理一遍。</summary>
    /// <param name="root">树根，通常是窗口的 Content。</param>
    public static void Suppress(DependencyObject root)
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, i);
            if (child is ComboBox combo)
            {
                combo.Loaded += OnComboLoaded;
            }
            Suppress(child);
        }
    }

    private static void OnComboLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not ComboBox combo) return;
        combo.Loaded -= OnComboLoaded;

        // 模板要等布局铺好后才有那个元素；先直接找一次，
        // 找不到再排到队列末尾补一刀。
        combo.ApplyTemplate();
        if (!Strip(combo))
        {
            combo.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () => Strip(combo));
        }
    }

    /// <summary>抹掉那圈外扩的边框；返回是否找到了目标元素。</summary>
    private static bool Strip(DependencyObject node)
    {
        if (node is FrameworkElement { Name: "HighlightBackground" } element
            && element is Border border)
        {
            border.BorderThickness = new Thickness(0);
            return true;
        }

        int count = VisualTreeHelper.GetChildrenCount(node);
        for (int i = 0; i < count; i++)
        {
            if (Strip(VisualTreeHelper.GetChild(node, i))) return true;
        }
        return false;
    }
}
