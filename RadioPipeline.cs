using System.Diagnostics;
using System.Text.RegularExpressions;
using RadioMan.Agents;

namespace RadioMan;

public sealed record TransmissionResult(
    string Status,
    string? Heard = null,
    string? AgentCallsign = null,
    RadioCall? Call = null,
    string? Response = null,
    TimeSpan TranscribeTime = default,
    TimeSpan FirstSynthTime = default,
    TimeSpan TotalSynthTime = default,
    TimeSpan PlaybackTime = default)
{
    public static TransmissionResult TooShort() => new("too-short");

    public static TransmissionResult NoMatch(string heard, TimeSpan transcribeTime)
        => new("no-match", Heard: heard, TranscribeTime: transcribeTime);

    public static TransmissionResult Ok(
        string heard, string agentCallsign, RadioCall call, string response,
        TimeSpan transcribeTime,
        TimeSpan firstSynthTime, TimeSpan totalSynthTime, TimeSpan playbackTime)
        => new("ok", heard, agentCallsign, call, response,
               transcribeTime, firstSynthTime, totalSynthTime, playbackTime);
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

        var sw = Stopwatch.StartNew();
        var heard = await _transcriber.TranscribeAsync(pcm, _input.SampleRate);
        var transcribeTime = sw.Elapsed;

        var routed = _router.Route(heard);
        if (routed is null)
            return TransmissionResult.NoMatch(heard, transcribeTime);

        var (agent, call) = routed.Value;
        var response = agent.Responder.Respond(call);

        // Sentence-level streaming: synthesize first sentence, then overlap each
        // subsequent synth with playback of the previous one. First audio starts
        // after one sentence's worth of synth, not the whole response.
        var sentences = SplitForStreaming(response);

        var totalSynthSw = Stopwatch.StartNew();
        var firstSynthSw = Stopwatch.StartNew();
        var currentWav = await agent.Tts.SynthesizeAsync(sentences[0]);
        var firstSynthTime = firstSynthSw.Elapsed;

        var playbackSw = Stopwatch.StartNew();
        for (int i = 1; i < sentences.Count; i++)
        {
            var nextSynth = agent.Tts.SynthesizeAsync(sentences[i]);
            await _output.PlayAsync(currentWav);
            currentWav = await nextSynth;
        }
        await _output.PlayAsync(currentWav);

        var totalSynthTime = totalSynthSw.Elapsed;
        var playbackTime = playbackSw.Elapsed;

        return TransmissionResult.Ok(heard, agent.Callsign, call, response,
            transcribeTime, firstSynthTime, totalSynthTime, playbackTime);
    }

    /// Splits the response on sentence boundaries (. ! ?). Very short fragments
    /// (under ~20 chars) get merged with the next/previous so we don't synthesize
    /// "Break." as its own one-syllable inference 6 times in a 9-line.
    private static List<string> SplitForStreaming(string text, int minLength = 20)
    {
        var raw = Regex.Split(text, @"(?<=[.!?])\s+")
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToArray();

        if (raw.Length == 0) return new() { text };

        var result = new List<string>();
        var buffer = new System.Text.StringBuilder();

        foreach (var s in raw)
        {
            if (buffer.Length > 0) buffer.Append(' ');
            buffer.Append(s);

            if (buffer.Length >= minLength)
            {
                result.Add(buffer.ToString());
                buffer.Clear();
            }
        }

        if (buffer.Length > 0)
        {
            if (result.Count > 0) result[^1] = $"{result[^1]} {buffer}";
            else result.Add(buffer.ToString());
        }

        return result;
    }

    public void Dispose()
    {
        _input.Dispose();
        _transcriber.Dispose();
        _router.Dispose();
        _output.Dispose();
    }
}
