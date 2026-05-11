using RadioMan.Agents;
using RadioMan.Dcs;

namespace RadioMan.Conditions;

/// AWACS proactive merge detection, multi-AWACS aware.
///
/// One supervisor watch scans every (recipient × friendly × hostile) triple
/// across all AWACS the player base is checked into. For each in-range triple
/// it registers a per-pair 5 s watch keyed `merge:{recipient}:{f}:{h}`.
///
/// Per-pair watches:
///   - fire "Viper 2-1, Magic, merged." when range drops below MergeRangeNm
///     (using the recipient name the pilot checked into)
///   - self-exit when the pair separates past DeactivationRangeNm,
///     when the pilot checks out, or when either unit disappears
public sealed class MergeDetector
{
    public double ActivationRangeNm { get; init; } = 50.0;
    public double DeactivationRangeNm { get; init; } = 60.0;
    public double MergeRangeNm { get; init; } = 3.0;
    public TimeSpan PairTick { get; init; } = TimeSpan.FromSeconds(5);
    public TimeSpan SupervisorTick { get; init; } = TimeSpan.FromSeconds(30);
    public TimeSpan FireCooldown { get; init; } = TimeSpan.FromMinutes(2);

    private readonly RadioAgent _awacs;
    private readonly WatchScheduler _scheduler;
    private readonly AwacsRoster _roster;
    private readonly Dictionary<(string Recipient, uint Friendly, uint Hostile), DateTime> _lastFired = new();

    public MergeDetector(RadioAgent awacs, WatchScheduler scheduler, AwacsRoster roster)
    {
        _awacs = awacs;
        _scheduler = scheduler;
        _roster = roster;
    }

    public void Start()
    {
        _scheduler.Register(new Watch
        {
            Id = "merge-supervisor",
            Interval = SupervisorTick,
            OnTick = Supervise,
            ShouldExit = _ => false,
        });
    }

    private ScheduledCall? Supervise(IDcsClient dcs)
    {
        if (!dcs.HasFreshData) return null;

        // Only AWACS recipients that have at least one checked-in pilot are
        // worth scanning. New recipients get added the moment any pilot
        // checks in with them.
        foreach (var recipient in _roster.Recipients)
        {
            foreach (var friendly in dcs.AllPlayers)
            {
                if (!friendly.IsAirborne) continue;
                if (!_roster.IsCheckedIn(recipient, friendly)) continue;

                var hostiles = Awacs.HostilesFor(dcs.AllAircraft, friendly.Coalition);
                foreach (var hostile in hostiles)
                {
                    var dist = Geo.DistanceNm(friendly.Lat, friendly.Lon, hostile.Lat, hostile.Lon);
                    if (dist > ActivationRangeNm) continue;

                    var id = PairId(recipient, friendly.Id, hostile.Id);
                    if (_scheduler.Has(id)) continue;
                    _scheduler.Register(MakePairWatch(recipient, friendly.Id, hostile.Id));
                }
            }
        }
        return null;
    }

    private Watch MakePairWatch(string recipient, uint friendlyId, uint hostileId)
    {
        return new Watch
        {
            Id = PairId(recipient, friendlyId, hostileId),
            Interval = PairTick,
            OnTick = dcs => CheckMerge(dcs, recipient, friendlyId, hostileId),
            ShouldExit = dcs =>
            {
                var f = dcs.AllAircraft.FirstOrDefault(a => a.Id == friendlyId);
                var h = dcs.AllAircraft.FirstOrDefault(a => a.Id == hostileId);
                if (f is null || h is null) return true;

                // Pilot checked out of this specific AWACS — drop.
                if (!_roster.IsCheckedIn(recipient, f)) return true;

                var dist = Geo.DistanceNm(f.Lat, f.Lon, h.Lat, h.Lon);
                return dist > DeactivationRangeNm;
            },
        };
    }

    private ScheduledCall? CheckMerge(IDcsClient dcs, string recipient, uint friendlyId, uint hostileId)
    {
        var friendly = dcs.AllAircraft.FirstOrDefault(a => a.Id == friendlyId);
        var hostile = dcs.AllAircraft.FirstOrDefault(a => a.Id == hostileId);
        if (friendly is null || hostile is null) return null;

        var dist = Geo.DistanceNm(friendly.Lat, friendly.Lon, hostile.Lat, hostile.Lon);
        if (dist > MergeRangeNm) return null;

        var key = (recipient, friendlyId, hostileId);
        var now = DateTime.UtcNow;
        if (_lastFired.TryGetValue(key, out var last) && now - last < FireCooldown)
            return null;
        _lastFired[key] = now;

        var caller = ResolveCaller(friendly);
        return new ScheduledCall(_awacs, $"{caller}, {recipient}, merged.");
    }

    private static string PairId(string recipient, uint friendlyId, uint hostileId)
        => $"merge:{recipient}:{friendlyId}:{hostileId}";

    private static string ResolveCaller(AircraftSnapshot a)
    {
        if (!string.IsNullOrWhiteSpace(a.Callsign)) return a.Callsign;
        if (!string.IsNullOrWhiteSpace(a.PlayerName)) return a.PlayerName!;
        return a.Name;
    }
}
