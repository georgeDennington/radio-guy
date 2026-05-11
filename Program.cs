using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using RadioMan;
using RadioMan.Agents;
using RadioMan.Audio;
using RadioMan.Conditions;
using RadioMan.Dcs;
using RadioMan.Parsing;
using RadioMan.Responses;
using RadioMan.Stt;
using RadioMan.Tts;

const int VK_SPACE = 0x20;
const int VK_ESCAPE = 0x1B;

// Static defaults — used if DCS isn't connected or has no matching units.
// RoleManager discovers more at runtime and unions with these.
string[] DefaultAwacsCallsigns = { "Wizard", "Magic" };
string[] DefaultJtacCallsigns = { "Hammer" };
string[] DefaultAirbossCallsigns = { "Boss" };

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

// Pick fresh random voices for each agent on every startup, so they're
// distinguishable from each other AND different from last session. When we
// later add more agents (per-flight, per-unit), this pool keeps expanding.
var voicePool = KokoroTts.AvailableMaleEnglishVoiceNames();
var picks = KokoroTts.PickDistinctVoices(voicePool, count: 3);
var awacsVoice = picks.Count > 0 ? picks[0] : "am_michael";
var jtacVoice = picks.Count > 1 ? picks[1] : awacsVoice;
var airbossVoice = picks.Count > 2 ? picks[2] : awacsVoice;

// DCS data source. Defaults to the real gRPC client which talks to
// DCS-gRPC (https://github.com/DCS-gRPC/rust-server). Pass `--offline`
// to skip the gRPC connection entirely — useful for developing the voice
// pipeline without DCS open. Either way, the system stays usable: when
// no fresh data is available, response generators degrade to generic
// responses.
IDcsClient dcs = args.Contains("--offline")
    ? new OfflineDcsClient()
    : new DcsGrpcClient();
using var _dcsDispose = dcs;

// Pilots who've checked in with AWACS. AWACS proactive calls (merge, splash,
// threat) only fire for callsigns in this roster. The AWACS response generator
// adds/removes entries via the check-in / check-out intents.
var awacsRoster = new AwacsRoster();

// Shared carrier map — keyed by airboss recipient callsign, populated by
// RoleManager from DCS. Pre-seeded with Roosevelt so offline testing has a
// carrier to address.
var carriers = new ConcurrentDictionary<string, Carrier>(StringComparer.OrdinalIgnoreCase);
carriers["Boss"] = Carriers.Roosevelt;

Console.WriteLine("Setting up agents:");
var awacsParser = new RegexIntentParser(DefaultAwacsCallsigns, IntentRulesets.Awacs);
var awacs = new RadioAgent(
    role: "AWACS",
    callsign: "AWACS",   // role-level label only — pilot uses any of the recipient callsigns
    parser: awacsParser,
    responder: new AwacsResponseGenerator(dcs, awacsRoster),
    tts: new KokoroTts(sharedKokoro, awacsVoice, speed: 1.0f));

var jtacParser = new RegexIntentParser(DefaultJtacCallsigns, IntentRulesets.Jtac);
var jtac = new RadioAgent(
    role: "JTAC",
    callsign: "JTAC",
    parser: jtacParser,
    responder: new JtacResponseGenerator(),
    tts: new KokoroTts(sharedKokoro, jtacVoice, speed: 0.85f));

var airbossParser = new RegexIntentParser(DefaultAirbossCallsigns, IntentRulesets.Airboss);
var airboss = new RadioAgent(
    role: "AIRBOSS",
    callsign: "AIRBOSS",
    parser: airbossParser,
    responder: new AirbossResponseGenerator(dcs, carriers),
    tts: new KokoroTts(sharedKokoro, airbossVoice, speed: 1.0f));

var agents = new[] { awacs, jtac, airboss };

// RoleManager keeps the parser recipient sets in sync with what's actually
// in DCS — discovers AWACS aircraft, JTAC ground units, carrier ships and
// updates each parser's recognized recipients accordingly. Static defaults
// above are always preserved.
using var roleManager = new RoleManager(
    dcs: dcs,
    carriers: carriers,
    awacsParser: awacsParser,
    jtacParser: jtacParser,
    airbossParser: airbossParser,
    staticAwacs: DefaultAwacsCallsigns,
    staticJtacs: DefaultJtacCallsigns,
    staticAirbosses: DefaultAirbossCallsigns);

using var pipeline = new RadioPipeline(
    input: new MicAudioInput(),
    transcriber: new WhisperTranscriber(modelPath),
    router: new AgentRouter(agents),
    output: new SpeakerAudioOutput());

// Proactive-call scheduler. Watches register here and emit ScheduledCalls
// when conditions fire (merges, splashes, etc.). Delivery goes through the
// pipeline's SpeakAsync so it shares the audio lock with PTT responses.
using var scheduler = new WatchScheduler(dcs, async call =>
{
    Console.WriteLine($"\n[sched] {call.Agent.Role}: {call.Message}");
    await pipeline.SpeakAsync(call.Agent, call.Message);
});

// First proactive condition: AWACS calls "merged" when a friendly closes to
// within 3 nm of a hostile — but only for pilots who've checked in.
var mergeDetector = new MergeDetector(awacs, scheduler, awacsRoster);
mergeDetector.Start();

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
