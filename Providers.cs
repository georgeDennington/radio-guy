namespace RadioMan;

public interface IAudioInput : IDisposable
{
    int SampleRate { get; }
    void Start();
    Task<byte[]> StopAsync();
}

public interface IAudioOutput : IDisposable
{
    Task PlayAsync(byte[] wavBytes);
}

public interface ITranscriber : IDisposable
{
    Task<string> TranscribeAsync(byte[] pcm16Mono, int sampleRate);
}

public interface IIntentParser
{
    RadioCall? Parse(string text);
}

public interface IResponseGenerator
{
    string Respond(RadioCall call);
    IReadOnlyList<NextStep> AvailableNext();
}

public interface ITextToSpeech : IDisposable
{
    Task<byte[]> SynthesizeAsync(string text);
}
