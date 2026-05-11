namespace RadioMan.Dcs;

/// Always-empty DCS client. Use this when you want to run radio-man without
/// any DCS connection — for development, unit tests, or just trying out
/// the voice pipeline. Response generators see "no fresh data" and fall back
/// to their generic responses.
public sealed class OfflineDcsClient : IDcsClient
{
    public bool HasFreshData => false;
    public bool IsConnected => false;
    public IReadOnlyCollection<AircraftSnapshot> AllAircraft => Array.Empty<AircraftSnapshot>();
    public IReadOnlyCollection<AircraftSnapshot> AllPlayers => Array.Empty<AircraftSnapshot>();
    public AircraftSnapshot? PlayerByCallsign(string callsign) => null;
    public BullseyePoint? Bullseye(string coalition) => null;
    public void Dispose() { }
}
