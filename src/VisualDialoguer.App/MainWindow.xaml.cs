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
/// 主窗口：工具栏 + 统一工作台 + 状态栏。
/// 流程图作为覆盖层，Ctrl+F 切换。
/// </summary>
public sealed partial class MainWindow : Window
{
    private readonly EditorDocument _document = new();
    private bool _graphVisible;

    public MainWindow()
    {
        InitializeComponent();

        ApplyTitleBarTheme();
        if (Content is FrameworkElement root)
        {
            root.ActualThemeChanged += (_, _) => ApplyTitleBarTheme();
        }

        Activated += OnWindowActivated;
        GraphPage.NodeClicked += OnGraphNodeClicked;

        _document.Changed += RefreshAll;
        AppWindow.Closing += OnWindowClosing;

        _document.NewScript();
        BindDocument();
    }

    private bool _windowPlaced;

    private void OnWindowActivated(object sender, WindowActivatedEventArgs args)
    {
        if (_windowPlaced) return;
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

        // 关键：启动时把逻辑焦点交给一个真实控件，否则根 Grid（带
        // KeyboardAccelerators）会成为焦点元素，WinUI 会在鼠标旁弹出
        // 跟随光标的快捷键提示泡（"Ctrl+N"）。
        Workspace.Loaded += (_, _) =>
        {
            if (Workspace.FindName("NodeSearch") is Control focusable)
                focusable.Focus(FocusState.Programmatic);
        };

        // 下拉框聚焦时框架会画一圈框外的黑边，主题改不掉，在这里抹掉。
        if (Content is FrameworkElement content)
        {
            ComboBoxFocusRing.Suppress(content);
            content.Loaded += (_, _) => ComboBoxFocusRing.Suppress(content);
        }
    }

    private void ApplyTitleBarTheme()
    {
        try
        {
            if (!Microsoft.UI.Windowing.AppWindowTitleBar.IsCustomizationSupported()) return;

            Windows.UI.Color none = Windows.UI.Color.FromArgb(0, 0, 0, 0);
            Microsoft.UI.Windowing.AppWindowTitleBar bar = AppWindow.TitleBar;

            bar.BackgroundColor = Theme.Swatch("LomaBase", none);
            bar.ForegroundColor = Theme.Swatch("LomaInk", none);
            bar.InactiveBackgroundColor = Theme.Swatch("LomaBase", none);
            bar.InactiveForegroundColor = Theme.Swatch("LomaInkTertiary", none);
            bar.ButtonBackgroundColor = Theme.Swatch("LomaBase", none);
            bar.ButtonInactiveBackgroundColor = Theme.Swatch("LomaBase", none);
            bar.ButtonForegroundColor = Theme.Swatch("LomaInkSecondary", none);
            bar.ButtonInactiveForegroundColor = Theme.Swatch("LomaInkTertiary", none);
            bar.ButtonHoverBackgroundColor = Theme.Swatch("LomaSunken", none);
            bar.ButtonHoverForegroundColor = Theme.Swatch("LomaInk", none);
            bar.ButtonPressedBackgroundColor = Theme.Swatch("LomaEdge", none);
            bar.ButtonPressedForegroundColor = Theme.Swatch("LomaInk", none);
        }
        catch (Exception) { }
    }

    private async void OnWindowClosing(
        Microsoft.UI.Windowing.AppWindow sender,
        Microsoft.UI.Windowing.AppWindowClosingEventArgs args)
    {
        if (!_document.IsDirty) return;
        args.Cancel = true;
        if (await ConfirmDiscardAsync("退出会丢弃当前未保存的改动。"))
        {
            _document.MarkSaved();
            Close();
        }
    }

    private void BindDocument()
    {
        Workspace.Bind(_document);
        GraphPage.Bind(_document);
        OnDocumentChanged();
    }

    // ---- 命令中枢 ------------------------------------------------------------

    private async void Execute(EditorCommandId command)
    {
        switch (command)
        {
            case EditorCommandId.FileNew:
                if (!await ConfirmDiscardAsync("新建会丢弃当前未保存的改动。")) return;
                _document.NewScript();
                break;
            case EditorCommandId.FileOpen:
                if (!await ConfirmDiscardAsync("打开会丢弃当前未保存的改动。")) return;
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
            case EditorCommandId.HelpAbout:
                ToggleHelp();
                break;
            case EditorCommandId.EditUndo:
                if (!_document.Undo()) { SetStatus("没有可撤销的改动。"); return; }
                break;
            case EditorCommandId.EditRedo:
                if (!_document.Redo()) { SetStatus("没有可重做的改动。"); return; }
                break;
            default:
                return;
        }
    }

    private void RefreshAll()
    {
        Workspace.Refresh();
        GraphPage.Refresh();
        OnDocumentChanged();
    }

    private void OnDocumentChanged()
    {
        int nodeCount = _document.Script.Nodes.Count;
        int instructionCount = 0;
        foreach (DialogueNode node in _document.Script.Nodes)
            instructionCount += node.Instructions.Count;
        CountsText.Text = $"节点 {nodeCount} · 指令 {instructionCount} · 撤销 {_document.UndoDepth} · 重做 {_document.RedoDepth}";
        Title = (_document.IsDirty ? "* " : string.Empty)
            + "VisualDialoguer"
            + (_document.FilePath is null ? string.Empty : " - " + Path.GetFileName(_document.FilePath));
    }

    private void SetStatus(string message) => StatusText.Text = message;

    // ---- 工具栏按钮 ----------------------------------------------------------

    private void OnNew(object sender, RoutedEventArgs e) => Execute(EditorCommandId.FileNew);
    private void OnOpen(object sender, RoutedEventArgs e) => Execute(EditorCommandId.FileOpen);
    private void OnSave(object sender, RoutedEventArgs e) => Execute(EditorCommandId.FileSave);
    private void OnSaveAs(object sender, RoutedEventArgs e) => Execute(EditorCommandId.FileSaveAs);
    private void OnValidate(object sender, RoutedEventArgs e) => Execute(EditorCommandId.FileValidate);
    private void OnUndo(object sender, RoutedEventArgs e) => Execute(EditorCommandId.EditUndo);
    private void OnRedo(object sender, RoutedEventArgs e) => Execute(EditorCommandId.EditRedo);

    private void OnToggleGraph(object sender, RoutedEventArgs e) => ToggleGraph();

    private void OnToggleTheme(object sender, RoutedEventArgs e)
    {
        FrameworkElement root = (FrameworkElement)Content;
        root.RequestedTheme = root.RequestedTheme == ElementTheme.Light
            ? ElementTheme.Dark
            : ElementTheme.Light;

        // 流程图是代码里逐笔画的，不会随 ThemeResource 自动变色，
        // 必须通知它重画；若覆盖层可见就立即刷新。
        Theme.RaiseChanged();
        if (_graphVisible)
        {
            GraphPage.Refresh();
        }
    }

    private void ToggleGraph()
    {
        _graphVisible = !_graphVisible;
        GraphOverlay.Visibility = _graphVisible ? Visibility.Visible : Visibility.Collapsed;
        if (_graphVisible) GraphPage.Refresh();
    }

    // ---- 帮助 --------------------------------------------------------------

    private bool _helpLoaded;

    private void OnToggleHelp(object sender, RoutedEventArgs e) => ToggleHelp();

    private void ToggleHelp()
    {
        HelpOverlay.Visibility = HelpOverlay.Visibility == Visibility.Visible
            ? Visibility.Collapsed
            : Visibility.Visible;
        if (HelpOverlay.Visibility == Visibility.Visible)
        {
            EnsureHelpLoaded();
        }
    }

    /// <summary>
    /// 首次打开时把嵌入的 CDLG 参考手册 HTML 装入 WebView2。
    /// 之后再次打开直接复用已加载的页面。
    /// </summary>
    private async void EnsureHelpLoaded()
    {
        if (_helpLoaded)
        {
            return;
        }
        _helpLoaded = true;
        try
        {
            string html = ReadEmbeddedHelp();
            await HelpWebView.EnsureCoreWebView2Async();
            HelpWebView.NavigateToString(html);
        }
        catch (Exception ex)
        {
            SetStatus($"帮助文档加载失败:{ex.Message}");
        }
    }

    /// <summary>从嵌入资源读取帮助 HTML(生成器产物,与静态站同一份)。</summary>
    private static string ReadEmbeddedHelp()
    {
        const string resourceName = "VisualDialoguer.App.Assets.Docs.cdlg-docs.html";
        using Stream? stream = System.Reflection.Assembly.GetExecutingAssembly()
            .GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"找不到嵌入资源 {resourceName}");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    // ---- 快捷键 --------------------------------------------------------------

    private void OnAcceleratorNew(KeyboardAccelerator s, KeyboardAcceleratorInvokedEventArgs e) { e.Handled = true; Execute(EditorCommandId.FileNew); }
    private void OnAcceleratorOpen(KeyboardAccelerator s, KeyboardAcceleratorInvokedEventArgs e) { e.Handled = true; Execute(EditorCommandId.FileOpen); }
    private void OnAcceleratorSave(KeyboardAccelerator s, KeyboardAcceleratorInvokedEventArgs e) { e.Handled = true; Execute(EditorCommandId.FileSave); }
    private void OnAcceleratorSaveAs(KeyboardAccelerator s, KeyboardAcceleratorInvokedEventArgs e) { e.Handled = true; Execute(EditorCommandId.FileSaveAs); }
    private void OnAcceleratorValidate(KeyboardAccelerator s, KeyboardAcceleratorInvokedEventArgs e) { e.Handled = true; Execute(EditorCommandId.FileValidate); }
    private void OnAcceleratorUndo(KeyboardAccelerator s, KeyboardAcceleratorInvokedEventArgs e) { e.Handled = true; Execute(EditorCommandId.EditUndo); }
    private void OnAcceleratorRedo(KeyboardAccelerator s, KeyboardAcceleratorInvokedEventArgs e) { e.Handled = true; Execute(EditorCommandId.EditRedo); }
    private void OnAcceleratorToggleGraph(KeyboardAccelerator s, KeyboardAcceleratorInvokedEventArgs e) { e.Handled = true; ToggleGraph(); }
    private void OnAcceleratorHelp(KeyboardAccelerator s, KeyboardAcceleratorInvokedEventArgs e) { e.Handled = true; ToggleHelp(); }

    // ---- 文件 ----------------------------------------------------------------

    private async Task OpenFileAsync()
    {
        var picker = new FileOpenPicker();
        picker.FileTypeFilter.Add(".cdlg");
        picker.FileTypeFilter.Add(".txt");
        InitializePicker(picker);

        StorageFile? file = await picker.PickSingleFileAsync();
        if (file is null) return;

        string text;
        try { text = await ReadAllTextAsync(file); }
        catch (Exception ex) { SetStatus($"读取失败：{ex.Message}"); return; }

        ParseResult result = CdlgFormat.Parse(text);
        if (result.Scripts.Count == 0) { SetStatus("这个文件里没有 dialogue 块。"); return; }

        _document.Load(result.Scripts, file.Path);
        BindDocument();
        SetStatus(result.Issues.Count > 0
            ? $"已载入，但解析报了 {result.Issues.Count} 个问题（按 F5 看详情）。"
            : $"已载入 {file.Path}");
    }

    public async Task OpenPathAsync(string path)
    {
        string text;
        try { text = await File.ReadAllTextAsync(path); }
        catch (Exception ex) { SetStatus($"读取失败：{ex.Message}"); return; }

        ParseResult result = CdlgFormat.Parse(text);
        if (result.Scripts.Count == 0) { SetStatus("这个文件里没有 dialogue 块。"); return; }

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
            try { file = await StorageFile.GetFileFromPathAsync(_document.FilePath); }
            catch (Exception) { file = null; }
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
            if (file is null) return;
        }

        string text = CdlgFormat.Serialize(_document.Scripts);
        try
        {
            byte[] bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(text);
            using Stream stream = await file.OpenStreamForWriteAsync();
            stream.SetLength(0);
            await stream.WriteAsync(bytes);
            await stream.FlushAsync();
        }
        catch (Exception ex) { SetStatus($"保存失败：{ex.Message}"); return; }

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
        return new UTF8Encoding(false).GetString(bytes).TrimStart('\uFEFF');
    }

    private void InitializePicker(object picker)
    {
        try { InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this)); }
        catch (Exception) { }
    }

    // ---- 对话框 --------------------------------------------------------------

    private async Task<bool> ConfirmDiscardAsync(string message)
    {
        if (!_document.IsDirty) return true;
        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = "还有未保存的改动",
            Content = message + "要继续吗？",
            PrimaryButtonText = "继续",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    private void ShowValidation()
    {
        IReadOnlyList<string> problems = _document.Validate();
        if (problems.Count == 0) { SetStatus("校验通过：没有发现问题。"); return; }

        var text = new StringBuilder();
        text.Append($"校验发现 {problems.Count} 个问题：");
        foreach (string problem in problems) text.Append('\n').Append("· ").Append(problem);
        SetStatus(text.ToString());

        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = "校验报告",
            Content = new ScrollViewer
            {
                MaxHeight = 420,
                Content = new TextBlock { Text = text.ToString(), TextWrapping = TextWrapping.Wrap },
            },
            CloseButtonText = "关闭",
        };
        _ = dialog.ShowAsync();
    }

    private void OnGraphNodeClicked(object? sender, string nodeId)
    {
        _document.SelectedNodeId = nodeId;
        _document.SelectedInstructionIndex = -1;
        ToggleGraph(); // 关闭覆盖层，回到工作台
    }
}
