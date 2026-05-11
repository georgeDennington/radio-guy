namespace RadioMan.Dcs;

/// A US Navy supercarrier (CVN) for the recovery cycle. The airboss reads off
/// position relative to "mom" (the carrier), so all distance/bearing calcs are
/// done from these coordinates rather than a fixed airfield.
///
/// BRC = Base Recovery Course — the true heading the ship steams during
/// recovery, picked to give a ~25 kts headwind across the deck. Real carriers
/// adjust this every cycle; for the MVP it's hardcoded.
public sealed record Carrier(
    string Name,                  // e.g. "CVN-71"
    string AirbossCallsign,       // e.g. "Boss" — what the airboss is called on the radio
    double Lat,
    double Lon,
    int BrcDeg,                   // base recovery course, degrees true
    int AngledDeckOffsetDeg);     // typically -10° from BRC (angled deck offset)

public static class Carriers
{
    /// One hardcoded carrier in the Black Sea, ~15nm NW of Batumi so it's
    /// reachable from the standard Caucasus mission start points.
    public static readonly Carrier Roosevelt = new(
        Name: "CVN-71",
        AirbossCallsign: "Boss",
        Lat: 41.8000,
        Lon: 41.4000,
        BrcDeg: 180,
        AngledDeckOffsetDeg: -10);
}
