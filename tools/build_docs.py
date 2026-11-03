#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
CDLG 帮助文档生成器
===================

把仓库 docs/ 下的 Markdown 章节打包成**单文件自包含 HTML** 静态网站:
- 无第三方依赖(仅 Python 标准库)
- 产物内嵌样式与导航 JS,浏览器直接打开即可浏览
- 同一个产物既可用于部署静态站点,也可嵌入 VisualDialoguer 编辑器的帮助面板

用法:
    python tools/build_docs.py [--out <输出路径>] [--embed <编辑器嵌入副本路径>]

默认输出:docs-site/index.html
若传入 --embed,再把同一份 HTML 复制到该路径(编辑器把它作为嵌入资源打包)。

支持的 Markdown 语法(与 docs/ 各章节实际用到的保持一致):
    标题 # ## ###、段落、表格、无序/有序列表、围栏代码块(```)、
    行内代码、粗体、引用、链接([text](url),相对章节链接自动映射为页内锚点)。
其余内容一律按纯文本转义输出,不会出错,只是不渲染。
"""

from __future__ import annotations

import argparse
import html
import re
import shutil
import sys
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parent.parent
DOCS_DIR = REPO_ROOT / "docs"
DEFAULT_OUT = REPO_ROOT / "docs-site" / "index.html"
# 编辑器嵌入副本:同一份 HTML 同时作为 VisualDialoguer 的帮助资源打包。
DEFAULT_EMBED = REPO_ROOT / "src" / "VisualDialoguer.App" / "Assets" / "Docs" / "cdlg-docs.html"

TITLE = "CDLG 对话脚本语言 · 完全参考手册"
SUBTITLE = "Conversations Dialogue Script — 格式 · 语法 · 字段 · 操作 · 原理"


# --------------------------------------------------------------------------
# Markdown 子集解析
# --------------------------------------------------------------------------

def _inline(text: str) -> str:
    """行内语法:行内代码、粗体、链接。顺序敏感:先保护代码,再处理其余。"""
    # 1) 行内代码先占位,防止其中内容被当作语法
    code_spans: list[str] = []

    def _protect_code(match: re.Match) -> str:
        code_spans.append(html.escape(match.group(1)))
        return f"\x00CODE{len(code_spans) - 1}\x00"

    text = re.sub(r"`([^`\n]+)`", _protect_code, text)

    # 2) 链接:[text](url) —— 相对 .md 链接映射为页内锚点
    def _link(match: re.Match) -> str:
        label, url = match.group(1), match.group(2)
        if url.startswith("http://") or url.startswith("https://"):
            target, cls = url, ""
        else:
            stem = re.sub(r"\.md$", "", url)
            target, cls = f"#ch-{stem}", " class=\"doc-link\""
        return f"<a href=\"{html.escape(target)}\"{cls}>{_inline(label)}</a>"

    text = re.sub(r"\[([^\]]+)\]\(([^)\s]+)\)", _link, text)

    # 3) 粗体
    text = re.sub(r"\*\*([^*\n]+)\*\*", r"<strong>\1</strong>", text)

    # 4) 还原行内代码
    def _restore_code(match: re.Match) -> str:
        return f"<code>{code_spans[int(match.group(1))]}</code>"

    return re.sub(r"\x00CODE(\d+)\x00", _restore_code, text)


def _table(lines: list[str]) -> str:
    """渲染一个 Markdown 表格(连续若干行,第二行为分隔行)。"""
    rows: list[list[str]] = []
    for line in lines:
        cells = [cell.strip() for cell in line.strip().strip("|").split("|")]
        rows.append(cells)
    header, body = rows[0], rows[1:]
    thead = "".join(f"<th>{_inline(c)}</th>" for c in header)
    tbody = ""
    for row in body:
        tds = "".join(f"<td>{_inline(c)}</td>" for c in row)
        tbody += f"<tr>{tds}</tr>"
    return f"<table><thead><tr>{thead}</tr></thead><tbody>{tbody}</tbody></table>"


def _list_block(lines: list[str], ordered: bool) -> str:
    """渲染一个列表块(以 - 或 1. 开头的连续行)。"""
    items: list[str] = []
    for line in lines:
        item = re.sub(r"^(\s*)(?:[-*]|\d+\.)\s+", "", line).strip()
        items.append(f"<li>{_inline(item)}</li>")
    tag = "ol" if ordered else "ul"
    return f"<{tag}>{''.join(items)}</{tag}>"


def markdown_to_html(source: str) -> str:
    """把一篇 Markdown 章节转成 <section> 内的 HTML。"""
    lines = source.splitlines()
    out: list[str] = []
    i, n = 0, len(lines)

    while i < n:
        line = lines[i]
        stripped = line.strip()

        # 围栏代码块
        if stripped.startswith("```"):
            lang = stripped[3:].strip()
            buf: list[str] = []
            i += 1
            while i < n and not lines[i].strip().startswith("```"):
                buf.append(lines[i])
                i += 1
            i += 1  # 跳过结束围栏
            label = f' class="lang-{html.escape(lang)}"' if lang else ""
            out.append(f"<pre{label}><code>{html.escape(chr(10).join(buf))}</code></pre>")
            continue

        # 标题
        heading = re.match(r"^(#{1,6})\s+(.*)$", stripped)
        if heading:
            level = len(heading.group(1))
            out.append(f"<h{level}>{_inline(heading.group(2))}</h{level}>")
            i += 1
            continue

        # 引用
        if stripped.startswith(">"):
            buf: list[str] = []
            while i < n and lines[i].strip().startswith(">"):
                buf.append(re.sub(r"^>\s?", "", lines[i]))
                i += 1
            out.append(f"<blockquote>{_inline(' '.join(x.strip() for x in buf))}</blockquote>")
            continue

        # 表格:第二行是分隔行(| --- |)
        if i + 1 < n and "|" in line and re.match(r"^\s*\|?[\s:|-]+\|?\s*$", lines[i + 1].strip()):
            buf = [lines[i]]
            i += 2
            while i < n and "|" in lines[i].strip():
                buf.append(lines[i])
                i += 1
            out.append(_table(buf))
            continue

        # 列表:无序/有序,收集连续项
        if re.match(r"^\s*[-*]\s+", line):
            buf = [line]
            i += 1
            while i < n and re.match(r"^\s*[-*]\s+", lines[i]):
                buf.append(lines[i])
                i += 1
            out.append(_list_block(buf, ordered=False))
            continue
        if re.match(r"^\s*\d+\.\s+", line):
            buf = [line]
            i += 1
            while i < n and re.match(r"^\s*\d+\.\s+", lines[i]):
                buf.append(lines[i])
                i += 1
            out.append(_list_block(buf, ordered=True))
            continue

        # 空行:段落分隔
        if not stripped:
            i += 1
            continue

        # 普通段落(合并到下一个空行)
        buf = [line.strip()]
        i += 1
        while i < n and lines[i].strip() and not re.match(r"^(#{1,6}\s|```|\s*[-*]\s|\s*\d+\.\s|>)", lines[i].strip()):
            buf.append(lines[i].strip())
            i += 1
        out.append(f"<p>{_inline(' '.join(buf))}</p>")

    return "\n".join(out)


# --------------------------------------------------------------------------
# 章节装载与目录
# --------------------------------------------------------------------------

def load_chapters() -> list[tuple[str, str, str]]:
    """返回 [(文件名去扩展, 章节标题, 正文 HTML)],按文件名排序。

    约定:index.md 排最前,其余按数字/字母前缀排序。
    """
    chapters: list[tuple[str, str, str]] = []
    for path in sorted(DOCS_DIR.glob("*.md"), key=lambda p: (p.name != "index.md", p.name)):
        text = path.read_text(encoding="utf-8")
        # 章节标题 = 第一个 # 标题
        title_match = re.search(r"^#\s+(.+)$", text, flags=re.MULTILINE)
        title = title_match.group(1).strip() if title_match else path.stem
        body = markdown_to_html(text)
        chapters.append((path.stem, title, body))
    return chapters


# --------------------------------------------------------------------------
# 页面骨架
# --------------------------------------------------------------------------

PAGE_CSS = """
:root {
  --paper: #f2efe9;
  --card: #faf8f4;
  --ink: #2e2a26;
  --ink-2: #5c564e;
  --ink-3: #8d867c;
  --accent: #b03a2e;
  --accent-soft: #e8cfca;
  --edge: #e2ddd3;
  --code-bg: #f7f4ee;
  --thread: #c9c2b6;
}
@media (prefers-color-scheme: dark) {
  :root {
    --paper: #1d1b18;
    --card: #26231f;
    --ink: #e8e2d8;
    --ink-2: #b3ab9e;
    --ink-3: #7d766b;
    --accent: #e08d80;
    --accent-soft: #4a2f2b;
    --edge: #3a352e;
    --code-bg: #2b2823;
    --thread: #57504a;
  }
}
* { box-sizing: border-box; }
html { scroll-behavior: smooth; }
body {
  margin: 0;
  background: var(--paper);
  color: var(--ink);
  font: 15px/1.75 "Segoe UI", "Microsoft YaHei", system-ui, sans-serif;
}
#layout { display: flex; min-height: 100vh; }
aside#toc {
  position: sticky; top: 0; align-self: flex-start;
  width: 260px; min-width: 260px; height: 100vh; overflow-y: auto;
  padding: 22px 14px;
  background: var(--card);
  border-right: 1px solid var(--edge);
}
#toc h1 { font-size: 17px; margin: 0 10px 4px; }
#toc .sub { font-size: 12px; color: var(--ink-3); margin: 0 10px 14px; line-height: 1.5; }
#toc nav a {
  display: block; padding: 7px 10px; margin: 2px 0;
  color: var(--ink-2); text-decoration: none;
  border-left: 3px solid transparent; border-radius: 0 8px 8px 0;
  font-size: 13.5px;
}
#toc nav a:hover { background: var(--edge); color: var(--ink); }
#toc nav a.active { border-left-color: var(--accent); color: var(--accent); font-weight: 600; }
main { flex: 1; min-width: 0; padding: 28px 44px 80px; max-width: 920px; }
main header { border-bottom: 1px solid var(--edge); padding-bottom: 14px; margin-bottom: 26px; }
main header h1 { font-size: 26px; margin: 0 0 4px; }
main header .sub { color: var(--ink-3); font-size: 13px; }
section.chapter { scroll-margin-top: 20px; }
section.chapter h2 { font-size: 21px; margin: 30px 0 12px; padding-bottom: 6px; border-bottom: 1px dashed var(--edge); }
section.chapter h3 { font-size: 17px; margin: 24px 0 8px; }
section.chapter h4 { font-size: 15px; margin: 18px 0 6px; }
section.chapter p { margin: 8px 0; }
section.chapter a { color: var(--accent); }
section.chapter blockquote {
  margin: 10px 0; padding: 10px 14px;
  background: var(--card); border-left: 4px solid var(--accent-soft);
  border-radius: 0 10px 10px 0; color: var(--ink-2);
}
section.chapter code {
  font-family: "Cascadia Code", Consolas, monospace;
  font-size: 13px; background: var(--code-bg);
  padding: 1px 5px; border-radius: 5px; border: 1px solid var(--edge);
}
section.chapter pre {
  background: var(--card); border: 1px solid var(--edge); border-radius: 12px;
  padding: 14px 16px; overflow-x: auto; margin: 12px 0;
}
section.chapter pre code { background: none; border: none; padding: 0; font-size: 13px; line-height: 1.6; }
section.chapter table {
  border-collapse: collapse; margin: 12px 0; width: 100%;
  font-size: 13.5px; background: var(--card);
  border: 1px solid var(--edge); border-radius: 10px; overflow: hidden;
}
section.chapter th, section.chapter td {
  text-align: left; padding: 7px 12px;
  border-bottom: 1px solid var(--edge); vertical-align: top;
}
section.chapter th { background: var(--edge); font-weight: 600; }
section.chapter tr:last-child td { border-bottom: none; }
section.chapter ul, section.chapter ol { margin: 8px 0; padding-left: 26px; }
section.chapter li { margin: 3px 0; }
#top-link {
  position: fixed; right: 22px; bottom: 22px;
  background: var(--accent); color: #fff; text-decoration: none;
  padding: 8px 14px; border-radius: 20px; font-size: 13px; opacity: .9;
}
#top-link:hover { opacity: 1; }
@media (max-width: 860px) {
  #layout { flex-direction: column; }
  aside#toc { position: static; width: auto; height: auto; border-right: none; border-bottom: 1px solid var(--edge); }
  main { padding: 20px; }
}
"""

PAGE_JS = """
(function () {
  var links = Array.prototype.slice.call(document.querySelectorAll('#toc nav a'));
  var sections = links.map(function (a) {
    return document.querySelector(a.getAttribute('href'));
  });
  function activate(id) {
    links.forEach(function (a) {
      a.classList.toggle('active', a.getAttribute('href') === id);
    });
  }
  var inView = 'ch-index';
  function onScroll() {
    var probe = window.scrollY + 120;
    var current = inView;
    sections.forEach(function (s, i) {
      if (s && s.offsetTop <= probe) current = links[i].getAttribute('href');
    });
    if (current !== inView) { inView = current; activate(current); }
  }
  window.addEventListener('scroll', onScroll, { passive: true });
  onScroll();
})();
"""


def build_page(chapters: list[tuple[str, str, str]]) -> str:
    toc_items = "".join(
        f'<a href="#ch-{stem}">{html.escape(title)}</a>' for stem, title, _ in chapters
    )
    sections = "\n".join(
        f'<section class="chapter" id="ch-{stem}">{body}</section>'
        for stem, _, body in chapters
    )
    return f"""<!DOCTYPE html>
<html lang="zh-CN">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>{html.escape(TITLE)}</title>
<style>{PAGE_CSS}</style>
</head>
<body>
<div id="layout">
<aside id="toc">
  <h1>{html.escape(TITLE)}</h1>
  <p class="sub">{html.escape(SUBTITLE)}</p>
  <nav>{toc_items}</nav>
</aside>
<main>
  <header>
    <h1>{html.escape(TITLE)}</h1>
    <div class="sub">{html.escape(SUBTITLE)}</div>
  </header>
  {sections}
</main>
</div>
<a id="top-link" href="#ch-index">回到顶部</a>
<script>{PAGE_JS}</script>
</body>
</html>
"""


# --------------------------------------------------------------------------
# 入口
# --------------------------------------------------------------------------

def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="CDLG 帮助文档静态站生成器")
    parser.add_argument("--out", type=Path, default=DEFAULT_OUT, help="输出 HTML 路径")
    parser.add_argument("--embed", type=Path, default=DEFAULT_EMBED,
                        help="同时复制一份到该路径(供编辑器作为嵌入资源打包),默认:%(default)s")
    args = parser.parse_args(argv)

    chapters = load_chapters()
    if not chapters:
        print(f"错误:在 {DOCS_DIR} 下没有找到任何 .md 章节。", file=sys.stderr)
        return 1

    page = build_page(chapters)
    args.out.parent.mkdir(parents=True, exist_ok=True)
    args.out.write_text(page, encoding="utf-8")
    print(f"已生成: {args.out} ({len(chapters)} 章,{len(page)} 字符)")

    if args.embed is not None:
        args.embed.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(args.out, args.embed)
        print(f"已复制嵌入副本: {args.embed}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
