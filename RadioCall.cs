namespace RadioMan;

public enum Intent
{
    // AWACS
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
