using System.Text.RegularExpressions;

namespace RadioMan.Dcs;

/// Pattern-based mapping from a DCS unit to one or more radio-man roles.
///
/// AWACS / Carrier are pure type matches (small known set of unit types).
/// JTAC has an extra layer because in DCS any TGP-capable ground unit *can*
/// be a JTAC depending on what the mission designer set up — so we accept
/// either a type match (Humvee/Stryker variants) OR a name/callsign
/// containing "JTAC" or "FAC".
public static class UnitRoleMatcher
{
    public static readonly Regex AwacsTypes =
        new(@"E-[23]|A-50|KJ-2000|RC-135|MQ-9|EC-130", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static readonly Regex CarrierTypes =
        new(@"CVN|CV[^\w]|CV$|Stennis|Roosevelt|Eisenhower|Vinson|Lincoln|Truman|Kuznetsov|Forrestal",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static readonly Regex JtacTypes =
        new(@"Humvee|Hummer|M1043|M1045|Stryker", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static readonly Regex JtacNameHint =
        new(@"\bJTAC\b|\bFAC\b|\bspotter\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// All roles this unit can fill. Most units return zero or one; nothing
    /// returns more than one in practice.
    public static IEnumerable<string> RolesFor(AircraftSnapshot u)
    {
        var type = u.AircraftType ?? "";
        var name = u.Name ?? "";
        var callsign = u.Callsign ?? "";

        if (AwacsTypes.IsMatch(type)) yield return "AWACS";
        if (CarrierTypes.IsMatch(type)) yield return "Carrier";

        var isJtacType = JtacTypes.IsMatch(type);
        var isJtacName = JtacNameHint.IsMatch(name) || JtacNameHint.IsMatch(callsign);
        if (isJtacType || isJtacName) yield return "JTAC";
    }

    /// The radio callsign to use when addressing this unit. Prefer DCS callsign,
    /// then unit name. Player name doesn't make sense for AI controllers.
    public static string CallsignFor(AircraftSnapshot u)
    {
        if (!string.IsNullOrWhiteSpace(u.Callsign)) return u.Callsign;
        if (!string.IsNullOrWhiteSpace(u.Name)) return u.Name;
        return $"Unit-{u.Id}";
    }
}
