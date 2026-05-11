namespace RadioMan.Dcs;

/// Small geographic helpers — distance in nautical miles, bearing in degrees true,
/// compass words from a bearing. Uses Haversine for distance and the standard
/// great-circle formula for bearing. Accurate enough for airfield-local work
/// (within ~20 nm), no flat-earth approximation needed at our scales.
public static class Geo
{
    private const double EarthRadiusNm = 3440.065;

    public static double DistanceNm(double lat1, double lon1, double lat2, double lon2)
    {
        var dLat = ToRad(lat2 - lat1);
        var dLon = ToRad(lon2 - lon1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(ToRad(lat1)) * Math.Cos(ToRad(lat2)) *
                Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return EarthRadiusNm * c;
    }

    /// Initial bearing FROM (lat1,lon1) TO (lat2,lon2), normalized to [0, 360).
    public static double BearingDeg(double lat1, double lon1, double lat2, double lon2)
    {
        var dLon = ToRad(lon2 - lon1);
        var y = Math.Sin(dLon) * Math.Cos(ToRad(lat2));
        var x = Math.Cos(ToRad(lat1)) * Math.Sin(ToRad(lat2)) -
                Math.Sin(ToRad(lat1)) * Math.Cos(ToRad(lat2)) * Math.Cos(dLon);
        var brg = Math.Atan2(y, x) * 180 / Math.PI;
        return (brg + 360) % 360;
    }

    /// Rounds a bearing to one of 8 compass words.
    public static string CompassFromBearing(double bearing)
    {
        string[] dirs =
        {
            "north", "northeast", "east", "southeast",
            "south", "southwest", "west", "northwest",
        };
        var idx = ((int)Math.Round(bearing / 45.0)) % 8;
        if (idx < 0) idx += 8;
        return dirs[idx];
    }

    /// Smallest signed angle from a to b, in [-180, 180].
    public static double AngleDiffDeg(double a, double b)
    {
        var d = ((b - a) % 360 + 540) % 360 - 180;
        return d;
    }

    private static double ToRad(double deg) => deg * Math.PI / 180.0;
}
