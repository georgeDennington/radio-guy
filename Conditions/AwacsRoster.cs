using System.Collections.Concurrent;
using RadioMan.Dcs;

namespace RadioMan.Conditions;

/// Per-AWACS roster. Keyed by the AWACS recipient callsign (e.g. "Magic" /
/// "Wizard" / "Overlord") so a pilot can be checked in with one AWACS while
/// uncheckedin with another. Acts as the gate for proactive AWACS calls —
/// only checked-in pilots get merge/spike/splash announcements.
///
/// Thread-safe: AWACS response generator writes on the pipeline thread,
/// scheduler watches read on the scheduler thread.
public sealed class AwacsRoster
{
    // Outer key = AWACS recipient (normalized). Inner = set of normalized
    // pilot identifiers (callsign / player name / unit name).
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> _rosters = new();

    public void CheckIn(string recipient, string pilotCallsign)
    {
        if (string.IsNullOrWhiteSpace(recipient) || string.IsNullOrWhiteSpace(pilotCallsign)) return;
        var slice = _rosters.GetOrAdd(Normalize(recipient), _ => new(StringComparer.OrdinalIgnoreCase));
        slice[Normalize(pilotCallsign)] = 0;
    }

    public void CheckOut(string recipient, string pilotCallsign)
    {
        if (string.IsNullOrWhiteSpace(recipient) || string.IsNullOrWhiteSpace(pilotCallsign)) return;
        if (_rosters.TryGetValue(Normalize(recipient), out var slice))
            slice.TryRemove(Normalize(pilotCallsign), out _);
    }

    /// Is this pilot checked in with the given AWACS recipient?
    /// Matches against callsign, player name, or unit name (any normalized).
    public bool IsCheckedIn(string recipient, AircraftSnapshot pilot)
    {
        if (!_rosters.TryGetValue(Normalize(recipient), out var slice)) return false;
        return slice.ContainsKey(Normalize(pilot.Callsign))
            || slice.ContainsKey(Normalize(pilot.PlayerName ?? ""))
            || slice.ContainsKey(Normalize(pilot.Name));
    }

    public IReadOnlyCollection<string> Recipients => _rosters.Keys.ToArray();

    public IReadOnlyCollection<string> CheckedInWith(string recipient)
        => _rosters.TryGetValue(Normalize(recipient), out var slice)
            ? slice.Keys.ToArray()
            : Array.Empty<string>();

    private static string Normalize(string s)
        => new string((s ?? "").Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
}
