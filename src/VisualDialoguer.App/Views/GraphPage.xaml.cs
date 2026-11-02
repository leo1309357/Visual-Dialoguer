/*
 * Copyright (c) 2026 Leo Jeang. All Rights Reserved.
 *
 * This is proprietary software. Unauthorized copying, modification,
 * or distribution is strictly prohibited.
 */

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using VisualDialoguer.Core.Editing;
using VisualDialoguer.Core.Model;
using Windows.Foundation;

namespace VisualDialoguer.App.Views;

/// <summary>
/// 「流程图」页 ·「Loma」轻拟物：案垫是一块带网格的桌面，节点是浮起
/// 的索引卡，choice / goto 是卡与卡之间的牵线。
///
/// 布局是算出来的，不是拖出来的：节点按顺序排在网格里，连线从源节点右侧
/// 指向目标节点左侧。这样脚本一改，图就跟着重画，不会出现"图和脚本对不
/// 上"的中间态。
///
/// 配色全部走 <see cref="Theme"/>，不写死颜色——同一份绘图代码在日间是
/// 浅色案头、在深色下是夜读案头。
/// </summary>
public sealed partial class GraphPage : EditorPageBase
{
    private const double NodeWidth = 168.0;
    private const double NodeHeight = 62.0;
    private const double GapX = 96.0;
    private const double GapY = 56.0;
    private const int Columns = 3;

    /// <summary>索引卡左侧书脊宽度。</summary>
    private const double SpineWidth = 4.0;

    /// <summary>卡片圆角。</summary>
    private const double CardRadius = 10.0;

    /// <summary>案垫网格的间距。</summary>
    private const double GridSpacing = 32.0;

    /// <summary>点了某个节点框。窗口负责切回「对话结构」并选中它。</summary>
    public event EventHandler<string>? NodeClicked;

    public GraphPage()
    {
        InitializeComponent();

        // 代码绘图画刷是重画那一刻才取的：主题切换后要重画一次，
        // 否则索引卡还停在旧配色上。
        Theme.Changed += Refresh;
    }

    public override void Refresh()
    {
        GraphCanvas.Children.Clear();
        GraphCanvas.Background = Transparent();

        EditorDocument? document = Document;
        List<DialogueNode> nodes = document?.Script.Nodes ?? new List<DialogueNode>();

        // 画布尺寸先定下来：空脚本也要铺出案垫和网格，不能是一片白。
        int rows = nodes.Count == 0 ? 0 : (nodes.Count + Columns - 1) / Columns;
        GraphCanvas.Width = Math.Max(800, Columns * (NodeWidth + GapX));
        GraphCanvas.Height = Math.Max(480, rows * (NodeHeight + GapY));

        GraphCanvas.Background = Theme.Brush("LomaGraphDesk", Transparent());
        DrawGrid();

        if (document is null || nodes.Count == 0)
        {
            return;
        }

        var positions = new Dictionary<string, (double X, double Y)>(StringComparer.Ordinal);
        for (int index = 0; index < nodes.Count; ++index)
        {
            int column = index % Columns;
            int row = index / Columns;
            positions[nodes[index].Id] = (column * (NodeWidth + GapX), row * (NodeHeight + GapY));
        }

        // 层次自下而上：网格 → 牵线 → 索引卡。
        foreach (DialogueNode node in nodes)
        {
            foreach (Instruction instruction in node.Instructions)
            {
                if (instruction.Kind is not (InstKind.Choice or InstKind.Goto))
                {
                    continue;
                }
                if (string.IsNullOrEmpty(instruction.TargetNode) || !positions.TryGetValue(instruction.TargetNode, out var target))
                {
                    // 目标节点不存在：这条边画不出来，校验会报出来。
                    continue;
                }
                DrawConnector(positions[node.Id], target, instruction);
            }
        }

        foreach (DialogueNode node in nodes)
        {
            DrawNode(node, positions[node.Id], document);
        }
    }

    /// <summary>案垫上的网格：很淡的一层刻度，给索引卡一个参照。</summary>
    private void DrawGrid()
    {
        Brush grid = Theme.Brush("LomaGraphGrid", Transparent());
        double width = GraphCanvas.Width;
        double height = GraphCanvas.Height;

        for (double x = GridSpacing; x < width; x += GridSpacing)
        {
            GraphCanvas.Children.Add(new Line
            {
                X1 = x,
                Y1 = 0,
                X2 = x,
                Y2 = height,
                Stroke = grid,
                StrokeThickness = 1,
            });
        }
        for (double y = GridSpacing; y < height; y += GridSpacing)
        {
            GraphCanvas.Children.Add(new Line
            {
                X1 = 0,
                Y1 = y,
                X2 = width,
                Y2 = y,
                Stroke = grid,
                StrokeThickness = 1,
            });
        }
    }

    /// <summary>
    /// 画一张索引卡：柔影 → 卡面（浮起渐变 + 发丝边）→ 书脊 → 标题与条数。
    /// 入口节点用批注朱砂（签名色），一眼能认出脚本从哪儿开始跑。
    /// </summary>
    private void DrawNode(DialogueNode node, (double X, double Y) position, EditorDocument document)
    {
        bool isEntry = string.Equals(node.Id, document.Script.EntryNodeId, StringComparison.Ordinal);

        AddShadows(position.X, position.Y, NodeWidth, NodeHeight, CardRadius);

        var card = new Border
        {
            Width = NodeWidth,
            Height = NodeHeight,
            CornerRadius = new CornerRadius(CardRadius),
            BorderThickness = new Thickness(0),
            Background = Theme.Brush("LomaCardFace", Paper()),
        };

        var content = new Grid();
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(SpineWidth) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // 书脊：左侧一道竖色，像索引卡露在案头外的那一条。
        var spine = new Border
        {
            CornerRadius = new CornerRadius(CardRadius, 0, 0, CardRadius),
            Background = isEntry ? Theme.Brush("LomaAccent", Hairline()) : SpineBrushFor(node),
        };
        Grid.SetColumn(spine, 0);
        content.Children.Add(spine);

        var stack = new StackPanel
        {
            Margin = new Thickness(9, 5, 8, 5),
            Spacing = 1,
        };
        stack.Children.Add(new TextBlock
        {
            Text = (isEntry ? "★ " : string.Empty) + node.Id,
            FontSize = Theme.Number("LomaFontSizeBody", 14),
            Foreground = Theme.Brush(isEntry ? "LomaAccent" : "LomaInk", Ink()),
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        stack.Children.Add(new TextBlock
        {
            Text = node.Instructions.Count + " 条指令",
            FontSize = Theme.Number("LomaFontSizeCaption", 12),
            Foreground = Theme.Brush("LomaInkTertiary", Ink()),
        });
        Grid.SetColumn(stack, 1);
        content.Children.Add(stack);

        card.Child = content;
        Canvas.SetLeft(card, position.X);
        Canvas.SetTop(card, position.Y);

        card.Tapped += (_, _) => NodeClicked?.Invoke(this, node.Id);
        GraphCanvas.Children.Add(card);
    }

    /// <summary>
    /// 三层柔影，与 XAML 卡片的 <c>LomaCard</c> 同一套做法：先铺一层大而淡的
    /// 环境影，再压一层贴边的关键影，最后补一道左上受光高光。单层实色偏移在
    /// 浅色案垫上会露出硬边，和旁边的纸片对不上。
    /// </summary>
    private void AddShadows(double x, double y, double width, double height, double radius)
    {
        var ambient = new Border
        {
            Width = width + 8,
            Height = height + 8,
            CornerRadius = new CornerRadius(radius + 4),
            Background = Theme.Brush("LomaShadowAmbient", Transparent()),
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(ambient, x - 4);
        Canvas.SetTop(ambient, y);
        GraphCanvas.Children.Add(ambient);

        // 左上受光高光：与关键影同尺寸、方向相反，模拟统一光源
        var light = new Border
        {
            Width = width + 2,
            Height = height + 2,
            CornerRadius = new CornerRadius(radius + 1),
            Background = Theme.Brush("LomaShadowLight", Transparent()),
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(light, x - 1);
        Canvas.SetTop(light, y - 1);
        GraphCanvas.Children.Add(light);

        var key = new Border
        {
            Width = width + 2,
            Height = height + 2,
            CornerRadius = new CornerRadius(radius + 1),
            Background = Theme.Brush("LomaShadowDarkDeep", Transparent()),
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(key, x - 1);
        Canvas.SetTop(key, y + 1);
        GraphCanvas.Children.Add(key);
    }

    /// <summary>
    /// 书脊颜色：取节点第一条指令的语义色，让卡片的颜色和内容挂钩；
    /// 空节点退到发丝边色，不抢眼。
    /// </summary>
    private static Brush SpineBrushFor(DialogueNode node) => node.Instructions.Count > 0
        ? Theme.KindBrush(node.Instructions[0].Kind)
        : Theme.Brush("LomaEdgeStrong", Hairline());

    /// <summary>
    /// 画一条牵线：源节点右侧 → 目标节点左侧，末端张开箭头；choice 的标签
    /// 做成压在中间的一片小纸片。
    /// </summary>
    private void DrawConnector((double X, double Y) from, (double X, double Y) to, Instruction instruction)
    {
        Brush thread = Theme.Brush("LomaThread", Hairline());

        double x1 = from.X + NodeWidth;
        double y1 = from.Y + NodeHeight * 0.5;
        double x2 = to.X;
        double y2 = to.Y + NodeHeight * 0.5;

        GraphCanvas.Children.Add(new Line
        {
            X1 = x1,
            Y1 = y1,
            X2 = x2,
            Y2 = y2,
            Stroke = thread,
            StrokeThickness = 1.5,
        });

        // 箭头：沿连线方向在终点张开一个小三角。
        double dx = x2 - x1;
        double dy = y2 - y1;
        double length = Math.Sqrt(dx * dx + dy * dy);
        if (length > 0.001)
        {
            dx /= length;
            dy /= length;
            const double HeadLength = 11.0;
            const double HeadWidth = 5.0;
            var head = new Polygon
            {
                Fill = thread,
                Points = new PointCollection
                {
                    new Point(x2, y2),
                    new Point(x2 - dx * HeadLength - dy * HeadWidth, y2 - dy * HeadLength + dx * HeadWidth),
                    new Point(x2 - dx * HeadLength + dy * HeadWidth, y2 - dy * HeadLength - dx * HeadWidth),
                },
            };
            GraphCanvas.Children.Add(head);
        }

        string label = instruction.Kind == InstKind.Choice ? instruction.Text : "goto";
        if (string.IsNullOrEmpty(label))
        {
            return;
        }

        // 标签压在连线上，中间要看得清：做一片带柔影的小纸片。
        var labelText = new TextBlock
        {
            Text = label,
            FontSize = Theme.Number("LomaFontSizeCaption", 12),
            Foreground = Theme.Brush("LomaInkSecondary", Ink()),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

        // 先量一遍拿到真实尺寸再居中：标签长短不一，写死偏移会让长标签
        // 偏出连线中点。
        labelText.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double chipWidth = labelText.DesiredSize.Width + 12;
        double chipHeight = labelText.DesiredSize.Height + 6;
        double centerX = (x1 + x2) * 0.5;
        double centerY = (y1 + y2) * 0.5;

        var chipShadow = new Border
        {
            Width = chipWidth,
            Height = chipHeight,
            CornerRadius = new CornerRadius(5),
            Background = Theme.Brush("LomaShadowDarkDeep", Transparent()),
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(chipShadow, centerX - chipWidth * 0.5 + 1);
        Canvas.SetTop(chipShadow, centerY - chipHeight * 0.5 + 1);
        GraphCanvas.Children.Add(chipShadow);

        var chip = new Border
        {
            Width = chipWidth,
            Height = chipHeight,
            CornerRadius = new CornerRadius(5),
            BorderThickness = new Thickness(0),
            Background = Theme.Brush("LomaRaised", Paper()),
            Child = labelText,
        };
        Canvas.SetLeft(chip, centerX - chipWidth * 0.5);
        Canvas.SetTop(chip, centerY - chipHeight * 0.5);
        GraphCanvas.Children.Add(chip);
    }

    // ---- 兜底色：主题键万一缺失时用，保证图仍可读而不是崩溃 --------------

    private static SolidColorBrush Transparent() => new(Windows.UI.Color.FromArgb(0, 0, 0, 0));

    private static SolidColorBrush Hairline() => new(Windows.UI.Color.FromArgb(0xFF, 0x80, 0x80, 0x80));

    private static SolidColorBrush Ink() => new(Windows.UI.Color.FromArgb(0xFF, 0x40, 0x40, 0x40));

    private static SolidColorBrush Paper() => new(Windows.UI.Color.FromArgb(0xFF, 0xE8, 0xE8, 0xE8));
}
