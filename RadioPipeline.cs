using RadioMan.Agents;

namespace RadioMan;

public sealed record TransmissionResult(
    string Status,
    string? Heard = null,
    string? AgentCallsign = null,
    RadioCall? Call = null,
    string? Response = null)
{
    public static TransmissionResult TooShort() => new("too-short");
    public static TransmissionResult NoMatch(string heard) => new("no-match", Heard: heard);
    public static TransmissionResult Ok(string heard, string agentCallsign, RadioCall call, string response)
        => new("ok", heard, agentCallsign, call, response);
}

public sealed class RadioPipeline : IDisposable
{
    private readonly IAudioInput _input;
    private readonly ITranscriber _transcriber;
    private readonly AgentRouter _router;
    private readonly IAudioOutput _output;

    public RadioPipeline(
        IAudioInput input,
        ITranscriber transcriber,
        AgentRouter router,
        IAudioOutput output)
    {
        _input = input;
        _transcriber = transcriber;
        _router = router;
        _output = output;
    }

    public void StartCapture() => _input.Start();

    public async Task<TransmissionResult> EndCaptureAsync()
    {
        var pcm = await _input.StopAsync();

        // ~0.4s @ 16kHz mono PCM16 = 12,800 bytes
        if (pcm.Length < 12_800)
            return TransmissionResult.TooShort();

        var heard = await _transcriber.TranscribeAsync(pcm, _input.SampleRate);

        var routed = _router.Route(heard);
        if (routed is null)
            return TransmissionResult.NoMatch(heard);

        var (agent, call) = routed.Value;
        var response = agent.Responder.Respond(call);
        var wav = await agent.Tts.SynthesizeAsync(response);
        await _output.PlayAsync(wav);

        return TransmissionResult.Ok(heard, agent.Callsign, call, response);
    }

    public void Dispose()
    {
        _input.Dispose();
        _transcriber.Dispose();
        _router.Dispose();
        _output.Dispose();
    }
}
