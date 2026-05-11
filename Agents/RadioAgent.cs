namespace RadioMan.Agents;

public sealed class RadioAgent : IDisposable
{
    public string Role { get; }
    public string Callsign { get; }
    public IIntentParser Parser { get; }
    public IResponseGenerator Responder { get; }
    public ITextToSpeech Tts { get; }

    /// Per-agent audio lock. Guards against the same agent talking over itself
    /// when multiple watches fire concurrently. Different agents run on
    /// different radio frequencies in the model — they don't share a lock,
    /// so AWACS and JTAC can transmit in parallel.
    public SemaphoreSlim AudioLock { get; } = new(1, 1);

    public RadioAgent(
        string role,
        string callsign,
        IIntentParser parser,
        IResponseGenerator responder,
        ITextToSpeech tts)
    {
        Role = role;
        Callsign = callsign;
        Parser = parser;
        Responder = responder;
        Tts = tts;
    }

    public void Dispose()
    {
        Tts.Dispose();
        AudioLock.Dispose();
    }
}
