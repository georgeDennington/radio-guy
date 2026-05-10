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
}

public sealed record RadioCall(
    string Caller,
    string Recipient,
    bool AddressedToRecipient,
    Intent Intent,
    string NormalizedText);

public sealed record NextStep(string Hint, string Example);
