using System.Text.RegularExpressions;

namespace RadioMan.Responses;

public sealed class JtacResponseGenerator : IResponseGenerator
{
    private static readonly Random Rng = new();

    private static readonly string[] Ips = { "ALPHA", "BRAVO", "CHARLIE", "DELTA", "ECHO", "FOXTROT" };

    private static readonly (string Description, int Count)[] TargetTypes =
    {
        ("BTRs in column", 4),
        ("T-72 tanks", 3),
        ("ZSU-23-4 air defense vehicles", 2),
        ("BMP-2s in defensive position", 5),
        ("artillery battery", 4),
        ("dismounted infantry", 12),
        ("SA-13 SAM systems", 2),
    };

    private static readonly string[] Compass8 =
    {
        "north", "northeast", "east", "southeast", "south", "southwest", "west", "northwest"
    };

    // Includes common Whisper mistranscriptions of NATO phonetic words —
    // "lemur"/"leemur" for Lima, "poppa" for Papa, "alfa" for Alpha, etc.
    private static readonly Dictionary<string, char> PhoneticReverse = new(StringComparer.OrdinalIgnoreCase)
    {
        ["alpha"] = 'A', ["alfa"] = 'A', ["alphas"] = 'A',
        ["bravo"] = 'B', ["brava"] = 'B', ["bravos"] = 'B',
        ["charlie"] = 'C', ["charley"] = 'C', ["charly"] = 'C',
        ["delta"] = 'D', ["deltas"] = 'D',
        ["echo"] = 'E', ["echos"] = 'E', ["echoes"] = 'E',
        ["foxtrot"] = 'F', ["fox"] = 'F',
        ["golf"] = 'G', ["gulf"] = 'G',
        ["hotel"] = 'H', ["hotels"] = 'H',
        ["india"] = 'I', ["indias"] = 'I',
        ["juliet"] = 'J', ["juliett"] = 'J', ["juliette"] = 'J', ["julliet"] = 'J',
        ["kilo"] = 'K', ["kilos"] = 'K', ["keelo"] = 'K',
        ["lima"] = 'L', ["lemur"] = 'L', ["leemur"] = 'L', ["leemer"] = 'L', ["leeman"] = 'L', ["lemma"] = 'L', ["leama"] = 'L',
        ["mike"] = 'M', ["mic"] = 'M', ["mikes"] = 'M',
        ["november"] = 'N', ["novembers"] = 'N',
        ["oscar"] = 'O', ["oscars"] = 'O',
        ["papa"] = 'P', ["poppa"] = 'P', ["poppy"] = 'P', ["popper"] = 'P', ["pap"] = 'P',
        ["quebec"] = 'Q', ["kibec"] = 'Q', ["kebek"] = 'Q',
        ["romeo"] = 'R', ["romeos"] = 'R',
        ["sierra"] = 'S', ["sierras"] = 'S',
        ["tango"] = 'T', ["tangos"] = 'T',
        ["uniform"] = 'U', ["uniforms"] = 'U',
        ["victor"] = 'V', ["victer"] = 'V', ["victors"] = 'V',
        ["whiskey"] = 'W', ["whisky"] = 'W', ["whiskeys"] = 'W',
        ["xray"] = 'X', ["x-ray"] = 'X', ["exray"] = 'X', ["ex-ray"] = 'X',
        ["yankee"] = 'Y', ["yankees"] = 'Y', ["yanky"] = 'Y',
        ["zulu"] = 'Z', ["zulus"] = 'Z', ["zoolu"] = 'Z',
    };

    private enum FlowState
    {
        Idle, CheckedIn,
        BriefInProgress1, BriefInProgress2,
        AwaitingReadback,
        BriefDone,
        ClearedHot, Complete,
    }

    private readonly string _shortName;

    private FlowState _state = FlowState.Idle;
    private NineLineBrief? _brief;
    private string _lastResponse = "";

    public JtacResponseGenerator(string callsign = "Warrior 1-1")
    {
        _shortName = callsign.Split(' ', 2)[0];
    }

    public string Respond(RadioCall call)
    {
        var response = Dispatch(call);
        if (call.Intent != Intent.JtacSayAgain)
            _lastResponse = response;
        return response;
    }

    public IReadOnlyList<NextStep> AvailableNext()
    {
        var list = new List<NextStep>();
        var caller = "Viper 2-1";

        switch (_state)
        {
            case FlowState.Idle:
            case FlowState.Complete:
                list.Add(new("check in",       $"{_shortName}, {caller}, ready for tasking"));
                list.Add(new("request 9-line", $"{_shortName}, {caller}, ready for 9-line"));
                break;

            case FlowState.CheckedIn:
                list.Add(new("request 9-line", $"{_shortName}, {caller}, ready for 9-line"));
                break;

            case FlowState.BriefInProgress1:
            case FlowState.BriefInProgress2:
                list.Add(new("continue brief", $"{_shortName}, {caller}, go ahead"));
                break;

            case FlowState.AwaitingReadback:
            {
                var b = _brief!;
                var ex = $"{_shortName}, {caller}, " +
                         $"elevation {b.Elevation} feet, " +
                         $"grid {b.GridLetters} {b.GridEasting:D4} {b.GridNorthing:D4}, " +
                         $"friendlies {b.FriendlyDistance} meters {b.FriendlyDirection}";
                list.Add(new("readback 4, 6, 8", ex));
                break;
            }

            case FlowState.BriefDone:
                list.Add(new("calling in",     $"{_shortName}, {caller}, in hot from south"));
                break;

            case FlowState.ClearedHot:
                list.Add(new("off target",     $"{_shortName}, {caller}, off west"));
                break;
        }

        if (!string.IsNullOrEmpty(_lastResponse))
            list.Add(new("say again", $"{_shortName}, {caller}, say again"));

        return list;
    }

    private string Dispatch(RadioCall call)
    {
        switch (call.Intent)
        {
            case Intent.JtacCheckIn:
                _state = FlowState.CheckedIn;
                return $"{call.Caller}, {_shortName}, copy your check-in. " +
                       "Tasking to follow. Advise ready for 9-line.";

            case Intent.JtacRequest9Line:
                if (_state is FlowState.Idle or FlowState.CheckedIn or FlowState.Complete)
                {
                    _brief = Generate9Line();
                    _state = FlowState.BriefInProgress1;
                    return $"{call.Caller}, {_shortName}, type 2 in effect. " +
                           $"Line 1, IP {_brief.Ip}. Break. " +
                           $"Line 2, heading {BrevityFormat.Digits(_brief.Heading, padding: 3)}, {_brief.Offset}. Break. " +
                           $"Line 3, distance {BrevityFormat.Decimal(_brief.Distance)} miles. " +
                           "Advise ready for the rest.";
                }
                return $"{call.Caller}, {_shortName}, brief already in progress.";

            case Intent.JtacReadyForRest when _state == FlowState.BriefInProgress1:
            {
                var b = _brief!;
                _state = FlowState.BriefInProgress2;
                return $"{call.Caller}, copy. " +
                       $"Line 4, target elevation {BrevityFormat.Digits(b.Elevation)} feet. Break. " +
                       $"Line 5, target description, {BrevityFormat.Digits(b.TargetCount)} {b.TargetType}. Break. " +
                       $"Line 6, target location grid " +
                            $"{BrevityFormat.Phonetic(b.GridLetters)}, " +
                            $"{BrevityFormat.Digits(b.GridEasting, padding: 4)}, " +
                            $"{BrevityFormat.Digits(b.GridNorthing, padding: 4)}. " +
                       "Advise ready for the rest.";
            }

            case Intent.JtacReadyForRest when _state == FlowState.BriefInProgress2:
            {
                var b = _brief!;
                _state = FlowState.AwaitingReadback;
                return $"{call.Caller}, copy. " +
                       $"Line 7, mark with {SpellMark(b)}. Break. " +
                       $"Line 8, friendlies {BrevityFormat.Digits(b.FriendlyDistance)} meters {b.FriendlyDirection}. Break. " +
                       $"Line 9, egress {b.EgressDirection}, climb to {BrevityFormat.Digits(b.EgressAltitudeKft)} thousand. " +
                       "Read back lines 4, 6, and 8.";
            }

            case Intent.JtacReadyForRest:
                return $"{call.Caller}, {_shortName}, no brief in progress.";

            case Intent.JtacReadback when _state == FlowState.AwaitingReadback:
                return VerifyReadback(call);

            case Intent.JtacReadback:
                return $"{call.Caller}, {_shortName}, no readback expected.";

            case Intent.JtacCallingIn when _state == FlowState.BriefDone:
                _state = FlowState.ClearedHot;
                return $"{call.Caller}, {_shortName}, cleared hot.";

            case Intent.JtacCallingIn when _state == FlowState.AwaitingReadback:
                return $"{call.Caller}, {_shortName}, negative clearance. Read back lines 4, 6, and 8 first.";

            case Intent.JtacCallingIn:
                return $"{call.Caller}, {_shortName}, abort, abort, abort. No clearance.";

            case Intent.JtacOff when _state == FlowState.ClearedHot:
                _state = FlowState.Complete;
                return $"{call.Caller}, {_shortName}, copy off. Pass BDA when able.";

            case Intent.JtacOff:
                return $"{call.Caller}, {_shortName}, copy.";

            case Intent.JtacSayAgain:
                return string.IsNullOrEmpty(_lastResponse)
                    ? $"{call.Caller}, {_shortName}, no prior transmission to repeat."
                    : _lastResponse;

            default:
                return $"{call.Caller}, {_shortName}, unable.";
        }
    }

    private string VerifyReadback(RadioCall call)
    {
        var b = _brief!;
        var text = call.NormalizedText;

        var heardNumbers = ExtractAllNumbers(text);
        var heardLetters = ExtractAllPhoneticCombinations(text);
        var heardDirections = ExtractAllDirections(text);

        // Diagnostic dump so the user can see what the parser actually grabbed
        // from Whisper's transcript when verification fails.
        Console.WriteLine($"  readback diag:");
        Console.WriteLine($"    heard numbers   : {Show(heardNumbers.OrderBy(n => n).Take(40))}");
        Console.WriteLine($"    heard letters   : {Show(heardLetters.OrderBy(s => s).Take(20))}");
        Console.WriteLine($"    heard directions: {Show(heardDirections)}");
        Console.WriteLine($"    expected        : elev={b.Elevation}, grid={b.GridLetters} {b.GridEasting:D4}/{b.GridNorthing:D4}, friend={b.FriendlyDistance}m {b.FriendlyDirection}");

        var corrections = new List<string>();

        if (!NumberFuzzyMatch(heardNumbers, b.Elevation))
        {
            corrections.Add(
                $"Line 4, target elevation {BrevityFormat.Digits(b.Elevation)} feet");
        }

        var gridOk = heardLetters.Contains(b.GridLetters)
                  && NumberFuzzyMatch(heardNumbers, b.GridEasting)
                  && NumberFuzzyMatch(heardNumbers, b.GridNorthing);
        if (!gridOk)
        {
            corrections.Add(
                $"Line 6, grid {BrevityFormat.Phonetic(b.GridLetters)}, " +
                $"{BrevityFormat.Digits(b.GridEasting, padding: 4)}, " +
                $"{BrevityFormat.Digits(b.GridNorthing, padding: 4)}");
        }

        var friendlyOk = NumberFuzzyMatch(heardNumbers, b.FriendlyDistance)
                      && heardDirections.Contains(b.FriendlyDirection);
        if (!friendlyOk)
        {
            corrections.Add(
                $"Line 8, friendlies {BrevityFormat.Digits(b.FriendlyDistance)} meters {b.FriendlyDirection}");
        }

        if (corrections.Count == 0)
        {
            _state = FlowState.BriefDone;
            return $"{call.Caller}, {_shortName}, good readback. Cleared to engage when ready.";
        }

        return $"{call.Caller}, {_shortName}, negative. Correction. " +
               string.Join(". ", corrections) + ". Read back again.";
    }

    /// Exact match wins; otherwise allows a one-character edit on the digit
    /// string to absorb one-digit Whisper mistakes (e.g. heard "661" for "681").
    /// Won't bridge gaps of 2+ digits, so genuinely wrong values still fail.
    private static bool NumberFuzzyMatch(HashSet<int> heard, int expected)
    {
        if (heard.Contains(expected)) return true;

        var expectedStr = expected.ToString();
        foreach (var h in heard)
        {
            var hStr = h.ToString();
            if (Math.Abs(hStr.Length - expectedStr.Length) > 1) continue;
            if (LevenshteinDistance(hStr, expectedStr) <= 1) return true;
        }
        return false;
    }

    private static int LevenshteinDistance(string a, string b)
    {
        if (a.Length == 0) return b.Length;
        if (b.Length == 0) return a.Length;

        var dp = new int[a.Length + 1, b.Length + 1];
        for (int i = 0; i <= a.Length; i++) dp[i, 0] = i;
        for (int j = 0; j <= b.Length; j++) dp[0, j] = j;

        for (int i = 1; i <= a.Length; i++)
            for (int j = 1; j <= b.Length; j++)
            {
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                dp[i, j] = Math.Min(
                    Math.Min(dp[i - 1, j] + 1, dp[i, j - 1] + 1),
                    dp[i - 1, j - 1] + cost);
            }
        return dp[a.Length, b.Length];
    }

    private static string Show<T>(IEnumerable<T> items)
    {
        var arr = items.ToArray();
        return arr.Length == 0 ? "(none)" : string.Join(", ", arr);
    }

    /// Collects every plausible integer from the transcript. Multi-digit tokens are
    /// taken as-is; runs of consecutive single-digit tokens (the result of "one two
    /// zero zero" being normalized to "1 2 0 0") are also expanded into every
    /// possible contiguous sub-sequence so any 2–8 digit value within a spelled run
    /// is detectable.
    private static HashSet<int> ExtractAllNumbers(string text)
    {
        var result = new HashSet<int>();
        var tokens = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        // Multi-digit tokens stand alone.
        foreach (var t in tokens)
        {
            if (t.Length >= 2 && t.All(char.IsDigit) && int.TryParse(t, out var n))
                result.Add(n);
        }

        // Runs of single-digit tokens — try every contiguous sub-sequence.
        int i = 0;
        while (i < tokens.Length)
        {
            if (tokens[i].Length == 1 && char.IsDigit(tokens[i][0]))
            {
                int j = i;
                while (j < tokens.Length && tokens[j].Length == 1 && char.IsDigit(tokens[j][0]))
                    j++;

                for (int start = i; start < j; start++)
                {
                    var sb = new System.Text.StringBuilder();
                    for (int end = start; end < j; end++)
                    {
                        sb.Append(tokens[end]);
                        if (sb.Length <= 9 && int.TryParse(sb.ToString(), out var n))
                            result.Add(n);
                    }
                }
                i = j;
            }
            else
            {
                i++;
            }
        }

        return result;
    }

    /// Finds every contiguous run of phonetic-alphabet words and yields all letter
    /// sub-strings ("papa alpha bravo" -> {"P","PA","PAB","A","AB","B"}). Whatever
    /// the pilot says around the grid letters, the expected pair will be in there.
    private static HashSet<string> ExtractAllPhoneticCombinations(string text)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var tokens = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        int i = 0;
        while (i < tokens.Length)
        {
            if (PhoneticReverse.ContainsKey(tokens[i]))
            {
                int j = i;
                while (j < tokens.Length && PhoneticReverse.ContainsKey(tokens[j]))
                    j++;

                for (int start = i; start < j; start++)
                {
                    var sb = new System.Text.StringBuilder();
                    for (int end = start; end < j; end++)
                    {
                        sb.Append(PhoneticReverse[tokens[end]]);
                        result.Add(sb.ToString());
                    }
                }
                i = j;
            }
            else
            {
                i++;
            }
        }

        return result;
    }

    private static HashSet<string> ExtractAllDirections(string text)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var d in Compass8)
        {
            if (Regex.IsMatch(text, $@"\b{d}\b", RegexOptions.IgnoreCase))
                result.Add(d);
        }
        return result;
    }

    private static string SpellMark(NineLineBrief b) => b.MarkType switch
    {
        "laser" => $"laser, code {BrevityFormat.Digits(b.LaserCode!.Value, padding: 4)}",
        _ => b.MarkType,
    };

    private static NineLineBrief Generate9Line()
    {
        var (desc, count) = TargetTypes[Rng.Next(TargetTypes.Length)];

        var markRoll = Rng.NextDouble();
        var (markType, laserCode) = markRoll switch
        {
            < 0.5  => ("laser",       (int?)(1500 + Rng.Next(200))),
            < 0.75 => ("white smoke", (int?)null),
            _      => ("IR pointer",  (int?)null),
        };

        return new NineLineBrief
        {
            Ip = Ips[Rng.Next(Ips.Length)],
            Heading = Rng.Next(0, 36) * 10,
            Offset = Rng.NextDouble() < 0.5 ? "offset left" : "offset right",
            Distance = Math.Round(Rng.NextDouble() * 8 + 2, 1),
            Elevation = Rng.Next(50, 5000),
            TargetCount = count,
            TargetType = desc,
            GridLetters = $"{(char)('A' + Rng.Next(26))}{(char)('A' + Rng.Next(26))}",
            GridEasting = Rng.Next(0, 10_000),
            GridNorthing = Rng.Next(0, 10_000),
            MarkType = markType,
            LaserCode = laserCode,
            FriendlyDistance = Rng.Next(300, 1500),
            FriendlyDirection = Compass8[Rng.Next(Compass8.Length)],
            EgressDirection = Compass8[Rng.Next(Compass8.Length)],
            EgressAltitudeKft = Rng.Next(10, 25),
        };
    }

    private sealed record NineLineBrief
    {
        public required string Ip { get; init; }
        public required int Heading { get; init; }
        public required string Offset { get; init; }
        public required double Distance { get; init; }
        public required int Elevation { get; init; }
        public required int TargetCount { get; init; }
        public required string TargetType { get; init; }
        public required string GridLetters { get; init; }
        public required int GridEasting { get; init; }
        public required int GridNorthing { get; init; }
        public required string MarkType { get; init; }
        public required int? LaserCode { get; init; }
        public required int FriendlyDistance { get; init; }
        public required string FriendlyDirection { get; init; }
        public required string EgressDirection { get; init; }
        public required int EgressAltitudeKft { get; init; }
    }
}
