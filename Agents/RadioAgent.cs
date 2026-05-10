namespace RadioMan.Agents;

public sealed class RadioAgent : IDisposable
{
    public string Role { get; }
    public string Callsign { get; }
    public IIntentParser Parser { get; }
    public IResponseGenerator Responder { get; }
    public ITextToSpeech Tts { get; }

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

    public void Dispose() => Tts.Dispose();
}
