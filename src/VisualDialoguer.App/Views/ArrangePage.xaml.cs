/*
 * Copyright (c) 2026 Leo Jeang. All Rights Reserved.
 *
 * This is proprietary software. Unauthorized copying, modification,
 * or distribution is strictly prohibited.
 */

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using VisualDialoguer.Core.Editing;
using VisualDialoguer.Core.Model;

namespace VisualDialoguer.App.Views;

/// <summary>
/// 「指令编排」页：隐藏节点表，指令表与属性面板占满宽度——集中编排一个
/// 节点里的指令时用。
///
/// 指令表的增删改移与属性面板都在基类 <see cref="EditorPageBase"/>；这一页
/// 只管顶部的节点下拉框。
/// </summary>
public sealed partial class ArrangePage : EditorPageBase
{
    public ArrangePage()
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
        var nodeIds = new List<string>();
        foreach (DialogueNode node in document.Script.Nodes)
        {
            nodeIds.Add(node.Id);
        }
        NodePicker.ItemsSource = nodeIds;
        NodePicker.SelectedItem = document.SelectedNodeId;
        SuppressSelection = false;

        RefreshInstructionList(InstructionList, Editor);
    }

    private void OnNodePicked(object sender, SelectionChangedEventArgs e)
    {
        if (SuppressSelection || Document is null)
        {
            return;
        }
        // 只写文档：选中项一变就触发 Changed → 窗口 RefreshAll。
        Document.SelectedNodeId = NodePicker.SelectedItem as string;
        Document.SelectedInstructionIndex = -1;
    }

    private void OnInstructionSelectionChanged(object sender, SelectionChangedEventArgs e) =>
        OnInstructionSelection(InstructionList);

    private void OnAddInstruction(object sender, RoutedEventArgs e) =>
        AddInstruction(InstKindInfo.FromDisplayName(KindPicker.SelectedItem as string));

    private void OnDeleteInstruction(object sender, RoutedEventArgs e) => DeleteInstructionCmd();

    private void OnMoveUp(object sender, RoutedEventArgs e) => MoveInstructionCmd(-1);

    private void OnMoveDown(object sender, RoutedEventArgs e) => MoveInstructionCmd(+1);

    private void OnSetEntry(object sender, RoutedEventArgs e) => SetEntryCmd();
}
