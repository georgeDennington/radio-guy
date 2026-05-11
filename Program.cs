using System.Runtime.InteropServices;
using RadioMan;
using RadioMan.Agents;
using RadioMan.Audio;
using RadioMan.Parsing;
using RadioMan.Responses;
using RadioMan.Stt;
using RadioMan.Tts;

const int VK_SPACE = 0x20;
const int VK_ESCAPE = 0x1B;

const string AwacsCallsign = "Wizard 1-1";
const string JtacCallsign = "Hammer 1-1";

if (args.Contains("--list-kokoro-voices"))
{
    KokoroTts.ListVoices();
    return;
}

Console.WriteLine("=== radio-man POC ===\n");

var modelPath = await WhisperTranscriber.EnsureModelAsync();

Console.WriteLine("Loading TTS model...");
using var sharedKokoro = KokoroTts.LoadSharedModel();

// Pre-warm Kokoro: the very first inference includes ONNX graph compilation
// that adds 1–2 seconds to the cold-start latency. Burning a tiny throwaway
// synth here pays that cost up-front so the first real call is fast.
Console.Write("Pre-warming TTS... ");
{
    using var prewarm = new KokoroTts(sharedKokoro, "am_michael", speed: 1f);
    _ = await prewarm.SynthesizeAsync("warm up");
}
Console.WriteLine("done.");

// Pick a fresh random voice for each agent on every startup, so AWACS and
// JTAC sound different from each other AND different from last session. When
// we later add more agents (per-flight, per-unit), this pool keeps expanding.
var voicePool = KokoroTts.AvailableMaleEnglishVoiceNames();
var picks = KokoroTts.PickDistinctVoices(voicePool, count: 2);
var awacsVoice = picks.Count > 0 ? picks[0] : "am_michael";
var jtacVoice = picks.Count > 1 ? picks[1] : awacsVoice;

Console.WriteLine("Setting up agents:");
var awacs = new RadioAgent(
    role: "AWACS",
    callsign: AwacsCallsign,
    parser: new RegexIntentParser(AwacsCallsign, IntentRulesets.Awacs),
    responder: new AwacsResponseGenerator(AwacsCallsign),
    tts: new KokoroTts(sharedKokoro, awacsVoice, speed: 1.0f));

var jtac = new RadioAgent(
    role: "JTAC",
    callsign: JtacCallsign,
    parser: new RegexIntentParser(JtacCallsign, IntentRulesets.Jtac),
    responder: new JtacResponseGenerator(JtacCallsign),
    tts: new KokoroTts(sharedKokoro, jtacVoice, speed: 0.85f));

var agents = new[] { awacs, jtac };

using var pipeline = new RadioPipeline(
    input: new MicAudioInput(),
    transcriber: new WhisperTranscriber(modelPath),
    router: new AgentRouter(agents),
    output: new SpeakerAudioOutput());

Console.WriteLine();
Console.WriteLine("Active agents:");
foreach (var a in agents)
    Console.WriteLine($"  {a.Role,-6} {a.Callsign}");


Console.WriteLine();
Console.WriteLine("Hold SPACE to talk, release to transmit. ESC to quit.");
Console.WriteLine();
PrintNext(agents);
Console.WriteLine("Ready.\n");

bool wasDown = false;
while (true)
{
    if (KeyDown(VK_ESCAPE))
    {
        Console.WriteLine("Bye.");
        break;
    }

    bool isDown = KeyDown(VK_SPACE);

    if (isDown && !wasDown)
    {
        Console.Write("[REC] ");
        pipeline.StartCapture();
    }
    else if (!isDown && wasDown)
    {
        Console.WriteLine("transcribing...");
        var result = await pipeline.EndCaptureAsync();
        switch (result.Status)
        {
            case "too-short":
                Console.WriteLine("  [too short, ignored]\n");
                break;
            case "no-match":
                Console.WriteLine($"  heard    : \"{result.Heard}\"");
                Console.WriteLine($"  timings  : transcribe {Ms(result.TranscribeTime)}");
                Console.WriteLine("  result   : (no recognized brevity call for any agent)\n");
                break;
            case "ok":
                Console.WriteLine($"  heard    : \"{result.Heard}\"");
                Console.WriteLine($"  routed to: {result.AgentCallsign}");
                Console.WriteLine($"  caller   : {result.Call!.Caller ?? "(unknown)"}");
                Console.WriteLine($"  recipient: {result.Call.Recipient}");
                Console.WriteLine($"  intent   : {result.Call.Intent}");
                Console.WriteLine($"  response : {result.Response}");
                Console.WriteLine(
                    $"  timings  : transcribe {Ms(result.TranscribeTime)}, " +
                    $"first-synth {Ms(result.FirstSynthTime)}, " +
                    $"total-synth {Ms(result.TotalSynthTime)}, " +
                    $"playback {Ms(result.PlaybackTime)}");
                Console.WriteLine(
                    $"             → audio starts after {Ms(result.TranscribeTime + result.FirstSynthTime)}\n");
                PrintNext(agents);
                break;
        }
    }

    wasDown = isDown;
    await Task.Delay(20);
}

static void PrintNext(IEnumerable<RadioAgent> agents)
{
    Console.WriteLine("Next available:");
    foreach (var a in agents)
    {
        var steps = a.Responder.AvailableNext();
        if (steps.Count == 0) continue;
        Console.WriteLine($"  {a.Role,-6} {a.Callsign}");
        foreach (var s in steps)
            Console.WriteLine($"    {s.Hint,-16} → \"{s.Example}\"");
    }
    Console.WriteLine();
}

static string Ms(TimeSpan t) => $"{t.TotalMilliseconds:F0}ms";

static bool KeyDown(int vKey) => (NativeMethods.GetAsyncKeyState(vKey) & 0x8000) != 0;

static class NativeMethods
{
    [DllImport("user32.dll")]
    public static extern short GetAsyncKeyState(int vKey);
}
