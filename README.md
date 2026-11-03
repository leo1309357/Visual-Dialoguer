# VisualDialoguer

CDLG 对话脚本可视化编辑器 —— **C# / WinUI 3** 独立桌面程序。

它是 Eraphanis 工程 `libs/cdlg` 对话脚本的可视化编辑器：读懂对话结构、
编排指令、看流程图、撤销重做、复制粘贴、校验，然后存回 `.cdlg` 脚本。
写出的文件能被 Eraphanis 的 `libs/cdlg` 解析器与 Session 运行时直接加载。

> 这个项目从 Eraphanis 仓库里独立出来了，不再与游戏工程共享任何构建、
> 依赖或源码目录。旧的 C/C++ + Dear ImGui 实现已经废弃。

## 架构

```
VisualDialoguer/
├── VisualDialoguer.sln
├── Directory.Build.props
├── samples/
│   └── sample.cdlg                # 与 cdlg 解析器兼容的示例脚本
├── src/
│   ├── VisualDialoguer.Core/      # 模型 / 序列化 / 编辑状态（不依赖 UI）
│   │   ├── Model/
│   │   │   ├── InstKind.cs        # 指令类型与字段表
│   │   │   ├── Instruction.cs     # 一条指令
│   │   │   ├── DialogueNode.cs    # 一个节点
│   │   │   ├── DialogueScript.cs  # 一个 dialogue 块
│   │   │   └── DialogueModel.cs   # 查找 / 增删改移 / 结构校验
│   │   ├── Format/
│   │   │   ├── CdlgFormat.cs      # 转义、序列化、解析
│   │   │   └── FormatIssue.cs     # 解析错误（行号 + 消息）
│   │   └── Editing/
│   │       ├── EditorCommandId.cs # 命令 id（与旧实现同号）
│   │       └── EditorDocument.cs  # 状态层：撤销/重做、两级剪贴板、脏标记
│   └── VisualDialoguer.App/       # WinUI 3 界面
│       ├── App.xaml(.cs)
│       ├── MainWindow.xaml(.cs)   # 命令栏 + 页签 + 状态栏 + 文件读写
│       └── Views/
│           ├── EditorPageBase.cs
│           ├── StructurePage.xaml(.cs)    # 对话结构
│           ├── ArrangePage.xaml(.cs)      # 指令编排
│           ├── GraphPage.xaml(.cs)        # 流程图
│           └── InstructionEditorPanel.xaml(.cs)  # 属性面板
└── tests/
    └── VisualDialoguer.Core.Tests/   # xUnit：格式往返、模型校验、撤销重做
```

- `VisualDialoguer.Core` **不引用任何 UI 框架**：模型、序列化与编辑状态全是
  纯 C#，所以能单独跑单元测试。
- `VisualDialoguer.App` 只做界面与文件对话框，把命令翻译成 Core 的调用。

## 构建与运行

需要 .NET SDK 8 或更高（Windows 10 1809 及以上 / Windows 11）。

```powershell
dotnet build VisualDialoguer.sln
dotnet test  tests\VisualDialoguer.Core.Tests\VisualDialoguer.Core.Tests.csproj
dotnet run --project src\VisualDialoguer.App\VisualDialoguer.App.csproj
```

### 发布单文件 portable exe

```powershell
dotnet publish src\VisualDialoguer.App\VisualDialoguer.App.csproj `
  -c Release -r win-x64 --self-contained true
```

产物为**单个 `VisualDialoguer.exe`**（约 83 MB），位于
`src\VisualDialoguer.App\bin\Release\net8.0-windows10.0.19041.0\win-x64\publish\`。
拷到任何 Windows 10 1809+ / Windows 11 x64 机器上双击即可运行——不需要
预装 .NET 运行时、不需要 MSIX 安装、不需要签名。.NET 运行时、Windows App
SDK 引导器与 WinUI XAML 原生库全部自解压打包在 exe 内
（`PublishSingleFile` + `IncludeNativeLibrariesForSelfExtract` + 压缩）。

目标框架为 `net8.0-windows10.0.19041.0`（Windows 10 2004 SDK），
最低运行版本 `TargetPlatformMinVersion=10.0.17763.0`（Windows 10 1809），
即 Win10 SDK 全系列兼容。卫星资源只保留 `zh-CN`，其余语言目录已剔除。

## 界面与功能

顶部命令栏 + 三个页签：

- **对话结构**：左边节点表（入口节点带 `★`），中间当前节点的指令表，右边
  属性面板按指令类型显示字段。新增节点 / 删除节点 / 设为入口；新增 / 删除 /
  上移 / 下移指令；切换指令类型时无关字段自动清空。
- **指令编排**：隐藏节点表，指令表与属性面板占满宽度——集中编排一个节点里
  的指令时用。
- **流程图**：节点按顺序排在规则网格上，`choice` / `goto` 画出带箭头的连
  线，选项边标注选项文本。点节点框跳回「对话结构」并选中该节点。

底部状态栏显示 `节点 N · 指令 N · 撤销 N · 重做 N`，标题栏在有未保存改动时
显示 `*`。

通用能力：

- **撤销 / 重做**：命令类操作（节点增删、设入口、指令增删移动、类型切换、
  粘贴、剪切）在改动前自动入栈；属性字段在获得焦点时取快照、失焦且确有改
  动才提交一条撤销记录（逐键入栈不做）。栈容量 50，新建 / 打开文件时清空。
- **复制 / 剪切 / 粘贴**：指令级可跨节点追加到当前节点尾部；节点级整体复制
  粘贴，新节点自动以 `-copy` 去重（冲突时 `-copy-2`、`-copy-3`…）。
- **校验**：入口节点存在、节点 id 不重复、节点非空、`goto` / `choice` 的目标
  节点存在；另外提示 `speaker` 说话人、`line` 台词、`choice` 选项标签、
  `set` 标志与值、`clear` 标志名、`emit` 信号 id 为空的问题。

## 主题与视觉规范

界面主题叫「Loma」，定义在
`src/VisualDialoguer.App/Themes/Paperstage.xaml`：日夜两套 `ThemeDictionaries`
＋一组与主题无关的尺度令牌。设计语言是轻拟物化的 Windows 11 Fluent：
柔和的高光与双层柔影、统一的圆角与发丝边、克制的材质差异。

**纸叠层次**（自下而上）：案垫 `PsDesk` → 稿纸 `PsPaper` → 浮起卡
`PsCard`（`PsCardFace` 渐变 + `PsEdge` 发丝边 + 顶缘 `PsSheenLine` 高光 +
`PsShadowAmbient`/`PsShadow` 双层柔影）→ 下沉井口 `PsWell`（`PsWellFace`
＋顶部内阴影）。列表行首的「书脊」用语义色，入口节点为签名色「批注朱砂」
`PsCinnabar`。

**组件样式**（都在主题文件里，页面只用 `Style` 引用，不重复写属性）：

| 样式 | 用途 |
| --- | --- |
| `PsCard` | 主界面所有卡片底座，替代原先每处手写的背景/描边/圆角三件套 |
| `PsWell` | 属性面板的字段区，顶部压暗、底部受光，读作「凹陷」 |
| `PsIconButton` | 工具栏图标钮：无边框，悬停浮出中性底，四态写在模板里 |
| `PsSectionTitle` / `PsCaption` | 卡片标题 / 次要说明文字 |
| `AccentButtonStyle` | 主操作按钮，用覆写框架 `AccentButton*` 键的方式取朱砂实底 |

**状态覆盖**：按钮、输入框、下拉框、列表项、命令栏按钮、页签均由覆写的
框架语义键驱动（hover / pressed / disabled / selected / focus）；
`PsIconButton` 因为要「无边框 + 中性悬停」，四态在自定义模板里显式写出。
悬停用中性暖灰、选中用朱砂淡染，两者刻意不同色，鼠标划过与选中不会混淆。
焦点框为朱砂，由 `SystemControlFocusVisualPrimaryBrush` 提供。

**校验**：`python check_theme.py` 会检查日夜两套键是否一一对应、XAML/C# 里
引用的键是否都已定义，并按 WCAG AA 核算关键「前景/背景」对的对比度
（正文与说明文字 4.5:1、焦点框与流程图牵线 3:1、8 种指令徽章字 4.5:1）。
改配色后跑一遍，低于阈值会直接失败。

## 快捷键

| 按键 | 动作 |
| --- | --- |
| `Ctrl+N` | 新建空白脚本 |
| `Ctrl+O` | 打开 `.cdlg` 文件 |
| `Ctrl+S` | 保存 |
| `Ctrl+Shift+S` | 另存为 |
| `F5` | 校验当前脚本 |
| `Ctrl+Z` / `Ctrl+Y` | 撤销 / 重做 |

## 指令类型

与 cdlg 语法一一对应，类型顺序固定：

| 类型 | 字段 | 生成语法 |
| --- | --- | --- |
| 说话人 speaker | 说话人 | `speaker <name>` |
| 台词 line | 文本 | `line <text>` |
| 选项 choice | 选项文本、目标节点 | `choice <label> -> <node>` |
| 跳转 goto | 目标节点 | `goto <node>` |
| 置标志 set | 标志键、标志值 | `set <flag> <value>` |
| 清标志 clear | 标志键 | `clear <flag>` |
| 发信号 emit | 信号 ID、信号载荷 | `emit <signal> [payload]` |
| 结束 end | — | `end` |

## 兼容性

- 缩进规范：`dialogue` / 顶层 `end` 缩进 0；`entry`、`node`、节点块 `end`
  缩进 2；指令缩进 4。
- 选项分隔符 `|` 在序列化时归一化为 `->`；转义序列 `\n \t \r \\ \"` 支持双
  向往返，未知的 `\x` 保留反斜杠。
- 解析宽容：BOM、`#` 注释、空行、`CRLF` 都能吃。
- 结构校验与 `libs/cdlg/src/Parser.cpp` 的规则一致。

## 已知限制

- 多个 `dialogue` 块只编辑首个：模型保留其余块，保存时原样写回，界面不提供
  切换入口。
- 流程图是按节点顺序算出来的规则网格，不支持手动拖动布局。
- 指令参数的合法性只在「校验」时统一检查，没有逐字段即时提示。
