using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Markit.Core;

public static class MarkdownPdfExporter
{
    private const double PageWidth = 595.28;
    private const double PageHeight = 841.89;
    private const double MarginLeft = 56;
    private const double MarginRight = 56;
    private const double MarginTop = 56;
    private const double MarginBottom = 58;
    private const double TextWidth = PageWidth - MarginLeft - MarginRight;

    private static readonly PdfColor TextColor = new(0.05, 0.09, 0.16);
    private static readonly PdfColor MutedTextColor = new(0.25, 0.29, 0.36);
    private static readonly PdfColor RuleColor = new(0.82, 0.86, 0.91);
    private static readonly PdfColor CodeBackground = new(0.95, 0.97, 0.99);
    private static readonly PdfColor TableHeaderBackground = new(0.91, 0.95, 0.99);
    private static readonly PdfColor TableCellBackground = new(0.99, 1.00, 1.00);
    private static readonly PdfColor HighlightFallback = new(1.00, 0.96, 0.64);

    public static void Export(string markdown, string fileName, string title)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        var directory = Path.GetDirectoryName(fileName);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var layout = new PdfLayout(title);
        RenderMarkdown(layout, markdown);
        File.WriteAllBytes(fileName, PdfWriter.Write(layout.Pages, title));
    }

    private static void RenderMarkdown(PdfLayout layout, string markdown)
    {
        var lines = BuildLines(markdown);
        var index = 0;
        var inCodeBlock = false;

        while (index < lines.Count)
        {
            var line = lines[index];
            var trimmed = line.Structural.Trim();

            if (trimmed.StartsWith("```", StringComparison.Ordinal))
            {
                inCodeBlock = !inCodeBlock;
                index++;
                continue;
            }

            if (inCodeBlock)
            {
                layout.AddCodeLine(line.Structural);
                index++;
                continue;
            }

            if (string.IsNullOrWhiteSpace(trimmed))
            {
                layout.AddSpace(7);
                index++;
                continue;
            }

            if (Regex.IsMatch(trimmed, @"^[-*_]{3,}$"))
            {
                layout.AddRule();
                index++;
                continue;
            }

            var heading = Regex.Match(trimmed, @"^(#{1,6})\s+(.+?)\s*#*$");
            if (heading.Success)
            {
                var level = heading.Groups[1].Value.Length;
                var headingText = line.Raw.Contains("<mark", StringComparison.OrdinalIgnoreCase)
                    ? ExtractHeadingSource(line.Raw)
                    : heading.Groups[2].Value;

                layout.AddHeading(ParseInlineRuns(headingText, line.ForceHighlight, line.HighlightColor), level);
                index++;
                continue;
            }

            if (LooksLikeTableRow(trimmed))
            {
                var tableRows = new List<TableRow>();

                while (index < lines.Count && LooksLikeTableRow(lines[index].Structural.Trim()))
                {
                    var current = lines[index];
                    var structural = current.Structural.Trim();

                    if (IsTableSeparatorRow(structural))
                    {
                        if (tableRows.Count > 0)
                        {
                            tableRows[^1].IsHeader = true;
                        }

                        index++;
                        continue;
                    }

                    var cells = SplitTableCells(current.Raw, current.Structural)
                        .Select(cell => new TableCell(ParseInlineRuns(cell, current.ForceHighlight, current.HighlightColor)))
                        .ToList();

                    tableRows.Add(new TableRow(cells));
                    index++;
                }

                layout.AddTable(tableRows);
                continue;
            }

            var listItem = Regex.Match(line.Structural, @"^(?<indent>\s*)(?<marker>(?:[-*+])|(?:\d+[.)]))\s+(?<text>.+)$");
            if (listItem.Success)
            {
                var marker = Regex.IsMatch(listItem.Groups["marker"].Value, @"\d") ? listItem.Groups["marker"].Value : "•";
                var depth = Math.Min(5, listItem.Groups["indent"].Value.Replace("\t", "    ").Length / 2);
                var sourceText = ExtractListSource(line.Raw, listItem.Groups["text"].Value);
                layout.AddListItem(marker, ParseInlineRuns(sourceText, line.ForceHighlight, line.HighlightColor), depth);
                index++;
                continue;
            }

            var quote = Regex.Match(line.Structural, @"^\s*>\s?(?<text>.*)$");
            if (quote.Success)
            {
                var sourceText = ExtractQuoteSource(line.Raw, quote.Groups["text"].Value);
                layout.AddQuote(ParseInlineRuns(sourceText, line.ForceHighlight, line.HighlightColor));
                index++;
                continue;
            }

            var paragraph = new StringBuilder();
            var forceHighlight = false;
            var highlightColor = HighlightFallback;

            while (index < lines.Count && IsParagraphContinuation(lines[index]))
            {
                if (paragraph.Length > 0)
                {
                    paragraph.Append(' ');
                }

                paragraph.Append(lines[index].Raw.Trim());
                forceHighlight |= lines[index].ForceHighlight;
                highlightColor = lines[index].HighlightColor;
                index++;
            }

            layout.AddParagraph(ParseInlineRuns(paragraph.ToString(), forceHighlight, highlightColor));
        }
    }

    private static List<MarkdownLine> BuildLines(string markdown)
    {
        var rawLines = markdown.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var lines = new List<MarkdownLine>(rawLines.Length);
        var activeHighlight = false;
        var activeColor = HighlightFallback;

        foreach (var raw in rawLines)
        {
            var startsHighlighted = activeHighlight;
            var opens = Regex.Matches(raw, @"<mark\b(?<attrs>[^>]*)>", RegexOptions.IgnoreCase);
            var closes = Regex.Matches(raw, @"</mark>", RegexOptions.IgnoreCase);
            var inlineOnly = !startsHighlighted && opens.Count > 0 && opens.Count == closes.Count;
            var forceHighlight = startsHighlighted || (opens.Count != closes.Count);
            var lineColor = startsHighlighted ? activeColor : HighlightFallback;

            if (opens.Count > 0)
            {
                lineColor = ExtractHighlightColor(opens[^1].Groups["attrs"].Value) ?? lineColor;
            }

            lines.Add(new MarkdownLine(raw, RemoveMarkTags(raw), forceHighlight && !inlineOnly, lineColor));

            foreach (Match tag in Regex.Matches(raw, @"<mark\b(?<attrs>[^>]*)>|</mark>", RegexOptions.IgnoreCase))
            {
                if (tag.Value.StartsWith("<mark", StringComparison.OrdinalIgnoreCase))
                {
                    activeHighlight = true;
                    activeColor = ExtractHighlightColor(tag.Groups["attrs"].Value) ?? activeColor;
                }
                else
                {
                    activeHighlight = false;
                    activeColor = HighlightFallback;
                }
            }
        }

        return lines;
    }

    private static bool IsParagraphContinuation(MarkdownLine line)
    {
        var trimmed = line.Structural.Trim();
        return !string.IsNullOrWhiteSpace(trimmed)
            && !trimmed.StartsWith("```", StringComparison.Ordinal)
            && !Regex.IsMatch(trimmed, @"^(#{1,6})\s+")
            && !Regex.IsMatch(line.Structural, @"^\s*((?:[-*+])|(?:\d+[.)]))\s+")
            && !Regex.IsMatch(line.Structural, @"^\s*>\s?")
            && !Regex.IsMatch(trimmed, @"^[-*_]{3,}$")
            && !LooksLikeTableRow(trimmed);
    }

    private static bool LooksLikeTableRow(string line)
    {
        return line.Contains('|') && line.Count(character => character == '|') >= 2;
    }

    private static bool IsTableSeparatorRow(string line)
    {
        return Regex.IsMatch(line, @"^\|?\s*:?-{3,}:?\s*(\|\s*:?-{3,}:?\s*)+\|?$");
    }

    private static IEnumerable<string> SplitTableCells(string rawLine, string structuralLine)
    {
        var rawCells = rawLine.Trim().Trim('|').Split('|', StringSplitOptions.TrimEntries);
        var structuralCells = structuralLine.Trim().Trim('|').Split('|', StringSplitOptions.TrimEntries);

        if (rawCells.Length == structuralCells.Length)
        {
            return rawCells;
        }

        return structuralCells;
    }

    private static string ExtractHeadingSource(string rawLine)
    {
        var match = Regex.Match(rawLine.Trim(), @"^(?:<mark\b[^>]*>\s*)?#{1,6}\s+(?<text>.+?)\s*#*(?:\s*</mark>)?$", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups["text"].Value : RemoveMarkTags(rawLine).TrimStart('#', ' ');
    }

    private static string ExtractListSource(string rawLine, string fallback)
    {
        var rawMatch = Regex.Match(rawLine, @"^\s*(?:<mark\b[^>]*>\s*)?(?:[-*+]|\d+[.)])\s+(?<text>.+?)(?:\s*</mark>)?$", RegexOptions.IgnoreCase);
        if (rawMatch.Success)
        {
            return rawMatch.Groups["text"].Value;
        }

        var structuralMatch = Regex.Match(RemoveMarkTags(rawLine), @"^\s*(?:[-*+]|\d+[.)])\s+(?<text>.+)$");
        return structuralMatch.Success ? structuralMatch.Groups["text"].Value : fallback;
    }

    private static string ExtractQuoteSource(string rawLine, string fallback)
    {
        var rawMatch = Regex.Match(rawLine, @"^\s*(?:<mark\b[^>]*>\s*)?>\s?(?<text>.*?)(?:\s*</mark>)?$", RegexOptions.IgnoreCase);
        if (rawMatch.Success)
        {
            return rawMatch.Groups["text"].Value;
        }

        var structuralMatch = Regex.Match(RemoveMarkTags(rawLine), @"^\s*>\s?(?<text>.*)$");
        return structuralMatch.Success ? structuralMatch.Groups["text"].Value : fallback;
    }

    private static string RemoveMarkTags(string text)
    {
        return Regex.Replace(text, @"</?mark\b[^>]*>", string.Empty, RegexOptions.IgnoreCase);
    }

    private static List<TextRun> ParseInlineRuns(string markdown, bool forceHighlight, PdfColor forcedHighlightColor)
    {
        var runs = new List<TextRun>();
        var currentIndex = 0;
        var matches = Regex.Matches(markdown, @"<mark\b(?<attrs>[^>]*)>(?<inner>.*?)</mark>", RegexOptions.IgnoreCase | RegexOptions.Singleline);

        foreach (Match match in matches)
        {
            AddPlainRun(runs, markdown[currentIndex..match.Index], forceHighlight, forcedHighlightColor);
            var color = ExtractHighlightColor(match.Groups["attrs"].Value) ?? forcedHighlightColor;
            AddPlainRun(runs, match.Groups["inner"].Value, highlight: true, color);
            currentIndex = match.Index + match.Length;
        }

        AddPlainRun(runs, markdown[currentIndex..], forceHighlight, forcedHighlightColor);

        if (runs.Count == 0)
        {
            runs.Add(new TextRun(string.Empty, forceHighlight, forcedHighlightColor));
        }

        return runs;
    }

    private static void AddPlainRun(List<TextRun> runs, string markdown, bool highlight, PdfColor color)
    {
        var text = StripInlineMarkdown(RemoveMarkTags(markdown));
        if (text.Length == 0)
        {
            return;
        }

        runs.Add(new TextRun(text, highlight, color));
    }

    private static string StripInlineMarkdown(string text)
    {
        var value = text;
        value = Regex.Replace(value, @"!\[(?<alt>[^\]]*)\]\([^)]+\)", "${alt}");
        value = Regex.Replace(value, @"\[(?<label>[^\]]+)\]\([^)]+\)", "${label}");
        value = Regex.Replace(value, @"</?\w+[^>]*>", string.Empty);
        value = Regex.Replace(value, @"(`+)(.*?)\1", "$2");
        value = Regex.Replace(value, @"(\*\*|__)(.*?)\1", "$2");
        value = Regex.Replace(value, @"(\*|_)(.*?)\1", "$2");
        value = Regex.Replace(value, @"\s+", " ");
        return value;
    }

    private static PdfColor? ExtractHighlightColor(string attributes)
    {
        var match = Regex.Match(attributes, @"background(?:-color)?\s*:\s*(?<color>#[0-9a-fA-F]{6})", RegexOptions.IgnoreCase);
        if (!match.Success)
        {
            return null;
        }

        return PdfColor.FromHex(match.Groups["color"].Value);
    }

    private sealed class PdfLayout
    {
        private double y = PageHeight - MarginTop;

        public PdfLayout(string title)
        {
            Pages = [new PdfPage()];
            AddFooter(title);
        }

        public List<PdfPage> Pages { get; }

        public void AddHeading(IReadOnlyList<TextRun> runs, int level)
        {
            var size = level switch
            {
                1 => 22,
                2 => 18,
                3 => 15,
                _ => 13
            };

            AddSpace(level <= 2 ? 14 : 10);
            AddWrappedRuns(runs, PdfFont.Bold, size, size + 7, MarginLeft, TextWidth, TextColor);
            AddSpace(level <= 2 ? 8 : 5);
        }

        public void AddParagraph(IReadOnlyList<TextRun> runs)
        {
            AddWrappedRuns(runs, PdfFont.Regular, 11, 16, MarginLeft, TextWidth, TextColor);
            AddSpace(5);
        }

        public void AddListItem(string marker, IReadOnlyList<TextRun> runs, int depth)
        {
            var indent = depth * 16;
            var markerX = MarginLeft + indent;
            var textX = markerX + 18;
            var maxWidth = PageWidth - MarginRight - textX;
            var lines = WrapRuns(runs, PdfFont.Regular, 10.8, maxWidth);
            var lineHeight = 15.5;

            for (var i = 0; i < lines.Count; i++)
            {
                EnsureSpace(lineHeight);
                if (i == 0)
                {
                    DrawText(marker, PdfFont.Regular, 10.8, markerX, y, TextColor);
                }

                DrawTextRunLine(lines[i], PdfFont.Regular, 10.8, textX, y, lineHeight, TextColor);
                y -= lineHeight;
            }

            AddSpace(2);
        }

        public void AddQuote(IReadOnlyList<TextRun> runs)
        {
            EnsureSpace(18);
            Current.Content.AppendLine($"q {RuleColor.Stroke()} 1 w {PdfNumber(MarginLeft)} {PdfNumber(y - 3)} m {PdfNumber(MarginLeft)} {PdfNumber(y - 18)} l S Q");
            AddWrappedRuns(runs, PdfFont.Regular, 10.8, 15.5, MarginLeft + 18, TextWidth - 18, MutedTextColor);
            AddSpace(5);
        }

        public void AddCodeLine(string text)
        {
            var lines = WrapText(text.TrimEnd(), PdfFont.Mono, 9.2, TextWidth - 20);
            foreach (var wrappedLine in lines.DefaultIfEmpty(string.Empty))
            {
                EnsureSpace(15);
                DrawBackground(MarginLeft - 4, y - 4, TextWidth + 8, 15, CodeBackground);
                DrawText(wrappedLine, PdfFont.Mono, 9.2, MarginLeft + 8, y, MutedTextColor);
                y -= 15;
            }
        }

        public void AddTable(IReadOnlyList<TableRow> rows)
        {
            if (rows.Count == 0)
            {
                return;
            }

            AddSpace(4);

            var columnCount = rows.Max(row => row.Cells.Count);
            var widths = CalculateTableColumnWidths(rows, columnCount);
            var xPositions = new double[columnCount];
            var currentX = MarginLeft;

            for (var i = 0; i < columnCount; i++)
            {
                xPositions[i] = currentX;
                currentX += widths[i];
            }

            foreach (var row in rows)
            {
                var font = row.IsHeader ? PdfFont.Bold : PdfFont.Regular;
                var fontSize = row.IsHeader ? 9.8 : 9.5;
                var lineHeight = 13.5;
                var wrappedCells = new List<List<List<TextRun>>>();
                var rowHeight = 0d;

                for (var i = 0; i < columnCount; i++)
                {
                    var runs = i < row.Cells.Count ? row.Cells[i].Runs : [new TextRun(string.Empty, false, HighlightFallback)];
                    var wrapped = WrapRuns(runs, font, fontSize, widths[i] - 14);
                    wrappedCells.Add(wrapped);
                    rowHeight = Math.Max(rowHeight, Math.Max(1, wrapped.Count) * lineHeight + 12);
                }

                EnsureSpace(rowHeight);

                for (var i = 0; i < columnCount; i++)
                {
                    var cellX = xPositions[i];
                    var cellColor = row.IsHeader ? TableHeaderBackground : TableCellBackground;
                    DrawBackground(cellX, y - rowHeight + 4, widths[i], rowHeight, cellColor);
                    DrawRectangle(cellX, y - rowHeight + 4, widths[i], rowHeight, RuleColor);

                    var cellY = y - 10;
                    foreach (var line in wrappedCells[i])
                    {
                        DrawTextRunLine(line, font, fontSize, cellX + 7, cellY, lineHeight, row.IsHeader ? TextColor : MutedTextColor);
                        cellY -= lineHeight;
                    }
                }

                y -= rowHeight;
            }

            AddSpace(8);
        }

        public void AddRule()
        {
            EnsureSpace(18);
            Current.Content.AppendLine($"q {RuleColor.Stroke()} 0.8 w {PdfNumber(MarginLeft)} {PdfNumber(y)} m {PdfNumber(PageWidth - MarginRight)} {PdfNumber(y)} l S Q");
            y -= 18;
        }

        public void AddSpace(double amount)
        {
            y -= amount;
            if (y < MarginBottom)
            {
                NewPage();
            }
        }

        private PdfPage Current => Pages[^1];

        private void AddWrappedRuns(IReadOnlyList<TextRun> runs, PdfFont font, double fontSize, double lineHeight, double x, double maxWidth, PdfColor color)
        {
            foreach (var line in WrapRuns(runs, font, fontSize, maxWidth))
            {
                EnsureSpace(lineHeight);
                DrawTextRunLine(line, font, fontSize, x, y, lineHeight, color);
                y -= lineHeight;
            }
        }

        private void DrawTextRunLine(IReadOnlyList<TextRun> runs, PdfFont font, double size, double x, double baseline, double lineHeight, PdfColor textColor)
        {
            var currentX = x;
            foreach (var run in runs)
            {
                if (string.IsNullOrEmpty(run.Text))
                {
                    continue;
                }

                var width = Measure(run.Text, font, size);
                if (run.Highlight)
                {
                    DrawBackground(currentX - 1.5, baseline - 2.5, Math.Max(width + 3, 4), lineHeight - 2, run.HighlightColor);
                }

                currentX += width;
            }

            var fullText = string.Concat(runs.Select(run => run.Text));
            DrawText(fullText, font, size, x, baseline, textColor);
        }

        private void DrawText(string text, PdfFont font, double size, double x, double baseline, PdfColor color)
        {
            Current.Content.AppendLine($"BT {color.Fill()} /{font.ResourceName} {PdfNumber(size)} Tf 1 0 0 1 {PdfNumber(x)} {PdfNumber(baseline)} Tm ({EscapePdfText(text)}) Tj ET");
        }

        private void DrawBackground(double x, double yPosition, double width, double height, PdfColor color)
        {
            Current.Content.AppendLine($"q {color.Fill()} {PdfNumber(x)} {PdfNumber(yPosition)} {PdfNumber(width)} {PdfNumber(height)} re f Q");
        }

        private void DrawRectangle(double x, double yPosition, double width, double height, PdfColor color)
        {
            Current.Content.AppendLine($"q {color.Stroke()} 0.6 w {PdfNumber(x)} {PdfNumber(yPosition)} {PdfNumber(width)} {PdfNumber(height)} re S Q");
        }

        private void EnsureSpace(double needed)
        {
            if (y - needed < MarginBottom)
            {
                NewPage();
            }
        }

        private void NewPage()
        {
            AddFooter(string.Empty);
            Pages.Add(new PdfPage());
            y = PageHeight - MarginTop;
        }

        private void AddFooter(string title)
        {
            var pageNumber = Pages.Count;
            var label = string.IsNullOrWhiteSpace(title) ? $"Markit - pagina {pageNumber}" : $"{title} - pagina {pageNumber}";
            Current.Content.AppendLine($"BT 0.42 0.47 0.55 rg /F1 8 Tf 1 0 0 1 {PdfNumber(MarginLeft)} 30 Tm ({EscapePdfText(label)}) Tj ET");
        }
    }

    private sealed record MarkdownLine(string Raw, string Structural, bool ForceHighlight, PdfColor HighlightColor);

    private sealed record TextRun(string Text, bool Highlight, PdfColor HighlightColor);

    private sealed class TableRow(List<TableCell> cells)
    {
        public List<TableCell> Cells { get; } = cells;

        public bool IsHeader { get; set; }
    }

    private sealed record TableCell(IReadOnlyList<TextRun> Runs);

    private sealed class PdfPage
    {
        public StringBuilder Content { get; } = new();
    }

    private sealed record PdfFont(string ResourceName, double WidthFactor)
    {
        public static readonly PdfFont Regular = new("F1", 0.52);
        public static readonly PdfFont Bold = new("F2", 0.55);
        public static readonly PdfFont Mono = new("F3", 0.6);
    }

    private sealed record PdfColor(double Red, double Green, double Blue)
    {
        public string Fill()
        {
            return $"{PdfNumber(Red)} {PdfNumber(Green)} {PdfNumber(Blue)} rg";
        }

        public string Stroke()
        {
            return $"{PdfNumber(Red)} {PdfNumber(Green)} {PdfNumber(Blue)} RG";
        }

        public static PdfColor FromHex(string hex)
        {
            var value = hex.TrimStart('#');
            var red = int.Parse(value[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255d;
            var green = int.Parse(value.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255d;
            var blue = int.Parse(value.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255d;
            return new PdfColor(red, green, blue);
        }
    }

    private static double[] CalculateTableColumnWidths(IReadOnlyList<TableRow> rows, int columnCount)
    {
        var minimumWidth = 70d;
        var widths = Enumerable.Repeat(minimumWidth, columnCount).ToArray();

        foreach (var row in rows)
        {
            for (var i = 0; i < row.Cells.Count; i++)
            {
                var text = string.Concat(row.Cells[i].Runs.Select(run => run.Text));
                widths[i] = Math.Max(widths[i], Math.Min(170, Measure(text, row.IsHeader ? PdfFont.Bold : PdfFont.Regular, 9.5) + 18));
            }
        }

        var total = widths.Sum();
        if (total <= TextWidth)
        {
            var remaining = TextWidth - total;
            for (var i = 0; i < widths.Length; i++)
            {
                widths[i] += remaining / widths.Length;
            }

            return widths;
        }

        var scale = TextWidth / total;
        for (var i = 0; i < widths.Length; i++)
        {
            widths[i] = Math.Max(52, widths[i] * scale);
        }

        return widths;
    }

    private static List<List<TextRun>> WrapRuns(IReadOnlyList<TextRun> runs, PdfFont font, double size, double maxWidth)
    {
        var lines = new List<List<TextRun>>();
        var current = new List<TextRun>();
        var currentWidth = 0d;

        foreach (var run in runs)
        {
            var normalized = NormalizePdfText(run.Text);
            foreach (Match tokenMatch in Regex.Matches(normalized, @"\s+|\S+"))
            {
                var tokenValue = Regex.IsMatch(tokenMatch.Value, @"^\s+$") ? " " : tokenMatch.Value;
                if (tokenValue == " " && current.Count == 0)
                {
                    continue;
                }

                foreach (var token in SplitToken(tokenValue, font, size, maxWidth))
                {
                    var tokenWidth = Measure(token, font, size);
                    if (current.Count > 0 && currentWidth + tokenWidth > maxWidth)
                    {
                        lines.Add(TrimLineEnd(current));
                        current = [];
                        currentWidth = 0;
                    }

                    current.Add(new TextRun(token, run.Highlight, run.HighlightColor));
                    currentWidth += tokenWidth;
                }
            }
        }

        if (current.Count > 0)
        {
            lines.Add(TrimLineEnd(current));
        }

        return lines.Count == 0 ? [[new TextRun(string.Empty, false, HighlightFallback)]] : lines;
    }

    private static IEnumerable<string> SplitToken(string token, PdfFont font, double size, double maxWidth)
    {
        if (Measure(token, font, size) <= maxWidth)
        {
            yield return token;
            yield break;
        }

        var chunk = new StringBuilder();
        foreach (var character in token)
        {
            var candidate = chunk.ToString() + character;
            if (chunk.Length > 0 && Measure(candidate, font, size) > maxWidth)
            {
                yield return chunk.ToString();
                chunk.Clear();
            }

            chunk.Append(character);
        }

        if (chunk.Length > 0)
        {
            yield return chunk.ToString();
        }
    }

    private static List<TextRun> TrimLineEnd(List<TextRun> runs)
    {
        for (var i = runs.Count - 1; i >= 0; i--)
        {
            var trimmed = runs[i].Text.TrimEnd();
            if (trimmed.Length == runs[i].Text.Length)
            {
                break;
            }

            if (trimmed.Length == 0)
            {
                runs.RemoveAt(i);
                continue;
            }

            runs[i] = runs[i] with { Text = trimmed };
            break;
        }

        return runs;
    }

    private static List<string> WrapText(string text, PdfFont font, double size, double maxWidth)
    {
        return WrapRuns([new TextRun(text, false, HighlightFallback)], font, size, maxWidth)
            .Select(line => string.Concat(line.Select(run => run.Text)))
            .ToList();
    }

    private static double Measure(string text, PdfFont font, double size)
    {
        return text.Sum(character => CharacterWidth(character, font.WidthFactor) * size);
    }

    private static double CharacterWidth(char character, double baseWidth)
    {
        return character switch
        {
            'i' or 'l' or 'I' or '|' or '.' or ',' or ':' or ';' or '\'' => baseWidth * 0.45,
            'm' or 'w' or 'M' or 'W' => baseWidth * 1.35,
            ' ' => baseWidth * 0.55,
            _ => baseWidth
        };
    }

    private static string NormalizePdfText(string text)
    {
        return text.Replace('\u2018', '\'')
            .Replace('\u2019', '\'')
            .Replace('\u201C', '"')
            .Replace('\u201D', '"')
            .Replace('\u2013', '-')
            .Replace('\u2014', '-')
            .Replace('\u2022', (char)149)
            .Replace('\u00A0', ' ');
    }

    private static string EscapePdfText(string text)
    {
        var normalized = NormalizePdfText(text);
        var escaped = new StringBuilder(normalized.Length);

        foreach (var character in normalized)
        {
            escaped.Append(character switch
            {
                '\\' => @"\\",
                '(' => @"\(",
                ')' => @"\)",
                '\n' or '\r' or '\t' => " ",
                >= (char)32 and <= (char)255 => character,
                _ => '?'
            });
        }

        return escaped.ToString();
    }

    private static string PdfNumber(double value)
    {
        return value.ToString("0.###", CultureInfo.InvariantCulture);
    }

    private static class PdfWriter
    {
        public static byte[] Write(IReadOnlyList<PdfPage> pages, string title)
        {
            var objects = new SortedDictionary<int, byte[]>();
            var pageObjectIds = new List<int>();

            objects[1] = Encode("<< /Type /Catalog /Pages 2 0 R >>");
            objects[3] = Encode("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");
            objects[4] = Encode("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>");
            objects[5] = Encode("<< /Type /Font /Subtype /Type1 /BaseFont /Courier /Encoding /WinAnsiEncoding >>");

            for (var i = 0; i < pages.Count; i++)
            {
                var pageObjectId = 6 + i * 2;
                var contentObjectId = pageObjectId + 1;
                pageObjectIds.Add(pageObjectId);

                var contentBytes = Encode(pages[i].Content.ToString());
                objects[pageObjectId] = Encode($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {PdfNumber(PageWidth)} {PdfNumber(PageHeight)}] /Resources << /Font << /F1 3 0 R /F2 4 0 R /F3 5 0 R >> >> /Contents {contentObjectId} 0 R >>");
                objects[contentObjectId] = Combine(Encode($"<< /Length {contentBytes.Length} >>\nstream\n"), contentBytes, Encode("endstream"));
            }

            objects[2] = Encode($"<< /Type /Pages /Count {pages.Count} /Kids [{string.Join(' ', pageObjectIds.Select(id => $"{id} 0 R"))}] >>");

            var output = new List<byte>();
            Append(output, "%PDF-1.4\n");
            var offsets = new Dictionary<int, int>();

            foreach (var (id, body) in objects)
            {
                offsets[id] = output.Count;
                Append(output, $"{id} 0 obj\n");
                output.AddRange(body);
                Append(output, "\nendobj\n");
            }

            var xrefOffset = output.Count;
            var maxObjectId = objects.Keys.Max();
            Append(output, $"xref\n0 {maxObjectId + 1}\n");
            Append(output, "0000000000 65535 f \n");

            for (var id = 1; id <= maxObjectId; id++)
            {
                var offset = offsets.TryGetValue(id, out var value) ? value : 0;
                Append(output, $"{offset:0000000000} 00000 n \n");
            }

            Append(output, $"trailer\n<< /Size {maxObjectId + 1} /Root 1 0 R /Info << /Title ({EscapePdfText(title)}) /Producer (Markit) >> >>\nstartxref\n{xrefOffset}\n%%EOF");
            return output.ToArray();
        }

        private static byte[] Encode(string text)
        {
            return Encoding.Latin1.GetBytes(text);
        }

        private static void Append(List<byte> output, string text)
        {
            output.AddRange(Encode(text));
        }

        private static byte[] Combine(params byte[][] chunks)
        {
            var length = chunks.Sum(chunk => chunk.Length);
            var result = new byte[length];
            var offset = 0;
            foreach (var chunk in chunks)
            {
                Buffer.BlockCopy(chunk, 0, result, offset, chunk.Length);
                offset += chunk.Length;
            }

            return result;
        }
    }
}
