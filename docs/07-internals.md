# 07 · 原理

本章讲 VisualDialoguer 内部是如何工作的。源码按职责分三层:

```
VisualDialoguer.Core   模型 / 解析 / 序列化 / 编辑状态(不依赖 UI)
VisualDialoguer.Theme  Loma 主题库(独立 WinUI 类库)
VisualDialoguer.App    界面(主窗口 / 工作台 / 流程图 / 属性面板)
```

## 7.1 解析原理(CdlgFormat.Parse)

解析器把 `.cdlg` 文本变成内存模型(`List<DialogueScript>`),并按行收集解析问题。

### 预处理

1. 若开头是 U+FEFF(BOM),**只跳这一个字符**(U+FEFF 是一个字符不是三个字节,跳三个会把正文头两个字吃掉);
2. 把 `\r\n` 与单独 `\r` 全部归一化为 `\n`,再按 `\n` 切行。

### 逐行切分

每一行计算两个数:

- `whitespaceCount`:前导空白字符数(用来切掉缩进拿到正文);
- `indentWidth`:**加权宽度**——空格 +1,Tab +2。这是为了把 Tab 正确折算进层级(一级缩进 = 2 空格,一个 Tab 顶一级)。

然后:

- 去掉行尾空白;
- 正文为空或以 `#` 开头 → 跳过(注释与空行);
- 按加权宽度算层级:`0 → 层级 0`,`1~2 → 层级 1`,`3+ → 层级 2`。

### 状态机

解析器只维护两个状态:**当前脚本**(`current`)与**当前节点**(`currentNode`),按层级分派:

| 层级 | 命令 | 行为 |
| --- | --- | --- |
| 0 | `dialogue <id>` | 建脚本,加入列表,重置节点上下文 |
| 0 | `end` | 关闭当前脚本(清空两个状态) |
| 1 | `entry <id>` | 写 `EntryNodeId` |
| 1 | `node <id>` | 建节点并追加到当前脚本 |
| 1 | `end` | 关闭当前节点 |
| 2 | 8 种指令 | 追加指令(命令名由首个空白切出,参数 Trim) |

参数解析细节:

- `speaker` / `line`:整段参数 `Unescape`;
- `choice`:先找 `->`,再找 `|`,切出标签与目标,两边 Trim,标签与目标都非空才有效;
- `set`:首词拆键,剩余为值;
- `emit`:首词拆信号 id,剩余为可选 payload;
- 每条指令都做**必填检查**,缺了就记一条 `FormatIssue(行号, 消息)`。

### 宽容但认得同一套语法

解析器对**空白形态**宽容(BOM、CRLF、Tab、空行、注释),但对**结构**严格(层级错、命令错都报行号)。这样设计让"人写的"和"编辑器写的"文件都能读进来,同时坏结构不会静默产生错误模型。

## 7.2 序列化原理(CdlgFormat.Serialize)

序列化是解析的逆过程,逐脚本、逐节点、逐指令写出,严格对齐解析器约定:

- 脚本 id 为空 → `dialogue untitled`;
- `entry` 为空 → 不输出;
- 节点:缩进 2 的 `node <id>`;
- 指令:缩进 4;`speaker` / `line` / `choice` 标签 / `set` 值 / `emit` payload 走 `Escape`;
- `choice` 分隔符**归一化为 ` -> `**;
- 节点块 `end` 缩进 2,脚本块 `end` 缩进 0;
- 多脚本块依次写出。

编码:UTF-8 无 BOM + LF,由 App 层 `SaveFileAsync` 负责写入(`new UTF8Encoding(false)`)。

## 7.3 校验原理(DialogueModel.Validate)

`Validate` 返回字符串列表,空列表 = 通过。检查顺序:

1. **入口**:缺失 → 报;指向不存在 → 报;
2. **标识符**:脚本 id、入口 id、每个节点 id、goto/choice 目标、set/clear 标志键、emit 信号 id 都过 `CheckIdentifier`(非空白、不含空白、不以 `#` 开头;空值不报,由专项检查管);
3. **id 唯一**:节点两两比对,每对重复报一次;
4. **空壳**:节点指令数为 0 → 报;
5. **引用**:goto/choice 目标空或不存在 → 报;
6. **必填字段**:speaker 缺说话人、line 缺文本、choice 标签空、choice 标签含 `->`/`|`、set 缺键值、clear 缺标志名、emit 缺信号 id → 各报各的。

校验**不改模型**,是纯只读检查;编辑器在 F5 时调用,保存前不会自动拦截(保存始终允许,校验是提示性质的)。

## 7.4 编辑状态原理(EditorDocument)

`EditorDocument` 是编辑器的状态层,**不碰任何 UI**;界面把命令翻译成它的调用,它通过 `Changed` 事件通知 UI 刷新。

### 脏标记是"比对出来的"

`IsDirty` 不是"改过就置位"的布尔,而是:

```
IsDirty = _savedText != CurrentText
CurrentText = CdlgFormat.Serialize(_scripts)
```

把当前模型序列化出来与上次保存(或新建/载入)的基准比对。好处:改完又撤销回去,内容与磁盘一致时**不显示未保存**,不会误报。

### 快照式撤销/重做

- 每次变更前 `BeginChange()`:把当前状态(整份深拷贝)压入撤销栈,清空重做栈;
- 容量 `MaxHistory = 50`,超限丢最旧的;
- `Undo`:当前状态压入重做栈,恢复栈顶快照;`Redo` 反之;
- 快照包含:全部脚本(每层 `Clone`)、选中节点、选中指令下标;
- 选整份快照而不是增量补丁:脚本量级小(几十个节点),简单、无"撤销回一半"的中间态。

### 属性编辑的特殊撤销策略

属性面板**逐键输入不入栈**(会让撤销栈被一串单字符淹没)。做法:

- 字段获得焦点时 `CaptureState()` 取快照;
- 失焦且确有改动时,`PushUndoState` 把该快照压栈。

### 引用同步

- `RenameNode(old,new)`:同步更新节点 id、所有 `TargetNode`、`EntryNodeId`、选中项;新 id 被占用则拒绝;
- `DeleteSelectedNode`:删除节点;若删的是入口,入口定义一并清空;
- `PasteNode`:副本 id = 原 id + `-copy`,冲突时 `-copy-2`…;引用不重定向;
- `SetSelectedInstructionKind`:切换类型时调用 `ClearIrrelevantFields` 清空无关字段。

### 多 dialogue 块

- 解析出的**全部块**都存进 `_scripts`;
- `Script` 属性返回 `_scripts[0]`(界面只编辑首个);
- `Scripts` 属性返回全部;保存时 `Serialize(_scripts)` **原样写回所有块**。

## 7.5 命令中枢

所有界面动作(工具栏、快捷键)汇入**一个命令枚举** `EditorCommandId`,数值与旧 Win32 `cdlg-editor` 的命令表一致:

| 区间 | 命令 |
| --- | --- |
| 1001–1006 | 文件:新建 / 打开 / 保存 / 另存为 / 校验 / 退出 |
| 1101–1103 | 节点:新增 / 删除 / 设为入口 |
| 1201–1204 | 指令:新增 / 删除 / 上移 / 下移 |
| 1301 | 帮助:关于 |
| 1401–1408 | 编辑:撤销 / 重做 / 复制剪切粘贴指令 / 复制剪切粘贴节点 |

`MainWindow.Execute(EditorCommandId)` 做分发;快捷键(`KeyboardAccelerator`)挂在根 Grid 上,回调里先 `e.Handled = true` 再分发。

## 7.6 流程图渲染原理(GraphPage)

流程图是**代码逐笔绘制**(不是 XAML 模板绑定),每次刷新清空画布重画。

### 布局

- 固定参数:卡宽 168、卡高 62、列间距 96、行间距 56、**3 列**;
- 节点按列表顺序填充:`列 = index % 3`,`行 = index / 3`;
- 画布尺寸:宽 = max(800, 3 × (卡宽+间距)),高 = max(480, 行数 × (卡高+间距));
- 空脚本也铺出案垫与网格,不是白屏。

### 绘制顺序(层次自下而上)

1. **案垫背景**(`LomaGraphDesk`)+ **网格**(`LomaGraphGrid`,32px 间距的细线);
2. **牵线**:遍历每个节点的指令,`choice` / `goto` 画连线——源卡右侧中点 → 目标卡左侧中点,1.5px 细线,终点张开三角箭头;目标不存在的边跳过;
3. **索引卡**:三层柔影(环境影 / 左上受光高光 / 关键影)→ 卡面(浮起渐变 + 发丝边)→ 书脊(入口用签名色,否则取节点第一条指令的语义色,空节点退回发丝边色)→ 标题与" N 条指令";
4. **连线标签**:`choice` 显示选项文本,`goto` 显示 `goto`;标签先 `Measure` 量出真实尺寸再居中,压在连线中点的小纸片(带柔影)上,长短标签都居中。

### 主题与重画

- 所有颜色经 `Theme.Brush("LomaXxx", fallback)` 从 Loma 主题读,**不写死颜色**;
- 代码画笔画刷是"重画那一刻"取的,主题切换后必须重画一次——`Theme.Changed += Refresh` 保证深/浅色切换立即生效;
- 主题键万一缺失,退回内置兜底色(透明 / 发丝灰 / 墨灰 / 纸色),保证图仍可读而不崩溃。

## 7.7 主题系统(Loma.Theme)

- Loma 是独立 WinUI 类库,资源字典 `Themes/Loma.xaml`;App 通过 `ms-appx:///Loma.Theme/Themes/Loma.xaml` 合并;
- 视图样式键:`LomaCard` / `LomaButton` / `LomaIconButton` / `LomaWell` / `LomaSectionTitle` / `LomaNodeRow` / `LomaInstructionRow` / `LomaPadCard` 等;
- 调色板键:`LomaBase` / `LomaInk`(系列)/ `LomaAccent` / `LomaSunken` / `LomaEdge` / `LomaShadow*` / `LomaGraphDesk` / `LomaGraphGrid` / `LomaThread` 等;
- 标题栏颜色也读 Loma(`Theme.Swatch`),深浅色切换时跟随;
- 主题库支持微交互动画(TranslateTransform + CubicEase / QuadraticEase)与桌面顶光渐变背景(`LomaDeskGradient`)。

## 7.8 文件读写

- 打开:`FileOpenPicker`(`.cdlg` / `.txt`)→ 读字节 → `new UTF8Encoding(false).GetString(bytes)`(兼容无/有 BOM)→ `TrimStart('\uFEFF')` → `Parse`;
- 保存:`Serialize` → UTF-8 无 BOM 字节 → 截断原文件 → 写入 → `MarkSaved`;
- 保存成功才更新 `FilePath` 与脏基准;失败则状态栏报错,不破坏原文件。
