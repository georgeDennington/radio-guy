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

    public static readonly IReadOnlyList<RegexIntentParser.IntentRule> Airboss = new[]
    {
        // "ready cats" / "ready for launch" / "ready to launch" / "ready on the cat"
        new RegexIntentParser.IntentRule(Intent.AirbossReadyForLaunch,
            new Regex(@"\bready\s+(?:cats?|on\s+(?:the\s+)?cat|to\s+launch|for\s+launch)\b", RxOpts)),

        // "off the wire" / "out of the wire" / "clear of the landing area"
        new RegexIntentParser.IntentRule(Intent.AirbossOffWire,
            new Regex(@"\b(?:off|out\s+of)\s+(?:the\s+)?(?:wire|wires|gear)\b|\bclear\s+of\s+(?:the\s+)?landing\b", RxOpts)),

        // The ball call: must come BEFORE inbound so "see you at the ball" doesn't
        // get captured by a too-greedy inbound rule.
        new RegexIntentParser.IntentRule(Intent.AirbossBall,
            new Regex(@"\b(?:see\s+you\s+at\s+(?:the\s+)?ball|ball)\b", RxOpts)),

        // Ready to descend from overhead to the break. Specific enough to not
        // collide with "ready cats" (launch) or generic "ready".
        new RegexIntentParser.IntentRule(Intent.AirbossCommence,
            new Regex(@"\b(?:ready\s+to\s+(?:push|commence)|commencing|pushing\s+for\s+(?:the\s+)?break)\b", RxOpts)),

        // In the break — pilot rolling in over the bow into downwind.
        new RegexIntentParser.IntentRule(Intent.AirbossInTheBreak,
            new Regex(@"\bin\s+the\s+break\b|\bbreaking\s+(?:left|right|now)\b", RxOpts)),

        // Abeam call (downwind, abeam LSO platform).
        new RegexIntentParser.IntentRule(Intent.AirbossAbeam,
            new Regex(@"\babeam\b", RxOpts)),

        // Inbound / initial: "ten miles inbound", "three mile initial", "X miles out".
        new RegexIntentParser.IntentRule(Intent.AirbossInbound,
            new Regex(@"\b(?:inbound|initial|miles\s+out|request(?:ing)?\s+(?:recovery|to\s+land))\b", RxOpts)),

        new RegexIntentParser.IntentRule(Intent.AirbossSayAgain,
            new Regex(@"\b(?:say\s+again|repeat(?:\s+your\s+last)?)\b", RxOpts)),
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
