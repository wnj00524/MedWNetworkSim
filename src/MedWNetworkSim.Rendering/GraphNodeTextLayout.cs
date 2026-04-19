namespace MedWNetworkSim.Rendering;

public enum GraphNodeTextKind
{
    Title,
    TypeLabel,
    Detail
}

public readonly record struct GraphWrappedTextLine(
    string Text,
    GraphNodeTextKind Kind,
    bool IsEmphasized,
    bool IsWarning);

public sealed class GraphNodeTextLayoutResult
{
    public required double Width { get; init; }
    public required IReadOnlyList<GraphWrappedTextLine> Lines { get; init; }
    public required IReadOnlyList<GraphWrappedTextLine> TitleLines { get; init; }
    public required IReadOnlyList<GraphWrappedTextLine> TypeLines { get; init; }
    public required IReadOnlyList<GraphWrappedTextLine> DetailLines { get; init; }
    public required double ContentHeight { get; init; }
    public required double Height { get; init; }
}

public static class GraphNodeTextLayout
{
    public const double MinWidth = 132d;
    public const double MaxWidth = 320d;
    public const double DefaultWidth = 168d;
    public const double MinHeight = 118d;
    public const double HorizontalPadding = 14d;
    public const double TopPadding = 18d;
    public const double BottomPadding = 24d;

    private const double TitleLineHeight = 18d;
    private const double TypeLineHeight = 14d;
    private const double DetailLineHeight = 14d;
    private const double TitleCharacterWidth = 7.5d;
    private const double TypeCharacterWidth = 6.1d;
    private const double DetailCharacterWidth = 5.9d;
    private const double WidthStep = 12d;
    private const int AllowedExtraWrappedLines = 1;
    private const double AllowedExtraContentHeight = DetailLineHeight * 2d;

    public static double ComputeNodeWidth(string? title, string? typeLabel, IReadOnlyList<GraphNodeTextLine> detailLines)
    {
        return BuildLayout(title, typeLabel, detailLines).Width;
    }

    public static GraphNodeTextLayoutResult BuildLayout(
        string? title,
        string? typeLabel,
        IReadOnlyList<GraphNodeTextLine> detailLines,
        int maxDetailLines = int.MaxValue)
    {
        var layouts = EnumerateCandidateWidths()
            .Select(width => BuildLayoutForWidth(title, typeLabel, detailLines, width, maxDetailLines))
            .ToList();

        var minimumWrappedLineCount = layouts.Min(layout => layout.Lines.Count);
        var minimumContentHeight = layouts.Min(layout => layout.ContentHeight);
        var preferred = layouts.FirstOrDefault(layout =>
            layout.Lines.Count <= minimumWrappedLineCount + AllowedExtraWrappedLines &&
            layout.ContentHeight <= minimumContentHeight + AllowedExtraContentHeight)
            ?? layouts
                .OrderBy(layout => layout.Lines.Count)
                .ThenBy(layout => layout.ContentHeight)
                .ThenBy(layout => layout.Width)
                .First();

        var measuredWidth = MeasureMaxLineWidth(preferred.Lines);
        var finalWidth = Math.Clamp(measuredWidth + (HorizontalPadding * 2d), MinWidth, preferred.Width);

        return new GraphNodeTextLayoutResult
        {
            Width = finalWidth,
            Lines = preferred.Lines,
            TitleLines = preferred.TitleLines,
            TypeLines = preferred.TypeLines,
            DetailLines = preferred.DetailLines,
            ContentHeight = preferred.ContentHeight,
            Height = preferred.Height
        };
    }

    public static GraphNodeTextLayoutResult BuildLayout(
        string? title,
        string? typeLabel,
        IReadOnlyList<GraphNodeTextLine> detailLines,
        double nodeWidth,
        int maxDetailLines = int.MaxValue)
    {
        return BuildLayoutForWidth(title, typeLabel, detailLines, nodeWidth, maxDetailLines);
    }

    public static IReadOnlyList<string> WrapText(string? text, GraphNodeTextKind kind, double maxWidth)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var normalized = text.Trim();
        var words = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
        {
            return [normalized];
        }

        var lines = new List<string>();
        var current = string.Empty;
        foreach (var word in words)
        {
            if (MeasureTextWidth(word, kind) > maxWidth)
            {
                if (!string.IsNullOrWhiteSpace(current))
                {
                    lines.Add(current);
                    current = string.Empty;
                }

                lines.AddRange(BreakLongWord(word, kind, maxWidth));
                continue;
            }

            var candidate = string.IsNullOrWhiteSpace(current) ? word : $"{current} {word}";
            if (MeasureTextWidth(candidate, kind) <= maxWidth)
            {
                current = candidate;
                continue;
            }

            if (!string.IsNullOrWhiteSpace(current))
            {
                lines.Add(current);
            }

            current = word;
        }

        if (!string.IsNullOrWhiteSpace(current))
        {
            lines.Add(current);
        }

        return lines;
    }

    private static GraphNodeTextLayoutResult BuildLayoutForWidth(
        string? title,
        string? typeLabel,
        IReadOnlyList<GraphNodeTextLine> detailLines,
        double nodeWidth,
        int maxDetailLines)
    {
        var availableWidth = Math.Max(88d, nodeWidth - (HorizontalPadding * 2d));
        var titleLines = WrapSegment(title, GraphNodeTextKind.Title, isEmphasized: true, isWarning: false, availableWidth);
        var typeLines = WrapSegment(typeLabel, GraphNodeTextKind.TypeLabel, isEmphasized: false, isWarning: false, availableWidth);
        var detailWrappedLines = detailLines
            .Take(Math.Max(0, maxDetailLines))
            .SelectMany(detailLine => WrapSegment(detailLine.Text, GraphNodeTextKind.Detail, detailLine.IsEmphasized, detailLine.IsWarning, availableWidth))
            .ToList();

        var wrappedLines = titleLines
            .Concat(typeLines)
            .Concat(detailWrappedLines)
            .ToList();

        var contentHeight =
            (titleLines.Count * TitleLineHeight) +
            (typeLines.Count * TypeLineHeight) +
            (detailWrappedLines.Count * DetailLineHeight);

        return new GraphNodeTextLayoutResult
        {
            Width = Math.Clamp(nodeWidth, MinWidth, MaxWidth),
            Lines = wrappedLines,
            TitleLines = titleLines,
            TypeLines = typeLines,
            DetailLines = detailWrappedLines,
            ContentHeight = contentHeight,
            Height = Math.Max(MinHeight, TopPadding + BottomPadding + contentHeight)
        };
    }

    private static IReadOnlyList<GraphWrappedTextLine> WrapSegment(
        string? text,
        GraphNodeTextKind kind,
        bool isEmphasized,
        bool isWarning,
        double availableWidth)
    {
        return WrapText(text, kind, availableWidth)
            .Select(line => new GraphWrappedTextLine(line, kind, isEmphasized, isWarning))
            .ToList();
    }

    private static IReadOnlyList<string> BreakLongWord(string word, GraphNodeTextKind kind, double maxWidth)
    {
        var fragments = new List<string>();
        var current = string.Empty;
        foreach (var character in word)
        {
            var candidate = current + character;
            if (!string.IsNullOrEmpty(current) && MeasureTextWidth(candidate, kind) > maxWidth)
            {
                fragments.Add(current);
                current = character.ToString();
                continue;
            }

            current = candidate;
        }

        if (!string.IsNullOrEmpty(current))
        {
            fragments.Add(current);
        }

        return fragments;
    }

    private static IEnumerable<double> EnumerateCandidateWidths()
    {
        for (var width = MinWidth; width < MaxWidth; width += WidthStep)
        {
            yield return width;
        }

        yield return MaxWidth;
    }

    private static double MeasureMaxLineWidth(IReadOnlyList<GraphWrappedTextLine> lines)
    {
        return lines.Count == 0
            ? DefaultWidth - (HorizontalPadding * 2d)
            : lines.Max(line => MeasureTextWidth(line.Text, line.Kind));
    }

    private static double MeasureTextWidth(string text, GraphNodeTextKind kind)
    {
        var characterWidth = kind switch
        {
            GraphNodeTextKind.Title => TitleCharacterWidth,
            GraphNodeTextKind.TypeLabel => TypeCharacterWidth,
            _ => DetailCharacterWidth
        };

        return text.Sum(character => MeasureCharacterWidth(character, characterWidth));
    }

    private static double MeasureCharacterWidth(char character, double baseWidth)
    {
        if (char.IsWhiteSpace(character))
        {
            return baseWidth * 0.45d;
        }

        if ("il.:,'|!".IndexOf(character) >= 0)
        {
            return baseWidth * 0.45d;
        }

        if ("MW@#%&".IndexOf(character) >= 0)
        {
            return baseWidth * 1.25d;
        }

        if (char.IsUpper(character))
        {
            return baseWidth * 1.08d;
        }

        if (char.IsDigit(character))
        {
            return baseWidth * 0.9d;
        }

        return baseWidth;
    }
}
