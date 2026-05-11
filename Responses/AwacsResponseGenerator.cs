using System.Text.RegularExpressions;
using RadioMan.Dcs;

namespace RadioMan.Responses;

/// AWACS controller for the picture / bogey dope / declare brevity calls.
/// Pulls real position data from IDcsClient when available; falls back to
/// plausible random data so the system stays usable without DCS connected.
public sealed class AwacsResponseGenerator : IResponseGenerator
{
    private static readonly Random Rng = new();
    private readonly string _shortName;
    private readonly IDcsClient _dcs;

    public AwacsResponseGenerator(IDcsClient dcs, string callsign = "Wizard 1-1")
    {
        _shortName = callsign.Split(' ', 2)[0];
        _dcs = dcs;
    }

    public string Respond(RadioCall call) => call.Intent switch
    {
        Intent.AwacsPicture => Picture(call),
        Intent.AwacsBogeyDope => BogeyDope(call),
        Intent.AwacsDeclare => Declare(call),
        Intent.Unrecognized => Unrecognized(call),
        _ => $"{call.Caller}, {_shortName}, unable.",
    };

    private string Unrecognized(RadioCall call)
    {
        if (call.Caller is null)
            return $"Unknown station calling {_shortName}, say again your callsign.";

        var options = string.Join(", ", AvailableNext().Select(s => s.Hint));
        return $"{call.Caller}, {_shortName}, did not copy. " +
               $"Possible calls: {options}.";
    }

    public IReadOnlyList<NextStep> AvailableNext() => new[]
    {
        new NextStep("picture",         $"{_shortName}, Viper 2-1, request picture"),
        new NextStep("bogey dope",      $"{_shortName}, Viper 2-1, bogey dope"),
        new NextStep("declare bullseye", $"{_shortName}, Viper 2-1, declare bullseye 270 35"),
        new NextStep("declare BRA",     $"{_shortName}, Viper 2-1, declare BRA 270 35"),
        new NextStep("declare nose",    $"{_shortName}, Viper 2-1, declare on the nose 35"),
    };

    // --- Picture ----------------------------------------------------------

    private string Picture(RadioCall call)
    {
        var caller = call.Caller is null ? null : _dcs.PlayerByCallsign(call.Caller);
        var bullseye = caller is null ? null : _dcs.Bullseye(caller.Coalition);

        // If we don't have either the caller's snapshot or their coalition's
        // bullseye, degrade to a plausible random picture.
        if (caller is null || bullseye is null || !_dcs.HasFreshData)
            return PicturePlaceholder(call);

        var hostiles = Awacs.HostilesFor(_dcs.AllAircraft, caller.Coalition);
        if (hostiles.Count == 0)
            return $"{call.Caller}, {_shortName}, picture clean.";

        var groups = Awacs.GroupHostiles(hostiles);

        // Build "Group 1 ... Group 2 ..." chunks.
        var parts = new List<string>();
        for (int i = 0; i < groups.Count && i < 3; i++)  // cap at 3 groups for readability
        {
            var g = groups[i];
            var (brg, range) = Geo.BullseyeRelative(bullseye.Lat, bullseye.Lon,
                                                     g.CenterLat, g.CenterLon);
            var altThousands = (int)Math.Round(g.LowestAltFt / 1000.0);
            var aspect = Geo.AspectFrom(g.Seed.Lat, g.Seed.Lon, g.Seed.HeadingDeg,
                                         bullseye.Lat, bullseye.Lon);

            parts.Add(
                $"group {i + 1}, {g.CountWord}, " +
                $"bullseye {BrevityFormat.Digits((int)Math.Round(brg), padding: 3)} " +
                $"for {BrevityFormat.Digits((int)Math.Round(range))}, " +
                $"{BrevityFormat.Digits(altThousands)} thousand, {aspect}");
        }

        var groupCount = groups.Count switch
        {
            1 => "single group",
            2 => "two groups",
            3 => "three groups",
            _ => $"{BrevityFormat.Digits(groups.Count)} groups",
        };

        return $"{call.Caller}, {_shortName}, picture: {groupCount}. " +
               string.Join(". ", parts) + ".";
    }

    private string PicturePlaceholder(RadioCall call)
    {
        if (Rng.NextDouble() < 0.15)
            return $"{call.Caller}, {_shortName}, picture clean.";

        int groups = Rng.Next(1, 4);
        var groupWord = groups == 1 ? "single group" : $"{Spell(groups)} groups";
        int bra = Rng.Next(0, 36) * 10;
        int range = Rng.Next(15, 80);
        int alt = Rng.Next(10, 40);
        var aspect = Rng.NextDouble() < 0.5 ? "hot" : "cold";
        return $"{call.Caller}, {_shortName}, picture: {groupWord}, " +
               $"bullseye {BrevityFormat.Digits(bra, padding: 3)} for " +
               $"{BrevityFormat.Digits(range)}, {BrevityFormat.Digits(alt)} thousand, " +
               $"{aspect}, hostile.";
    }

    // --- Bogey dope -------------------------------------------------------

    private string BogeyDope(RadioCall call)
    {
        var caller = call.Caller is null ? null : _dcs.PlayerByCallsign(call.Caller);
        var bullseye = caller is null ? null : _dcs.Bullseye(caller.Coalition);

        if (caller is null || bullseye is null || !_dcs.HasFreshData)
            return BogeyDopePlaceholder(call);

        var nearest = Awacs.NearestHostile(caller, _dcs.AllAircraft);
        if (nearest is null)
            return $"{call.Caller}, {_shortName}, no contacts.";

        var (brg, range) = Geo.BullseyeRelative(bullseye.Lat, bullseye.Lon,
                                                 nearest.Lat, nearest.Lon);
        var altThousands = (int)Math.Round(nearest.AltFt / 1000.0);
        var aspect = Geo.AspectFrom(nearest.Lat, nearest.Lon, nearest.HeadingDeg,
                                     caller.Lat, caller.Lon);

        return $"{call.Caller}, {_shortName}, single contact, " +
               $"bullseye {BrevityFormat.Digits((int)Math.Round(brg), padding: 3)} " +
               $"for {BrevityFormat.Digits((int)Math.Round(range))}, " +
               $"{BrevityFormat.Digits(altThousands)} thousand, " +
               $"{aspect}, hostile.";
    }

    private string BogeyDopePlaceholder(RadioCall call)
    {
        int bra = Rng.Next(0, 36) * 10;
        int range = Rng.Next(10, 60);
        int alt = Rng.Next(8, 40);
        var aspect = Rng.NextDouble() < 0.5 ? "hot" : "flank";
        return $"{call.Caller}, {_shortName}, bogey dope: " +
               $"bullseye {BrevityFormat.Digits(bra, padding: 3)} for " +
               $"{BrevityFormat.Digits(range)}, {BrevityFormat.Digits(alt)} thousand, " +
               $"{aspect}, hostile.";
    }

    // --- Declare ----------------------------------------------------------

    private enum DeclareForm { Unknown, Bullseye, Bra, Nose }

    private string Declare(RadioCall call)
    {
        var caller = call.Caller is null ? null : _dcs.PlayerByCallsign(call.Caller);
        var (form, bearing, range) = ParseDeclare(call.NormalizedText);

        if (form == DeclareForm.Unknown || !_dcs.HasFreshData || caller is null)
            return DeclarePlaceholder(call);

        // For each form, compute the target lat/lon, then find the nearest contact.
        AircraftSnapshot? contact = null;

        if (form == DeclareForm.Bullseye)
        {
            var bullseye = _dcs.Bullseye(caller.Coalition);
            if (bullseye is null || bearing is null || range is null)
                return DeclarePlaceholder(call);
            contact = Awacs.NearestContactAt(bullseye, bearing.Value, range.Value, _dcs.AllAircraft);
        }
        else if (form == DeclareForm.Bra && bearing is not null && range is not null)
        {
            // BRA: from caller's position, true bearing + range.
            var (tLat, tLon) = Geo.ProjectFrom(caller.Lat, caller.Lon, bearing.Value, range.Value);
            contact = Awacs.NearestAircraftNear(tLat, tLon, _dcs.AllAircraft);
        }
        else if (form == DeclareForm.Nose && range is not null)
        {
            // On the nose: caller's heading is the bearing.
            var (tLat, tLon) = Geo.ProjectFrom(caller.Lat, caller.Lon, caller.HeadingDeg, range.Value);
            contact = Awacs.NearestAircraftNear(tLat, tLon, _dcs.AllAircraft);
        }

        if (contact is null)
            return $"{call.Caller}, {_shortName}, declare: clean.";

        if (string.Equals(contact.Coalition, caller.Coalition, StringComparison.OrdinalIgnoreCase))
            return $"{call.Caller}, {_shortName}, declare: friendly.";
        if (string.Equals(contact.Coalition, "Neutral", StringComparison.OrdinalIgnoreCase))
            return $"{call.Caller}, {_shortName}, declare: neutral, no IFF.";

        return $"{call.Caller}, {_shortName}, declare: hostile, type {contact.AircraftType}.";
    }

    /// Parse the declare call. Three forms:
    ///   bullseye  — "declare bullseye 270 35"        → bearing+range from bullseye
    ///   bra       — "declare BRA 270 35"             → bearing+range from caller
    ///   nose      — "declare on the nose 35" / "nose 35" → caller's heading, range nm
    private static (DeclareForm Form, int? Bearing, int? Range) ParseDeclare(string normalizedText)
    {
        // Check for "on the nose" / "nose" cue first (most specific).
        var noseMatch = Regex.Match(normalizedText,
            @"\b(?:on\s+(?:the|my|your)\s+)?nose\b|\bboresight\b",
            RegexOptions.IgnoreCase);
        if (noseMatch.Success)
        {
            var after = normalizedText[(noseMatch.Index + noseMatch.Length)..];
            var nums = ExtractFirstNumbers(after, 1);
            return (DeclareForm.Nose, null, nums.Count >= 1 ? nums[0] : null);
        }

        // BRA: "bra" as single word, or "b r a" as three single-letter tokens.
        var braMatch = Regex.Match(normalizedText,
            @"\bbra\b|\bb\s+r\s+a\b|\bbearing\b",
            RegexOptions.IgnoreCase);
        if (braMatch.Success)
        {
            var after = normalizedText[(braMatch.Index + braMatch.Length)..];
            var nums = ExtractFirstNumbers(after, 2);
            return (DeclareForm.Bra,
                    nums.Count >= 1 ? nums[0] : null,
                    nums.Count >= 2 ? nums[1] : null);
        }

        // Default: bullseye form. Anchor on the "declare" or "bullseye" keyword.
        var beMatch = Regex.Match(normalizedText,
            @"\b(?:bullseye|declare)\b",
            RegexOptions.IgnoreCase);
        if (beMatch.Success)
        {
            var after = normalizedText[(beMatch.Index + beMatch.Length)..];
            var nums = ExtractFirstNumbers(after, 2);
            if (nums.Count >= 2)
                return (DeclareForm.Bullseye, nums[0], nums[1]);
        }

        return (DeclareForm.Unknown, null, null);
    }

    /// Extract up to `max` integers from text. Consecutive single-digit tokens
    /// collapse into one number ("2 7 0" → 270); a multi-digit token stands alone.
    private static List<int> ExtractFirstNumbers(string text, int max)
    {
        var tokens = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var result = new List<int>();
        var buf = new System.Text.StringBuilder();

        void Flush()
        {
            if (buf.Length > 0 && int.TryParse(buf.ToString(), out var n))
                result.Add(n);
            buf.Clear();
        }

        foreach (var t in tokens)
        {
            if (result.Count >= max) break;
            if (t.All(char.IsDigit))
            {
                if (t.Length > 1)
                {
                    Flush();
                    if (int.TryParse(t, out var multi)) result.Add(multi);
                }
                else
                {
                    buf.Append(t);
                }
            }
            else
            {
                Flush();
            }
        }
        Flush();

        return result;
    }

    private string DeclarePlaceholder(RadioCall call)
    {
        var roll = Rng.NextDouble();
        if (roll < 0.10) return $"{call.Caller}, {_shortName}, declare: clean.";
        if (roll < 0.25) return $"{call.Caller}, {_shortName}, declare: friendly.";
        if (roll < 0.35) return $"{call.Caller}, {_shortName}, declare: unknown, no IFF.";

        var types = new[] { "Su-27", "Su-30", "Su-35", "MiG-29", "MiG-31", "Su-24", "Su-25" };
        var type = types[Rng.Next(types.Length)];
        return $"{call.Caller}, {_shortName}, declare: hostile, type {type}.";
    }

    private static string Spell(int n) => n switch
    {
        1 => "one", 2 => "two", 3 => "three", 4 => "four",
        5 => "five", 6 => "six", 7 => "seven", 8 => "eight", 9 => "nine",
        _ => n.ToString()
    };
}
