namespace RadioMan.Dcs;

/// One aircraft's last known state — could be a human player OR an AI flight.
/// For AI units PlayerName is null. PlayerByCallsign in IDcsClient only returns
/// human-controlled aircraft; AllAircraft returns everything.
public sealed record AircraftSnapshot(
    uint Id,
    string Name,            // unit name in the mission editor
    string Callsign,        // radio callsign set on the unit (e.g. "Enfield 1-1")
    string? PlayerName,     // null for AI; the human's display name otherwise
    string AircraftType,    // e.g. "FA-18C_hornet"
    string Coalition,       // "Blue" / "Red" / "Neutral"
    double Lat,
    double Lon,
    double AltFt,
    double HeadingDeg,
    double SpeedKts,
    DateTime UpdatedAt)
{
    public TimeSpan Age => DateTime.UtcNow - UpdatedAt;
    public bool IsPlayer => !string.IsNullOrEmpty(PlayerName);

    /// Airborne = moving fast enough to not be parked. Filters out cold aircraft
    /// at ramp from the tactical picture.
    public bool IsAirborne => SpeedKts > 50;
}

/// The bullseye reference point for a coalition. Mission-static; fetched once
/// on first request and cached forever.
public sealed record BullseyePoint(double Lat, double Lon);
