using System.Text.RegularExpressions;

namespace RadioMan.Parsing;

public sealed class RegexIntentParser : IIntentParser
{
    public sealed record IntentRule(Intent Intent, Regex Pattern);

    private static readonly Dictionary<string, string> NumberWords = new(StringComparer.OrdinalIgnoreCase)
    {
        ["zero"] = "0", ["one"] = "1", ["two"] = "2", ["three"] = "3", ["four"] = "4",
        ["five"] = "5", ["six"] = "6", ["seven"] = "7", ["eight"] = "8", ["nine"] = "9",
        ["niner"] = "9", ["dash"] = "-",
    };

    private readonly IReadOnlyList<IntentRule> _rules;
    private readonly object _lock = new();

    // Mutable: rebuilt when SetRecipients is called. Read under _lock.
    private HashSet<string> _recipientFirstWords;
    private Regex _callsignRx;

    public RegexIntentParser(IEnumerable<string> recipientCallsigns, IEnumerable<IntentRule> rules)
    {
        _rules = rules.ToArray();
        _recipientFirstWords = BuildSet(recipientCallsigns);
        _callsignRx = BuildCallsignRegex(_recipientFirstWords);
    }

    /// Update the list of accepted recipients at runtime. Used by the
    /// RoleManager when new DCS units appear/disappear that match a role.
    public void SetRecipients(IEnumerable<string> recipientCallsigns)
    {
        var newSet = BuildSet(recipientCallsigns);
        var newRx = BuildCallsignRegex(newSet);
        lock (_lock)
        {
            _recipientFirstWords = newSet;
            _callsignRx = newRx;
        }
    }

    public RadioCall? Parse(string rawText)
    {
        if (string.IsNullOrWhiteSpace(rawText)) return null;

        HashSet<string> recipients;
        Regex rx;
        lock (_lock)
        {
            recipients = _recipientFirstWords;
            rx = _callsignRx;
        }

        var text = Normalize(rawText);

        string? recipient = null;
        string? caller = null;

        foreach (Match m in rx.Matches(text))
        {
            var rawName = m.Groups["name"].Value.ToLowerInvariant();

            // Canonicalize via global alias table (e.g. "wavya" -> "Warrior",
            // "wisard" -> "Wizard"). If the canonical name is one of our known
            // recipients, this match is for us.
            var canonical = Callsigns.Canonical(rawName);
            var firstWord = (canonical ?? rawName).Split(' ', 2)[0].ToLowerInvariant();

            if (recipients.Contains(firstWord))
            {
                // Always output the canonical name (with proper capitalization)
                // so response generators can use call.Recipient as a stable key.
                recipient ??= FormatCallsign(m, canonical ?? CapitalizeFirstWord(rawName));
            }
            else
            {
                caller ??= FormatCallsign(m, canonical);
            }
        }

        // No recipient mention — this transmission isn't for any of our agents.
        if (recipient is null) return null;

        Intent? matched = null;
        foreach (var rule in _rules)
        {
            if (rule.Pattern.IsMatch(text)) { matched = rule.Intent; break; }
        }

        var finalIntent = (caller is not null && matched.HasValue)
            ? matched.Value
            : Intent.Unrecognized;

        return new RadioCall(
            Caller: caller,
            Recipient: recipient,
            AddressedToRecipient: true,
            Intent: finalIntent,
            NormalizedText: text);
    }

    private static HashSet<string> BuildSet(IEnumerable<string> recipients)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in recipients)
        {
            if (string.IsNullOrWhiteSpace(r)) continue;
            set.Add(r.Split(' ', 2)[0].ToLowerInvariant());
        }
        return set;
    }

    private static Regex BuildCallsignRegex(IEnumerable<string> recipientFirstWords)
    {
        var names = Callsigns.All.Select(c => c.ToLowerInvariant())
            .Concat(Callsigns.Aliases.Keys.Select(a => a.ToLowerInvariant()))
            .Concat(recipientFirstWords)
            .Distinct();
        var alts = string.Join("|", names.Select(Regex.Escape));
        var pattern =
            @"\b(?<name>" + alts + @")\b" +
            @"(?:\s+(?<num>\d+(?:[\s\-]\d+){0,2}))?";
        return new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);
    }

    private static string Normalize(string text)
    {
        text = text.ToLowerInvariant();
        // Strip non-word, non-whitespace including hyphens like "6-7-2".
        text = Regex.Replace(text, @"[^\w\s]", " ");
        // Split letter-digit transitions ("wizard11viper21" → "wizard 11 viper 21").
        text = Regex.Replace(text, @"(?<=[a-z])(?=\d)|(?<=\d)(?=[a-z])", " ");
        var tokens = text.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < tokens.Length; i++)
            if (NumberWords.TryGetValue(tokens[i], out var d)) tokens[i] = d;
        return string.Join(' ', tokens);
    }

    private static string FormatCallsign(Match m, string? canonicalName = null)
    {
        var name = canonicalName ?? m.Groups["name"].Value;
        if (name.Length > 0)
            name = char.ToUpper(name[0]) + name[1..].ToLowerInvariant();

        var num = m.Groups["num"].Value;
        if (string.IsNullOrEmpty(num)) return name;

        num = Regex.Replace(num.Trim(), @"\s+", "-");
        return $"{name} {num}";
    }

    private static string CapitalizeFirstWord(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        return char.ToUpper(s[0]) + s[1..].ToLowerInvariant();
    }
}
