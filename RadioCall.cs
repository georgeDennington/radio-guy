namespace RadioMan;

public enum Intent
{
    // AWACS
    AwacsCheckIn,
    AwacsCheckOut,
    AwacsPicture,
    AwacsBogeyDope,
    AwacsDeclare,

    // JTAC
    JtacCheckIn,
    JtacRequest9Line,
    JtacReadyForRest,
    JtacReadback,
    JtacSayAgain,
    JtacCallingIn,
    JtacOff,

    // Airboss (carrier ops — Case I recovery cycle, launches; no LSO talkdown)
    AirbossReadyForLaunch,
    AirbossInbound,        // "X miles inbound" — Boss puts pilot into overhead holding
    AirbossCommence,       // "ready to push" / "ready to commence" — Boss clears the descent
    AirbossInTheBreak,     // pilot rolling in over the bow — clears to downwind
    AirbossAbeam,          // abeam the LSO platform on downwind
    AirbossBall,           // 3/4 mile, ball call — Boss says "roger ball"
    AirbossOffWire,        // post-trap
    AirbossSayAgain,

    // Cover-all: recipient was heard but the call wasn't fully understood.
    // Either the caller's callsign couldn't be parsed (Caller is null) or no
    // intent regex matched the rest of the transcript.
    Unrecognized,
}

public sealed record RadioCall(
    string? Caller,
    string Recipient,
    bool AddressedToRecipient,
    Intent Intent,
    string NormalizedText);

public sealed record NextStep(string Hint, string Example);
