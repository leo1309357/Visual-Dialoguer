/*
 * Copyright (c) 2026 Leo Jeang. All Rights Reserved.
 *
 * This is proprietary software. Unauthorized copying, modification,
 * or distribution is strictly prohibited.
 */

namespace VisualDialoguer.Core.Model;

/// <summary>一个对话节点：一串按顺序执行的指令。</summary>
public sealed class DialogueNode
{
    /// <summary>节点 id，全脚本内唯一（重复会被校验拦下）。</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>节点内的指令，按执行顺序排列。</summary>
    public List<Instruction> Instructions { get; } = new();

    /// <summary>深拷贝。</summary>
    public DialogueNode Clone()
    {
        var copy = new DialogueNode { Id = Id };
        foreach (Instruction instruction in Instructions)
        {
            copy.Instructions.Add(instruction.Clone());
        }
        return copy;
    }
}
