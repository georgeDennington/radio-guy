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
    private readonly HashSet<int> _confirmedLines = new();

    public JtacResponseGenerator(string callsign = "Hammer 1-1")
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
                var pending = new[] { 4, 6, 8 }.Where(l => !_confirmedLines.Contains(l)).ToList();

                var parts = new List<string>();
                if (pending.Contains(4)) parts.Add($"elevation {b.Elevation} feet");
                if (pending.Contains(6)) parts.Add($"grid {b.GridLetters} {b.GridEasting:D4} {b.GridNorthing:D4}");
                if (pending.Contains(8)) parts.Add($"friendlies {b.FriendlyDistance} meters {b.FriendlyDirection}");

                var hint = pending.Count == 1
                    ? $"readback line {pending[0]}"
                    : $"readback {string.Join("/", pending)}";
                list.Add(new(hint, $"{_shortName}, {caller}, {string.Join(", ", parts)}"));
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
                    _confirmedLines.Clear();
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

            case Intent.Unrecognized:
                return Unrecognized(call);

            default:
                return $"{call.Caller}, {_shortName}, unable.";
        }
    }

    private string Unrecognized(RadioCall call)
    {
        if (call.Caller is null)
            return $"Unknown station calling {_shortName}, say again your callsign.";

        var options = string.Join(", ", AvailableNext().Select(s => s.Hint));
        return $"{call.Caller}, {_shortName}, did not copy. " +
               $"Possible calls: {options}.";
    }

    private string VerifyReadback(RadioCall call)
    {
        var b = _brief!;
        var text = call.NormalizedText;

        var heardNumbers = ExtractAllNumbers(text);
        var heardLetters = ExtractAllPhoneticCombinations(text);
        var heardDirections = ExtractAllDirections(text);

        Console.WriteLine($"  readback diag:");
        Console.WriteLine($"    heard numbers   : {Show(heardNumbers.OrderBy(n => n).Take(40))}");
        Console.WriteLine($"    heard letters   : {Show(heardLetters.OrderBy(s => s).Take(20))}");
        Console.WriteLine($"    heard directions: {Show(heardDirections)}");
        Console.WriteLine($"    expected        : elev={b.Elevation}, grid={b.GridLetters} {b.GridEasting:D4}/{b.GridNorthing:D4}, friend={b.FriendlyDistance}m {b.FriendlyDirection}");
        Console.WriteLine($"    prior confirmed : {Show(_confirmedLines.OrderBy(n => n))}");

        var line4Ok = heardNumbers.Contains(b.Elevation);
        var line6Ok = heardLetters.Contains(b.GridLetters)
                   && heardNumbers.Contains(b.GridEasting)
                   && heardNumbers.Contains(b.GridNorthing);
        var line8Ok = heardNumbers.Contains(b.FriendlyDistance)
                   && heardDirections.Contains(b.FriendlyDirection);

        var newlyConfirmed = new List<int>();
        var corrections = new List<string>();

        // Lines already confirmed in a previous readback attempt stay confirmed.
        // For each not-yet-confirmed line: if heard correctly now, mark it; otherwise
        // flag it for correction. Wrong/missing both result in a correction string —
        // the pilot gets the right value either way.
        Process(4, line4Ok,
            $"Line 4, target elevation {BrevityFormat.Digits(b.Elevation)} feet");
        Process(6, line6Ok,
            $"Line 6, grid {BrevityFormat.Phonetic(b.GridLetters)}, " +
            $"{BrevityFormat.Digits(b.GridEasting, padding: 4)}, " +
            $"{BrevityFormat.Digits(b.GridNorthing, padding: 4)}");
        Process(8, line8Ok,
            $"Line 8, friendlies {BrevityFormat.Digits(b.FriendlyDistance)} meters {b.FriendlyDirection}");

        if (_confirmedLines.Count == 3)
        {
            _state = FlowState.BriefDone;
            return $"{call.Caller}, {_shortName}, good readback. Cleared to engage when ready.";
        }

        var sb = new System.Text.StringBuilder();
        sb.Append($"{call.Caller}, {_shortName}, ");

        if (newlyConfirmed.Count > 0)
            sb.Append($"good copy {LineList(newlyConfirmed)}. ");

        if (corrections.Count > 0)
        {
            sb.Append("Negative. Correction. ");
            sb.Append(string.Join(". ", corrections));
            sb.Append(". ");
        }

        var stillPending = new[] { 4, 6, 8 }
            .Where(l => !_confirmedLines.Contains(l))
            .ToList();
        sb.Append($"Read back line{(stillPending.Count > 1 ? "s" : "")} {LineList(stillPending)}.");
        return sb.ToString();

        void Process(int line, bool ok, string correction)
        {
            if (_confirmedLines.Contains(line)) return;
            if (ok)
            {
                _confirmedLines.Add(line);
                newlyConfirmed.Add(line);
            }
            else
            {
                corrections.Add(correction);
            }
        }
    }

    private static string LineList(IEnumerable<int> lines)
    {
        var arr = lines.ToArray();
        return arr.Length switch
        {
            0 => "",
            1 => arr[0].ToString(),
            2 => $"{arr[0]} and {arr[1]}",
            _ => $"{string.Join(", ", arr.Take(arr.Length - 1))}, and {arr[^1]}",
        };
    }

    private static string Show<T>(IEnumerable<T> items)
    {
        var arr = items.ToArray();
        return arr.Length == 0 ? "(none)" : string.Join(", ", arr);
    }

    /// Collects every plausible integer from the transcript.
    ///
    /// Treats *every contiguous run of digit tokens* (single OR multi-digit) as one
    /// digit stream. Whisper is inconsistent — for "two-six-two-zero" it might emit
    /// "2 6 2 0", "26 2 0", "2 6 20", or "2620". Joining the run and enumerating
    /// every contiguous substring means all of those forms produce the same set of
    /// candidate numbers (which includes 2620, 26, 620, etc.).
    private static HashSet<int> ExtractAllNumbers(string text)
    {
        var result = new HashSet<int>();
        var tokens = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        int i = 0;
        while (i < tokens.Length)
        {
            if (!IsAllDigits(tokens[i])) { i++; continue; }

            // Greedily consume the digit-token run, joining as we go.
            var sb = new System.Text.StringBuilder();
            int j = i;
            while (j < tokens.Length && IsAllDigits(tokens[j]))
            {
                sb.Append(tokens[j]);
                j++;
            }

            var run = sb.ToString();
            // Every contiguous substring becomes a candidate.
            for (int start = 0; start < run.Length; start++)
            {
                for (int len = 1; len <= 9 && start + len <= run.Length; len++)
                {
                    if (int.TryParse(run.AsSpan(start, len), out var n))
                        result.Add(n);
                }
            }

            i = j;
        }

        return result;
    }

    private static bool IsAllDigits(string s) => s.Length > 0 && s.All(char.IsDigit);

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
