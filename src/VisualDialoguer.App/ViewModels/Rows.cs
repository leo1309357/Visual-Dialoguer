/*
 * Copyright (c) 2026 Leo Jeang. All Rights Reserved.
 *
 * This is proprietary software. Unauthorized copying, modification,
 * or distribution is strictly prohibited.
 */

using Microsoft.UI.Xaml.Media;
using VisualDialoguer.App.Views;
using VisualDialoguer.Core.Model;

namespace VisualDialoguer.App.ViewModels;

/// <summary>
/// 节点列表的一行。对应「Loma」主题的 <c>LomaNodeRow</c> 模板：
/// 一道书脊 + 节点名 + 指令条数。
/// </summary>
public sealed class NodeRow
{
    /// <summary>节点 id。</summary>
    public required string Id { get; init; }

    /// <summary>是不是入口节点。</summary>
    public required bool IsEntry { get; init; }

    /// <summary>节点里的指令条数。</summary>
    public required int InstructionCount { get; init; }

    /// <summary>右侧的条数标注。</summary>
    public string CountText => $"{InstructionCount} 条";

    /// <summary>
    /// 书脊颜色：入口节点用签名色（一眼认出从哪儿开始跑），
    /// 普通节点用发丝边色，退到底层不抢眼。
    /// </summary>
    public Brush SpineBrush => IsEntry
        ? Theme.Brush("LomaAccent", new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x70, 0x80, 0x90)))
        : Theme.Brush("LomaEdgeStrong", new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x80, 0x80, 0x80)));
}

/// <summary>
/// 指令列表的一行。对应「Loma」主题的 <c>LomaInstructionRow</c> 模板：
/// 语义色单字徽章 + 摘要 + 类型名。
/// </summary>
public sealed class InstructionRow
{
    /// <summary>指令本体（选中时拿它做编辑）。</summary>
    public required Instruction Instruction { get; init; }

    /// <summary>类型标签（如「台词 line」），显示为摘要下方的次要信息。</summary>
    public required string KindLabel { get; init; }

    /// <summary>
    /// 该指令在节点指令列表中的真实下标。筛选后列表下标和真实下标不再一致，
    /// 选中回写文档时必须用这个值，不能用 ListView 的 SelectedIndex。
    /// </summary>
    public required int OriginalIndex { get; init; }

    /// <summary>徽章上的单字（说/词/选/跳/置/清/信/止）。</summary>
    public string KindGlyph => InstKindInfo.Badge(Instruction.Kind);

    /// <summary>徽章底色：与流程图节点书脊同源，同类型同色。</summary>
    public Brush KindBrush => Theme.KindBrush(Instruction.Kind);

    /// <summary>
    /// 行内摘要。没有内容的指令（如 end、刚建的空指令）退而显示类型名，
    /// 免得留一片空白看不出这行是什么。
    /// </summary>
    public string SummaryText
    {
        get
        {
            string summary = Instruction.Summary();
            return string.IsNullOrEmpty(summary) ? KindLabel : summary;
        }
    }
}
