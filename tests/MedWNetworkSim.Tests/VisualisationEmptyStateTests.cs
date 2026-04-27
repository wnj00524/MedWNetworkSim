using MedWNetworkSim.App.Models;
using MedWNetworkSim.App.VisualAnalytics;
using MedWNetworkSim.App.VisualAnalytics.Sankey;
using MedWNetworkSim.Rendering;
using MedWNetworkSim.Rendering.Geo;
using SkiaSharp;
using Xunit;

namespace MedWNetworkSim.Tests;

public sealed class VisualisationEmptyStateTests
{
    [Fact]
    public void SankeyMode_WithNoTrafficOutcomes_ReturnsEmptyStateMessage()
    {
        var model = new SankeyProjectionService().Build(new VisualAnalyticsSnapshot
        {
            Network = new NetworkModel(),
            TrafficOutcomes = [],
            ConsumerCosts = [],
            Period = 0
        });

        Assert.Equal("Run a simulation to build the Sankey view.", model.EmptyStateMessage);
    }

    [Fact]
    public void MapMode_WithNoCoordinates_ReturnsMapEmptyStateMessage()
    {
        using var bitmap = new SKBitmap(640, 360);
        using var canvas = new SKCanvas(bitmap);
        var renderer = new MapGraphRenderer();
        renderer.Render(canvas, new GraphScene(), new GraphViewport(), new GraphSize(640, 360), new Dictionary<string, MapGeoCoordinate>(), showBackground: true, out var fallbackMessage);

        Assert.Equal("Drag on the map to select an area, then choose Import selected area.", fallbackMessage);
    }
}
