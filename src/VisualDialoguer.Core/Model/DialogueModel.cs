/*
 * Copyright (c) 2026 Leo Jeang. All Rights Reserved.
 *
 * This is proprietary software. Unauthorized copying, modification,
 * or distribution is strictly prohibited.
 */

namespace VisualDialoguer.Core.Model;

/// <summary>
/// 内存模型的静态操作：查找、增删改移、深拷贝、结构校验。
///
/// 校验规则与 cdlg 解析器（libs/cdlg/src/Parser.cpp）保持一致：
/// 入口节点必须存在、节点 id 不重复、节点不能是空壳、goto / choice 的
/// 目标节点必须存在；另外在编辑器侧额外提示各指令的必填字段为空。
/// </summary>
public static class DialogueModel
{
    /// <summary>按 id 找节点。找不到返回 <c>null</c>。</summary>
    public static DialogueNode? FindNode(DialogueScript script, string? id)
    {
        if (script is null || string.IsNullOrEmpty(id))
        {
            return null;
        }
        foreach (DialogueNode node in script.Nodes)
        {
            if (string.Equals(node.Id, id, StringComparison.Ordinal))
            {
                return node;
            }
        }
        return null;
    }

    /// <summary>节点在脚本里的下标。找不到返回 -1。</summary>
    public static int IndexOfNode(DialogueScript script, string? id)
    {
        if (script is null || string.IsNullOrEmpty(id))
        {
            return -1;
        }
        for (int index = 0; index < script.Nodes.Count; ++index)
        {
            if (string.Equals(script.Nodes[index].Id, id, StringComparison.Ordinal))
            {
                return index;
            }
        }
        return -1;
    }

    /// <summary>新增节点并追加到末尾，返回新节点。</summary>
    /// <exception cref="ArgumentException">id 为空或空白。</exception>
    public static DialogueNode AddNode(DialogueScript script, string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException("节点 id 不能为空。", nameof(id));
        }
        var node = new DialogueNode { Id = id };
        script.Nodes.Add(node);
        return node;
    }

    /// <summary>
    /// 在脚本末尾生成一个不重复的节点 id（<paramref name="prefix"/> + 数字后缀）。
    /// </summary>
    public static string NextNodeId(DialogueScript script, string prefix = "node")
    {
        for (int index = 1; ; ++index)
        {
            string candidate = prefix + index.ToString();
            if (FindNode(script, candidate) is null)
            {
                return candidate;
            }
        }
    }

    /// <summary>新增指令并追加到节点末尾，返回新指令。</summary>
    public static Instruction AddInstruction(DialogueNode node, InstKind kind)
    {
        var instruction = new Instruction { Kind = kind };
        node.Instructions.Add(instruction);
        return instruction;
    }

    /// <summary>删除节点。</summary>
    public static void RemoveNode(DialogueScript script, DialogueNode node) => script.Nodes.Remove(node);

    /// <summary>删除指令。</summary>
    public static void RemoveInstruction(DialogueNode node, Instruction instruction) =>
        node.Instructions.Remove(instruction);

    /// <summary>
    /// 在节点内上移（-1）或下移（+1）一条指令。越界不动。
    /// </summary>
    public static bool MoveInstruction(DialogueNode node, Instruction instruction, int direction)
    {
        int index = node.Instructions.IndexOf(instruction);
        if (index < 0)
        {
            return false;
        }
        int target = index + (direction < 0 ? -1 : 1);
        if (target < 0 || target >= node.Instructions.Count)
        {
            return false;
        }
        node.Instructions.RemoveAt(index);
        node.Instructions.Insert(target, instruction);
        return true;
    }

    /// <summary>
    /// 切换指令类型时清掉无关字段：换类型后原来那个类型的值不该留在
    /// 指令里（否则保存时会写出一半旧一半新的字段）。
    /// </summary>
    public static void ClearIrrelevantFields(Instruction instruction, InstKind newKind)
    {
        instruction.Kind = newKind;
        if (newKind != InstKind.Speaker)
        {
            instruction.Speaker = string.Empty;
        }
        if (newKind != InstKind.Line && newKind != InstKind.Choice)
        {
            instruction.Text = string.Empty;
        }
        if (newKind != InstKind.Choice && newKind != InstKind.Goto)
        {
            instruction.TargetNode = string.Empty;
        }
        if (newKind != InstKind.Set && newKind != InstKind.Clear)
        {
            instruction.FlagKey = string.Empty;
        }
        if (newKind != InstKind.Set)
        {
            instruction.FlagValue = string.Empty;
        }
        if (newKind != InstKind.Emit)
        {
            instruction.SignalId = string.Empty;
            instruction.SignalPayload = string.Empty;
        }
    }

    /// <summary>
    /// 结构校验。返回空列表表示脚本可用。
    /// </summary>
    public static IReadOnlyList<string> Validate(DialogueScript? script)
    {
        var errors = new List<string>();
        if (script is null)
        {
            errors.Add("缺少脚本");
            return errors;
        }

        if (string.IsNullOrEmpty(script.EntryNodeId))
        {
            errors.Add("缺少入口节点（entry）定义");
        }
        else if (FindNode(script, script.EntryNodeId) is null)
        {
            errors.Add($"入口节点 '{script.EntryNodeId}' 不存在");
        }

        // 标识符合法性：cdlg 是按词切分的行式语法，id 里带空白或 # 会让
        // 保存出来的脚本读不回来。
        CheckIdentifier(errors, "脚本", script.Id);
        CheckIdentifier(errors, "入口节点", script.EntryNodeId);

        // 节点 id 重复：两两比对，重复的每一对都报一次。
        for (int i = 0; i < script.Nodes.Count; ++i)
        {
            CheckIdentifier(errors, $"节点 #{i + 1} 的 id", script.Nodes[i].Id);
            for (int j = i + 1; j < script.Nodes.Count; ++j)
            {
                if (string.Equals(script.Nodes[i].Id, script.Nodes[j].Id, StringComparison.Ordinal))
                {
                    errors.Add($"节点 id 重复：'{script.Nodes[i].Id}'");
                }
            }
        }

        foreach (DialogueNode node in script.Nodes)
        {
            if (node.Instructions.Count == 0)
            {
                errors.Add($"节点 '{node.Id}' 没有任何指令");
                continue;
            }
            foreach (Instruction instruction in node.Instructions)
            {
                if (instruction.Kind is InstKind.Goto or InstKind.Choice)
                {
                    if (string.IsNullOrEmpty(instruction.TargetNode))
                    {
                        errors.Add($"节点 '{node.Id}' 的指令缺少目标节点");
                    }
                    else if (FindNode(script, instruction.TargetNode) is null)
                    {
                        errors.Add($"节点 '{node.Id}' 引用了不存在的节点 '{instruction.TargetNode}'");
                    }
                    CheckIdentifier(errors, $"节点 '{node.Id}' 的目标节点", instruction.TargetNode);
                }
                if (instruction.Kind == InstKind.Speaker && string.IsNullOrEmpty(instruction.Speaker))
                {
                    errors.Add($"节点 '{node.Id}' 的 speaker 指令缺少说话人");
                }
                if (instruction.Kind == InstKind.Line && string.IsNullOrEmpty(instruction.Text))
                {
                    errors.Add($"节点 '{node.Id}' 的 line 指令缺少台词文本");
                }
                if (instruction.Kind == InstKind.Choice && string.IsNullOrEmpty(instruction.Text))
                {
                    errors.Add($"节点 '{node.Id}' 的 choice 选项标签不能为空");
                }
                if (instruction.Kind == InstKind.Choice)
                {
                    // 选项标签里的 -> 和 | 会被解析器当成分隔符，标签含它们
                    // 就切不出原来的目标节点了。
                    int separator = instruction.Text.IndexOf("->", StringComparison.Ordinal);
                    if (separator < 0)
                    {
                        separator = instruction.Text.IndexOf('|');
                    }
                    if (separator >= 0)
                    {
                        errors.Add($"节点 '{node.Id}' 的 choice 标签不能包含分隔符 '->' 或 '|'");
                    }
                }
                if (instruction.Kind == InstKind.Set
                    && (string.IsNullOrEmpty(instruction.FlagKey) || string.IsNullOrEmpty(instruction.FlagValue)))
                {
                    errors.Add($"节点 '{node.Id}' 的 set 指令需要 <标志> <值>");
                }
                if (instruction.Kind is InstKind.Set or InstKind.Clear)
                {
                    CheckIdentifier(errors, $"节点 '{node.Id}' 的标志名", instruction.FlagKey);
                }
                if (instruction.Kind == InstKind.Clear && string.IsNullOrEmpty(instruction.FlagKey))
                {
                    errors.Add($"节点 '{node.Id}' 的 clear 指令缺少标志名");
                }
                if (instruction.Kind == InstKind.Emit && string.IsNullOrEmpty(instruction.SignalId))
                {
                    errors.Add($"节点 '{node.Id}' 的 emit 指令缺少信号 id");
                }
                if (instruction.Kind == InstKind.Emit)
                {
                    CheckIdentifier(errors, $"节点 '{node.Id}' 的信号 id", instruction.SignalId);
                }
            }
        }

        return errors;
    }

    /// <summary>
    /// 检查一个按词切分的标识符字段：不能为空白、不能含空白字符、
    /// 不能以 <c>#</c> 开头（会被解析器当注释吃掉）。
    /// 空值不报——各字段是否为必填由上面的专项检查看管。
    /// </summary>
    private static void CheckIdentifier(List<string> errors, string what, string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return;
        }
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add($"{what}只含空白字符");
            return;
        }
        if (value[0] == '#')
        {
            errors.Add($"{what} '{value}' 不能以 '#' 开头（会被当作注释）");
        }
        foreach (char c in value)
        {
            if (char.IsWhiteSpace(c))
            {
                errors.Add($"{what} '{value}' 不能包含空白字符");
                return;
            }
        }
    }
}
