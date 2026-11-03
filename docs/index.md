# CDLG 对话脚本语言 · 完全参考手册

> **Conversations Dialogue Script** —— Eraphanis 工程 `libs/cdlg` 使用的对话脚本语言,以及它的可视化编辑器 **VisualDialoguer** 的完整帮助文档。

本手册面向脚本作者、编辑器使用者与想要理解 CDLG 工作原理的开发者。所有语法、字段与校验规则均与 `VisualDialoguer.Core` 的实现一一对应,不凭空发挥。

## 章节导航

| # | 章节 | 内容 |
| --- | --- | --- |
| 01 | [简介](01-intro.md) | CDLG 是什么、设计目标、与 Eraphanis 游戏工程的关系 |
| 02 | [快速开始](02-quickstart.md) | 十分钟写出第一个对话脚本,并用编辑器打开、保存、运行 |
| 03 | [格式与语法](03-format.md) | 完整语法参考:块结构、三级缩进、注释、转义、分隔符 |
| 04 | [指令类型与字段](04-instructions.md) | 8 种指令的语法、字段、示例与校验规则 |
| 05 | [节点与入口](05-nodes.md) | 节点 id 规则、入口节点、goto / choice 引用语义 |
| 06 | [编辑器操作指南](06-editor.md) | 界面布局、工作台三栏、筛选、快捷键、校验、流程图 |
| 07 | [原理](07-internals.md) | 解析、序列化、校验、撤销重做、流程图渲染的实现原理 |
| 08 | [常见问题](08-faq.md) | 兼容性、已知限制、排错 |

## 一句话速览

```
dialogue opening
  entry start
  node start
    speaker 旅店老板
    line 欢迎来到云雾镇,旅行者。
    choice "打听传闻" -> rumor
    choice "直接出发" -> leave
  end
  node rumor
    line 据说北边的古堡最近闹鬼。
    goto leave
  end
  node leave
    line 一路小心。
    end
end
```

`dialogue` 定义一个脚本块,`node` 定义一个节点(一串顺序执行的指令),`entry` 指明入口节点,`end` 结束块。编辑器读得懂、改得动、存得回这样的文本;Eraphanis 的 `libs/cdlg` 解析器与 Session 运行时也能直接加载编辑器写出的文件。

## 文档生成与导出

本手册的 Markdown 源文件位于仓库 `docs/` 目录。可以用生成器一键导出为静态网站:

```
python tools/build_docs.py
```

产物为单文件自包含 HTML(内嵌样式与导航),可用浏览器直接打开,也可以部署到任意静态托管。
