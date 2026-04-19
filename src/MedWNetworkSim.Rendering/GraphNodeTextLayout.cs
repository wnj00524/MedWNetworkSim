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
    public required IReadOnlyList<GraphWrappedTextLine> Lines { get; init; }
    public required double Height { get; init; }
}

public static class GraphNodeTextLayout
{
    public const double MinWidth = 132d;
    public const double MaxWidth = 248d;
    public const double DefaultWidth = 168d;
    public const double MinHeight = 118d;
    public const double HorizontalPadding = 14d;
    public const double TopPadding = 18d;
    public const double BottomPadding = 20d;

    private const double TitleLineHeight = 18d;
    private const double TypeLineHeight = 14d;
    private const double DetailLineHeight = 14d;
    private const double TitleCharacterWidth = 7.5d;
    private const double TypeCharacterWidth = 6.1d;
    private const double DetailCharacterWidth = 5.9d;

    public static double ComputeNodeWidth(string? title, string? typeLabel, IReadOnlyList<GraphNodeTextLine> detailLines)
    {
        var samples = new List<(string Text, GraphNodeTextKind Kind)>
        {
            (title ?? string.Empty, GraphNodeTextKind.Title),
            (typeLabel ?? string.Empty, GraphNodeTextKind.TypeLabel)
        };
        samples.AddRange(detailLines.Select(line => (line.Text, GraphNodeTextKind.Detail)));

        var contentWidth = samples
            .Where(sample => !string.IsNullOrWhiteSpace(sample.Text))
            .Select(sample => MeasureTextWidth(sample.Text.Trim(), sample.Kind))
            .DefaultIfEmpty(DefaultWidth - (HorizontalPadding * 2d))
            .Max();

        return Math.Clamp(contentWidth + (HorizontalPadding * 2d), MinWidth, MaxWidth);
    }

    public static GraphNodeTextLayoutResult BuildLayout(
        string? title,
        string? typeLabel,
        IReadOnlyList<GraphNodeTextLine> detailLines,
        double nodeWidth,
        int maxDetailLines = int.MaxValue)
    {
        var availableWidth = Math.Max(88d, nodeWidth - (HorizontalPadding * 2d));
        var wrappedLines = new List<GraphWrappedTextLine>();
        double totalHeight = TopPadding + BottomPadding;

        AppendWrappedLines(title, GraphNodeTextKind.Title, isEmphasized: true, isWarning: false, availableWidth, TitleLineHeight, wrappedLines, ref totalHeight);
        AppendWrappedLines(typeLabel, GraphNodeTextKind.TypeLabel, isEmphasized: false, isWarning: false, availableWidth, TypeLineHeight, wrappedLines, ref totalHeight);

        foreach (var detailLine in detailLines.Take(Math.Max(0, maxDetailLines)))
        {
            AppendWrappedLines(detailLine.Text, GraphNodeTextKind.Detail, detailLine.IsEmphasized, detailLine.IsWarning, availableWidth, DetailLineHeight, wrappedLines, ref totalHeight);
        }

        return new GraphNodeTextLayoutResult
        {
            Lines = wrappedLines,
            Height = Math.Max(MinHeight, totalHeight)
        };
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

    private static void AppendWrappedLines(
        string? text,
        GraphNodeTextKind kind,
        bool isEmphasized,
        bool isWarning,
        double availableWidth,
        double lineHeight,
        ICollection<GraphWrappedTextLine> lines,
        ref double totalHeight)
    {
        foreach (var line in WrapText(text, kind, availableWidth))
        {
            lines.Add(new GraphWrappedTextLine(line, kind, isEmphasized, isWarning));
            totalHeight += lineHeight;
        }
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
