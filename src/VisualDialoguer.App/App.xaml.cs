/*
 * Copyright (c) 2026 Leo Jeang. All Rights Reserved.
 *
 * This is proprietary software. Unauthorized copying, modification,
 * or distribution is strictly prohibited.
 */

using Microsoft.UI.Xaml;
using Windows.UI.ViewManagement;

namespace VisualDialoguer.App;

/// <summary>
/// 应用入口。非打包（unpackaged）应用没有 XAML 生成的启动代码，
/// 所以窗口在这里手工创建并激活。
///
/// 主题跟随系统：WinUI 3 的内容区不会自动跟随系统日/夜设置（默认落在
/// 深色），所以这里显式读一次 <see cref="UISettings"/> 把
/// <see cref="Application.RequestedTheme"/> 对齐，并订阅设置变化做实时切换——
/// 用户在系统里换主题，窗口跟着换，不用重启。
/// </summary>
public partial class App : Application
{
    private Window? _window;
    private readonly UISettings _uiSettings = new();

    public App()
    {
        InitializeComponent();
        RequestedTheme = PreferredTheme();
        _uiSettings.ColorValuesChanged += OnSystemColorsChanged;
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        _window.Activate();

        // 命令行带一个 .cdlg / .txt 路径时直接打开它：双击文件、从资源管理器
        // 拖进来都走这条路，也便于截图验证带内容的界面。
        // 非打包应用的 LaunchActivatedEventArgs.Arguments 可能为空，
        // 直接读进程命令行更稳（第 0 项是 exe 自己，跳过）。
        string arg = Environment.GetCommandLineArgs()
            .Skip(1)
            .FirstOrDefault(candidate => candidate.EndsWith(".cdlg", StringComparison.OrdinalIgnoreCase)
                                         || candidate.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
            ?? string.Empty;
        if (arg.Length > 0 && File.Exists(arg) && _window is MainWindow main)
        {
            _ = main.OpenPathAsync(arg);
        }
    }

    /// <summary>
    /// 当前应使用的主题：环境变量 <c>VD_THEME</c>（Light / Dark）优先，
    /// 否则跟随系统日/夜设置。
    ///
    /// 环境变量的口子有两个用处：验证时能强制截某一档配色的图；用户想
    /// 让编辑器固定在某一档、不跟系统走时也有个开关。
    /// </summary>
    private static ApplicationTheme PreferredTheme()
    {
        string? forced = Environment.GetEnvironmentVariable("VD_THEME");
        if (string.Equals(forced, "Light", StringComparison.OrdinalIgnoreCase))
        {
            return ApplicationTheme.Light;
        }
        if (string.Equals(forced, "Dark", StringComparison.OrdinalIgnoreCase))
        {
            return ApplicationTheme.Dark;
        }
        return SystemPrefersDark() ? ApplicationTheme.Dark : ApplicationTheme.Light;
    }

    /// <summary>
    /// 系统是否偏好深色：看系统窗口底色，接近黑即深色模式。
    /// 这是 WinUI 里判断系统日/夜的惯用做法。
    /// </summary>
    private static bool SystemPrefersDark()
    {
        var settings = new UISettings();
        Windows.UI.Color background = settings.GetColorValue(UIColorType.Background);
        // 深色模式的系统底色亮度很低；用亮度阈值比写死某个色值稳。
        double luminance = 0.299 * background.R + 0.587 * background.G + 0.114 * background.B;
        return luminance < 128;
    }

    private void OnSystemColorsChanged(UISettings sender, object args)
    {
        // 设置变化的回调不在 UI 线程上：切主题要回 UI 线程。窗口还没建起来
        // 时（启动早期）不必处理——构造函数已经对齐过一次。
        _window?.DispatcherQueue.TryEnqueue(() =>
        {
            ApplicationTheme preferred = PreferredTheme();
            if (RequestedTheme == preferred)
            {
                return;
            }
            RequestedTheme = preferred;
            Views.Theme.RaiseChanged();
        });
    }
}
