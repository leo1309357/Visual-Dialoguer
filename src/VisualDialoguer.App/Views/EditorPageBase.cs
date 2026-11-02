/*
 * Copyright (c) 2026 Leo Jeang. All Rights Reserved.
 *
 * This is proprietary software. Unauthorized copying, modification,
 * or distribution is strictly prohibited.
 */

using Microsoft.UI.Xaml.Controls;
using VisualDialoguer.App.ViewModels;
using VisualDialoguer.Core.Editing;
using VisualDialoguer.Core.Model;

namespace VisualDialoguer.App.Views;

/// <summary>
/// 编辑页的共同底座：一个页面知道自己看的是哪份文档，并能在文档变动后
/// 重新画一遍自己。
///
/// 「对话结构」与「指令编排」两页的指令表逻辑几乎一模一样，都收在这里；
/// 各自的节点选择器（一个 ListView、一个 ComboBox）留在子类。指令表控件
/// 由子类按名字传进来——两页 XAML 里都叫 InstructionList / Editor，但生成
/// 的字段属于各自的 partial 类，基类拿不到，只能参数注入。
/// </summary>
public abstract class EditorPageBase : UserControl
{
    /// <summary>本页面对应的文档。由窗口注入。</summary>
    protected EditorDocument? Document { get; set; }

    /// <summary>
    /// 程序化刷新选中项时置真，用来挡住 ListView / ComboBox 的
    /// SelectionChanged 回写文档——否则会"刷新→触发选中→再刷新"打转。
    /// </summary>
    protected bool SuppressSelection { get; set; }

    /// <summary>把文档绑到这一页。</summary>
    public void Bind(EditorDocument? document)
    {
        Document = document;
        OnBind(document);
        Refresh();
    }

    /// <summary>文档刚绑上来时的额外动作（子类把子面板也绑上）。</summary>
    protected virtual void OnBind(EditorDocument? document)
    {
    }

    /// <summary>文档变了，重画这一页。</summary>
    public abstract void Refresh();

    // ---- 指令表：两页共用 ----------------------------------------------------

    /// <summary>
    /// 重画指令表：按当前选中节点的指令重建行、恢复选中，并把选中的指令
    /// 显示到属性面板。重建列表会触发 SelectionChanged，用
    /// <see cref="SuppressSelection"/> 挡住回写。
    /// </summary>
    protected void RefreshInstructionList(ListView list, InstructionEditorPanel editor)
    {
        EditorDocument? document = Document;
        if (document is null)
        {
            return;
        }

        SuppressSelection = true;
        var rows = new List<InstructionRow>();
        DialogueNode? node = document.SelectedNode;
        if (node is not null)
        {
            foreach (Instruction instruction in node.Instructions)
            {
                rows.Add(new InstructionRow
                {
                    Instruction = instruction,
                    KindLabel = InstKindInfo.DisplayName(instruction.Kind),
                });
            }
        }
        list.ItemsSource = rows;
        list.SelectedIndex =
            document.SelectedInstructionIndex >= 0 && document.SelectedInstructionIndex < rows.Count
                ? document.SelectedInstructionIndex
                : -1;
        SuppressSelection = false;

        editor.Load(document.SelectedInstruction);
    }

    /// <summary>用户在指令表里点选了一行：把选中下标写回文档。</summary>
    /// <remarks>
    /// 只写文档——写入触发 <see cref="EditorDocument.Changed"/>，由窗口统一
    /// <c>RefreshAll</c>，属性面板在 <see cref="RefreshInstructionList"/> 里跟着
    /// 更新。这里不再手动 Load / 通知，避免和事件驱动重复刷新。
    /// </remarks>
    protected void OnInstructionSelection(ListView list)
    {
        if (SuppressSelection || Document is null)
        {
            return;
        }
        Document.SelectedInstructionIndex = list.SelectedIndex;
    }

    /// <summary>按类型在选中节点末尾加一条指令。</summary>
    protected void AddInstruction(InstKind kind) => Document?.AddInstruction(kind);

    /// <summary>删除选中的指令。</summary>
    protected void DeleteInstructionCmd() => Document?.DeleteSelectedInstruction();

    /// <summary>上移（-1）或下移（+1）选中的指令。</summary>
    protected void MoveInstructionCmd(int direction) => Document?.MoveSelectedInstruction(direction);

    /// <summary>删除选中的节点。</summary>
    protected void DeleteSelectedNodeCmd() => Document?.DeleteSelectedNode();

    /// <summary>把当前选中的节点设为入口。</summary>
    protected void SetEntryCmd() => Document?.SetEntryNode(Document?.SelectedNodeId);
}
