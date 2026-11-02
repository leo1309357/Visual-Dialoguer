/*
 * Copyright (c) 2026 Leo Jeang. All Rights Reserved.
 *
 * This is proprietary software. Unauthorized copying, modification,
 * or distribution is strictly prohibited.
 */

using System.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using VisualDialoguer.App.Views;
using VisualDialoguer.Core.Editing;
using VisualDialoguer.Core.Format;
using VisualDialoguer.Core.Model;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace VisualDialoguer.App;

/// <summary>
/// 主窗口：命令栏 + 三个页签 + 状态栏。
///
/// 菜单、快捷键、页签里的按钮全部汇入 <see cref="Execute"/> 一个命令中枢，
/// 与旧 cdlg-editor 的做法一致：一条命令只有一个地方实现。
/// </summary>
public sealed partial class MainWindow : Window
{
    private readonly EditorDocument _document = new();

    public MainWindow()
    {
        InitializeComponent();

        // 标题栏也属于"案头"：Win11 的原生标题栏默认是系统色，和暖纸拼在一起
        // 像两块不同材质，这里把它涂成案垫色、字用墨色。系统换主题时重涂一次。
        ApplyTitleBarTheme();
        if (Content is FrameworkElement root)
        {
            root.ActualThemeChanged += (_, _) => ApplyTitleBarTheme();
        }

        // WinUI 的 Window 默认没有尺寸，启动时只够包下内容、小得不成样子。
        // 尺寸在窗口第一次激活时设：那时 XAML 已加载，能读到真实的内容
        // 缩放比（RasterizationScale），把设计用的逻辑像素换算成
        // AppWindow.Resize 吃的物理像素，任何 DPI 下都给得出舒适大小。
        Activated += OnWindowActivated;

        GraphPage.NodeClicked += OnGraphNodeClicked;

        // 文档一变就整体重画：命令处理器只管改文档，刷新交给这里，
        // 不会再有"改完忘了刷新"的陈旧界面。
        _document.Changed += RefreshAll;

        // 点标题栏的关闭按钮时拦一道：有未保存改动就先问。
        AppWindow.Closing += OnWindowClosing;

        _document.NewScript();
        BindDocument();
    }

    private bool _windowPlaced;

    /// <summary>
    /// 首次激活时给窗口一个舒适的初始大小并居中，只跑一次。
    /// AppWindow 的尺寸与 DisplayArea 都用逻辑像素（DIP），与 DPI 缩放无关，
    /// 直接给设计尺寸即可。
    /// </summary>
    private void OnWindowActivated(object sender, WindowActivatedEventArgs args)
    {
        if (_windowPlaced)
        {
            return;
        }
        _windowPlaced = true;

        AppWindow.Resize(new Windows.Graphics.SizeInt32(1280, 820));
        ApplyTitleBarTheme();

        var displayArea = Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(
            AppWindow.Id, Microsoft.UI.Windowing.DisplayAreaFallback.Nearest);
        if (displayArea is not null)
        {
            var work = displayArea.WorkArea;
            int x = work.X + Math.Max(0, (work.Width - AppWindow.Size.Width) / 2);
            int y = work.Y + Math.Max(0, (work.Height - AppWindow.Size.Height) / 2);
            AppWindow.Move(new Windows.Graphics.PointInt32(x, y));
        }
    }

    /// <summary>
    /// 标题栏配色跟随主题：底色取案垫、标题字取墨、按钮平时与栏同色、
    /// 悬停才压深——和窗口里"纸叠在案垫上"的层次连成一片。
    /// 系统不支持标题栏自定义（如 Win10）时直接跳过。
    /// </summary>
    private void ApplyTitleBarTheme()
    {
        try
        {
            if (!Microsoft.UI.Windowing.AppWindowTitleBar.IsCustomizationSupported())
            {
                return;
            }

            Windows.UI.Color none = Windows.UI.Color.FromArgb(0, 0, 0, 0);
            Microsoft.UI.Windowing.AppWindowTitleBar bar = AppWindow.TitleBar;

            Windows.UI.Color desk = Theme.Swatch("PsDesk", none);
            Windows.UI.Color ink = Theme.Swatch("PsInk", none);
            Windows.UI.Color inkSecondary = Theme.Swatch("PsInkSecondary", none);
            Windows.UI.Color inkTertiary = Theme.Swatch("PsInkTertiary", none);

            bar.BackgroundColor = desk;
            bar.ForegroundColor = ink;
            bar.InactiveBackgroundColor = desk;
            bar.InactiveForegroundColor = inkTertiary;

            // 按钮区必须给实色：设成全透明时 DWM 会把它涂成纯黑，
            // 在浅色标题栏上切出一块突兀的黑条。
            bar.ButtonBackgroundColor = desk;
            bar.ButtonInactiveBackgroundColor = desk;
            bar.ButtonForegroundColor = inkSecondary;
            bar.ButtonInactiveForegroundColor = inkTertiary;
            bar.ButtonHoverBackgroundColor = Theme.Swatch("PsPaperSunken", none);
            bar.ButtonHoverForegroundColor = ink;
            bar.ButtonPressedBackgroundColor = Theme.Swatch("PsEdge", none);
            bar.ButtonPressedForegroundColor = ink;
        }
        catch (Exception)
        {
            // 标题栏着色是锦上添花：拿不到就不涂，不该影响启动。
        }
    }

    private async void OnWindowClosing(
        Microsoft.UI.Windowing.AppWindow sender,
        Microsoft.UI.Windowing.AppWindowClosingEventArgs args)
    {
        if (!_document.IsDirty)
        {
            return;
        }

        // 先挡下这次关闭，问完再决定：异步对话框还没出结果就放行，
        // 窗口会带着未保存的改动直接消失。
        args.Cancel = true;
        if (await ConfirmDiscardAsync("退出会丢弃当前未保存的改动。"))
        {
            _document.MarkSaved();
            Close();
        }
    }

    private void BindDocument()
    {
        StructurePage.Bind(_document);
        ArrangePage.Bind(_document);
        GraphPage.Bind(_document);
        OnDocumentChanged();
    }

    // ---- 命令中枢 ------------------------------------------------------------

    /// <summary>
    /// 执行一条命令。返回 true 表示命令被消费掉了（界面需要重画）。
    /// </summary>
    private async void Execute(EditorCommandId command)
    {
        switch (command)
        {
            case EditorCommandId.FileNew:
                if (!await ConfirmDiscardAsync("新建会丢弃当前未保存的改动。"))
                {
                    return;
                }
                _document.NewScript();
                break;

            case EditorCommandId.FileOpen:
                if (!await ConfirmDiscardAsync("打开会丢弃当前未保存的改动。"))
                {
                    return;
                }
                await OpenFileAsync();
                break;

            case EditorCommandId.FileSave:
                await SaveFileAsync(saveAs: false);
                break;

            case EditorCommandId.FileSaveAs:
                await SaveFileAsync(saveAs: true);
                break;

            case EditorCommandId.FileValidate:
                ShowValidation();
                break;

            case EditorCommandId.FileExit:
                Close();
                return;

            case EditorCommandId.NodeAdd:
                AddNode();
                break;

            case EditorCommandId.NodeDelete:
                if (!_document.DeleteSelectedNode())
                {
                    return;
                }
                break;

            case EditorCommandId.NodeSetEntry:
                if (!_document.SetEntryNode(_document.SelectedNodeId))
                {
                    return;
                }
                break;

            case EditorCommandId.InstAdd:
                if (_document.AddInstruction(InstKind.Speaker) is null)
                {
                    return;
                }
                break;

            case EditorCommandId.InstDelete:
                if (!_document.DeleteSelectedInstruction())
                {
                    return;
                }
                break;

            case EditorCommandId.InstUp:
                if (!_document.MoveSelectedInstruction(-1))
                {
                    return;
                }
                break;

            case EditorCommandId.InstDown:
                if (!_document.MoveSelectedInstruction(+1))
                {
                    return;
                }
                break;

            case EditorCommandId.EditUndo:
                if (!_document.Undo())
                {
                    SetStatus("没有可撤销的改动。");
                    return;
                }
                break;

            case EditorCommandId.EditRedo:
                if (!_document.Redo())
                {
                    SetStatus("没有可重做的改动。");
                    return;
                }
                break;

            case EditorCommandId.EditCopyInst:
                if (!_document.CopyInstruction())
                {
                    SetStatus("没有选中指令。");
                    return;
                }
                SetStatus("已复制指令。");
                return;

            case EditorCommandId.EditCutInst:
                if (!_document.CutInstruction())
                {
                    SetStatus("没有选中指令。");
                    return;
                }
                break;

            case EditorCommandId.EditPasteInst:
                if (!_document.PasteInstruction())
                {
                    SetStatus("剪贴板是空的。");
                    return;
                }
                break;

            case EditorCommandId.EditCopyNode:
                if (!_document.CopyNode())
                {
                    SetStatus("没有选中节点。");
                    return;
                }
                SetStatus("已复制节点。");
                return;

            case EditorCommandId.EditCutNode:
                if (!_document.CutNode())
                {
                    SetStatus("没有选中节点。");
                    return;
                }
                break;

            case EditorCommandId.EditPasteNode:
                if (!_document.PasteNode())
                {
                    SetStatus("剪贴板是空的。");
                    return;
                }
                break;

            case EditorCommandId.HelpAbout:
                await ShowAboutAsync();
                return;

            default:
                return;
        }
    }

    private void RefreshAll()
    {
        StructurePage.Refresh();
        ArrangePage.Refresh();
        GraphPage.Refresh();
        OnDocumentChanged();
    }

    private void OnDocumentChanged()
    {
        int nodeCount = _document.Script.Nodes.Count;
        int instructionCount = 0;
        foreach (DialogueNode node in _document.Script.Nodes)
        {
            instructionCount += node.Instructions.Count;
        }
        CountsText.Text = $"节点 {nodeCount} · 指令 {instructionCount} · 撤销 {_document.UndoDepth} · 重做 {_document.RedoDepth}";
        Title = (_document.IsDirty ? "* " : string.Empty)
            + "VisualDialoguer"
            + (_document.FilePath is null ? string.Empty : " - " + Path.GetFileName(_document.FilePath));
    }

    private void SetStatus(string message) => StatusText.Text = message;

    private void AddNode() => _document.AddNode(DialogueModel.NextNodeId(_document.Script));

    // ---- 命令栏 --------------------------------------------------------------

    private void OnNew(object sender, RoutedEventArgs e) => Execute(EditorCommandId.FileNew);

    private void OnOpen(object sender, RoutedEventArgs e) => Execute(EditorCommandId.FileOpen);

    private void OnSave(object sender, RoutedEventArgs e) => Execute(EditorCommandId.FileSave);

    private void OnSaveAs(object sender, RoutedEventArgs e) => Execute(EditorCommandId.FileSaveAs);

    private void OnValidate(object sender, RoutedEventArgs e) => Execute(EditorCommandId.FileValidate);

    private void OnUndo(object sender, RoutedEventArgs e) => Execute(EditorCommandId.EditUndo);

    private void OnRedo(object sender, RoutedEventArgs e) => Execute(EditorCommandId.EditRedo);

    // ---- 快捷键 --------------------------------------------------------------

    private void OnAcceleratorNew(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        Execute(EditorCommandId.FileNew);
    }

    private void OnAcceleratorOpen(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        Execute(EditorCommandId.FileOpen);
    }

    private void OnAcceleratorSave(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        Execute(EditorCommandId.FileSave);
    }

    private void OnAcceleratorSaveAs(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        Execute(EditorCommandId.FileSaveAs);
    }

    private void OnAcceleratorValidate(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        Execute(EditorCommandId.FileValidate);
    }

    private void OnAcceleratorUndo(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        Execute(EditorCommandId.EditUndo);
    }

    private void OnAcceleratorRedo(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        Execute(EditorCommandId.EditRedo);
    }

    // ---- 文件 ----------------------------------------------------------------

    private async Task OpenFileAsync()
    {
        var picker = new FileOpenPicker();
        picker.FileTypeFilter.Add(".cdlg");
        picker.FileTypeFilter.Add(".txt");
        InitializePicker(picker);

        StorageFile? file = await picker.PickSingleFileAsync();
        if (file is null)
        {
            return;
        }

        string text;
        try
        {
            text = await ReadAllTextAsync(file);
        }
        catch (Exception ex)
        {
            SetStatus($"读取失败：{ex.Message}");
            return;
        }

        ParseResult result = CdlgFormat.Parse(text);
        if (result.Scripts.Count == 0)
        {
            SetStatus("这个文件里没有 dialogue 块。");
            return;
        }

        _document.Load(result.Scripts, file.Path);
        BindDocument();

        if (result.Issues.Count > 0)
        {
            SetStatus($"已载入，但解析报了 {result.Issues.Count} 个问题（按 F5 看详情）。");
        }
        else
        {
            SetStatus($"已载入 {file.Path}");
        }
    }

    /// <summary>
    /// 直接按磁盘路径打开（命令行传参、双击文件时）。与
    /// <see cref="OpenFileAsync"/> 共用解析与载入流程。
    /// </summary>
    public async Task OpenPathAsync(string path)
    {
        string text;
        try
        {
            text = await File.ReadAllTextAsync(path);
        }
        catch (Exception ex)
        {
            SetStatus($"读取失败：{ex.Message}");
            return;
        }

        ParseResult result = CdlgFormat.Parse(text);
        if (result.Scripts.Count == 0)
        {
            SetStatus("这个文件里没有 dialogue 块。");
            return;
        }

        _document.Load(result.Scripts, path);
        BindDocument();

        SetStatus(result.Issues.Count > 0
            ? $"已载入，但解析报了 {result.Issues.Count} 个问题（按 F5 看详情）。"
            : $"已载入 {path}");
    }

    private async Task SaveFileAsync(bool saveAs)
    {
        StorageFile? file = null;
        if (!saveAs && _document.FilePath is not null)
        {
            try
            {
                file = await StorageFile.GetFileFromPathAsync(_document.FilePath);
            }
            catch (Exception)
            {
                // 文件没了（被删了 / 移动了）：退回另存为。
                file = null;
            }
        }

        if (file is null)
        {
            var picker = new FileSavePicker
            {
                SuggestedFileName = _document.Script.Id + ".cdlg",
            };
            picker.FileTypeChoices.Add("CDLG 对话脚本", new[] { ".cdlg" });
            InitializePicker(picker);

            file = await picker.PickSaveFileAsync();
            if (file is null)
            {
                return;
            }
        }

        string text = CdlgFormat.Serialize(_document.Scripts);
        try
        {
            byte[] bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(text);
            using (Stream stream = await file.OpenStreamForWriteAsync())
            {
                stream.SetLength(0);
                await stream.WriteAsync(bytes);
                await stream.FlushAsync();
            }
        }
        catch (Exception ex)
        {
            SetStatus($"保存失败：{ex.Message}");
            return;
        }

        _document.FilePath = file.Path;
        _document.MarkSaved();
        SetStatus($"已保存 {file.Path}");
        OnDocumentChanged();
    }

    private static async Task<string> ReadAllTextAsync(StorageFile file)
    {
        using Stream stream = await file.OpenStreamForReadAsync();
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory);
        byte[] bytes = memory.ToArray();
        string text = new UTF8Encoding(false).GetString(bytes);
        return text.TrimStart('\uFEFF');
    }

    private void InitializePicker(object picker)
    {
        // 非打包应用没有隐式的窗口归属，文件选择器必须显式拿到窗口句柄，
        // 否则它弹到没有所有者的地方。
        try
        {
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        }
        catch (Exception)
        {
            // 拿不到句柄也不致命：选择器仍然能用，只是归属不对。
        }
    }

    // ---- 对话框 --------------------------------------------------------------

    private async Task<bool> ConfirmDiscardAsync(string message)
    {
        if (!_document.IsDirty)
        {
            return true;
        }
        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = "还有未保存的改动",
            Content = message + "要继续吗？",
            PrimaryButtonText = "继续",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
        };
        ContentDialogResult result = await dialog.ShowAsync();
        return result == ContentDialogResult.Primary;
    }

    private void ShowValidation()
    {
        IReadOnlyList<string> problems = _document.Validate();
        if (problems.Count == 0)
        {
            SetStatus("校验通过：没有发现问题。");
            return;
        }

        var text = new StringBuilder();
        text.Append($"校验发现 {problems.Count} 个问题：");
        foreach (string problem in problems)
        {
            text.Append('\n').Append("· ").Append(problem);
        }
        SetStatus(text.ToString());

        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = "校验报告",
            Content = new ScrollViewer
            {
                MaxHeight = 420,
                Content = new TextBlock
                {
                    Text = text.ToString(),
                    TextWrapping = TextWrapping.Wrap,
                },
            },
            CloseButtonText = "关闭",
        };
        _ = dialog.ShowAsync();
    }

    private async Task ShowAboutAsync()
    {
        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = "关于 VisualDialoguer",
            Content = "CDLG 对话脚本可视化编辑器。\n\n"
                + "读写 Eraphanis 工程 libs/cdlg 解析器认得的 .cdlg 脚本："
                + "对话结构、指令编排、流程图、撤销重做、复制粘贴与校验。",
            CloseButtonText = "关闭",
        };
        await dialog.ShowAsync();
    }

    private void OnGraphNodeClicked(object? sender, string nodeId)
    {
        // 选中项写入触发 Changed，窗口据此重画；这里只负责切到「对话结构」页。
        _document.SelectedNodeId = nodeId;
        _document.SelectedInstructionIndex = -1;
        Tabs.SelectedIndex = 0;
    }
}
