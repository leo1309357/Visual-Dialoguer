/*
 * Copyright (c) 2026 Leo Jeang. All Rights Reserved.
 *
 * This is proprietary software. Unauthorized copying, modification,
 * or distribution is strictly prohibited.
 */

namespace VisualDialoguer.Core.Format;

/// <summary>一条解析错误：行号 + 消息。行号为 1 起。</summary>
/// <param name="Line">出错行号。</param>
/// <param name="Message">错误描述。</param>
public sealed record FormatIssue(int Line, string Message)
{
    /// <summary>一行式描述，直接放进状态栏 / 报告列表。</summary>
    public override string ToString() => $"第 {Line} 行：{Message}";
}

/// <summary>解析结果：脚本表 + 解析错误。</summary>
/// <param name="Scripts">解析出的脚本（可能为空，也可能多于一个）。</param>
/// <param name="Issues">解析过程中的错误。</param>
public sealed record ParseResult(
    IReadOnlyList<Model.DialogueScript> Scripts,
    IReadOnlyList<FormatIssue> Issues);
