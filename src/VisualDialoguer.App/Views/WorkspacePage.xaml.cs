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
/// 统一工作台：左节点栏 + 中指令流 + 右属性面板。
/// 合并了原来的「对话结构」与「指令编排」两页，减少页签切换。
/// </summary>
public sealed partial class WorkspacePage : EditorPageBase
{
    private string _nodeFilter = string.Empty;
    private string _instFilter = string.Empty;
    private InstKind? _kindFilter;

    public WorkspacePage()
    {
        InitializeComponent();
        KindPicker.ItemsSource = Enum.GetValues<InstKind>()
            .Select(kind => InstKindInfo.DisplayName(kind))
            .ToList();
        KindPicker.SelectedIndex = 0;
        BuildFilterChips();
    }

    protected override void OnBind(EditorDocument? document) => Editor.Bind(document);

    /// <summary>流程图页请求切回工作台时，由主窗口调用。</summary>

    public override void Refresh()
    {
        EditorDocument? document = Document;
        if (document is null)
        {
            return;
        }

        SuppressSelection = true;

        // 节点列表（带筛选）
        var nodeRows = new List<NodeRow>();
        foreach (DialogueNode node in document.Script.Nodes)
        {
            if (!string.IsNullOrEmpty(_nodeFilter) &&
                !node.Id.Contains(_nodeFilter, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            nodeRows.Add(new NodeRow
            {
                Id = node.Id,
                IsEntry = string.Equals(node.Id, document.Script.EntryNodeId, StringComparison.Ordinal),
                InstructionCount = node.Instructions.Count,
            });
        }
        NodeList.ItemsSource = nodeRows;
        NodeList.SelectedIndex = IndexOfNode(nodeRows, document.SelectedNodeId);
        CurrentNodeLabel.Text = document.SelectedNodeId ?? "未选择节点";

        SuppressSelection = false;

        RefreshInstructionList(InstructionList, Editor);
    }

    protected override void RefreshInstructionList(ListView list, InstructionEditorPanel editor)
    {
        EditorDocument? document = Document;
        if (document is null) return;

        SuppressSelection = true;
        var rows = new List<InstructionRow>();
        DialogueNode? node = document.SelectedNode;
        if (node is not null)
        {
            for (int originalIndex = 0; originalIndex < node.Instructions.Count; ++originalIndex)
            {
                Instruction instruction = node.Instructions[originalIndex];
                // 类型筛选
                if (_kindFilter.HasValue && instruction.Kind != _kindFilter.Value) continue;
                // 文本筛选
                if (!string.IsNullOrEmpty(_instFilter))
                {
                    string summary = instruction.Summary();
                    if (!summary.Contains(_instFilter, StringComparison.OrdinalIgnoreCase) &&
                        !instruction.Speaker.Contains(_instFilter, StringComparison.OrdinalIgnoreCase) &&
                        !instruction.TargetNode.Contains(_instFilter, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                }
                rows.Add(new InstructionRow
                {
                    Instruction = instruction,
                    KindLabel = InstKindInfo.DisplayName(instruction.Kind),
                    OriginalIndex = originalIndex,
                });
            }
        }
        list.ItemsSource = rows;
        // 筛选后列表下标≠真实下标，按 OriginalIndex 找回当前选中行
        list.SelectedIndex = rows.FindIndex(row => row.OriginalIndex == document.SelectedInstructionIndex);
        SuppressSelection = false;

        editor.Load(document.SelectedInstruction);
    }

    private static int IndexOfNode(List<NodeRow> rows, string? id)
    {
        if (id is null) return -1;
        return rows.FindIndex(row => string.Equals(row.Id, id, StringComparison.Ordinal));
    }

    // ---- 筛选 chips ----

    private void BuildFilterChips()
    {
        FilterBar.Children.Clear();
        AddFilterChip("全部", null);
        foreach (InstKind kind in Enum.GetValues<InstKind>())
        {
            AddFilterChip(InstKindInfo.Badge(kind), kind);
        }
    }

    private void AddFilterChip(string label, InstKind? kind)
    {
        var chip = new Button
        {
            Content = label,
            Padding = new Thickness(10, 4, 10, 4),
            FontSize = 12,
            CornerRadius = new CornerRadius(12),
            Tag = kind,
        };
        chip.Click += OnFilterChipClick;
        FilterBar.Children.Add(chip);
    }

    private void OnFilterChipClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        _kindFilter = btn.Tag as InstKind?;
        Refresh();
    }

    // ---- 搜索 ----

    // 自定义模板下 TextBox 内部 GoToState 在跨框切焦时偶发不触发 Unfocused，
    // 显式兜底：失焦时强制把 FocusStates 切回 Unfocused，让凹坑落回凹陷态。
    private void OnSearchGotFocus(object sender, RoutedEventArgs e) =>
        VisualStateManager.GoToState((Control)sender, "Focused", useTransitions: true);

    private void OnSearchLostFocus(object sender, RoutedEventArgs e) =>
        VisualStateManager.GoToState((Control)sender, "Unfocused", useTransitions: true);

    private void OnNodeSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        _nodeFilter = ((TextBox)sender).Text ?? string.Empty;
        Refresh();
    }

    private void OnInstructionSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        _instFilter = ((TextBox)sender).Text ?? string.Empty;
        Refresh();
    }

    private void OnInstructionSearchClear(object sender, RoutedEventArgs e)
    {
        InstructionSearch.Text = string.Empty;
    }

    // ---- 选中 ----

    private void OnNodeSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SuppressSelection || Document is null) return;
        Document.SelectedNodeId = NodeList.SelectedIndex >= 0 && NodeList.SelectedIndex < NodeList.Items.Count
            ? ((NodeRow)NodeList.Items[NodeList.SelectedIndex]).Id
            : null;
        Document.SelectedInstructionIndex = -1;
    }

    private void OnNodeDoubleTapped(object sender, Microsoft.UI.Xaml.Input.DoubleTappedRoutedEventArgs e)
    {
        if (NodeList.SelectedItem is not NodeRow row || Document is null) return;
        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = "重命名节点",
            PrimaryButtonText = "确定",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
        };
        var box = new TextBox { Text = row.Id };
        dialog.Content = box;
        _ = RenameNodeAsync(dialog, box, row.Id);
    }

    private async System.Threading.Tasks.Task RenameNodeAsync(ContentDialog dialog, TextBox box, string oldId)
    {
        await dialog.ShowAsync();
        if (Document is null) return;

        string newId = box.Text.Trim();
        if (newId == oldId || string.IsNullOrEmpty(newId))
        {
            return;
        }

        // 前置校验：cdlg 标识符不能含空白或 #，否则保存后读不回来
        if (newId[0] == '#' || newId.Any(char.IsWhiteSpace))
        {
            await ShowErrorAsync("节点 id 不能包含空白字符，也不能以 # 开头。");
            return;
        }

        if (!Document.RenameNode(oldId, newId))
        {
            await ShowErrorAsync($"节点 id “{newId}”已经存在，请换一个名字。");
        }
    }

    private async System.Threading.Tasks.Task ShowErrorAsync(string message)
    {
        var error = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = "无法重命名",
            Content = message,
            CloseButtonText = "知道了",
        };
        await error.ShowAsync();
    }

    private void OnInstructionSelectionChanged(object sender, SelectionChangedEventArgs e) =>
        OnInstructionSelection(InstructionList);

    /// <summary>
    /// 筛选后列表下标≠真实指令下标，必须从选中行的 OriginalIndex 取，
    /// 不能直接用 ListView.SelectedIndex。
    /// </summary>
    protected override void OnInstructionSelection(ListView list)
    {
        if (SuppressSelection || Document is null) return;
        if (list.SelectedItem is InstructionRow row)
        {
            Document.SelectedInstructionIndex = row.OriginalIndex;
        }
        else
        {
            Document.SelectedInstructionIndex = -1;
        }
    }

    // ---- 命令 ----

    private void OnAddNode(object sender, RoutedEventArgs e)
    {
        if (Document is null) return;
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
