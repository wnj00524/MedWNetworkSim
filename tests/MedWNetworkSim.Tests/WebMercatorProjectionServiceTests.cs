using MedWNetworkSim.App.Geo;
using Xunit;

namespace MedWNetworkSim.Tests;

public sealed class WebMercatorProjectionServiceTests
{
    [Theory]
    [InlineData(37.7749, -122.4194)]
    [InlineData(40.7128, -74.0060)]
    [InlineData(34.0522, -118.2437)]
    public void ProjectThenUnproject_RoundTripsWithinTolerance(double latitude, double longitude)
    {
        var service = new WebMercatorProjectionService();
        var viewport = new GeoProjectionViewport(1280, 720, 39.5, -98.35, 0.0012d);

        var projected = service.Project(new GeoCoordinate(latitude, longitude), viewport);
        var unprojected = service.Unproject(projected.X, projected.Y, viewport);

        Assert.InRange(Math.Abs(unprojected.Latitude - latitude), 0d, 0.0001d);
        Assert.InRange(Math.Abs(unprojected.Longitude - longitude), 0d, 0.0001d);
    }
}
