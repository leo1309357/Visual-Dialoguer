/*
 * Copyright (c) 2026 Leo Jeang. All Rights Reserved.
 *
 * This is proprietary software. Unauthorized copying, modification,
 * or distribution is strictly prohibited.
 */

namespace VisualDialoguer.Core.Model;

/// <summary>
/// 一条指令。字段按类型使用：换类型时无关字段会被清空
/// （见 <see cref="DialogueModel.ClearIrrelevantFields"/>），这样序列化
/// 出来的脚本不会带着上一条指令的残渣。
/// </summary>
public sealed class Instruction
{
    /// <summary>指令类型。</summary>
    public InstKind Kind { get; set; }

    /// <summary>Speaker 指令的说话人名字。</summary>
    public string Speaker { get; set; } = string.Empty;

    /// <summary>Line 指令的台词；Choice 指令的选项标签。</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>Choice / Goto 指令的目标节点 id。</summary>
    public string TargetNode { get; set; } = string.Empty;

    /// <summary>Set / Clear 指令的标志键。</summary>
    public string FlagKey { get; set; } = string.Empty;

    /// <summary>Set 指令的标志值。</summary>
    public string FlagValue { get; set; } = string.Empty;

    /// <summary>Emit 指令的信号 id。</summary>
    public string SignalId { get; set; } = string.Empty;

    /// <summary>Emit 指令的可选载荷。</summary>
    public string SignalPayload { get; set; } = string.Empty;

    /// <summary>深拷贝。撤销/重做与节点复制都靠它。</summary>
    public Instruction Clone() => new()
    {
        Kind = Kind,
        Speaker = Speaker,
        Text = Text,
        TargetNode = TargetNode,
        FlagKey = FlagKey,
        FlagValue = FlagValue,
        SignalPayload = SignalPayload,
        SignalId = SignalId,
    };

    /// <summary>一句话摘要，列表里用它当副标题。</summary>
    public string Summary()
    {
        switch (Kind)
        {
            case InstKind.Speaker:
                return Speaker;
            case InstKind.Line:
                return Text;
            case InstKind.Choice:
                return $"{Text} -> {TargetNode}";
            case InstKind.Goto:
                return TargetNode;
            case InstKind.Set:
                return $"{FlagKey} {FlagValue}";
            case InstKind.Clear:
                return FlagKey;
            case InstKind.Emit:
                return string.IsNullOrEmpty(SignalPayload) ? SignalId : $"{SignalId} {SignalPayload}";
            case InstKind.End:
                return string.Empty;
            default:
                return string.Empty;
        }
    }
}
