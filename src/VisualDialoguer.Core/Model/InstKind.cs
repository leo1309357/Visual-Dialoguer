/*
 * Copyright (c) 2026 Leo Jeang. All Rights Reserved.
 *
 * This is proprietary software. Unauthorized copying, modification,
 * or distribution is strictly prohibited.
 */

namespace VisualDialoguer.Core.Model;

/// <summary>
/// 指令类型。顺序与 cdlg::InstructionKind 一致：序列化按类型分派，
/// 界面上的类型下拉框也按这个顺序排。
/// </summary>
public enum InstKind
{
    /// <summary>说话人：<c>speaker &lt;name&gt;</c>。</summary>
    Speaker = 0,

    /// <summary>台词：<c>line &lt;text&gt;</c>。</summary>
    Line,

    /// <summary>选项：<c>choice &lt;label&gt; -&gt; &lt;node&gt;</c>。</summary>
    Choice,

    /// <summary>跳转：<c>goto &lt;node&gt;</c>。</summary>
    Goto,

    /// <summary>置标志：<c>set &lt;flag&gt; &lt;value&gt;</c>。</summary>
    Set,

    /// <summary>清标志：<c>clear &lt;flag&gt;</c>。</summary>
    Clear,

    /// <summary>发信号：<c>emit &lt;signal&gt; [payload]</c>。</summary>
    Emit,

    /// <summary>结束节点块：<c>end</c>。</summary>
    End,
}

/// <summary>指令类型的显示名与语法摘要，供界面使用。</summary>
public static class InstKindInfo
{
    /// <summary>语法里的关键字（也是序列化写出的第一个词）。</summary>
    public static string Keyword(InstKind kind) => kind switch
    {
        InstKind.Speaker => "speaker",
        InstKind.Line => "line",
        InstKind.Choice => "choice",
        InstKind.Goto => "goto",
        InstKind.Set => "set",
        InstKind.Clear => "clear",
        InstKind.Emit => "emit",
        InstKind.End => "end",
        _ => "?",
    };

    /// <summary>界面上显示的类型名。</summary>
    public static string DisplayName(InstKind kind) => kind switch
    {
        InstKind.Speaker => "说话人 speaker",
        InstKind.Line => "台词 line",
        InstKind.Choice => "选项 choice",
        InstKind.Goto => "跳转 goto",
        InstKind.Set => "置标志 set",
        InstKind.Clear => "清标志 clear",
        InstKind.Emit => "发信号 emit",
        InstKind.End => "结束 end",
        _ => "?",
    };

    /// <summary>
    /// <see cref="DisplayName"/> 的逆操作：把界面上的显示名还原成
    /// <see cref="InstKind"/>。下拉框选中的就是显示名，回写文档前要转回来。
    /// 对不上（含 null / 空串）时返回 <see cref="InstKind.Speaker"/>。
    /// </summary>
    public static InstKind FromDisplayName(string? displayName)
    {
        if (!string.IsNullOrEmpty(displayName))
        {
            foreach (InstKind kind in Enum.GetValues<InstKind>())
            {
                if (DisplayName(kind) == displayName)
                {
                    return kind;
                }
            }
        }
        return InstKind.Speaker;
    }

    /// <summary>
    /// 指令类型的单字徽章，用于列表与流程图上的语义色圆标。
    /// 与 <see cref="DisplayName"/> 一样属于模型元数据，不含任何界面依赖。
    /// </summary>
    public static string Badge(InstKind kind) => kind switch
    {
        InstKind.Speaker => "说",
        InstKind.Line => "词",
        InstKind.Choice => "选",
        InstKind.Goto => "跳",
        InstKind.Set => "置",
        InstKind.Clear => "清",
        InstKind.Emit => "信",
        InstKind.End => "止",
        _ => "?",
    };

    /// <summary>属性面板里的字段名，顺序即界面里的上下顺序。</summary>
    public static IReadOnlyList<(string Label, string Field)> Fields(InstKind kind) => kind switch
    {
        InstKind.Speaker => new[] { ("说话人", nameof(Instruction.Speaker)) },
        InstKind.Line => new[] { ("文本", nameof(Instruction.Text)) },
        InstKind.Choice => new[] { ("选项文本", nameof(Instruction.Text)), ("目标节点", nameof(Instruction.TargetNode)) },
        InstKind.Goto => new[] { ("目标节点", nameof(Instruction.TargetNode)) },
        InstKind.Set => new[] { ("标志键", nameof(Instruction.FlagKey)), ("标志值", nameof(Instruction.FlagValue)) },
        InstKind.Clear => new[] { ("标志键", nameof(Instruction.FlagKey)) },
        InstKind.Emit => new[] { ("信号 ID", nameof(Instruction.SignalId)), ("信号载荷", nameof(Instruction.SignalPayload)) },
        InstKind.End => Array.Empty<(string, string)>(),
        _ => Array.Empty<(string, string)>(),
    };
}
