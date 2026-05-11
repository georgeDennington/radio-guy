namespace RadioMan.Dcs;

/// Abstraction over the DCS data source. Implemented by:
///   - DcsGrpcClient — talks to a real DCS-gRPC server
///   - OfflineDcsClient — returns nothing; for tests or running without DCS
///
/// Response generators query through this interface so they can degrade
/// gracefully when no game data is available.
public interface IDcsClient : IDisposable
{
    /// True when we have any aircraft snapshots received within the last few seconds.
    bool HasFreshData { get; }

    /// True if the underlying transport is up (gRPC channel connected and streaming).
    bool IsConnected { get; }

    /// All currently-tracked aircraft, players and AI. Snapshot order is undefined.
    IReadOnlyCollection<AircraftSnapshot> AllAircraft { get; }

    /// Human-controlled aircraft only — everything where PlayerName is set.
    IReadOnlyCollection<AircraftSnapshot> AllPlayers { get; }

    /// Find a player by their radio callsign / DCS callsign / display name.
    /// Match is case-insensitive and tolerant of spaces and dashes — "Viper 2-1"
    /// matches "viper21", "Viper-2-1", "viper2-1", etc. Returns null for AI.
    AircraftSnapshot? PlayerByCallsign(string callsign);

    /// The bullseye reference point for the given coalition ("Red"/"Blue").
    /// Cached for the mission lifetime — fetched lazily on first call.
    /// Returns null if the gRPC channel isn't connected or the coalition is unknown.
    BullseyePoint? Bullseye(string coalition);
}
