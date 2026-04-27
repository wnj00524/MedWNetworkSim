namespace MedWNetworkSim.App.Geo;

public readonly record struct GeoCoordinate(double Latitude, double Longitude);

public readonly record struct GeoProjectionViewport(double Width, double Height, double CenterLatitude, double CenterLongitude, double Zoom);

public interface IGeoProjectionService
{
    (double X, double Y) Project(GeoCoordinate coordinate, GeoProjectionViewport viewport);
    GeoCoordinate Unproject(double x, double y, GeoProjectionViewport viewport);
}

public sealed class WebMercatorProjectionService : IGeoProjectionService
{
    private const double EarthRadius = 6378137d;
    private const double MinLatitude = -85.05112878d;
    private const double MaxLatitude = 85.05112878d;

    public (double X, double Y) Project(GeoCoordinate coordinate, GeoProjectionViewport viewport)
    {
        var clampedLat = Math.Clamp(coordinate.Latitude, MinLatitude, MaxLatitude);
        var latRad = DegreesToRadians(clampedLat);
        var lonRad = DegreesToRadians(NormalizeLongitude(coordinate.Longitude));

        var xMeters = EarthRadius * lonRad;
        var yMeters = EarthRadius * Math.Log(Math.Tan((Math.PI / 4d) + (latRad / 2d)));

        var center = ProjectToMeters(new GeoCoordinate(viewport.CenterLatitude, viewport.CenterLongitude));
        var scale = Math.Max(0.0001d, viewport.Zoom);

        return ((xMeters - center.X) * scale + (viewport.Width / 2d), (center.Y - yMeters) * scale + (viewport.Height / 2d));
    }

    public GeoCoordinate Unproject(double x, double y, GeoProjectionViewport viewport)
    {
        var center = ProjectToMeters(new GeoCoordinate(viewport.CenterLatitude, viewport.CenterLongitude));
        var scale = Math.Max(0.0001d, viewport.Zoom);
        var xMeters = ((x - (viewport.Width / 2d)) / scale) + center.X;
        var yMeters = center.Y - ((y - (viewport.Height / 2d)) / scale);

        var lon = RadiansToDegrees(xMeters / EarthRadius);
        var lat = Math.Clamp(RadiansToDegrees((2d * Math.Atan(Math.Exp(yMeters / EarthRadius))) - (Math.PI / 2d)), MinLatitude, MaxLatitude);
        return new GeoCoordinate(lat, NormalizeLongitude(lon));
    }

    private static (double X, double Y) ProjectToMeters(GeoCoordinate coordinate)
    {
        var latRad = DegreesToRadians(Math.Clamp(coordinate.Latitude, MinLatitude, MaxLatitude));
        var lonRad = DegreesToRadians(NormalizeLongitude(coordinate.Longitude));
        return (EarthRadius * lonRad, EarthRadius * Math.Log(Math.Tan((Math.PI / 4d) + (latRad / 2d))));
    }

    private static double DegreesToRadians(double value) => value * Math.PI / 180d;
    private static double RadiansToDegrees(double value) => value * 180d / Math.PI;
    private static double NormalizeLongitude(double longitude)
    {
        var normalized = ((longitude + 180d) % 360d + 360d) % 360d - 180d;
        return normalized == -180d && longitude > 0d ? 180d : normalized;
    }
}
