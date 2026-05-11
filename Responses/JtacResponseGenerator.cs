using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace RadioMan.Responses;

/// JTAC. Multi-unit aware — one generator serves any number of JTAC units in
/// the field. Per-recipient state machine: each JTAC callsign tracks its own
/// 9-line, readback, cleared-hot, off-wire flow independently.
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

    /// Per-recipient state. One per JTAC callsign in play.
    private sealed class BriefState
    {
        public FlowState Flow = FlowState.Idle;
        public NineLineBrief? Brief;
        public string LastResponse = "";
        public readonly HashSet<int> ConfirmedLines = new();
    }

    private readonly ConcurrentDictionary<string, BriefState> _states =
        new(StringComparer.OrdinalIgnoreCase);

    /// Used only by AvailableNext to show contextually-appropriate options
    /// for whichever JTAC was last talked to. Pure UX — doesn't affect routing.
    private string _lastRecipient = "Hammer";

    /// Example recipient name shown in AvailableNext when no real one has been
    /// addressed yet.
    private readonly string _exampleRecipient;

    public JtacResponseGenerator(string exampleRecipient = "Hammer")
    {
        _exampleRecipient = exampleRecipient;
        _lastRecipient = exampleRecipient;
    }

    private BriefState GetState(string recipient)
        => _states.GetOrAdd(recipient, _ => new BriefState());

    public string Respond(RadioCall call)
    {
        _lastRecipient = call.Recipient;
        var state = GetState(call.Recipient);
        var response = Dispatch(call, state);
        if (call.Intent != Intent.JtacSayAgain)
            state.LastResponse = response;
        return response;
    }

    public IReadOnlyList<NextStep> AvailableNext()
    {
        var recipient = _lastRecipient;
        var state = _states.TryGetValue(recipient, out var s) ? s : new BriefState();
        var list = new List<NextStep>();
        var caller = "Viper 2-1";

        switch (state.Flow)
        {
            case FlowState.Idle:
            case FlowState.Complete:
                list.Add(new("check in",       $"{recipient}, {caller}, ready for tasking"));
                list.Add(new("request 9-line", $"{recipient}, {caller}, ready for 9-line"));
                break;

            case FlowState.CheckedIn:
                list.Add(new("request 9-line", $"{recipient}, {caller}, ready for 9-line"));
                break;

            case FlowState.BriefInProgress1:
            case FlowState.BriefInProgress2:
                list.Add(new("continue brief", $"{recipient}, {caller}, go ahead"));
                break;

            case FlowState.AwaitingReadback:
            {
                var b = state.Brief!;
                var ex = $"{recipient}, {caller}, " +
                         $"elevation {b.Elevation} feet, " +
                         $"grid {b.GridLetters} {b.GridEasting:D4} {b.GridNorthing:D4}, " +
                         $"friendlies {b.FriendlyDistance} meters {b.FriendlyDirection}";
                list.Add(new("readback 4, 6, 8", ex));
                break;
            }

            case FlowState.BriefDone:
                list.Add(new("calling in",     $"{recipient}, {caller}, in hot from south"));
                break;

            case FlowState.ClearedHot:
                list.Add(new("off target",     $"{recipient}, {caller}, off west"));
                break;
        }

        if (!string.IsNullOrEmpty(state.LastResponse))
            list.Add(new("say again", $"{recipient}, {caller}, say again"));

        return list;
    }

    private string Dispatch(RadioCall call, BriefState state)
    {
        var recipient = call.Recipient;

        switch (call.Intent)
        {
            case Intent.JtacCheckIn:
                state.Flow = FlowState.CheckedIn;
                return $"{call.Caller}, {recipient}, copy your check-in. " +
                       "Tasking to follow. Advise ready for 9-line.";

            case Intent.JtacRequest9Line:
                if (state.Flow is FlowState.Idle or FlowState.CheckedIn or FlowState.Complete)
                {
                    state.Brief = Generate9Line();
                    state.ConfirmedLines.Clear();
                    state.Flow = FlowState.BriefInProgress1;
                    return $"{call.Caller}, {recipient}, type 2 in effect. " +
                           $"Line 1, IP {state.Brief.Ip}. Break. " +
                           $"Line 2, heading {BrevityFormat.Digits(state.Brief.Heading, padding: 3)}, {state.Brief.Offset}. Break. " +
                           $"Line 3, distance {BrevityFormat.Decimal(state.Brief.Distance)} miles. " +
                           "Advise ready for the rest.";
                }
                return $"{call.Caller}, {recipient}, brief already in progress.";

            case Intent.JtacReadyForRest when state.Flow == FlowState.BriefInProgress1:
            {
                var b = state.Brief!;
                state.Flow = FlowState.BriefInProgress2;
                return $"{call.Caller}, copy. " +
                       $"Line 4, target elevation {BrevityFormat.Digits(b.Elevation)} feet. Break. " +
                       $"Line 5, target description, {BrevityFormat.Digits(b.TargetCount)} {b.TargetType}. Break. " +
                       $"Line 6, target location grid " +
                            $"{BrevityFormat.Phonetic(b.GridLetters)}, " +
                            $"{BrevityFormat.Digits(b.GridEasting, padding: 4)}, " +
                            $"{BrevityFormat.Digits(b.GridNorthing, padding: 4)}. " +
                       "Advise ready for the rest.";
            }

            case Intent.JtacReadyForRest when state.Flow == FlowState.BriefInProgress2:
            {
                var b = state.Brief!;
                state.Flow = FlowState.AwaitingReadback;
                return $"{call.Caller}, copy. " +
                       $"Line 7, mark with {SpellMark(b)}. Break. " +
                       $"Line 8, friendlies {BrevityFormat.Digits(b.FriendlyDistance)} meters {b.FriendlyDirection}. Break. " +
                       $"Line 9, egress {b.EgressDirection}, climb to {BrevityFormat.Digits(b.EgressAltitudeKft)} thousand. " +
                       "Read back lines 4, 6, and 8.";
            }

            case Intent.JtacReadyForRest:
                return $"{call.Caller}, {recipient}, no brief in progress.";

            case Intent.JtacReadback when state.Flow == FlowState.AwaitingReadback:
                return VerifyReadback(call, state);

            case Intent.JtacReadback:
                return $"{call.Caller}, {recipient}, no readback expected.";

            case Intent.JtacCallingIn when state.Flow == FlowState.BriefDone:
                state.Flow = FlowState.ClearedHot;
                return $"{call.Caller}, {recipient}, cleared hot.";

            case Intent.JtacCallingIn when state.Flow == FlowState.AwaitingReadback:
                return $"{call.Caller}, {recipient}, negative clearance. Read back lines 4, 6, and 8 first.";

            case Intent.JtacCallingIn:
                return $"{call.Caller}, {recipient}, abort, abort, abort. No clearance.";

            case Intent.JtacOff when state.Flow == FlowState.ClearedHot:
                state.Flow = FlowState.Complete;
                return $"{call.Caller}, {recipient}, copy off. Pass BDA when able.";

            case Intent.JtacOff:
                return $"{call.Caller}, {recipient}, copy.";

            case Intent.JtacSayAgain:
                return string.IsNullOrEmpty(state.LastResponse)
                    ? $"{call.Caller}, {recipient}, no prior transmission to repeat."
                    : state.LastResponse;

            case Intent.Unrecognized:
                return Unrecognized(call);

            default:
                return $"{call.Caller}, {recipient}, unable.";
        }
    }

    private string Unrecognized(RadioCall call)
    {
        if (call.Caller is null)
            return $"Unknown station calling {call.Recipient}, say again your callsign.";

        var options = string.Join(", ", AvailableNext().Select(s => s.Hint));
        return $"{call.Caller}, {call.Recipient}, did not copy. Possible calls: {options}.";
    }

    private string VerifyReadback(RadioCall call, BriefState state)
    {
        var b = state.Brief!;
        var text = call.NormalizedText;

        var heardNumbers = ExtractAllNumbers(text);
        var heardLetters = ExtractAllPhoneticCombinations(text);
        var heardDirections = ExtractAllDirections(text);

        Console.WriteLine($"  readback diag:");
        Console.WriteLine($"    heard numbers   : {Show(heardNumbers.OrderBy(n => n).Take(40))}");
        Console.WriteLine($"    heard letters   : {Show(heardLetters.OrderBy(s => s).Take(20))}");
        Console.WriteLine($"    heard directions: {Show(heardDirections)}");
        Console.WriteLine($"    expected        : elev={b.Elevation}, grid={b.GridLetters} {b.GridEasting:D4}/{b.GridNorthing:D4}, friend={b.FriendlyDistance}m {b.FriendlyDirection}");
        Console.WriteLine($"    prior confirmed : {Show(state.ConfirmedLines.OrderBy(n => n))}");

        var line4Ok = heardNumbers.Contains(b.Elevation);
        var line6Ok = heardLetters.Contains(b.GridLetters)
                   && heardNumbers.Contains(b.GridEasting)
                   && heardNumbers.Contains(b.GridNorthing);
        var line8Ok = heardNumbers.Contains(b.FriendlyDistance)
                   && heardDirections.Contains(b.FriendlyDirection);

        var newlyConfirmed = new List<int>();
        var corrections = new List<string>();

        Process(4, line4Ok,
            $"Line 4, target elevation {BrevityFormat.Digits(b.Elevation)} feet");
        Process(6, line6Ok,
            $"Line 6, grid {BrevityFormat.Phonetic(b.GridLetters)}, " +
            $"{BrevityFormat.Digits(b.GridEasting, padding: 4)}, " +
            $"{BrevityFormat.Digits(b.GridNorthing, padding: 4)}");
        Process(8, line8Ok,
            $"Line 8, friendlies {BrevityFormat.Digits(b.FriendlyDistance)} meters {b.FriendlyDirection}");

        if (state.ConfirmedLines.Count == 3)
        {
            state.Flow = FlowState.BriefDone;
            return $"{call.Caller}, {call.Recipient}, good readback. Cleared to engage when ready.";
        }

        var sb = new System.Text.StringBuilder();
        sb.Append($"{call.Caller}, {call.Recipient}, ");

        if (newlyConfirmed.Count > 0)
            sb.Append($"good copy {LineList(newlyConfirmed)}. ");

        if (corrections.Count > 0)
        {
            sb.Append("Negative. Correction. ");
            sb.Append(string.Join(". ", corrections));
            sb.Append(". ");
        }

        var stillPending = new[] { 4, 6, 8 }
            .Where(l => !state.ConfirmedLines.Contains(l))
            .ToList();
        sb.Append($"Read back line{(stillPending.Count > 1 ? "s" : "")} {LineList(stillPending)}.");
        return sb.ToString();

        void Process(int line, bool ok, string correction)
        {
            if (state.ConfirmedLines.Contains(line)) return;
            if (ok)
            {
                state.ConfirmedLines.Add(line);
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

    private static HashSet<int> ExtractAllNumbers(string text)
    {
        var result = new HashSet<int>();
        var tokens = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        int i = 0;
        while (i < tokens.Length)
        {
            if (!IsAllDigits(tokens[i])) { i++; continue; }

            var sb = new System.Text.StringBuilder();
            int j = i;
            while (j < tokens.Length && IsAllDigits(tokens[j]))
            {
                sb.Append(tokens[j]);
                j++;
            }

            var run = sb.ToString();
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
            else { i++; }
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

    private static string Show<T>(IEnumerable<T> items)
    {
        var arr = items.ToArray();
        return arr.Length == 0 ? "(none)" : string.Join(", ", arr);
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
