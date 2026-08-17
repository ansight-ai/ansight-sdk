namespace Ansight.Location;

internal static class LocationMath
{
    private const double EarthRadiusMeters = 6_371_000;

    internal static double DistanceMeters(LocationSample first, LocationSample second)
    {
        var latitudeDelta = DegreesToRadians(second.Latitude - first.Latitude);
        var longitudeDelta = DegreesToRadians(second.Longitude - first.Longitude);
        var firstLatitude = DegreesToRadians(first.Latitude);
        var secondLatitude = DegreesToRadians(second.Latitude);
        var haversine = Math.Pow(Math.Sin(latitudeDelta / 2), 2)
                        + Math.Cos(firstLatitude) * Math.Cos(secondLatitude)
                        * Math.Pow(Math.Sin(longitudeDelta / 2), 2);
        return 2 * EarthRadiusMeters * Math.Asin(Math.Sqrt(haversine));
    }

    private static double DegreesToRadians(double degrees) => degrees * Math.PI / 180;
}
