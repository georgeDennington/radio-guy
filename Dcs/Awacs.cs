namespace RadioMan.Dcs;

/// AWACS-specific tactical analysis. Pure functions over snapshots — no I/O.
public static class Awacs
{
    /// Greedy clustering: pick first ungrouped contact, sweep up everything
    /// within `radiusNm` of it, repeat. Order-dependent but good enough for
    /// the picture call where pilots don't compare against prior frames.
    public static IReadOnlyList<HostileGroup> GroupHostiles(
        IReadOnlyCollection<AircraftSnapshot> hostiles,
        double radiusNm = 5.0)
    {
        var ungrouped = hostiles.ToList();
        var groups = new List<HostileGroup>();

        while (ungrouped.Count > 0)
        {
            var seed = ungrouped[0];
            var members = ungrouped
                .Where(h => Geo.DistanceNm(seed.Lat, seed.Lon, h.Lat, h.Lon) <= radiusNm)
                .ToList();
            groups.Add(new HostileGroup(members, seed));
            foreach (var m in members) ungrouped.Remove(m);
        }

        return groups;
    }

    /// All enemy aircraft of the opposite coalition. Neutrals don't count.
    /// Filters out aircraft still on the ramp (not airborne).
    public static IReadOnlyList<AircraftSnapshot> HostilesFor(
        IReadOnlyCollection<AircraftSnapshot> all,
        string friendlyCoalition)
    {
        var enemy = EnemyOf(friendlyCoalition);
        if (enemy is null) return Array.Empty<AircraftSnapshot>();

        return all
            .Where(a => string.Equals(a.Coalition, enemy, StringComparison.OrdinalIgnoreCase))
            .Where(a => a.IsAirborne)
            .ToList();
    }

    public static AircraftSnapshot? NearestHostile(
        AircraftSnapshot caller,
        IReadOnlyCollection<AircraftSnapshot> all)
    {
        return HostilesFor(all, caller.Coalition)
            .Select(h => (Unit: h, Range: Geo.DistanceNm(caller.Lat, caller.Lon, h.Lat, h.Lon)))
            .OrderBy(t => t.Range)
            .Select(t => t.Unit)
            .FirstOrDefault();
    }

    /// Find the single aircraft nearest to a bullseye-relative position, with
    /// independent bearing and range tolerances. Pilots eyeball bullseye coords
    /// from their RWR or DDI; their numbers won't be exact, so the lookup is
    /// forgiving — "roughly on this bearing, roughly at this range" wins.
    ///
    /// Considers all airborne aircraft regardless of coalition — caller decides
    /// whether the result is hostile or friendly based on what it gets back.
    public static AircraftSnapshot? NearestContactAt(
        BullseyePoint bullseye,
        double targetBearingDeg,
        double targetRangeNm,
        IReadOnlyCollection<AircraftSnapshot> all,
        double bearingTolDeg = 15.0,
        double rangeTolNm = 10.0)
    {
        AircraftSnapshot? best = null;
        double bestScore = double.MaxValue;

        foreach (var a in all)
        {
            if (!a.IsAirborne) continue;

            var (brg, rng) = Geo.BullseyeRelative(bullseye.Lat, bullseye.Lon, a.Lat, a.Lon);
            var bearingDiff = Math.Abs(Geo.AngleDiffDeg(brg, targetBearingDeg));
            var rangeDiff = Math.Abs(rng - targetRangeNm);

            if (bearingDiff > bearingTolDeg) continue;
            if (rangeDiff > rangeTolNm) continue;

            // Normalized score so bearing-error and range-error contribute equally.
            // Closer-to-zero on both dimensions wins.
            var score = (bearingDiff / bearingTolDeg) + (rangeDiff / rangeTolNm);
            if (score < bestScore)
            {
                bestScore = score;
                best = a;
            }
        }

        return best;
    }

    /// Find the single nearest aircraft to a target lat/lon, within `radiusNm`.
    /// Used for BRA and "on the nose" declare forms — the pilot's calling off
    /// what they see on radar, so a simple proximity check is enough.
    public static AircraftSnapshot? NearestAircraftNear(
        double targetLat, double targetLon,
        IReadOnlyCollection<AircraftSnapshot> all,
        double radiusNm = 10.0)
    {
        AircraftSnapshot? best = null;
        double bestDist = double.MaxValue;

        foreach (var a in all)
        {
            if (!a.IsAirborne) continue;
            var dist = Geo.DistanceNm(targetLat, targetLon, a.Lat, a.Lon);
            if (dist > radiusNm) continue;
            if (dist < bestDist)
            {
                bestDist = dist;
                best = a;
            }
        }
        return best;
    }

    private static string? EnemyOf(string coalition) => coalition.ToLowerInvariant() switch
    {
        "red" => "Blue",
        "blue" => "Red",
        _ => null,
    };
}

/// A cluster of nearby hostiles, as AWACS reads them out in a picture call.
public sealed record HostileGroup(
    IReadOnlyList<AircraftSnapshot> Members,
    AircraftSnapshot Seed)        // representative member — used for heading/aspect
{
    public int Count => Members.Count;

    /// Group's geographic center, used to compute bullseye position.
    public double CenterLat => Members.Average(m => m.Lat);
    public double CenterLon => Members.Average(m => m.Lon);

    /// Lowest member, used for the picture's altitude band ("Group, 25 thousand").
    public double LowestAltFt => Members.Min(m => m.AltFt);

    /// Brevity word for group size.
    public string CountWord => Count switch
    {
        1 => "single contact",
        2 => "two ship",
        3 => "three ship",
        _ => "heavy",
    };
}
