/*
 * Copyright (c) 2026 Leo Jeang. All Rights Reserved.
 *
 * This is proprietary software. Unauthorized copying, modification,
 * or distribution is strictly prohibited.
 */

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using VisualDialoguer.App.ViewModels;
using VisualDialoguer.Core.Editing;
using VisualDialoguer.Core.Model;

namespace VisualDialoguer.App.Views;

/// <summary>
/// 「对话结构」页：左边节点表，中间当前节点的指令表，右边属性面板。
///
/// 指令表的增删改移与属性面板都在基类 <see cref="EditorPageBase"/>；这一页
/// 只管左边的节点表。
/// </summary>
public sealed partial class StructurePage : EditorPageBase
{
    public StructurePage()
    {
        InitializeComponent();
        KindPicker.ItemsSource = Enum.GetValues<InstKind>()
            .Select(kind => InstKindInfo.DisplayName(kind))
            .ToList();
        KindPicker.SelectedIndex = 0;
    }

    protected override void OnBind(EditorDocument? document) => Editor.Bind(document);

    public override void Refresh()
    {
        EditorDocument? document = Document;
        if (document is null)
        {
            return;
        }

        SuppressSelection = true;
        var nodeRows = new List<NodeRow>();
        foreach (DialogueNode node in document.Script.Nodes)
        {
            nodeRows.Add(new NodeRow
            {
                Id = node.Id,
                IsEntry = string.Equals(node.Id, document.Script.EntryNodeId, StringComparison.Ordinal),
                InstructionCount = node.Instructions.Count,
            });
        }
        NodeList.ItemsSource = nodeRows;
        NodeList.SelectedIndex = IndexOfNode(nodeRows, document.SelectedNodeId);
        SuppressSelection = false;

        RefreshInstructionList(InstructionList, Editor);
    }

    private static int IndexOfNode(List<NodeRow> rows, string? id)
    {
        if (id is null)
        {
            return -1;
        }
        return rows.FindIndex(row => string.Equals(row.Id, id, StringComparison.Ordinal));
    }

    // ---- 选中 ---------------------------------------------------------------

    private void OnNodeSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SuppressSelection || Document is null)
        {
            return;
        }
        // 只写文档：选中项一变就触发 Changed → 窗口 RefreshAll，指令表与
        // 属性面板随之更新，这里不再手动刷新。
        Document.SelectedNodeId = NodeList.SelectedIndex >= 0 && NodeList.SelectedIndex < NodeList.Items.Count
            ? ((NodeRow)NodeList.Items[NodeList.SelectedIndex]).Id
            : null;
        Document.SelectedInstructionIndex = -1;
    }

    private void OnInstructionSelectionChanged(object sender, SelectionChangedEventArgs e) =>
        OnInstructionSelection(InstructionList);

    // ---- 命令 ---------------------------------------------------------------

    private void OnAddNode(object sender, RoutedEventArgs e)
    {
        if (Document is null)
        {
            return;
        }
        Document.AddNode(DialogueModel.NextNodeId(Document.Script));
    }

    private void OnDeleteNode(object sender, RoutedEventArgs e) => DeleteSelectedNodeCmd();

    private void OnSetEntry(object sender, RoutedEventArgs e) => SetEntryCmd();

    private void OnAddInstruction(object sender, RoutedEventArgs e) =>
        AddInstruction(InstKindInfo.FromDisplayName(KindPicker.SelectedItem as string));

    private void OnDeleteInstruction(object sender, RoutedEventArgs e) => DeleteInstructionCmd();

    private void OnMoveUp(object sender, RoutedEventArgs e) => MoveInstructionCmd(-1);

    private void OnMoveDown(object sender, RoutedEventArgs e) => MoveInstructionCmd(+1);
}
