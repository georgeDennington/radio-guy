using System.Text.RegularExpressions;

namespace RadioMan.Parsing;

public static class IntentRulesets
{
    private const RegexOptions RxOpts = RegexOptions.IgnoreCase | RegexOptions.Compiled;

    public static readonly IReadOnlyList<RegexIntentParser.IntentRule> Awacs = new[]
    {
        new RegexIntentParser.IntentRule(Intent.AwacsPicture,
            new Regex(@"\b(?:request(?:ing)?\s+)?picture\b", RxOpts)),

        new RegexIntentParser.IntentRule(Intent.AwacsBogeyDope,
            new Regex(@"\b(?:request(?:ing)?\s+)?(?:bogey|bogie|boogie)\s+dope\b", RxOpts)),

        new RegexIntentParser.IntentRule(Intent.AwacsDeclare,
            new Regex(@"\b(?:request(?:ing)?\s+)?declare\b", RxOpts)),
    };

    // Order matters: most-specific patterns first so the permissive
    // "ReadyForRest" rule doesn't swallow "ready for tasking" / "ready for 9-line".
    public static readonly IReadOnlyList<RegexIntentParser.IntentRule> Jtac = new[]
    {
        new RegexIntentParser.IntentRule(Intent.JtacRequest9Line,
            new Regex(@"\b(?:request(?:ing)?\s+|ready\s+for\s+|send\s+(?:me\s+)?)?(?:nine|9)[\s-]*line\b", RxOpts)),

        new RegexIntentParser.IntentRule(Intent.JtacCheckIn,
            new Regex(@"\b(?:checking\s+in|check\s+in|ready\s+for\s+tasking|on\s+station)\b", RxOpts)),

        new RegexIntentParser.IntentRule(Intent.JtacSayAgain,
            new Regex(@"\b(?:say\s+again|repeat(?:\s+your\s+last)?)\b", RxOpts)),

        new RegexIntentParser.IntentRule(Intent.JtacCallingIn,
            new Regex(@"\bin\s+(?:hot|cold|dry|from)\b", RxOpts)),

        new RegexIntentParser.IntentRule(Intent.JtacOff,
            new Regex(@"\boff\s+(?:hot|cold|dry|target|left|right|north|south|east|west)\b", RxOpts)),

        // Readback keywords — fires whenever the pilot mentions any of the
        // line-4/6/8 values. The state machine in the responder decides what
        // to do with it (verify vs reject as out-of-order).
        new RegexIntentParser.IntentRule(Intent.JtacReadback,
            new Regex(@"\b(?:read\s*back|elevation|grid|friendlies)\b", RxOpts)),

        // ReadyForRest is intentionally last because the bare "ready" branch
        // would shadow the more specific phrases above.
        new RegexIntentParser.IntentRule(Intent.JtacReadyForRest,
            new Regex(
                @"\b(?:" +
                    @"ready\s+(?:to\s+copy|for\s+(?:the\s+)?(?:rest|more|next))" +    // ready to copy / ready for the rest / ready for more / ready for next
                    @"|send\s+(?:it|the\s+rest|when\s+ready)" +                       // send it / send the rest / send when ready
                    @"|go\s+ahead" +                                                  // go ahead
                    @"|continue" +                                                    // continue
                    @"|proceed" +                                                     // proceed
                    @"|copy(?:\s+(?:and\s+)?continue)?" +                             // copy / copy continue / copy and continue
                    @"|ready" +                                                       // bare "ready" (last resort)
                @")\b",
                RxOpts)),
    };
}
