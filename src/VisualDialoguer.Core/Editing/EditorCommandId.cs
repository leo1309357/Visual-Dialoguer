/*
 * Copyright (c) 2026 Leo Jeang. All Rights Reserved.
 *
 * This is proprietary software. Unauthorized copying, modification,
 * or distribution is strictly prohibited.
 */

namespace VisualDialoguer.Core.Editing;

/// <summary>
/// 编辑器命令 id。数值与旧 cdlg-editor（Win32 时代）的命令表保持一致，
/// 便于对照与后续扩展；界面上的菜单、快捷键、右键菜单全部汇入这一个
/// 命令中枢。
/// </summary>
public enum EditorCommandId
{
    /// <summary>新建空白脚本。</summary>
    FileNew = 1001,

    /// <summary>打开文件。</summary>
    FileOpen = 1002,

    /// <summary>保存。</summary>
    FileSave = 1003,

    /// <summary>另存为。</summary>
    FileSaveAs = 1004,

    /// <summary>校验脚本。</summary>
    FileValidate = 1005,

    /// <summary>退出。</summary>
    FileExit = 1006,

    /// <summary>新增节点。</summary>
    NodeAdd = 1101,

    /// <summary>删除当前节点。</summary>
    NodeDelete = 1102,

    /// <summary>设为入口节点。</summary>
    NodeSetEntry = 1103,

    /// <summary>新增指令。</summary>
    InstAdd = 1201,

    /// <summary>删除当前指令。</summary>
    InstDelete = 1202,

    /// <summary>上移指令。</summary>
    InstUp = 1203,

    /// <summary>下移指令。</summary>
    InstDown = 1204,

    /// <summary>关于。</summary>
    HelpAbout = 1301,

    /// <summary>撤销。</summary>
    EditUndo = 1401,

    /// <summary>重做。</summary>
    EditRedo = 1402,

    /// <summary>复制指令。</summary>
    EditCopyInst = 1403,

    /// <summary>剪切指令。</summary>
    EditCutInst = 1404,

    /// <summary>粘贴指令。</summary>
    EditPasteInst = 1405,

    /// <summary>复制节点。</summary>
    EditCopyNode = 1406,

    /// <summary>剪切节点。</summary>
    EditCutNode = 1407,

    /// <summary>粘贴节点。</summary>
    EditPasteNode = 1408,
}
