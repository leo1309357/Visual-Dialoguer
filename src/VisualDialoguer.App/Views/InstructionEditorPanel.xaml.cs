/*
 * Copyright (c) 2026 Leo Jeang. All Rights Reserved.
 *
 * This is proprietary software. Unauthorized copying, modification,
 * or distribution is strictly prohibited.
 */

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using VisualDialoguer.Core.Editing;
using VisualDialoguer.Core.Model;

namespace VisualDialoguer.App.Views;

/// <summary>
/// 指令属性面板：按指令类型显示对应字段，并就地编辑。
///
/// 字段是按类型现搭的：改类型时面板重建，无关字段清空——这是模型层
/// <see cref="DialogueModel.ClearIrrelevantFields"/> 的规矩，界面照着做。
///
/// 就地编辑直接写模型对象，再调 <see cref="EditorDocument.NotifyContentChanged"/>
/// 让窗口刷新列表副标题与状态栏；撤销不逐键入栈，而是 <see cref="OnFieldGotFocus"/>
/// 取快照、<see cref="OnFieldLostFocus"/> 时把整段编辑一次性压栈。
/// </summary>
public sealed partial class InstructionEditorPanel : UserControl
{
    private readonly Dictionary<TextBox, string> _originalValues = new();
    private EditorDocument? _document;
    private Instruction? _instruction;
    private object? _pendingSnapshot;
    private bool _loading;

    public InstructionEditorPanel()
    {
        InitializeComponent();
        TypeBox.ItemsSource = Enum.GetValues<InstKind>()
            .Select(kind => InstKindInfo.DisplayName(kind))
            .ToList();
    }

    /// <summary>绑文档（撤销栈在文档里）。</summary>
    public void Bind(EditorDocument? document) => _document = document;

    /// <summary>显示一条指令；传 null 表示没有选中。</summary>
    /// <remarks>
    /// 正在就地编辑这条指令时（某个字段输入框持焦点）不重建：属性编辑每敲
    /// 一键都会触发文档 <see cref="EditorDocument.Changed"/> → 全量刷新 → 回到
    /// 这里，若照样重建 TextBox，用户正在输入的框会被销毁、焦点与光标丢失，
    /// 没法连续打字，连 <see cref="OnFieldGotFocus"/> 存的撤销快照都被
    /// <see cref="BuildFields"/> 清空。此时模型已是最新、字段显示的就是用户敲
    /// 进去的值，跳过即可。焦点在别处（点了列表换选中、点了类型下拉框）时
    /// <c>_originalValues</c> 是空的，照常重建。
    /// </remarks>
    public void Load(Instruction? instruction)
    {
        if (_originalValues.Count > 0 && ReferenceEquals(instruction, _instruction))
        {
            return;
        }

        _loading = true;
        _instruction = instruction;

        bool has = instruction is not null;
        EmptyHint.Visibility = has ? Visibility.Collapsed : Visibility.Visible;
        TypeBox.IsEnabled = has;
        WellHost.Visibility = has ? Visibility.Visible : Visibility.Collapsed;

        if (instruction is not null)
        {
            TypeBox.SelectedItem = InstKindInfo.DisplayName(instruction.Kind);
            BuildFields(instruction);
        }
        else
        {
            FieldsHost.Children.Clear();
        }
        _loading = false;
    }

    private void BuildFields(Instruction instruction)
    {
        FieldsHost.Children.Clear();
        _originalValues.Clear();

        foreach ((string label, string field) in InstKindInfo.Fields(instruction.Kind))
        {
            var grid = new Grid { ColumnSpacing = 8 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(92) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var caption = new TextBlock
            {
                Text = label,
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 13,
                Foreground = Theme.Brush("PsInkSecondary", new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x80, 0x80, 0x80))),
            };
            Grid.SetColumn(caption, 0);
            grid.Children.Add(caption);

            var box = new TextBox { Text = ReadField(instruction, field) };
            Grid.SetColumn(box, 1);
            box.Tag = field;
            box.GotFocus += OnFieldGotFocus;
            box.LostFocus += OnFieldLostFocus;
            box.TextChanged += OnFieldTextChanged;
            grid.Children.Add(box);

            FieldsHost.Children.Add(grid);
        }
    }

    private void OnTypeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || _instruction is null || _document is null)
        {
            return;
        }
        InstKind kind = InstKindInfo.FromDisplayName(TypeBox.SelectedItem as string);
        if (!_document.SetSelectedInstructionKind(kind))
        {
            return;
        }
        // 改类型时焦点在类型下拉框上（字段框已失焦、_originalValues 为空），
        // 所以这里的 Load 会照常重建，把新类型的字段搭出来。
        Load(_instruction);
    }

    private void OnFieldGotFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox box)
        {
            return;
        }
        _originalValues[box] = box.Text;
        _pendingSnapshot = _document?.CaptureState();
    }

    private void OnFieldLostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox box || _document is null)
        {
            return;
        }
        if (_originalValues.TryGetValue(box, out string? original) && original != box.Text && _pendingSnapshot is not null)
        {
            _document.PushUndoState(_pendingSnapshot);
        }
        _originalValues.Remove(box);
    }

    private void OnFieldTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_loading || _instruction is null || _document is null)
        {
            return;
        }
        if (sender is not TextBox box || box.Tag is not string field)
        {
            return;
        }
        WriteField(_instruction, field, box.Text);

        // 就地改了模型对象，文档自己感知不到：显式通知一声，窗口据此刷新
        // 列表副标题与状态栏。不压撤销栈——那是 LostFocus 按整段编辑入栈的。
        _document.NotifyContentChanged();
    }

    private static string ReadField(Instruction instruction, string field) => field switch
    {
        nameof(Instruction.Speaker) => instruction.Speaker,
        nameof(Instruction.Text) => instruction.Text,
        nameof(Instruction.TargetNode) => instruction.TargetNode,
        nameof(Instruction.FlagKey) => instruction.FlagKey,
        nameof(Instruction.FlagValue) => instruction.FlagValue,
        nameof(Instruction.SignalId) => instruction.SignalId,
        nameof(Instruction.SignalPayload) => instruction.SignalPayload,
        _ => string.Empty,
    };

    private static void WriteField(Instruction instruction, string field, string value)
    {
        switch (field)
        {
            case nameof(Instruction.Speaker):
                instruction.Speaker = value;
                break;
            case nameof(Instruction.Text):
                instruction.Text = value;
                break;
            case nameof(Instruction.TargetNode):
                instruction.TargetNode = value;
                break;
            case nameof(Instruction.FlagKey):
                instruction.FlagKey = value;
                break;
            case nameof(Instruction.FlagValue):
                instruction.FlagValue = value;
                break;
            case nameof(Instruction.SignalId):
                instruction.SignalId = value;
                break;
            case nameof(Instruction.SignalPayload):
                instruction.SignalPayload = value;
                break;
        }
    }
}
