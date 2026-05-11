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

    private readonly string _primaryRecipientName;
    private readonly HashSet<string> _recipientNames;
    private readonly IReadOnlyList<IntentRule> _rules;
    private readonly Regex _callsignRx;

    public RegexIntentParser(
        string recipientCallsign,
        IEnumerable<IntentRule> rules,
        IEnumerable<string>? recipientAliases = null)
    {
        _primaryRecipientName = recipientCallsign.Split(' ', 2)[0];
        var primary = _primaryRecipientName.ToLowerInvariant();
        _recipientNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { primary };
        if (recipientAliases is not null)
        {
            foreach (var a in recipientAliases)
                _recipientNames.Add(a.ToLowerInvariant());
        }

        _rules = rules.ToArray();
        _callsignRx = BuildCallsignRegex(_recipientNames);
    }

    public RadioCall? Parse(string rawText)
    {
        if (string.IsNullOrWhiteSpace(rawText)) return null;

        var text = Normalize(rawText);

        // Find recipient and caller via the callsign regex.
        string? recipient = null;
        string? caller = null;

        foreach (Match m in _callsignRx.Matches(text))
        {
            var rawName = m.Groups["name"].Value.ToLowerInvariant();

            if (_recipientNames.Contains(rawName))
                recipient ??= FormatCallsign(m, _primaryRecipientName);
            else
                caller ??= FormatCallsign(m, Callsigns.Canonical(rawName));
        }

        // No mention of our recipient → call isn't for this agent.
        if (recipient is null) return null;

        // Try to match an intent.
        Intent? matched = null;
        foreach (var rule in _rules)
        {
            if (rule.Pattern.IsMatch(text)) { matched = rule.Intent; break; }
        }

        // Decide what we actually managed to understand.
        // - Caller heard AND intent matched -> the matched intent
        // - Either is missing -> Unrecognized (cover-all path in the responder)
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

    private static Regex BuildCallsignRegex(IEnumerable<string> recipientNames)
    {
        var names = Callsigns.All.Select(c => c.ToLowerInvariant())
            .Concat(Callsigns.Aliases.Keys.Select(a => a.ToLowerInvariant()))
            .Concat(recipientNames)
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
        // Strip all non-word, non-whitespace characters (including hyphens like
        // "6-7-2" that Whisper inserts when reading digit-by-digit).
        text = Regex.Replace(text, @"[^\w\s]", " ");
        // Whisper sometimes runs words and digits together (e.g. "wizard11viper21").
        // Split at every letter↔digit transition so callsign matching can find the boundary.
        text = Regex.Replace(text, @"(?<=[a-z])(?=\d)|(?<=\d)(?=[a-z])", " ");
        var tokens = text.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < tokens.Length; i++)
            if (NumberWords.TryGetValue(tokens[i], out var d)) tokens[i] = d;
        return string.Join(' ', tokens);
    }

    private static string FormatCallsign(Match m, string? canonicalName = null)
    {
        // Prefer the supplied canonical (e.g. "Viper" when the match was "wiper");
        // fall back to the matched text if no canonical mapping exists.
        var name = canonicalName ?? m.Groups["name"].Value;
        if (name.Length > 0)
            name = char.ToUpper(name[0]) + name[1..].ToLowerInvariant();

        var num = m.Groups["num"].Value;
        if (string.IsNullOrEmpty(num)) return name;

        num = Regex.Replace(num.Trim(), @"\s+", "-");
        return $"{name} {num}";
    }
}
