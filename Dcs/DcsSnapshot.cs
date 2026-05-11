namespace RadioMan.Dcs;

/// One frame of game state pushed by the Export.lua script.
/// Sent ~once per second over UDP, parsed by DcsExportClient.
public sealed record DcsSnapshot(
    string? Callsign,
    double Lat,
    double Lon,
    double AltFt,           // feet MSL
    double Heading,         // degrees true (0 = north, 90 = east)
    double SpeedKts,        // indicated airspeed
    bool GearDown,
    int WindFromTrue,       // direction the wind is coming FROM, degrees true
    int WindKts)
{
    public DateTime ReceivedAt { get; init; } = DateTime.UtcNow;
    public TimeSpan Age => DateTime.UtcNow - ReceivedAt;
}
