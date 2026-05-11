using System.Collections.Concurrent;
using RadioMan.Parsing;

namespace RadioMan.Dcs;

/// Observes the live DCS aircraft/ship/ground stream and keeps each role's
/// parser-recipient-list in sync. Runs on a slow timer (every 10 s) so role
/// changes propagate quickly enough but the cost stays trivial.
///
/// Also maintains the shared mutable Carriers dictionary for the airboss.
///
/// A `staticDefaults` set is union'd with the DCS-discovered set on every
/// reconcile pass, so the system still has working agents when DCS is offline
/// (test mode, no gRPC server) or when no matching units exist in the mission.
public sealed class RoleManager : IDisposable
{
    private readonly IDcsClient _dcs;
    private readonly RegexIntentParser? _awacsParser;
    private readonly RegexIntentParser? _jtacParser;
    private readonly RegexIntentParser? _airbossParser;
    private readonly ConcurrentDictionary<string, Carrier> _carriers;
    private readonly IReadOnlyCollection<string> _staticAwacs;
    private readonly IReadOnlyCollection<string> _staticJtacs;
    private readonly IReadOnlyCollection<string> _staticAirbosses;
    private readonly Timer _timer;

    private HashSet<string> _knownAwacs = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> _knownJtacs = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> _knownAirbosses = new(StringComparer.OrdinalIgnoreCase);

    public RoleManager(
        IDcsClient dcs,
        ConcurrentDictionary<string, Carrier> carriers,
        RegexIntentParser? awacsParser = null,
        RegexIntentParser? jtacParser = null,
        RegexIntentParser? airbossParser = null,
        IEnumerable<string>? staticAwacs = null,
        IEnumerable<string>? staticJtacs = null,
        IEnumerable<string>? staticAirbosses = null)
    {
        _dcs = dcs;
        _carriers = carriers;
        _awacsParser = awacsParser;
        _jtacParser = jtacParser;
        _airbossParser = airbossParser;
        _staticAwacs = staticAwacs?.ToArray() ?? Array.Empty<string>();
        _staticJtacs = staticJtacs?.ToArray() ?? Array.Empty<string>();
        _staticAirbosses = staticAirbosses?.ToArray() ?? Array.Empty<string>();

        // Initial sync uses the static defaults only. First reconcile pass
        // happens shortly after startup.
        SyncInitial();

        _timer = new Timer(_ => Reconcile(), null,
            dueTime: TimeSpan.FromSeconds(3),
            period: TimeSpan.FromSeconds(10));
    }

    private void SyncInitial()
    {
        _knownAwacs = new(_staticAwacs, StringComparer.OrdinalIgnoreCase);
        _knownJtacs = new(_staticJtacs, StringComparer.OrdinalIgnoreCase);
        _knownAirbosses = new(_staticAirbosses, StringComparer.OrdinalIgnoreCase);

        _awacsParser?.SetRecipients(_knownAwacs);
        _jtacParser?.SetRecipients(_knownJtacs);
        _airbossParser?.SetRecipients(_knownAirbosses);
    }

    private void Reconcile()
    {
        try
        {
            var foundAwacs = new HashSet<string>(_staticAwacs, StringComparer.OrdinalIgnoreCase);
            var foundJtacs = new HashSet<string>(_staticJtacs, StringComparer.OrdinalIgnoreCase);
            var foundCarriers = new HashSet<string>(_staticAirbosses, StringComparer.OrdinalIgnoreCase);

            foreach (var unit in _dcs.AllAircraft)
            {
                foreach (var role in UnitRoleMatcher.RolesFor(unit))
                {
                    var callsign = UnitRoleMatcher.CallsignFor(unit);
                    if (string.IsNullOrWhiteSpace(callsign)) continue;

                    switch (role)
                    {
                        case "AWACS":
                            foundAwacs.Add(callsign);
                            break;

                        case "JTAC":
                            foundJtacs.Add(callsign);
                            break;

                        case "Carrier":
                            foundCarriers.Add(callsign);
                            // Refresh the carrier record with current position +
                            // heading (BRC). Carriers steam, so this updates per
                            // reconcile tick.
                            _carriers[callsign] = new Carrier(
                                Name: unit.AircraftType,
                                AirbossCallsign: callsign,
                                Lat: unit.Lat,
                                Lon: unit.Lon,
                                BrcDeg: (int)Math.Round(unit.HeadingDeg),
                                AngledDeckOffsetDeg: -10);
                            break;
                    }
                }
            }

            ApplyDiff("AWACS", foundAwacs, ref _knownAwacs, _awacsParser);
            ApplyDiff("JTAC", foundJtacs, ref _knownJtacs, _jtacParser);
            ApplyDiff("AIRBOSS", foundCarriers, ref _knownAirbosses, _airbossParser);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[roles] reconcile threw: {ex.Message}");
        }
    }

    private static void ApplyDiff(
        string role,
        HashSet<string> found,
        ref HashSet<string> known,
        RegexIntentParser? parser)
    {
        if (found.SetEquals(known)) return;

        var added = found.Except(known).ToList();
        var removed = known.Except(found).ToList();
        if (added.Count > 0) Console.WriteLine($"[roles] +{role}: {string.Join(", ", added)}");
        if (removed.Count > 0) Console.WriteLine($"[roles] -{role}: {string.Join(", ", removed)}");

        known = found;
        parser?.SetRecipients(found);
    }

    public void Dispose() => _timer.Dispose();
}
