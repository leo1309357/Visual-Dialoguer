/*
 * Copyright (c) 2026 Leo Jeang. All Rights Reserved.
 *
 * This is proprietary software. Unauthorized copying, modification,
 * or distribution is strictly prohibited.
 */

using VisualDialoguer.Core.Format;
using VisualDialoguer.Core.Model;

namespace VisualDialoguer.Core.Tests;

/// <summary>
/// 格式层的回归测试：读写必须与 Eraphanis 工程 libs/cdlg 解析器认得的
/// 语法一致，并且往返不丢东西。
/// </summary>
public class CdlgFormatTests
{
    private const string Sample = """
        # 示例对话脚本：与 cdlg 解析器兼容
        dialogue intro
          entry start
          node start
            speaker 旁白
            line 你好，这是一段示例对话。
            choice 继续交谈 -> talk
            choice 结束对话 -> bye
          node talk
            speaker 旅行者
            line 世界，你好！
            set mood happy
            emit quest_done "chapter=1"
            goto start
          node bye
            speaker 旁白
            line 再见，祝你旅途愉快。
            end
        end
        """;

    [Fact]
    public void SampleParsesIntoOneScriptWithoutIssues()
    {
        ParseResult result = CdlgFormat.Parse(Sample);

        Assert.Empty(result.Issues);
        Assert.Single(result.Scripts);

        DialogueScript script = result.Scripts[0];
        Assert.Equal("intro", script.Id);
        Assert.Equal("start", script.EntryNodeId);
        Assert.Equal(3, script.Nodes.Count);
    }

    [Fact]
    public void SerializeWritesTheExpectedIndentation()
    {
        ParseResult result = CdlgFormat.Parse(Sample);
        string text = CdlgFormat.Serialize(result.Scripts);

        Assert.Contains("dialogue intro\n", text, StringComparison.Ordinal);
        Assert.Contains("  entry start\n", text, StringComparison.Ordinal);
        Assert.Contains("  node start\n", text, StringComparison.Ordinal);
        Assert.Contains("    speaker 旁白\n", text, StringComparison.Ordinal);
        Assert.Contains("    choice 继续交谈 -> talk\n", text, StringComparison.Ordinal);
        Assert.Contains("  end\nend\n", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseSerializeRoundTripIsStable()
    {
        ParseResult first = CdlgFormat.Parse(Sample);
        string once = CdlgFormat.Serialize(first.Scripts);
        ParseResult second = CdlgFormat.Parse(once);
        string twice = CdlgFormat.Serialize(second.Scripts);

        Assert.Equal(once, twice);
        Assert.Empty(second.Issues);
    }

    [Fact]
    public void PipeChoiceSeparatorIsNormalizedToArrow()
    {
        ParseResult result = CdlgFormat.Parse("""
            dialogue d
              entry a
              node a
                choice 走 | b
              node b
                end
            end
            """);

        Assert.Empty(result.Issues);
        Instruction choice = result.Scripts[0].Nodes[0].Instructions[0];
        Assert.Equal(InstKind.Choice, choice.Kind);
        Assert.Equal("走", choice.Text);
        Assert.Equal("b", choice.TargetNode);

        Assert.Contains("choice 走 -> b", CdlgFormat.Serialize(result.Scripts), StringComparison.Ordinal);
    }

    [Fact]
    public void EscapesRoundTrip()
    {
        const string raw = "第一行\n第二行\t制表\\反斜杠\"引号";
        string escaped = CdlgFormat.Escape(raw);

        Assert.Equal("第一行\\n第二行\\t制表\\\\反斜杠\\\"引号", escaped);
        Assert.Equal(raw, CdlgFormat.Unescape(escaped));
    }

    [Fact]
    public void UnknownEscapeKeepsItsBackslash()
    {
        Assert.Equal("a\\qb", CdlgFormat.Unescape("a\\qb"));
    }

    [Fact]
    public void BomAndCommentsAreIgnored()
    {
        ParseResult result = CdlgFormat.Parse("\uFEFF# 注释\ndialogue d\n  entry a\n  node a\n    end\nend\n");

        Assert.Empty(result.Issues);
        Assert.Single(result.Scripts);
        Assert.Equal("d", result.Scripts[0].Id);
    }

    [Fact]
    public void InstructionsOutsideANodeAreReported()
    {
        ParseResult result = CdlgFormat.Parse("""
            dialogue d
              entry a
                line 孤儿
            end
            """);

        Assert.Contains(result.Issues, issue => issue.Message.Contains("出现在 node 块之外", StringComparison.Ordinal));
    }

    [Fact]
    public void UnknownCommandIsReported()
    {
        ParseResult result = CdlgFormat.Parse("""
            dialogue d
              entry a
              node a
                frobnicate x
            end
            """);

        Assert.Contains(result.Issues, issue => issue.Message.Contains("未知节点指令", StringComparison.Ordinal));
    }

    [Fact]
    public void MissingEntryIsReported()
    {
        ParseResult result = CdlgFormat.Parse("""
            dialogue d
              entry
              node a
                end
            end
            """);

        Assert.Contains(result.Issues, issue => issue.Message.Contains("entry 缺少节点 id", StringComparison.Ordinal));
    }

    [Fact]
    public void TabIndentationIsParsedAtTheRightLevel()
    {
        // 一个 Tab 顶一级（=2 空格）：dialogue 0 级、entry/node 1 级、指令 2 级。
        ParseResult result = CdlgFormat.Parse(
            "dialogue d\n" +
            "\tentry a\n" +
            "\tnode a\n" +
            "\t\tline 台词\n" +
            "\t\tend\n" +
            "end\n");

        Assert.Empty(result.Issues);
        Assert.Single(result.Scripts);
        Assert.Equal("a", result.Scripts[0].EntryNodeId);
        Assert.Single(result.Scripts[0].Nodes);
        Assert.Equal(InstKind.Line, result.Scripts[0].Nodes[0].Instructions[0].Kind);
        Assert.Equal("台词", result.Scripts[0].Nodes[0].Instructions[0].Text);
    }
}
