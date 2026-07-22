using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;

namespace ReadmeReader;

internal static class MarkdownDocumentRenderer
{
    private const double MinimumContentWidth = 620;
    private const double DefaultContentWidth = 920;
    private const double MaximumContentWidth = 1320;

    private static DocumentColors Colors = DocumentColors.Light();
    private static double CurrentContentWidth = DefaultContentWidth;
    private static double CurrentZoomScale = 1;

    public static FlowDocument Render(string markdown, string? baseDirectory, bool darkMode = false, double viewportWidth = 0, bool immersiveMode = false, double zoomPercent = 100)
    {
        Colors = darkMode ? DocumentColors.Dark() : DocumentColors.Light();
        CurrentZoomScale = Math.Clamp(zoomPercent, 50, 300) / 100;
        var layout = ReadingLayout.FromViewport(viewportWidth, immersiveMode);
        CurrentContentWidth = layout.ContentWidth;

        var document = new FlowDocument
        {
            FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI"),
            FontSize = Scale(16),
            Foreground = Colors.BodyBrush,
            Background = Colors.DocumentBackground,
            PagePadding = layout.PagePadding,
            PageWidth = layout.PageWidth,
            ColumnWidth = layout.ContentWidth,
            ColumnGap = 0,
            MaxPageWidth = layout.PageWidth,
            MinPageWidth = 420,
            TextAlignment = TextAlignment.Left
        };
        TextOptions.SetTextFormattingMode(document, TextFormattingMode.Ideal);
        TextOptions.SetTextRenderingMode(document, TextRenderingMode.Auto);

        var lines = Normalize(markdown).Split('\n');
        var index = SkipFrontMatter(lines);

        while (index < lines.Length)
        {
            var line = lines[index];

            if (string.IsNullOrWhiteSpace(line))
            {
                index++;
                continue;
            }

            if (IsFence(line))
            {
                index = AddCodeBlock(document, lines, index);
                continue;
            }

            if (TryAddHeading(document, line))
            {
                index++;
                continue;
            }

            if (IsHorizontalRule(line))
            {
                AddHorizontalRule(document);
                index++;
                continue;
            }

            if (IsTableStart(lines, index))
            {
                index = AddTable(document, lines, index);
                continue;
            }

            if (IsListItem(line))
            {
                index = AddList(document, lines, index);
                continue;
            }

            if (line.TrimStart().StartsWith(">", StringComparison.Ordinal))
            {
                index = AddQuote(document, lines, index);
                continue;
            }

            if (TryAddImage(document, line.Trim(), baseDirectory))
            {
                index++;
                continue;
            }

            index = AddParagraph(document, lines, index);
        }

        return document;
    }

    private static int SkipFrontMatter(string[] lines)
    {
        if (lines.Length == 0 || lines[0].Trim() != "---")
        {
            return 0;
        }

        for (var i = 1; i < lines.Length; i++)
        {
            if (lines[i].Trim() == "---")
            {
                return i + 1;
            }
        }

        return 0;
    }

    private static bool TryAddHeading(FlowDocument document, string line)
    {
        var match = Regex.Match(line, @"^(#{1,6})\s+(.+?)\s*#*$");
        if (!match.Success)
        {
            return false;
        }

        var level = match.Groups[1].Value.Length;
        var paragraph = new Paragraph
        {
            Margin = level == 1 ? new Thickness(0, 0, 0, 22) : new Thickness(0, 28, 0, 10),
            FontWeight = FontWeights.Bold,
            FontSize = level switch
            {
                1 => Scale(36),
                2 => Scale(27),
                3 => Scale(22),
                4 => Scale(18),
                _ => Scale(16)
            },
            LineHeight = level switch
            {
                1 => Scale(44),
                2 => Scale(34),
                3 => Scale(29),
                _ => Scale(25)
            },
            Foreground = Colors.HeadingBrush,
            KeepWithNext = true
        };

        AddInlineContent(paragraph.Inlines, match.Groups[2].Value.Trim());
        document.Blocks.Add(paragraph);
        return true;
    }

    private static int AddParagraph(FlowDocument document, string[] lines, int start)
    {
        var builder = new StringBuilder();
        var index = start;

        while (index < lines.Length && !IsBlockBoundary(lines, index))
        {
            if (builder.Length > 0)
            {
                builder.Append(' ');
            }

            builder.Append(lines[index].Trim());
            index++;
        }

        var paragraph = new Paragraph
        {
            Margin = new Thickness(0, 0, 0, 16),
            LineHeight = Scale(27)
        };
        AddInlineContent(paragraph.Inlines, builder.ToString());
        document.Blocks.Add(paragraph);
        return index;
    }

    private static int AddCodeBlock(FlowDocument document, string[] lines, int start)
    {
        var builder = new StringBuilder();
        var index = start + 1;

        while (index < lines.Length && !IsFence(lines[index]))
        {
            builder.AppendLine(lines[index]);
            index++;
        }

        var textBlock = new TextBlock
        {
            Text = builder.ToString().TrimEnd(),
            FontFamily = new FontFamily("Cascadia Mono, Consolas"),
            FontSize = Scale(14),
            TextWrapping = TextWrapping.NoWrap,
            Foreground = Colors.CodeTextBrush,
            Padding = new Thickness(16),
            LineHeight = Scale(20)
        };

        document.Blocks.Add(new BlockUIContainer(new Border
        {
            Background = Colors.CodeBackground,
            BorderBrush = Colors.BorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Margin = new Thickness(0, 6, 0, 18),
            MaxWidth = CurrentContentWidth,
            Child = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                CanContentScroll = false,
                Content = textBlock
            }
        }));

        return index < lines.Length ? index + 1 : index;
    }

    private static int AddList(FlowDocument document, string[] lines, int start)
    {
        var ordered = IsOrderedListItem(lines[start]);
        var list = new List
        {
            MarkerStyle = ordered ? TextMarkerStyle.Decimal : TextMarkerStyle.Disc,
            Margin = new Thickness(28, 0, 0, 18),
            Padding = new Thickness(12, 0, 0, 0)
        };

        var index = start;
        while (index < lines.Length && IsListItem(lines[index]) && IsOrderedListItem(lines[index]) == ordered)
        {
            var itemText = Regex.Replace(lines[index].Trim(), ordered ? @"^\d+[.)]\s+" : @"^[-*+]\s+", "");
            var paragraph = new Paragraph { Margin = new Thickness(0, 0, 0, 7), LineHeight = Scale(26) };
            AddInlineContent(paragraph.Inlines, itemText);
            list.ListItems.Add(new ListItem(paragraph));
            index++;
        }

        document.Blocks.Add(list);
        return index;
    }

    private static int AddQuote(FlowDocument document, string[] lines, int start)
    {
        var builder = new StringBuilder();
        var index = start;

        while (index < lines.Length && lines[index].TrimStart().StartsWith(">", StringComparison.Ordinal))
        {
            if (builder.Length > 0)
            {
                builder.AppendLine();
            }

            builder.Append(lines[index].TrimStart().TrimStart('>').TrimStart());
            index++;
        }

        var textBlock = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Foreground = Colors.MutedBrush,
            FontSize = Scale(16),
            LineHeight = Scale(26),
            Text = builder.ToString()
        };

        document.Blocks.Add(new BlockUIContainer(new Border
        {
            Background = Colors.QuoteBackgroundBrush,
            BorderBrush = Colors.QuoteBorderBrush,
            BorderThickness = new Thickness(4, 0, 0, 0),
            CornerRadius = new CornerRadius(0, 6, 6, 0),
            Padding = new Thickness(16, 10, 14, 10),
            Margin = new Thickness(0, 6, 0, 18),
            Child = textBlock
        }));

        return index;
    }

    private static int AddTable(FlowDocument document, string[] lines, int start)
    {
        var header = SplitTableRow(lines[start]);
        var rows = new List<string[]>();
        var index = start + 2;

        while (index < lines.Length && lines[index].Contains('|') && !string.IsNullOrWhiteSpace(lines[index]))
        {
            rows.Add(SplitTableRow(lines[index]));
            index++;
        }

        var table = new Table
        {
            CellSpacing = 0,
            Margin = new Thickness(0, 6, 0, 20)
        };

        for (var i = 0; i < header.Length; i++)
        {
            table.Columns.Add(new TableColumn());
        }

        var group = new TableRowGroup();
        var headerRow = new TableRow { Background = Colors.TableHeaderBrush };
        foreach (var cell in header)
        {
            headerRow.Cells.Add(CreateTableCell(cell, isHeader: true));
        }

        group.Rows.Add(headerRow);

        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            var row = rows[rowIndex];
            var tableRow = new TableRow
            {
                Background = rowIndex % 2 == 1 ? Colors.TableAlternateRowBrush : null
            };
            for (var i = 0; i < header.Length; i++)
            {
                tableRow.Cells.Add(CreateTableCell(i < row.Length ? row[i] : string.Empty, isHeader: false));
            }

            group.Rows.Add(tableRow);
        }

        table.RowGroups.Add(group);
        document.Blocks.Add(table);
        return index;
    }

    private static TableCell CreateTableCell(string text, bool isHeader)
    {
        var paragraph = new Paragraph { Margin = new Thickness(0), LineHeight = Scale(23) };
        AddInlineContent(paragraph.Inlines, text.Trim());

        return new TableCell(paragraph)
        {
            BorderBrush = Colors.BorderBrush,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(12, 9, 12, 9),
            FontWeight = isHeader ? FontWeights.Bold : FontWeights.Normal
        };
    }

    private static bool TryAddImage(FlowDocument document, string line, string? baseDirectory)
    {
        var match = Regex.Match(line, @"^!\[(?<alt>[^\]]*)\]\((?<path>[^)]+)\)$");
        if (!match.Success)
        {
            return false;
        }

        var imagePath = match.Groups["path"].Value.Trim().Trim('"');
        var alt = match.Groups["alt"].Value.Trim();

        if (System.Uri.TryCreate(imagePath, System.UriKind.Absolute, out var remoteUri) && !remoteUri.IsFile)
        {
            var paragraph = new Paragraph { Margin = new Thickness(0, 0, 0, 14), Foreground = Colors.MutedBrush };
            paragraph.Inlines.Add(new Run(string.IsNullOrWhiteSpace(alt) ? imagePath : $"{alt}: {imagePath}"));
            document.Blocks.Add(paragraph);
            return true;
        }

        var resolved = Path.IsPathRooted(imagePath) ? imagePath : Path.Combine(baseDirectory ?? "", imagePath);
        if (!File.Exists(resolved))
        {
            var paragraph = new Paragraph { Margin = new Thickness(0, 0, 0, 14), Foreground = Colors.MutedBrush };
            paragraph.Inlines.Add(new Run(string.IsNullOrWhiteSpace(alt) ? $"Imagen no encontrada: {imagePath}" : alt));
            document.Blocks.Add(paragraph);
            return true;
        }

        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.UriSource = new System.Uri(resolved, System.UriKind.Absolute);
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.EndInit();
        bitmap.Freeze();

        var image = new Image
        {
            Source = bitmap,
            Stretch = Stretch.Uniform,
            MaxWidth = CurrentContentWidth,
            MaxHeight = 560,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 4, 0, string.IsNullOrWhiteSpace(alt) ? 18 : 8),
            ToolTip = string.IsNullOrWhiteSpace(alt) ? imagePath : alt
        };

        if (string.IsNullOrWhiteSpace(alt))
        {
            document.Blocks.Add(new BlockUIContainer(image));
            return true;
        }

        document.Blocks.Add(new BlockUIContainer(new StackPanel
        {
            Margin = new Thickness(0, 4, 0, 18),
            Children =
            {
                image,
                new TextBlock
                {
                    Text = alt,
                    Foreground = Colors.MutedBrush,
                    FontSize = Scale(13),
                    Margin = new Thickness(0, 2, 0, 0),
                    TextWrapping = TextWrapping.Wrap
                }
            }
        }));

        return true;
    }

    private static void AddHorizontalRule(FlowDocument document)
    {
        document.Blocks.Add(new BlockUIContainer(new Border
        {
            Height = 1,
            Background = Colors.BorderBrush,
            Margin = new Thickness(0, 10, 0, 20)
        }));
    }

    private static void AddInlineContent(InlineCollection inlines, string text)
    {
        var index = 0;
        while (index < text.Length)
        {
            var code = text.IndexOf('`', index);
            var link = text.IndexOf('[', index);
            var bold = text.IndexOf("**", index, StringComparison.Ordinal);
            var italic = FindItalic(text, index);
            var mark = FindMark(text, index);

            var next = MinPositive(code, link, bold, italic, mark);
            if (next < 0)
            {
                AddRun(inlines, text[index..]);
                break;
            }

            if (next > index)
            {
                AddRun(inlines, text[index..next]);
            }

            if (next == code && TryConsumeCode(inlines, text, next, out var afterCode))
            {
                index = afterCode;
                continue;
            }

            if (next == link && TryConsumeLink(inlines, text, next, out var afterLink))
            {
                index = afterLink;
                continue;
            }

            if (next == bold && TryConsumeBold(inlines, text, next, out var afterBold))
            {
                index = afterBold;
                continue;
            }

            if (next == italic && TryConsumeItalic(inlines, text, next, out var afterItalic))
            {
                index = afterItalic;
                continue;
            }

            if (next == mark && TryConsumeMark(inlines, text, next, out var afterMark))
            {
                index = afterMark;
                continue;
            }

            AddRun(inlines, text[next].ToString());
            index = next + 1;
        }
    }

    private static bool TryConsumeCode(InlineCollection inlines, string text, int start, out int after)
    {
        after = start;
        var end = text.IndexOf('`', start + 1);
        if (end < 0)
        {
            return false;
        }

        inlines.Add(new Run(text[(start + 1)..end])
        {
            FontFamily = new FontFamily("Cascadia Mono, Consolas"),
            Background = Colors.CodeBackground,
            Foreground = Colors.CodeTextBrush
        });
        after = end + 1;
        return true;
    }

    private static bool TryConsumeLink(InlineCollection inlines, string text, int start, out int after)
    {
        after = start;
        var close = text.IndexOf("](", start, StringComparison.Ordinal);
        if (close < 0)
        {
            return false;
        }

        var end = text.IndexOf(')', close + 2);
        if (end < 0)
        {
            return false;
        }

        var label = text[(start + 1)..close];
        var target = text[(close + 2)..end];
        var hyperlink = new Hyperlink { Foreground = Colors.LinkBrush };
        hyperlink.Inlines.Add(new Run(label));
        if (System.Uri.TryCreate(target, System.UriKind.Absolute, out var uri))
        {
            hyperlink.NavigateUri = uri;
            hyperlink.RequestNavigate += OpenLink;
        }
        else
        {
            hyperlink.ToolTip = target;
        }

        inlines.Add(hyperlink);
        after = end + 1;
        return true;
    }

    private static bool TryConsumeBold(InlineCollection inlines, string text, int start, out int after)
    {
        after = start;
        var end = text.IndexOf("**", start + 2, StringComparison.Ordinal);
        if (end < 0)
        {
            return false;
        }

        var span = new Bold();
        AddInlineContent(span.Inlines, text[(start + 2)..end]);
        inlines.Add(span);
        after = end + 2;
        return true;
    }

    private static bool TryConsumeItalic(InlineCollection inlines, string text, int start, out int after)
    {
        after = start;
        var end = text.IndexOf('*', start + 1);
        if (end < 0 || (start > 0 && text[start - 1] == '*') || (end + 1 < text.Length && text[end + 1] == '*'))
        {
            return false;
        }

        var span = new Italic();
        AddInlineContent(span.Inlines, text[(start + 1)..end]);
        inlines.Add(span);
        after = end + 1;
        return true;
    }

    private static bool TryConsumeMark(InlineCollection inlines, string text, int start, out int after)
    {
        after = start;
        var open = Regex.Match(text[start..], @"^<mark\b(?<attrs>[^>]*)>", RegexOptions.IgnoreCase);
        if (!open.Success)
        {
            return false;
        }

        var contentStart = start + open.Length;
        var close = text.IndexOf("</mark>", contentStart, StringComparison.OrdinalIgnoreCase);
        if (close < 0)
        {
            return false;
        }

        var color = "#FFF3A3";
        var colorMatch = Regex.Match(open.Groups["attrs"].Value, @"background-color\s*:\s*(?<color>#[0-9a-fA-F]{3,8}|[a-zA-Z]+)", RegexOptions.IgnoreCase);
        if (colorMatch.Success)
        {
            color = colorMatch.Groups["color"].Value;
        }

        var span = new Span
        {
            Background = Brush(color),
            Foreground = Brush("#111827")
        };
        AddInlineContent(span.Inlines, text[contentStart..close]);
        inlines.Add(span);
        after = close + "</mark>".Length;
        return true;
    }

    private static void AddRun(InlineCollection inlines, string text)
    {
        if (!string.IsNullOrEmpty(text))
        {
            inlines.Add(new Run(text));
        }
    }

    private static void OpenLink(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }

    private static int FindItalic(string text, int start)
    {
        for (var i = start; i < text.Length; i++)
        {
            if (text[i] == '*' && (i + 1 >= text.Length || text[i + 1] != '*') && (i == 0 || text[i - 1] != '*'))
            {
                return i;
            }
        }

        return -1;
    }

    private static int FindMark(string text, int start)
    {
        var match = Regex.Match(text[start..], @"<mark\b", RegexOptions.IgnoreCase);
        return match.Success ? start + match.Index : -1;
    }

    private static int MinPositive(params int[] values)
    {
        return values.Where(value => value >= 0).DefaultIfEmpty(-1).Min();
    }

    private static bool IsBlockBoundary(string[] lines, int index)
    {
        var line = lines[index];
        return string.IsNullOrWhiteSpace(line)
            || IsFence(line)
            || Regex.IsMatch(line, @"^(#{1,6})\s+")
            || IsHorizontalRule(line)
            || IsListItem(line)
            || line.TrimStart().StartsWith(">", StringComparison.Ordinal)
            || IsTableStart(lines, index)
            || Regex.IsMatch(line.Trim(), @"^!\[[^\]]*\]\([^)]+\)$");
    }

    private static bool IsFence(string line) => line.TrimStart().StartsWith("```", StringComparison.Ordinal);

    private static bool IsHorizontalRule(string line)
    {
        var trimmed = line.Trim();
        return trimmed.Length >= 3 && trimmed.All(character => character == '-')
            || trimmed.Length >= 3 && trimmed.All(character => character == '*')
            || trimmed.Length >= 3 && trimmed.All(character => character == '_');
    }

    private static bool IsListItem(string line)
    {
        var trimmed = line.TrimStart();
        return Regex.IsMatch(trimmed, @"^([-*+]\s+|\d+[.)]\s+)");
    }

    private static bool IsOrderedListItem(string line) => Regex.IsMatch(line.TrimStart(), @"^\d+[.)]\s+");

    private static bool IsTableStart(string[] lines, int index)
    {
        return index + 1 < lines.Length
            && lines[index].Contains('|')
            && Regex.IsMatch(lines[index + 1].Trim(), @"^\|?\s*:?-{3,}:?\s*(\|\s*:?-{3,}:?\s*)+\|?$");
    }

    private static string[] SplitTableRow(string line)
    {
        return line.Trim().Trim('|').Split('|').Select(part => part.Trim()).ToArray();
    }

    private static string Normalize(string markdown) => markdown.Replace("\r\n", "\n").Replace('\r', '\n');

    private readonly record struct ReadingLayout(double PageWidth, double ContentWidth, Thickness PagePadding)
    {
        public static ReadingLayout FromViewport(double viewportWidth, bool immersiveMode)
        {
            if (viewportWidth <= 0)
            {
                viewportWidth = DefaultContentWidth + 104;
            }

            if (viewportWidth < 700)
            {
                var compactPadding = Math.Max(20, viewportWidth * 0.06);
                var compactContent = Math.Max(320, viewportWidth - compactPadding * 2);

                return new ReadingLayout(
                    viewportWidth,
                    compactContent,
                    new Thickness(compactPadding, 36, compactPadding, 64));
            }

            var outerMargin = immersiveMode ? 80 : 96;
            var maxContent = immersiveMode ? 1480 : MaximumContentWidth;
            var contentWidth = Math.Clamp(viewportWidth - outerMargin, MinimumContentWidth, maxContent);
            var sidePadding = Math.Max(42, (viewportWidth - contentWidth) / 2);
            var topPadding = immersiveMode ? 76 : 52;
            var bottomPadding = immersiveMode ? 92 : 72;

            return new ReadingLayout(
                viewportWidth,
                contentWidth,
                new Thickness(sidePadding, topPadding, sidePadding, bottomPadding));
        }
    }

    private static double Scale(double value) => Math.Round(value * CurrentZoomScale, 1);

    private static SolidColorBrush Brush(string color)
    {
        var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(color)!;
        brush.Freeze();
        return brush;
    }

    private sealed class DocumentColors
    {
        public required Brush BodyBrush { get; init; }
        public required Brush HeadingBrush { get; init; }
        public required Brush MutedBrush { get; init; }
        public required Brush BorderBrush { get; init; }
        public required Brush CodeBackground { get; init; }
        public required Brush CodeTextBrush { get; init; }
        public required Brush LinkBrush { get; init; }
        public required Brush QuoteBorderBrush { get; init; }
        public required Brush QuoteBackgroundBrush { get; init; }
        public required Brush TableHeaderBrush { get; init; }
        public required Brush TableAlternateRowBrush { get; init; }
        public required Brush DocumentBackground { get; init; }

        public static DocumentColors Light() => new()
        {
            BodyBrush = Brush("#243041"),
            HeadingBrush = Brush("#111827"),
            MutedBrush = Brush("#64748B"),
            BorderBrush = Brush("#D8DEE8"),
            CodeBackground = Brush("#F1F5F9"),
            CodeTextBrush = Brush("#182235"),
            LinkBrush = Brush("#0B63CE"),
            QuoteBorderBrush = Brush("#94A3B8"),
            QuoteBackgroundBrush = Brush("#F8FAFC"),
            TableHeaderBrush = Brush("#EAF0F7"),
            TableAlternateRowBrush = Brush("#F8FAFC"),
            DocumentBackground = Brush("#FFFFFF")
        };

        public static DocumentColors Dark() => new()
        {
            BodyBrush = Brush("#D8D2C7"),
            HeadingBrush = Brush("#F4EFE7"),
            MutedBrush = Brush("#A8A29E"),
            BorderBrush = Brush("#3A3732"),
            CodeBackground = Brush("#24221F"),
            CodeTextBrush = Brush("#ECE7DE"),
            LinkBrush = Brush("#D6B877"),
            QuoteBorderBrush = Brush("#8B7D66"),
            QuoteBackgroundBrush = Brush("#24221F"),
            TableHeaderBrush = Brush("#2A2825"),
            TableAlternateRowBrush = Brush("#201F1D"),
            DocumentBackground = Brush("#1B1A18")
        };
    }
}
