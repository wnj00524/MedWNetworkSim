using SkiaSharp;

namespace MedWNetworkSim.Rendering;

public sealed class PieChartSlice
{
    public string Label { get; set; } = "";
    public double Value { get; set; }
    public SKColor? Color { get; set; }
}

public sealed class PieChartModel
{
    public List<PieChartSlice> Slices { get; set; } = new();
}

public sealed record PieChartLegendItem(string Label, double Value, double Share, SKColor Color);

public sealed class PieChartRenderer
{
    private static readonly SKColor[] StablePalette =
    [
        SKColor.Parse("#37A7FF"),
        SKColor.Parse("#2FD38F"),
        SKColor.Parse("#E8B24A"),
        SKColor.Parse("#EF5B5B"),
        SKColor.Parse("#C27DFF"),
        SKColor.Parse("#FF8A5B"),
        SKColor.Parse("#69D2E7")
    ];

    public void Draw(SKCanvas canvas, PieChartModel model, SKRect bounds)
    {
        var slices = BuildLegend(model);
        var total = slices.Sum(slice => slice.Value);
        if (total <= 0d)
        {
            return;
        }

        var startAngle = 0f;

        foreach (var slice in slices)
        {
            var sweep = (float)(slice.Value / total * 360f);

            using var paint = new SKPaint
            {
                Style = SKPaintStyle.Fill,
                Color = slice.Color,
                IsAntialias = true
            };

            canvas.DrawArc(bounds, startAngle, sweep, true, paint);

            startAngle += sweep;
        }
    }

    public void DrawLegend(SKCanvas canvas, PieChartModel model, SKPoint origin)
    {
        var legend = BuildLegend(model);
        using var paint = new SKPaint { IsAntialias = true };
        using var font = new SKFont { Size = 12f };
        var y = origin.Y;
        foreach (var item in legend)
        {
            paint.Style = SKPaintStyle.Fill;
            paint.Color = item.Color;
            canvas.DrawRoundRect(new SKRect(origin.X, y - 10f, origin.X + 10f, y), 5f, 5f, paint);
            paint.Color = new SKColor(230, 235, 245);
            canvas.DrawText($"{item.Label} {item.Share:P0}", origin.X + 16f, y, SKTextAlign.Left, font, paint);
            y += 18f;
        }
    }

    public IReadOnlyList<PieChartLegendItem> BuildLegend(PieChartModel model)
    {
        var total = model.Slices.Sum(slice => Math.Max(0d, slice.Value));
        return model.Slices
            .Select((slice, index) =>
            {
                var value = Math.Max(0d, slice.Value);
                return new PieChartLegendItem(
                    slice.Label,
                    value,
                    total <= 0d ? 0d : value / total,
                    slice.Color ?? GetColor(slice.Label, index));
            })
            .ToArray();
    }

    private static SKColor GetColor(string key, int index)
    {
        if (key.Contains("unmet", StringComparison.OrdinalIgnoreCase) ||
            key.Contains("blocked", StringComparison.OrdinalIgnoreCase) ||
            key.Contains("high", StringComparison.OrdinalIgnoreCase))
        {
            return SKColor.Parse("#EF5B5B");
        }

        if (key.Contains("delivered", StringComparison.OrdinalIgnoreCase) ||
            key.Contains("enabled", StringComparison.OrdinalIgnoreCase) ||
            key.Contains("balanced", StringComparison.OrdinalIgnoreCase))
        {
            return SKColor.Parse("#2FD38F");
        }

        if (key.Contains("idle", StringComparison.OrdinalIgnoreCase) ||
            key.Contains("medium", StringComparison.OrdinalIgnoreCase) ||
            key.Contains("warning", StringComparison.OrdinalIgnoreCase))
        {
            return SKColor.Parse("#E8B24A");
        }

        if (string.IsNullOrWhiteSpace(key))
        {
            return StablePalette[index % StablePalette.Length];
        }

        var stable = Math.Abs(StableHash(key));
        return StablePalette[stable % StablePalette.Length];
    }

    private static int StableHash(string value)
    {
        unchecked
        {
            var hash = 23;
            foreach (var ch in value)
            {
                hash = (hash * 31) + char.ToUpperInvariant(ch);
            }

            return hash == int.MinValue ? 0 : hash;
        }
    }
}
