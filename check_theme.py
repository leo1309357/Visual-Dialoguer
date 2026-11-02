"""校验「稿台」主题字典的键完整性。

三件事：
1. Light / Dark 两套字典定义的键必须完全一致（缺一侧，对应主题就走兜底色）；
2. 所有 XAML / C# 里引用的主题键必须真的被定义（写错键名不会编译报错，
   只会在运行时解析失败或静默降级）；
3. StaticResource 引用的颜色键必须在同一字典内存在。
"""
import re
import sys
from pathlib import Path

ROOT = Path(r"C:\personal\VisualDialoguer\src\VisualDialoguer.App")
THEME = ROOT / "Themes" / "Paperstage.xaml"

text = THEME.read_text(encoding="utf-8")

# ---- 按 ThemeDictionaries 切出 Light / Dark 两段 -------------------------
def section(marker: str) -> str:
    start = text.index(f'x:Key="{marker}"')
    end = text.index("</ResourceDictionary>", start)
    return text[start:end]

light = section("Light")
dark = section("Dark")

# ---- 1. 两套字典的键集合比对 -------------------------------------------
KEY_DEF = re.compile(r'x:Key="([^"]+)"')

light_keys = set(KEY_DEF.findall(light)) - {"Light"}
dark_keys = set(KEY_DEF.findall(dark)) - {"Dark"}

problems = []

only_light = light_keys - dark_keys
only_dark = dark_keys - light_keys
if only_light:
    problems.append(f"仅 Light 定义、Dark 缺失: {sorted(only_light)}")
if only_dark:
    problems.append(f"仅 Dark 定义、Light 缺失: {sorted(only_dark)}")

# 主题无关区（圆角、字号、模板）也要能被两侧引用，单独收集
theme_agnostic_start = text.index("<!-- ═══════════ 主题无关")
agnostic = set(KEY_DEF.findall(text[theme_agnostic_start:]))

defined = light_keys & dark_keys | agnostic

# ---- 2. 交叉引用：StaticResource 指向的颜色键是否都存在 -------------------
# 只检查主题字典内部的自引用；Color 键只在自己的字典里可见，故按侧校验。
for name, body in (("Light", light), ("Dark", dark)):
    for m in re.finditer(r'\{StaticResource ([^}]+)\}', body):
        key = m.group(1).strip()
        if key not in defined:
            problems.append(f"{name}: StaticResource 引用了未定义的键 '{key}'")

# ---- 3. 代码与 XAML 里用到的键是否都已定义 --------------------------------
USED = re.compile(r'(?:ThemeResource|StaticResource)\s+([A-Za-z][\w]*)')
CODE_KEYS = re.compile(r'(?:Theme\.(?:Brush|Color)\(\s*)"([\w]+)"|(?:Theme\.(?:Brush|Color)\(\s*)KindBrushKey')

referenced = set()
for path in list(ROOT.rglob("*.xaml")) + list(ROOT.rglob("*.cs")):
    # 只扫源码：构建产物目录里也有同名文件（含二进制 DLL），扫进去会报权限错
    if any(part in ("obj", "bin") for part in path.parts) or path == THEME:
        continue
    body = path.read_text(encoding="utf-8", errors="replace")
    for m in USED.finditer(body):
        referenced.add(m.group(1))
    for m in CODE_KEYS.finditer(body):
        if m.group(1):
            referenced.add(m.group(1))

# KindBrushKey 返回的键，从 Theme.cs 里抓字面量补齐
theme_cs = (ROOT / "Views" / "Theme.cs").read_text(encoding="utf-8")
referenced |= set(re.findall(r'=>\s*"(PsKind\w+)"', theme_cs))
referenced |= set(re.findall(r'"(PsKind\w+)"', theme_cs))

# 框架自带的语义键不在我们的字典里，属正常（由 XamlControlsResources 提供）
FRAMEWORK_KEYS = {"AccentButtonStyle"}
missing = {k for k in referenced
           if k not in defined and k not in FRAMEWORK_KEYS and not k.startswith("Xaml")}
if missing:
    problems.append(f"代码/XAML 引用了未定义的键: {sorted(missing)}")


# ---- 4. 对比度：文字 4.5:1、图形界面元素 3:1（WCAG 2.1 AA）----------------
def _rgb(hex_value: str):
    value = hex_value.lstrip("#")
    if len(value) == 8:          # 去掉 alpha，只算不透明部分
        value = value[2:]
    return tuple(int(value[i:i + 2], 16) for i in (0, 2, 4))


def _luminance(hex_value: str) -> float:
    def channel(c: int) -> float:
        c /= 255
        return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4

    r, g, b = (channel(c) for c in _rgb(hex_value))
    return 0.2126 * r + 0.7152 * g + 0.0722 * b


def contrast(a: str, b: str) -> float:
    la, lb = _luminance(a), _luminance(b)
    hi, lo = max(la, lb), min(la, lb)
    return (hi + 0.05) / (lo + 0.05)


def colors(block: str) -> dict:
    """键 -> 色值。画刷 PsInk 由 PsInkColor 定义，故两种写法都能查到。"""
    out = {}
    for m in re.finditer(r'<Color x:Key="(\w+)">(#[0-9A-Fa-f]+)</Color>', block):
        name = m.group(1)
        out[name] = m.group(2)
        if name.endswith("Color"):
            out.setdefault(name[:-5], m.group(2))
    return out


# (前景, 背景, 最低比值, 用途)
TEXT_PAIRS = [
    ("PsInk", "PsPaper", 4.5, "正文/卡片标题"),
    ("PsInk", "PsDesk", 4.5, "标题在案垫上"),
    ("PsInk", "PsPaperSunken", 4.5, "输入框正文"),
    ("PsInkSecondary", "PsPaper", 4.5, "次要文字"),
    ("PsInkSecondary", "PsDesk", 4.5, "状态栏说明"),
    ("PsInkSecondary", "PsGraphDesk", 4.5, "流程图标注重叠在案垫"),
    ("PsInkTertiary", "PsPaper", 4.5, "12px 说明文字"),
    ("PsInkTertiary", "PsDesk", 4.5, "未选中页签"),
    ("PsInkTertiary", "PsPaperRaisedTop", 4.5, "抬升卡顶部最亮处"),
    ("PsInkTertiary", "PsPaperSunken", 4.5, "占位符/禁用字"),
    ("PsCinnabar", "PsPaper", 4.5, "选中/强调文字"),
    ("PsCinnabar", "PsDesk", 4.5, "标题栏强调"),
    ("PsOnAccent", "PsCinnabar", 4.5, "主按钮文字"),
    ("PsOnAccent", "PsCinnabarHover", 4.5, "主按钮悬停"),
    ("PsOnAccent", "PsCinnabarPressed", 4.5, "主按钮按下"),
    # 图形界面元素：焦点框、牵线属于"必须看得见"的界面信息
    ("PsCinnabar", "PsPaper", 3.0, "焦点框"),
    ("PsCinnabar", "PsDesk", 3.0, "焦点框在案垫上"),
    ("PsThread", "PsGraphDesk", 3.0, "流程牵线"),
]

KINDS = ["Speaker", "Line", "Choice", "Goto", "Set", "Clear", "Emit", "End"]

print("\n对比度（AA：文字 4.5:1 / 界面元素 3:1）")
for name, block in (("Light", light), ("Dark", dark)):
    palette = colors(block)
    pairs = list(TEXT_PAIRS) + [
        (f"PsKind{k}", "PsBadgeInk", 0, "") for k in KINDS
    ]
    for fg, bg, minimum, usage in pairs:
        if fg not in palette or bg not in palette:
            problems.append(f"{name}: 对比度检查缺少色键 {fg} 或 {bg}")
            continue
        ratio = contrast(palette[fg], palette[bg])
        # 徽章是反查：底是语义色、字是 PsBadgeInk
        if minimum == 0:
            ratio = contrast(palette[bg], palette[fg])
            minimum = 4.5
            usage = f"徽章字在 {fg[6:] if fg.startswith('PsKind') else fg} 底上"
        flag = "OK " if ratio >= minimum else "LOW"
        if ratio < minimum:
            problems.append(f"{name}: {fg} on {bg} = {ratio:.2f} < {minimum}（{usage}）")
        if flag == "LOW" or minimum >= 4.5:
            print(f"  [{name}] {flag} {ratio:5.2f}  {fg} / {bg}   {usage}")

# ---- 输出 --------------------------------------------------------------
print(f"\nLight 键数: {len(light_keys)}  Dark 键数: {len(dark_keys)}  主题无关键数: {len(agnostic)}")
print(f"两侧共有(生效): {len(light_keys & dark_keys)}   被引用: {len(referenced)}")
if problems:
    print("\n发现问题:")
    for p in problems:
        print("  -", p)
    sys.exit(1)
print("\n校验通过：日夜两套键完全对应，所有引用键均已定义，对比度满足 WCAG AA。")
