/*
 * Copyright (c) 2026 Leo Jeang. All Rights Reserved.
 *
 * This is proprietary software. Unauthorized copying, modification,
 * or distribution is strictly prohibited.
 */

using VisualDialoguer.Core.Editing;
using VisualDialoguer.Core.Model;

namespace VisualDialoguer.Core.Tests;

/// <summary>
/// 模型校验与编辑状态（撤销/重做、剪贴板）的测试。
/// </summary>
public class DialogueModelTests
{
    private static DialogueScript MakeScript()
    {
        var script = new DialogueScript { Id = "demo", EntryNodeId = "start" };
        DialogueNode start = DialogueModel.AddNode(script, "start");
        DialogueModel.AddInstruction(start, InstKind.Speaker).Speaker = "旁白";
        DialogueModel.AddInstruction(start, InstKind.Line).Text = "你好";
        DialogueNode end = DialogueModel.AddNode(script, "end");
        DialogueModel.AddInstruction(end, InstKind.End);
        return script;
    }

    [Fact]
    public void ValidScriptHasNoProblems()
    {
        Assert.Empty(DialogueModel.Validate(MakeScript()));
    }

    [Fact]
    public void MissingEntryIsReported()
    {
        DialogueScript script = MakeScript();
        script.EntryNodeId = string.Empty;

        Assert.Contains(DialogueModel.Validate(script), problem => problem.Contains("缺少入口节点", StringComparison.Ordinal));
    }

    [Fact]
    public void UnknownEntryIsReported()
    {
        DialogueScript script = MakeScript();
        script.EntryNodeId = "nowhere";

        Assert.Contains(DialogueModel.Validate(script), problem => problem.Contains("入口节点 'nowhere' 不存在", StringComparison.Ordinal));
    }

    [Fact]
    public void DuplicateNodeIdIsReported()
    {
        DialogueScript script = MakeScript();
        DialogueModel.AddNode(script, "start");

        Assert.Contains(DialogueModel.Validate(script), problem => problem.Contains("节点 id 重复：'start'", StringComparison.Ordinal));
    }

    [Fact]
    public void EmptyNodeIsReported()
    {
        DialogueScript script = MakeScript();
        DialogueModel.AddNode(script, "empty");

        Assert.Contains(DialogueModel.Validate(script), problem => problem.Contains("节点 'empty' 没有任何指令", StringComparison.Ordinal));
    }

    [Fact]
    public void DanglingTargetIsReported()
    {
        DialogueScript script = MakeScript();
        DialogueModel.AddInstruction(script.Nodes[0], InstKind.Goto).TargetNode = "nowhere";

        Assert.Contains(DialogueModel.Validate(script), problem => problem.Contains("引用了不存在的节点 'nowhere'", StringComparison.Ordinal));
    }

    [Fact]
    public void ChangingKindClearsIrrelevantFields()
    {
        var instruction = new Instruction
        {
            Kind = InstKind.Choice,
            Text = "选项",
            TargetNode = "next",
            Speaker = "旁白",
        };

        DialogueModel.ClearIrrelevantFields(instruction, InstKind.Line);

        Assert.Equal(InstKind.Line, instruction.Kind);
        Assert.Equal(string.Empty, instruction.Speaker);
        Assert.Equal(string.Empty, instruction.TargetNode);
    }
}

/// <summary>编辑状态层：撤销/重做与两级剪贴板。</summary>
public class EditorDocumentTests
{
    [Fact]
    public void UndoRestoresThePreviousScript()
    {
        var document = new EditorDocument();
        document.NewScript();
        document.AddNode("a");

        Assert.Single(document.Script.Nodes);
        Assert.True(document.Undo());
        Assert.Empty(document.Script.Nodes);
    }

    [Fact]
    public void RedoReappliesTheUndoneChange()
    {
        var document = new EditorDocument();
        document.NewScript();
        document.AddNode("a");

        Assert.True(document.Undo());
        Assert.True(document.Redo());
        Assert.Single(document.Script.Nodes);
    }

    [Fact]
    public void UndoStackIsBounded()
    {
        var document = new EditorDocument();
        document.NewScript();
        for (int index = 0; index < EditorDocument.MaxHistory + 20; ++index)
        {
            document.AddNode("node" + index.ToString());
        }

        Assert.Equal(EditorDocument.MaxHistory, document.UndoDepth);
    }

    [Fact]
    public void InstructionClipboardAppendsToTheCurrentNode()
    {
        var document = new EditorDocument();
        document.NewScript();
        DialogueNode node = document.AddNode("a");
        document.SelectedNodeId = "a";
        Instruction instruction = document.AddInstruction(InstKind.Line)!;
        instruction.Text = "台词";

        Assert.True(document.CopyInstruction());
        Assert.True(document.PasteInstruction());

        Assert.Equal(2, node.Instructions.Count);
        Assert.Equal("台词", node.Instructions[1].Text);
    }

    [Fact]
    public void NodePasteDeduplicatesTheId()
    {
        var document = new EditorDocument();
        document.NewScript();
        document.AddNode("a");

        Assert.True(document.CopyNode());
        Assert.True(document.PasteNode());
        Assert.True(document.PasteNode());

        List<string> ids = document.Script.Nodes.Select(node => node.Id).ToList();
        Assert.Contains("a-copy", ids);
        Assert.Contains("a-copy-2", ids);
    }

    [Fact]
    public void DeletingTheEntryNodeClearsTheEntry()
    {
        var document = new EditorDocument();
        document.NewScript();
        document.AddNode("a");
        document.SelectedNodeId = "a";
        document.SetEntryNode("a");

        Assert.True(document.DeleteSelectedNode());
        Assert.Equal(string.Empty, document.Script.EntryNodeId);
    }

    [Fact]
    public void NewScriptClearsTheHistory()
    {
        var document = new EditorDocument();
        document.NewScript();
        document.AddNode("a");

        document.NewScript();

        Assert.Equal(0, document.UndoDepth);
        Assert.Equal(0, document.RedoDepth);
        Assert.False(document.IsDirty);
    }

    [Fact]
    public void DirtyFlagClearsAfterUndoingBackToTheSavedState()
    {
        var document = new EditorDocument();
        document.NewScript();
        Assert.False(document.IsDirty);

        document.AddNode("a");
        Assert.True(document.IsDirty);

        // 改完又撤销回去：内容与新建时一致，不该再报未保存。
        document.Undo();
        Assert.False(document.IsDirty);
    }

    [Fact]
    public void EditingAndSavingResetsTheDirtyFlag()
    {
        var document = new EditorDocument();
        document.NewScript();
        document.AddNode("a");
        Assert.True(document.IsDirty);

        document.MarkSaved();
        Assert.False(document.IsDirty);
    }

    [Fact]
    public void NextNodeIdSkipsExistingIds()
    {
        var document = new EditorDocument();
        document.NewScript();
        document.AddNode("node1");
        document.AddNode("node2");

        Assert.Equal("node3", DialogueModel.NextNodeId(document.Script));
    }

    [Fact]
    public void AddingANodeWithABlankIdThrows()
    {
        Assert.Throws<ArgumentException>(() => DialogueModel.AddNode(new DialogueScript(), "   "));
    }
}

/// <summary>标识符合法性校验：空白、注释前缀、choice 分隔符。</summary>
public class IdentifierValidationTests
{
    [Fact]
    public void NodeIdWithWhitespaceIsReported()
    {
        var script = new DialogueScript { Id = "d", EntryNodeId = "a b" };
        DialogueNode node = DialogueModel.AddNode(script, "a b");
        DialogueModel.AddInstruction(node, InstKind.End);

        Assert.Contains(DialogueModel.Validate(script), problem => problem.Contains("不能包含空白字符", StringComparison.Ordinal));
    }

    [Fact]
    public void NodeIdStartingWithHashIsReported()
    {
        var script = new DialogueScript { Id = "d", EntryNodeId = "#a" };
        DialogueNode node = DialogueModel.AddNode(script, "#a");
        DialogueModel.AddInstruction(node, InstKind.End);

        Assert.Contains(DialogueModel.Validate(script), problem => problem.Contains("不能以 '#' 开头", StringComparison.Ordinal));
    }

    [Fact]
    public void ChoiceLabelContainingSeparatorIsReported()
    {
        var script = new DialogueScript { Id = "d", EntryNodeId = "a" };
        DialogueNode node = DialogueModel.AddNode(script, "a");
        Instruction choice = DialogueModel.AddInstruction(node, InstKind.Choice);
        choice.Text = "坏 -> 标签";
        choice.TargetNode = "a";

        Assert.Contains(DialogueModel.Validate(script), problem => problem.Contains("不能包含分隔符", StringComparison.Ordinal));
    }
}

/// <summary>EditorDocument.Changed 事件：变更要触发、且每次操作只触发一次。</summary>
public class EditorDocumentChangedEventTests
{
    /// <summary>跑一个动作，数它触发了几次 Changed。</summary>
    private static int CountChanges(EditorDocument document, Action action)
    {
        int count = 0;
        document.Changed += () => ++count;
        action();
        return count;
    }

    [Fact]
    public void AddNodeFiresChangedExactlyOnce()
    {
        var document = new EditorDocument();
        document.NewScript();

        // NewScript 内部会改选中项；变更方法末尾统一触发一次，
        // 不该因为内部写选中而叠加成多次。
        Assert.Equal(1, CountChanges(document, () => document.AddNode("a")));
    }

    [Fact]
    public void UndoFiresChangedExactlyOnce()
    {
        var document = new EditorDocument();
        document.NewScript();
        document.AddNode("a");

        Assert.Equal(1, CountChanges(document, () => document.Undo()));
    }

    [Fact]
    public void ChangingSelectionFiresChanged()
    {
        var document = new EditorDocument();
        document.NewScript();
        document.AddNode("a");
        document.AddNode("b"); // 此时选中 "b"

        // 从 "b" 切到 "a"，值变了才触发。
        Assert.Equal(1, CountChanges(document, () => document.SelectedNodeId = "a"));
    }

    [Fact]
    public void AssigningTheSameSelectionDoesNotFireChanged()
    {
        var document = new EditorDocument();
        document.NewScript();
        document.AddNode("a"); // 此时已选中 "a"

        Assert.Equal(0, CountChanges(document, () => document.SelectedNodeId = "a"));
    }

    [Fact]
    public void CopyInstructionDoesNotFireChanged()
    {
        var document = new EditorDocument();
        document.NewScript();
        document.AddNode("a");
        document.AddInstruction(InstKind.Line);

        // 复制只读文档、不改内容，触发刷新是无谓的。
        Assert.Equal(0, CountChanges(document, () => document.CopyInstruction()));
    }

    [Fact]
    public void NotifyContentChangedFiresChanged()
    {
        var document = new EditorDocument();
        document.NewScript();

        Assert.Equal(1, CountChanges(document, () => document.NotifyContentChanged()));
    }

    [Fact]
    public void PasteNodeFiresChangedExactlyOnce()
    {
        var document = new EditorDocument();
        document.NewScript();
        document.AddNode("a");
        document.CopyNode();

        Assert.Equal(1, CountChanges(document, () => document.PasteNode()));
    }
}
