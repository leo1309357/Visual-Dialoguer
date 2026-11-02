/*
 * Copyright (c) 2026 Leo Jeang. All Rights Reserved.
 *
 * This is proprietary software. Unauthorized copying, modification,
 * or distribution is strictly prohibited.
 */

using System.Text;
using VisualDialoguer.Core.Model;

namespace VisualDialoguer.Core.Format;

/// <summary>
/// cdlg 文本格式的序列化与解析。
///
/// 写出的缩进与转义必须与 Eraphanis 工程里的 <c>libs/cdlg</c> 解析器
/// 完全对得上：
///
/// <list type="bullet">
/// <item><c>dialogue</c> 与顶层 <c>end</c> 缩进 0；<c>entry</c>、<c>node</c>
/// 与节点块 <c>end</c> 缩进 2；指令缩进 4。</item>
/// <item>选项分隔符 <c>|</c> 序列化时归一化为 <c>-&gt;</c>。</item>
/// <item>转义 <c>\n \t \r \\ \"</c> 双向往返；未知的 <c>\x</c> 保留反斜杠。</item>
/// </list>
/// </summary>
public static class CdlgFormat
{
    private const string Speaker = "speaker";
    private const string Line = "line";
    private const string Choice = "choice";
    private const string Goto = "goto";
    private const string Set = "set";
    private const string Clear = "clear";
    private const string Emit = "emit";
    private const string End = "end";

    /// <summary>转义指令载荷里的 <c>\n \t \r \\ "</c>。</summary>
    public static string Escape(string? source)
    {
        if (source is null)
        {
            return string.Empty;
        }
        var builder = new StringBuilder(source.Length + 8);
        foreach (char c in source)
        {
            switch (c)
            {
                case '\\':
                    builder.Append("\\\\");
                    break;
                case '"':
                    builder.Append("\\\"");
                    break;
                case '\n':
                    builder.Append("\\n");
                    break;
                case '\t':
                    builder.Append("\\t");
                    break;
                case '\r':
                    builder.Append("\\r");
                    break;
                default:
                    builder.Append(c);
                    break;
            }
        }
        return builder.ToString();
    }

    /// <summary>反转义。未知的 <c>\x</c> 保留反斜杠原样。</summary>
    public static string Unescape(string? source)
    {
        if (source is null)
        {
            return string.Empty;
        }
        var builder = new StringBuilder(source.Length);
        for (int i = 0; i < source.Length; ++i)
        {
            if (source[i] == '\\' && i + 1 < source.Length)
            {
                ++i;
                switch (source[i])
                {
                    case 'n':
                        builder.Append('\n');
                        break;
                    case 't':
                        builder.Append('\t');
                        break;
                    case 'r':
                        builder.Append('\r');
                        break;
                    case '\\':
                        builder.Append('\\');
                        break;
                    case '"':
                        builder.Append('"');
                        break;
                    default:
                        builder.Append('\\');
                        builder.Append(source[i]);
                        break;
                }
            }
            else
            {
                builder.Append(source[i]);
            }
        }
        return builder.ToString();
    }

    /// <summary>把脚本表序列化成 cdlg 文本（LF 换行，无 BOM）。</summary>
    public static string Serialize(IEnumerable<DialogueScript> scripts)
    {
        var builder = new StringBuilder();
        foreach (DialogueScript script in scripts)
        {
            WriteScript(builder, script);
        }
        return builder.ToString();
    }

    private static void WriteScript(StringBuilder builder, DialogueScript script)
    {
        builder.Append("dialogue ").Append(string.IsNullOrEmpty(script.Id) ? "untitled" : script.Id).Append('\n');

        if (!string.IsNullOrEmpty(script.EntryNodeId))
        {
            builder.Append("  entry ").Append(script.EntryNodeId).Append('\n');
        }

        foreach (DialogueNode node in script.Nodes)
        {
            builder.Append("  node ").Append(node.Id).Append('\n');
            foreach (Instruction instruction in node.Instructions)
            {
                WriteInstruction(builder, instruction);
            }
        }

        // 节点块结束（缩进 2），脚本块结束（缩进 0）。
        builder.Append("  end\n");
        builder.Append("end\n");
    }

    private static void WriteInstruction(StringBuilder builder, Instruction instruction)
    {
        switch (instruction.Kind)
        {
            case InstKind.Speaker:
                builder.Append("    speaker ").Append(Escape(instruction.Speaker)).Append('\n');
                break;
            case InstKind.Line:
                builder.Append("    line ").Append(Escape(instruction.Text)).Append('\n');
                break;
            case InstKind.Choice:
                builder.Append("    choice ").Append(Escape(instruction.Text))
                    .Append(" -> ").Append(instruction.TargetNode).Append('\n');
                break;
            case InstKind.Goto:
                builder.Append("    goto ").Append(instruction.TargetNode).Append('\n');
                break;
            case InstKind.Set:
                builder.Append("    set ").Append(instruction.FlagKey).Append(' ')
                    .Append(Escape(instruction.FlagValue)).Append('\n');
                break;
            case InstKind.Clear:
                builder.Append("    clear ").Append(instruction.FlagKey).Append('\n');
                break;
            case InstKind.Emit:
                builder.Append("    emit ").Append(instruction.SignalId);
                if (!string.IsNullOrEmpty(instruction.SignalPayload))
                {
                    builder.Append(' ').Append(Escape(instruction.SignalPayload));
                }
                builder.Append('\n');
                break;
            case InstKind.End:
                builder.Append("    end\n");
                break;
        }
    }

    /// <summary>
    /// 解析 cdlg 文本。宽容但认得同一套语法：三级缩进、<c>#</c> 注释、
    /// 空行跳过、BOM 与 CR 忽略。
    /// </summary>
    public static ParseResult Parse(string? source)
    {
        var issues = new List<FormatIssue>();
        var scripts = new List<DialogueScript>();
        if (source is null)
        {
            return new ParseResult(scripts, issues);
        }

        string text = source;
        if (text.Length >= 1 && text[0] == '\uFEFF')
        {
            // U+FEFF 是一个字符，不是三个字节：只跳这一个。跳三个会把脚本的
            // 头两个字吃掉。
            text = text[1..];
        }

        // 逐行切开：记下行号与缩进层级，跳过空行与注释。
        var lines = new List<(int Number, int Level, string Text)>();
        string[] rawLines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        for (int number = 1; number <= rawLines.Length; ++number)
        {
            string raw = rawLines[number - 1];

            // 前导空白分开算两件事：whitespaceCount 是字符数，用来切掉
            // 缩进拿到正文；indentWidth 是加权宽度（空格算 1、Tab 算 2，
            // 因为 cdlg 一级缩进就是 2 空格，一个 Tab 顶一级），用来判断
            // 层级。两者混用会让 Tab 缩进的指令行算错层。
            int whitespaceCount = 0;
            int indentWidth = 0;
            while (whitespaceCount < raw.Length)
            {
                char c = raw[whitespaceCount];
                if (c == ' ')
                {
                    indentWidth += 1;
                }
                else if (c == '\t')
                {
                    indentWidth += 2;
                }
                else
                {
                    break;
                }
                ++whitespaceCount;
            }
            string trimmed = raw[whitespaceCount..].TrimEnd(' ', '\t');
            if (trimmed.Length == 0 || trimmed[0] == '#')
            {
                continue;
            }
            int level = indentWidth == 0 ? 0 : (indentWidth <= 2 ? 1 : 2);
            lines.Add((number, level, trimmed));
        }

        DialogueScript? current = null;
        DialogueNode? currentNode = null;

        foreach ((int number, int level, string line) in lines)
        {
            (string command, string args) = SplitCommand(line);

            if (level == 0)
            {
                if (command == "dialogue")
                {
                    if (args.Length == 0)
                    {
                        issues.Add(new FormatIssue(number, "dialogue 缺少脚本 id"));
                    }
                    else
                    {
                        current = new DialogueScript { Id = args };
                        scripts.Add(current);
                        currentNode = null;
                    }
                }
                else if (command == End)
                {
                    current = null;
                    currentNode = null;
                }
                else
                {
                    issues.Add(new FormatIssue(number, $"顶层命令应为 dialogue <id>，得到 '{command}'"));
                }
            }
            else if (level == 1)
            {
                if (current is null)
                {
                    issues.Add(new FormatIssue(number, $"命令 '{command}' 出现在 dialogue 块之外"));
                }
                else if (command == "entry")
                {
                    if (args.Length == 0)
                    {
                        issues.Add(new FormatIssue(number, "entry 缺少节点 id"));
                    }
                    else
                    {
                        current.EntryNodeId = args;
                    }
                }
                else if (command == "node")
                {
                    if (args.Length == 0)
                    {
                        issues.Add(new FormatIssue(number, "node 缺少节点 id"));
                    }
                    else
                    {
                        currentNode = DialogueModel.AddNode(current, args);
                    }
                }
                else if (command == End)
                {
                    currentNode = null;
                }
                else
                {
                    issues.Add(new FormatIssue(number, $"dialogue 块内命令应为 entry / node / end，得到 '{command}'"));
                }
            }
            else
            {
                if (currentNode is null)
                {
                    issues.Add(new FormatIssue(number, $"指令 '{command}' 出现在 node 块之外"));
                    continue;
                }

                switch (command)
                {
                    case Speaker:
                    {
                        string name = Unescape(args);
                        if (name.Length == 0)
                        {
                            issues.Add(new FormatIssue(number, "speaker 缺少说话人名称"));
                        }
                        else
                        {
                            Instruction instruction = DialogueModel.AddInstruction(currentNode, InstKind.Speaker);
                            instruction.Speaker = name;
                        }
                        break;
                    }
                    case Line:
                    {
                        string lineText = Unescape(args);
                        if (lineText.Length == 0)
                        {
                            issues.Add(new FormatIssue(number, "line 缺少台词文本"));
                        }
                        else
                        {
                            Instruction instruction = DialogueModel.AddInstruction(currentNode, InstKind.Line);
                            instruction.Text = lineText;
                        }
                        break;
                    }
                    case Choice:
                    {
                        if (!TrySplitChoice(args, out string label, out string target))
                        {
                            issues.Add(new FormatIssue(number, "choice 格式应为 '<标签> -> <节点>' 或 '<标签> | <节点>'"));
                            break;
                        }
                        Instruction instruction = DialogueModel.AddInstruction(currentNode, InstKind.Choice);
                        instruction.Text = Unescape(label);
                        instruction.TargetNode = target;
                        break;
                    }
                    case Goto:
                    {
                        if (args.Length == 0)
                        {
                            issues.Add(new FormatIssue(number, "goto 缺少目标节点"));
                        }
                        else
                        {
                            Instruction instruction = DialogueModel.AddInstruction(currentNode, InstKind.Goto);
                            instruction.TargetNode = args;
                        }
                        break;
                    }
                    case Set:
                    {
                        (string key, string remainder) = SplitFirstArg(args);
                        string value = Unescape(remainder);
                        if (key.Length == 0 || value.Length == 0)
                        {
                            issues.Add(new FormatIssue(number, "set 格式应为 '<标志> <值>'"));
                        }
                        else
                        {
                            Instruction instruction = DialogueModel.AddInstruction(currentNode, InstKind.Set);
                            instruction.FlagKey = key;
                            instruction.FlagValue = value;
                        }
                        break;
                    }
                    case Clear:
                    {
                        if (args.Length == 0)
                        {
                            issues.Add(new FormatIssue(number, "clear 缺少标志名"));
                        }
                        else
                        {
                            Instruction instruction = DialogueModel.AddInstruction(currentNode, InstKind.Clear);
                            instruction.FlagKey = args;
                        }
                        break;
                    }
                    case Emit:
                    {
                        (string id, string remainder) = SplitFirstArg(args);
                        string payload = Unescape(remainder);
                        if (id.Length == 0)
                        {
                            issues.Add(new FormatIssue(number, "emit 缺少信号 id"));
                        }
                        else
                        {
                            Instruction instruction = DialogueModel.AddInstruction(currentNode, InstKind.Emit);
                            instruction.SignalId = id;
                            if (payload.Length != 0)
                            {
                                instruction.SignalPayload = payload;
                            }
                        }
                        break;
                    }
                    case End:
                    {
                        DialogueModel.AddInstruction(currentNode, InstKind.End);
                        currentNode = null;
                        break;
                    }
                    default:
                        issues.Add(new FormatIssue(number, $"未知节点指令 '{command}'"));
                        break;
                }
            }
        }

        return new ParseResult(scripts, issues);
    }

    private static (string Command, string Args) SplitCommand(string text)
    {
        int index = 0;
        while (index < text.Length && text[index] != ' ' && text[index] != '\t')
        {
            ++index;
        }
        string command = text[..index];
        string args = index < text.Length ? text[(index + 1)..].Trim() : string.Empty;
        return (command, args);
    }

    private static (string First, string Remainder) SplitFirstArg(string text)
    {
        int index = 0;
        while (index < text.Length && text[index] != ' ' && text[index] != '\t')
        {
            ++index;
        }
        string first = text[..index];
        string remainder = index < text.Length ? text[(index + 1)..].Trim() : string.Empty;
        return (first, remainder);
    }

    private static bool TrySplitChoice(string text, out string label, out string target)
    {
        int separator = text.IndexOf("->", StringComparison.Ordinal);
        int separatorLength = 2;
        if (separator < 0)
        {
            separator = text.IndexOf('|');
            separatorLength = 1;
        }
        if (separator < 0)
        {
            label = string.Empty;
            target = string.Empty;
            return false;
        }
        label = text[..separator].Trim();
        target = text[(separator + separatorLength)..].Trim();
        return label.Length != 0 && target.Length != 0;
    }
}
