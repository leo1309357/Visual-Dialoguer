/*
 * Copyright (c) 2026 Leo Jeang. All Rights Reserved.
 *
 * This is proprietary software. Unauthorized copying, modification,
 * or distribution is strictly prohibited.
 */

using VisualDialoguer.Core.Format;
using VisualDialoguer.Core.Model;

namespace VisualDialoguer.Core.Editing;

/// <summary>
/// 编辑器的状态层：脚本、选中项、脏标记、撤销/重做、两级剪贴板。
///
/// 这里不碰任何界面：文件对话框与消息框留在 WinUI 层，命令中枢在两边
/// 各管一段——界面层把命令翻译成这里的调用，状态层只管数据。
///
/// 撤销/重做用整份快照：脚本量级很小（几十个节点），快照比增量补丁
/// 简单得多，也就不会有"撤销回一半"的中间态。
/// </summary>
public sealed class EditorDocument
{
    /// <summary>撤销栈容量。</summary>
    public const int MaxHistory = 50;

    private readonly List<DialogueScript> _scripts = new() { new DialogueScript { Id = "untitled" } };
    private readonly List<Snapshot> _undo = new();
    private readonly List<Snapshot> _redo = new();

    private Instruction? _instructionClipboard;
    private DialogueNode? _nodeClipboard;

    /// <summary>
    /// 文档发生任何变更（内容、选中项、脏状态）时触发。UI 层订阅它来
    /// 自动刷新，取代过去"改完记得手动调 Refresh"的做法——不会再有忘了
    /// 刷新导致的陈旧界面。
    /// </summary>
    public event Action? Changed;

    private void OnChanged() => Changed?.Invoke();

    /// <summary>
    /// 外部直接改了模型对象（如属性面板就地写字段）后，用它通知文档
    /// "内容变了"、触发刷新。不走撤销栈。
    /// </summary>
    public void NotifyContentChanged() => OnChanged();

    /// <summary>当前编辑的脚本（通常是首个 dialogue 块）。</summary>
    public DialogueScript Script => _scripts.Count > 0 ? _scripts[0] : _fallbackScript;

    /// <summary>文件里的全部脚本块。界面只编辑首个，其余块保存时原样写回。</summary>
    public IReadOnlyList<DialogueScript> Scripts => _scripts;

    /// <summary>
    /// 有未保存的改动：当前内容与上次保存（或新建/载入）时的内容不同。
    ///
    /// 用实际比对而不是"改过就置位"的布尔标记：改完又撤销回去，脚本
    /// 和磁盘上的一模一样，这时再显示未保存星号是误报。
    /// </summary>
    public bool IsDirty => _savedText != CurrentText;

    /// <summary>当前内容的序列化形式，用来与已保存基准比对。</summary>
    private string CurrentText => CdlgFormat.Serialize(_scripts);

    /// <summary>上次保存/新建/载入时的内容基准。</summary>
    private string _savedText = string.Empty;

    /// <summary>当前文件路径。为 <c>null</c> 表示还没存过盘。</summary>
    public string? FilePath { get; set; }

    private string? _selectedNodeId;
    private int _selectedInstructionIndex = -1;

    /// <summary>
    /// 选中的节点 id。外部直接赋值时触发 <see cref="Changed"/>；文档自己的
    /// 变更方法内部写 <c>_selectedNodeId</c>，由方法末尾统一触发一次，避免
    /// 一次操作引发多次刷新。
    /// </summary>
    public string? SelectedNodeId
    {
        get => _selectedNodeId;
        set
        {
            if (string.Equals(_selectedNodeId, value, StringComparison.Ordinal))
            {
                return;
            }
            _selectedNodeId = value;
            OnChanged();
        }
    }

    /// <summary>
    /// 选中节点内选中的指令下标（-1 表示没选中）。赋值语义同
    /// <see cref="SelectedNodeId"/>。
    /// </summary>
    public int SelectedInstructionIndex
    {
        get => _selectedInstructionIndex;
        set
        {
            if (_selectedInstructionIndex == value)
            {
                return;
            }
            _selectedInstructionIndex = value;
            OnChanged();
        }
    }

    /// <summary>选中节点。没选中时返回 <c>null</c>。</summary>
    public DialogueNode? SelectedNode => DialogueModel.FindNode(Script, SelectedNodeId);

    /// <summary>选中的指令。没选中时返回 <c>null</c>。</summary>
    public Instruction? SelectedInstruction
    {
        get
        {
            DialogueNode? node = SelectedNode;
            if (node is null || SelectedInstructionIndex < 0 || SelectedInstructionIndex >= node.Instructions.Count)
            {
                return null;
            }
            return node.Instructions[SelectedInstructionIndex];
        }
    }

    /// <summary>撤销栈深度。</summary>
    public int UndoDepth => _undo.Count;

    /// <summary>重做栈深度。</summary>
    public int RedoDepth => _redo.Count;

    private readonly DialogueScript _fallbackScript = new();

    /// <summary>新建空白脚本，清空历史与剪贴板。</summary>
    public void NewScript()
    {
        _scripts.Clear();
        _scripts.Add(new DialogueScript { Id = "untitled" });
        FilePath = null;
        ClearHistory();
        _selectedNodeId = null;
        _selectedInstructionIndex = -1;
        _savedText = CurrentText;
        OnChanged();
    }

    /// <summary>载入脚本（来自文件解析）。清空历史，标记为已保存。</summary>
    public void Load(IEnumerable<DialogueScript> scripts, string? path)
    {
        _scripts.Clear();
        _scripts.AddRange(scripts);
        if (_scripts.Count == 0)
        {
            _scripts.Add(new DialogueScript { Id = "untitled" });
        }
        FilePath = path;
        ClearHistory();
        _selectedNodeId = _scripts[0].Nodes.Count > 0 ? _scripts[0].Nodes[0].Id : null;
        _selectedInstructionIndex = -1;
        _savedText = CurrentText;
        OnChanged();
    }

    /// <summary>标记为已保存（写盘之后调用）。把当前内容记为新基准。</summary>
    public void MarkSaved() => _savedText = CurrentText;

    /// <summary>
    /// 取一份当前状态的快照。属性编辑在获得焦点时取，失焦且确有改动时
    /// 用 <see cref="PushUndoState"/> 把它压进撤销栈——逐键入栈不做，
    /// 那会让撤销栈被一串单字符记录淹没。
    /// </summary>
    public object CaptureState() => Capture();

    /// <summary>把 <see cref="CaptureState"/> 取到的快照压进撤销栈。</summary>
    public void PushUndoState(object state)
    {
        if (state is not Snapshot snapshot)
        {
            return;
        }
        _undo.Add(snapshot);
        if (_undo.Count > MaxHistory)
        {
            _undo.RemoveAt(0);
        }
        _redo.Clear();
    }

    /// <summary>清空撤销/重做栈。</summary>
    public void ClearHistory()
    {
        _undo.Clear();
        _redo.Clear();
    }

    // ---- 撤销 / 重做 --------------------------------------------------------

    /// <summary>
    /// 在改动之前调用：把当前状态压进撤销栈，并清空重做栈。
    /// </summary>
    public void BeginChange()
    {
        _undo.Add(Capture());
        if (_undo.Count > MaxHistory)
        {
            _undo.RemoveAt(0);
        }
        _redo.Clear();
    }

    /// <summary>撤销一步。返回 false 表示没有可撤销的改动。</summary>
    public bool Undo()
    {
        if (_undo.Count == 0)
        {
            return false;
        }
        Snapshot current = Capture();
        Snapshot previous = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        _redo.Add(current);
        Restore(previous);
        OnChanged();
        return true;
    }

    /// <summary>重做一步。返回 false 表示没有可重做的改动。</summary>
    public bool Redo()
    {
        if (_redo.Count == 0)
        {
            return false;
        }
        Snapshot current = Capture();
        Snapshot next = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        _undo.Add(current);
        Restore(next);
        OnChanged();
        return true;
    }

    private Snapshot Capture()
    {
        var copies = new List<DialogueScript>(_scripts.Count);
        foreach (DialogueScript script in _scripts)
        {
            copies.Add(script.Clone());
        }
        return new Snapshot(copies, SelectedNodeId, SelectedInstructionIndex);
    }

    private void Restore(Snapshot snapshot)
    {
        _scripts.Clear();
        foreach (DialogueScript script in snapshot.Scripts)
        {
            _scripts.Add(script.Clone());
        }
        _selectedNodeId = snapshot.SelectedNodeId;
        _selectedInstructionIndex = snapshot.SelectedInstructionIndex;
    }

    // ---- 节点 ---------------------------------------------------------------

    /// <summary>新增节点。返回新节点。</summary>
    public DialogueNode AddNode(string id)
    {
        BeginChange();
        DialogueNode node = DialogueModel.AddNode(Script, id);
        _selectedNodeId = node.Id;
        _selectedInstructionIndex = -1;
        OnChanged();
        return node;
    }

    /// <summary>删除选中的节点。入口节点被删时，入口定义一并清空。</summary>
    public bool DeleteSelectedNode()
    {
        DialogueNode? node = SelectedNode;
        if (node is null)
        {
            return false;
        }
        BeginChange();
        int index = Script.Nodes.IndexOf(node);
        DialogueModel.RemoveNode(Script, node);
        if (string.Equals(Script.EntryNodeId, node.Id, StringComparison.Ordinal))
        {
            Script.EntryNodeId = string.Empty;
        }
        _selectedNodeId = Script.Nodes.Count > 0
            ? Script.Nodes[Math.Min(index, Script.Nodes.Count - 1)].Id
            : null;
        _selectedInstructionIndex = -1;
        OnChanged();
        return true;
    }

    /// <summary>把某个节点设为入口节点。</summary>
    public bool SetEntryNode(string? nodeId)
    {
        if (string.IsNullOrEmpty(nodeId) || DialogueModel.FindNode(Script, nodeId) is null)
        {
            return false;
        }
        BeginChange();
        Script.EntryNodeId = nodeId!;
        OnChanged();
        return true;
    }

    // ---- 指令 ---------------------------------------------------------------

    /// <summary>在选中节点末尾新增一条指令。</summary>
    public Instruction? AddInstruction(InstKind kind)
    {
        DialogueNode? node = SelectedNode;
        if (node is null)
        {
            return null;
        }
        BeginChange();
        Instruction instruction = DialogueModel.AddInstruction(node, kind);
        _selectedInstructionIndex = node.Instructions.Count - 1;
        OnChanged();
        return instruction;
    }

    /// <summary>删除选中的指令。删除后自动选中相邻的一条。</summary>
    public bool DeleteSelectedInstruction()
    {
        DialogueNode? node = SelectedNode;
        Instruction? instruction = SelectedInstruction;
        if (node is null || instruction is null)
        {
            return false;
        }
        BeginChange();
        int index = SelectedInstructionIndex;
        DialogueModel.RemoveInstruction(node, instruction);
        _selectedInstructionIndex = node.Instructions.Count == 0
            ? -1
            : Math.Min(index, node.Instructions.Count - 1);
        OnChanged();
        return true;
    }

    /// <summary>上移（-1）或下移（+1）选中的指令。</summary>
    public bool MoveSelectedInstruction(int direction)
    {
        DialogueNode? node = SelectedNode;
        Instruction? instruction = SelectedInstruction;
        if (node is null || instruction is null)
        {
            return false;
        }
        int index = node.Instructions.IndexOf(instruction);
        int target = index + (direction < 0 ? -1 : 1);
        if (target < 0 || target >= node.Instructions.Count)
        {
            return false;
        }
        BeginChange();
        DialogueModel.MoveInstruction(node, instruction, direction);
        _selectedInstructionIndex = target;
        OnChanged();
        return true;
    }

    /// <summary>切换选中指令的类型，无关字段会被清空。</summary>
    public bool SetSelectedInstructionKind(InstKind kind)
    {
        Instruction? instruction = SelectedInstruction;
        if (instruction is null || instruction.Kind == kind)
        {
            return false;
        }
        BeginChange();
        DialogueModel.ClearIrrelevantFields(instruction, kind);
        OnChanged();
        return true;
    }

    // ---- 剪贴板 -------------------------------------------------------------

    /// <summary>复制选中的指令。</summary>
    public bool CopyInstruction()
    {
        Instruction? instruction = SelectedInstruction;
        if (instruction is null)
        {
            return false;
        }
        _instructionClipboard = instruction.Clone();
        return true;
    }

    /// <summary>剪切选中的指令。</summary>
    public bool CutInstruction()
    {
        if (!CopyInstruction())
        {
            return false;
        }
        return DeleteSelectedInstruction();
    }

    /// <summary>把剪贴板里的指令追加到当前节点末尾。</summary>
    public bool PasteInstruction()
    {
        DialogueNode? node = SelectedNode;
        if (node is null || _instructionClipboard is null)
        {
            return false;
        }
        BeginChange();
        node.Instructions.Add(_instructionClipboard.Clone());
        _selectedInstructionIndex = node.Instructions.Count - 1;
        OnChanged();
        return true;
    }

    /// <summary>复制选中的节点。</summary>
    public bool CopyNode()
    {
        DialogueNode? node = SelectedNode;
        if (node is null)
        {
            return false;
        }
        _nodeClipboard = node.Clone();
        return true;
    }

    /// <summary>剪切选中的节点。</summary>
    public bool CutNode()
    {
        if (!CopyNode())
        {
            return false;
        }
        return DeleteSelectedNode();
    }

    /// <summary>
    /// 粘贴节点。新节点 id 由原 id 加 <c>-copy</c> 去重（冲突时
    /// <c>-copy-2</c>、<c>-copy-3</c>…）。
    /// </summary>
    public bool PasteNode()
    {
        if (_nodeClipboard is null)
        {
            return false;
        }
        BeginChange();
        var copy = _nodeClipboard.Clone();
        copy.Id = UniqueNodeId(copy.Id + "-copy");
        Script.Nodes.Add(copy);
        _selectedNodeId = copy.Id;
        _selectedInstructionIndex = -1;
        OnChanged();
        return true;
    }

    private string UniqueNodeId(string candidate)
    {
        if (DialogueModel.FindNode(Script, candidate) is null)
        {
            return candidate;
        }
        for (int suffix = 2; ; ++suffix)
        {
            string attempt = $"{candidate}-{suffix}";
            if (DialogueModel.FindNode(Script, attempt) is null)
            {
                return attempt;
            }
        }
    }

    // ---- 校验 ---------------------------------------------------------------

    /// <summary>校验当前脚本，返回问题列表（空 = 通过）。</summary>
    public IReadOnlyList<string> Validate() => DialogueModel.Validate(Script);

    private sealed record Snapshot(
        List<DialogueScript> Scripts,
        string? SelectedNodeId,
        int SelectedInstructionIndex);
}
