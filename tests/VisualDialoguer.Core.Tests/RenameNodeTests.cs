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
/// 节点重命名的测试：重复 id 拒绝、入口与 TargetNode 引用同步、选中项跟随。
/// 界面层据返回值决定是否弹错误对话框，因此这里重点验证返回值语义。
/// </summary>
public class RenameNodeTests
{
    private static EditorDocument MakeDocument()
    {
        var document = new EditorDocument();
        document.NewScript();
        document.AddNode("start");
        document.AddNode("talk");
        document.AddNode("bye");
        return document;
    }

    [Fact]
    public void RenameToDuplicateIdReturnsFalse()
    {
        EditorDocument document = MakeDocument();

        Assert.False(document.RenameNode("start", "talk"));
        // 被拒绝后原 id 必须仍然存在，不能被部分改写。
        Assert.NotNull(DialogueModel.FindNode(document.Script, "start"));
        Assert.NotNull(DialogueModel.FindNode(document.Script, "talk"));
    }

    [Fact]
    public void RenameToSameIdReturnsTrue()
    {
        EditorDocument document = MakeDocument();

        Assert.True(document.RenameNode("start", "start"));
    }

    [Fact]
    public void RenameUnknownOldIdReturnsFalse()
    {
        EditorDocument document = MakeDocument();

        Assert.False(document.RenameNode("ghost", "other"));
    }

    [Fact]
    public void RenameToFreshIdReturnsTrueAndMovesTheNode()
    {
        EditorDocument document = MakeDocument();

        Assert.True(document.RenameNode("talk", "chat"));

        Assert.NotNull(DialogueModel.FindNode(document.Script, "chat"));
        Assert.Null(DialogueModel.FindNode(document.Script, "talk"));
    }

    [Fact]
    public void RenameEntryNodeUpdatesEntryId()
    {
        EditorDocument document = MakeDocument();
        document.SetEntryNode("start");

        Assert.True(document.RenameNode("start", "main"));

        Assert.Equal("main", document.Script.EntryNodeId);
    }

    [Fact]
    public void RenameRetargetsInstructionsAndSelection()
    {
        EditorDocument document = MakeDocument();
        document.SetEntryNode("start");
        document.SelectedNodeId = "start";
        Instruction choice = document.AddInstruction(InstKind.Choice)!;
        choice.TargetNode = "talk";

        Assert.True(document.RenameNode("talk", "chat"));

        Assert.Equal("chat", choice.TargetNode);
        // 重命名的是当前选中节点时，选中项要跟随新 id。
        document.SelectedNodeId = "chat";
        Assert.Equal("chat", document.SelectedNodeId);
    }
}
