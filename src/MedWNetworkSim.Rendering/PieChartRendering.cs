using SkiaSharp;

namespace MedWNetworkSim.Rendering;

public sealed class PieChartSlice
{
    public string Label { get; set; } = "";
    public double Value { get; set; }
}

public sealed class PieChartModel
{
    public List<PieChartSlice> Slices { get; set; } = new();
}

public sealed class PieChartRenderer
{
    public void Draw(SKCanvas canvas, PieChartModel model, SKRect bounds)
    {
        var total = model.Slices.Sum(slice => slice.Value);
        if (total <= 0d)
        {
            return;
        }

        var startAngle = 0f;

        foreach (var slice in model.Slices)
        {
            var sweep = (float)(slice.Value / total * 360f);

            using var paint = new SKPaint
            {
                Style = SKPaintStyle.Fill,
                Color = GetColor(slice.Label),
                IsAntialias = true
            };

            canvas.DrawArc(bounds, startAngle, sweep, true, paint);

            startAngle += sweep;
        }
    }

    private static SKColor GetColor(string key)
    {
        var hash = key.GetHashCode();
        return new SKColor(
            (byte)(hash & 0xFF),
            (byte)((hash >> 8) & 0xFF),
            (byte)((hash >> 16) & 0xFF));
    }
}
