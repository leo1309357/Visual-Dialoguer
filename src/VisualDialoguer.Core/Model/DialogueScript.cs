/*
 * Copyright (c) 2026 Leo Jeang. All Rights Reserved.
 *
 * This is proprietary software. Unauthorized copying, modification,
 * or distribution is strictly prohibited.
 */

namespace VisualDialoguer.Core.Model;

/// <summary>
/// 一个对话脚本。cdlg 文件里一个 <c>dialogue</c> 块就是一个脚本；一个文件
/// 可以含多个块，编辑器只编辑首个（其余块原样保留、保存时写回）。
/// </summary>
public sealed class DialogueScript
{
    /// <summary>脚本 id（<c>dialogue &lt;id&gt;</c>）。</summary>
    public string Id { get; set; } = "untitled";

    /// <summary>入口节点 id（<c>entry &lt;node&gt;</c>）。</summary>
    public string EntryNodeId { get; set; } = string.Empty;

    /// <summary>节点表，顺序即文件里的书写顺序。</summary>
    public List<DialogueNode> Nodes { get; } = new();

    /// <summary>深拷贝。</summary>
    public DialogueScript Clone()
    {
        var copy = new DialogueScript { Id = Id, EntryNodeId = EntryNodeId };
        foreach (DialogueNode node in Nodes)
        {
            copy.Nodes.Add(node.Clone());
        }
        return copy;
    }
}
