# radio-man

A local, voice-driven AI controller for DCS World (and any flight sim that doesn't object). Talk to the mic; an AWACS or a JTAC talks back.

This is a proof-of-concept. It runs entirely on the local machine — no cloud calls — using Whisper for speech-to-text, Kokoro for text-to-speech, and a pile of regex + state machines for everything in between.

---

## What works right now

- **Push-to-talk**: hold SPACE, speak, release.
- **Two AI controllers** sharing one mic / speaker:
  - **AWACS** — *Wizard 1-1*. Voice: Kokoro `am_michael`.
    - `picture`, `bogey dope`, `declare`
  - **JTAC** — *Warrior 1-1*. Voice: Kokoro `am_adam` at 0.85x speed.
    - `check in`, `request 9-line`, `ready for the rest` (and friends — see Aliasing), `say again`, `readback verification`, `calling in`, `off target`
- **Numbers and grids spoken digit-by-digit** with comma-paused cadence — *"two, seven, zero"*, *"papa, alpha, four, five, six, seven, eight, nine, zero, one"*.
- **9-line decision tree** with breaks, readback verification, and an "abort, abort, abort" if the pilot calls in without clearance.
- **Loose readback matching**: pilot's readback verifies if the data is *somewhere* in the transcript, in any order, regardless of phrasing.
- **Whisper-aware aliasing**: "Wiper", "Wavya", "Lemur" all route to *Viper*, *Warrior*, *Lima* respectively.
- **Dynamic console panel**: shows the natural next things to say, updated after every turn.

---

## Quick start

### Prerequisites

- Windows 10 (1903 / build 19041 or later) or Windows 11
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (`winget install Microsoft.DotNet.SDK.8`)
- A working microphone and speakers
- ~500 MB free disk on first run for the Whisper + Kokoro models

### Run

```powershell
cd radio-man
dotnet run
```

First launch downloads `ggml-base.en.bin` (~140 MB) and the Kokoro 82M ONNX model (~320 MB) into the project folder. Subsequent launches start in seconds.

You'll see something like:

```
=== radio-man POC ===

Loading TTS model...
Setting up agents:
  voice : Kokoro/am_michael  (Male, en-US, speed 1.00)
  voice : Kokoro/am_adam     (Male, en-US, speed 0.85)

Active agents:
  AWACS  Wizard 1-1
  JTAC   Warrior 1-1

Hold SPACE to talk, release to transmit. ESC to quit.

Next available:
  AWACS  Wizard 1-1
    picture          → "Wizard, Viper 2-1, request picture"
    bogey dope       → "Wizard, Viper 2-1, bogey dope"
    declare          → "Wizard, Viper 2-1, declare bullseye 270 35"
  JTAC   Warrior 1-1
    check in         → "Warrior, Viper 2-1, ready for tasking"
    request 9-line   → "Warrior, Viper 2-1, ready for 9-line"

Ready.
```

### CLI flags

| Flag | What it does |
|---|---|
| `--list-kokoro-voices` | Dumps every Kokoro voice with gender + language and exits |

---

## How it works

```
                                       ┌──────────────┐
                                       │  AWACS Agent │
                                       │  Wizard 1-1  │
                                       │  ├ Parser    │
                                       │  ├ Responder │
                                       │  └ TTS voice │
                                       └──────┬───────┘
 ┌────────┐   ┌─────────┐   ┌─────────┐       │
 │  Mic   │ → │ Whisper │ → │ Router  │ ──────┤
 │ NAudio │   │  STT    │   │         │       │
 └────────┘   └─────────┘   └─────────┘       │
                                              ┌──────┴───────┐
                                              │  JTAC Agent  │
                                              │  Warrior 1-1 │
                                              │  ├ Parser    │
                                              │  ├ Responder │
                                              │  └ TTS voice │
                                              └──────┬───────┘
                                                     │
                            ┌────────┐   ┌──────────▼─────┐
                            │Speaker │ ◀ │  Kokoro TTS   │
                            │ NAudio │   │  (shared model)│
                            └────────┘   └────────────────┘
```

### The pipeline (per push-to-talk cycle)

1. **Capture** — `MicAudioInput` records 16 kHz mono PCM16 into a buffer while SPACE is held.
2. **Transcribe** — `WhisperTranscriber` wraps the PCM as WAV, runs Whisper.net's `base.en` model with a vocabulary-biasing prompt ("aviation radio brevity, callsigns Wizard 1-1, Viper 2-1…"), returns text.
3. **Route** — `AgentRouter` walks each agent's parser. The first one whose parser produces `AddressedToRecipient = true` wins.
4. **Parse** — `RegexIntentParser` normalizes the transcript (lowercase, strip punctuation including hyphens, split letter-digit boundaries, convert "one"–"nine" to digits), then tests each intent rule's regex. Returns a `RadioCall { Caller, Recipient, Intent, NormalizedText }`.
5. **Respond** — the matched agent's `IResponseGenerator` builds the reply string, advancing its conversation state if applicable.
6. **Speak** — `KokoroTts` tokenizes the reply, submits a `KokoroJob` directly to the shared `KokoroTTS` engine (bypassing its built-in playback), receives raw `float[]` samples, wraps as 24 kHz mono WAV.
7. **Play** — `SpeakerAudioOutput` plays the WAV through the default audio device via NAudio.

---

## Project layout

```
radio-man/
├── Program.cs                          ← entry point + PTT loop + agent wiring
├── Providers.cs                        ← all interfaces (IAudioInput, ITranscriber, …)
├── RadioCall.cs                        ← Intent enum, RadioCall record, NextStep record
├── RadioPipeline.cs                    ← orchestrator
├── RadioMan.csproj
│
├── Audio/         (RadioMan.Audio)
│   ├── MicAudioInput.cs                ← NAudio WaveInEvent → PCM16 buffer
│   └── SpeakerAudioOutput.cs           ← NAudio WaveOutEvent + WaveFileReader
│
├── Stt/           (RadioMan.Stt)
│   └── WhisperTranscriber.cs           ← Whisper.net + vocabulary prompt
│
├── Tts/           (RadioMan.Tts)
│   ├── KokoroTts.cs                    ← KokoroSharp; supports sharing one model
│   └── WindowsTts.cs                   ← Windows OneCore SAPI fallback (legacy)
│
├── Parsing/       (RadioMan.Parsing)
│   ├── Callsigns.cs                    ← 200 canonical callsigns + alias map
│   ├── IntentRulesets.cs               ← AWACS + JTAC regex rule lists
│   └── RegexIntentParser.cs            ← normalize → match intent → extract callsigns
│
├── Responses/     (RadioMan.Responses)
│   ├── BrevityFormat.cs                ← Digits/Decimal/Phonetic helpers (comma-paused)
│   ├── AwacsResponseGenerator.cs       ← stateless: picture / bogey dope / declare
│   └── JtacResponseGenerator.cs        ← state machine: check-in → 9-line → readback → cleared hot → off
│
└── Agents/        (RadioMan.Agents)
    ├── RadioAgent.cs                   ← (Role, Callsign, Parser, Responder, Tts) bundle
    └── AgentRouter.cs                  ← first-parser-that-recognizes-recipient wins
```

---

## Conversation examples

### AWACS — picture

```
You    : "Wizard, Viper 2-1, request picture."
Wizard : "Viper 2-1, Wizard, picture: two groups, bullseye two, seven, zero
          for three, five, two, five thousand, hot, hostile."
```

### JTAC — full 9-line cycle

```
You    : "Warrior, Viper 2-1, ready for tasking."
Warrior: "Viper 2-1, Warrior, copy your check-in. Tasking to follow.
          Advise ready for 9-line."

You    : "Warrior, Viper 2-1, ready for 9-line."
Warrior: "Viper 2-1, Warrior, type 2 in effect.
          Line 1, IP DELTA. Break.
          Line 2, heading two, seven, zero, offset right. Break.
          Line 3, distance four point two miles. Advise ready for the rest."

You    : "Warrior, Viper 2-1, go ahead."          ← any of: ready / send it / continue / proceed / copy / etc.
Warrior: "Line 4, target elevation one, two, zero, zero feet. Break.
          Line 5, target description, four BTRs in column. Break.
          Line 6, target location grid papa, alpha, four, five, six, seven,
          eight, nine, zero, one. Advise ready for the rest."

You    : "Warrior, Viper 2-1, send it."
Warrior: "Line 7, mark with laser, code one, six, eight, eight. Break.
          Line 8, friendlies six, zero, zero meters west. Break.
          Line 9, egress north, climb to one, five thousand.
          Read back lines 4, 6, and 8."

You    : "Warrior, Viper 2-1, elevation 1200 feet, grid PA 4567 8901,
          friendlies 600 meters west."
Warrior: "Viper 2-1, Warrior, good readback. Cleared to engage when ready."

You    : "Warrior, Viper 2-1, in hot from south."
Warrior: "Viper 2-1, Warrior, cleared hot."

You    : "Warrior, Viper 2-1, off west."
Warrior: "Viper 2-1, Warrior, copy off. Pass BDA when able."
```

### JTAC — wrong readback

```
You    : "Warrior, Viper 2-1, elevation 1500 feet, grid PA 4567 8901,
          friendlies 600 meters west."   ← elevation wrong
Warrior: "Viper 2-1, Warrior, negative. Correction.
          Line 4, target elevation one, two, zero, zero feet.
          Read back again."
```

The console also prints a diagnostic block when verification runs:

```
readback diag:
  heard numbers   : 600, 1500, 4567, 8901, …
  heard letters   : P, PA, A
  heard directions: west
  expected        : elev=1200, grid=PA 4567/8901, friend=600m west
```

### JTAC — out of order

```
You    : "Warrior, Viper 2-1, in hot from south."   ← no active brief
Warrior: "Viper 2-1, Warrior, abort, abort, abort. No clearance."
```

---

## Architecture notes

### Provider abstractions

Every external dependency is behind an interface in [Providers.cs](Providers.cs):

| Interface | Default implementation | What it costs to swap |
|---|---|---|
| `IAudioInput` | `MicAudioInput` (NAudio) | new file in `Audio/` |
| `IAudioOutput` | `SpeakerAudioOutput` (NAudio) | new file in `Audio/`; could add a radio-FX decorator |
| `ITranscriber` | `WhisperTranscriber` (Whisper.net + base.en) | new file in `Stt/`; e.g. cloud STT |
| `ITextToSpeech` | `KokoroTts` (KokoroSharp 82M) | new file in `Tts/`; e.g. Piper, ElevenLabs |
| `IIntentParser` | `RegexIntentParser` | new class; or an LLM-driven parser |
| `IResponseGenerator` | per-agent (Awacs / Jtac) | new class in `Responses/` |

`Program.cs` is the only place that knows which concrete implementation goes where.

### Agents and the router

A `RadioAgent` is a bundle of `(Role, Callsign, Parser, Responder, Tts)`. The `AgentRouter` runs each agent's parser and picks the first one that recognizes its own recipient callsign and an intent. This means:

- Each agent has its own intent ruleset, response generator, and voice.
- Agents are independent; the JTAC's state machine doesn't see AWACS calls and vice versa.
- Adding a new controller is one `RadioAgent(...)` and one entry in the array passed to `AgentRouter`.

### State machines

`AwacsResponseGenerator` is stateless — every call is independent.

`JtacResponseGenerator` walks an explicit `FlowState` enum:

```
Idle / Complete
   ↓ CheckIn
CheckedIn
   ↓ Request9Line
BriefInProgress1   ── ReadyForRest ──▶ BriefInProgress2
                                            │
                                       ReadyForRest
                                            ▼
                                     AwaitingReadback ──◀ wrong readback (stays here)
                                            │
                                     correct readback
                                            ▼
                                       BriefDone
                                            ↓ CallingIn
                                       ClearedHot
                                            ↓ Off
                                       Complete (loops to Idle)
```

`SayAgain` is allowed from any state and replays `_lastResponse`. Calls that don't match the current state get one of: `"unable"`, `"no brief in progress"`, or `"abort, abort, abort"`.

### `IResponseGenerator.AvailableNext()`

Each generator exposes the natural next moves for its current state as a list of `NextStep { Hint, Example }`. `Program.cs` prints these after every successful turn, so the panel always reflects what's possible *now*.

This is the seed for the future formal decision-tree: when (if) a `DecisionNode { Id, Edges }` graph type lands, `AvailableNext` becomes `currentNode.Edges` — same shape, no caller changes.

---

## Extension points

### Adding a new intent to an existing agent

Three small edits:

1. Add the value to the `Intent` enum in [RadioCall.cs](RadioCall.cs).
2. Add a regex rule to the appropriate list in [IntentRulesets.cs](Parsing/IntentRulesets.cs). Order matters when patterns can overlap — see the comment block above the JTAC list.
3. Add a switch arm in the agent's response generator. Optionally add a state transition.

### Adding a whole new controller (e.g. tanker)

1. Add intents (e.g. `TankerPreContact`, `TankerCleared`, …) to the `Intent` enum.
2. Add a rule list `IntentRulesets.Tanker` in [IntentRulesets.cs](Parsing/IntentRulesets.cs).
3. Write `Responses/TankerResponseGenerator.cs : IResponseGenerator`.
4. In [Program.cs](Program.cs), add one more `RadioAgent` and pass it to `AgentRouter`.

### Adding a callsign or alias

Open [Callsigns.cs](Parsing/Callsigns.cs):

- New canonical callsign → add a string to `All`.
- Whisper mistranscribes an existing one → add an entry to `Aliases`:

  ```csharp
  ["whatever-whisper-said"] = "ProperCallsign",
  ```

Both forms register with the parser regex. Alias matches get canonicalized — i.e., "Wiper 2-1" shows up as "Viper 2-1" in `RadioCall.Caller`, even though the raw transcript said "Wiper".

### Tuning voices and pacing

In [Program.cs](Program.cs):

```csharp
tts: new KokoroTts(sharedKokoro, "am_michael", speed: 1.0f),   // AWACS, normal
tts: new KokoroTts(sharedKokoro, "am_adam",   speed: 0.85f),   // JTAC, slower
```

- **Voice name** — see `KokoroVoiceManager.Voices` (or run `dotnet run -- --list-kokoro-voices`). Useful starters: `am_michael`, `am_adam`, `bm_george`, `bm_lewis`, `af_heart`, `af_nicole`.
- **Speed** — `1.0` is normal; `0.85`–`0.75` for slower, more deliberate delivery; `1.1`–`1.2` for snappier.

### Reading numbers and grids more or less choppy

[BrevityFormat.cs](Responses/BrevityFormat.cs) joins digits with `", "` to introduce TTS pauses. To make it slower still, change the separator to `". "` (period = longer pause). To speed up, replace with `" "` (just spaces).

---

## Trade-offs and known issues

- **Whisper accuracy is the weak link.** `base.en` is fast but mishears callsigns and aviation jargon. The vocabulary prompt and alias map mitigate this; bumping to `small.en` (470 MB) measurably improves recognition. To switch, change `EnsureModelAsync()`'s default in [WhisperTranscriber.cs](Stt/WhisperTranscriber.cs) to `GgmlType.SmallEn` and adjust the filename.
- **Loose readback matching can miss swaps.** If the pilot reads back the right *numbers* but assigns them to the wrong *lines* (e.g., elevation 600 / friendlies 1200 when the brief was the opposite), both numbers are present so verification passes. Acceptable for a POC where natural phrasing matters more.
- **Word-form numbers** like "twelve hundred" aren't converted (only "zero"–"nine"). Pilots reading digit-by-digit ("one, two, zero, zero") is the supported form. Whisper transcribing "1200" as a single token also works.
- **Push-to-talk is keyboard-only.** Real DCS PTT is a HOTAS button. Swapping the SPACE key check for a DirectInput joystick read is a small change in [Program.cs](Program.cs).
- **No audio FX yet** — the AWACS/JTAC voice is dry. A radio-effect band-pass + noise decorator on `IAudioOutput` would belong in [SpeakerAudioOutput.cs](Audio/SpeakerAudioOutput.cs) or a new `RadioFxOutput` wrapper.
- **One JTAC, one global state.** If multiple flights talked to the same JTAC simultaneously, they'd share the 9-line state. Per-caller state is a `Dictionary<callsign, FlowState>` away.
- **Stateful generators aren't unified.** The pattern in `JtacResponseGenerator` (manual state enum + switch) will get unwieldy past two or three flows. When that happens, extracting a generic decision-tree class is the obvious refactor — see `AvailableNext()` for the seed of that API.

---

## Tech stack

| Layer | Library | Why |
|---|---|---|
| Audio I/O | [NAudio](https://github.com/naudio/NAudio) | Mature Windows audio. WaveInEvent for capture, WaveOutEvent for playback. |
| STT | [Whisper.net](https://github.com/sandrohanea/whisper.net) | Wraps whisper.cpp. Local, free, GGML models. |
| TTS | [KokoroSharp.CPU](https://github.com/Lyrcaxis/KokoroSharp) | Wraps Kokoro 82M neural TTS via ONNX Runtime. Voice quality is dramatically better than SAPI. |
| Runtime | .NET 8 (`net8.0-windows10.0.19041.0`) | WinRT-capable TFM, lets us reach `Windows.Media.SpeechSynthesis` if we ever want SAPI fallback. |

---

## Roadmap

In rough priority order, things this project could do next:

1. **Hook DCS Lua Export** — replace stub responses with real game-state data (bullseye, bogey positions, fuel, weapons, friendlies). The `IResponseGenerator` boundary is exactly where this plugs in.
2. **Joystick PTT** — read DirectInput buttons instead of SPACE.
3. **Radio FX decorator** — band-pass + light noise on `IAudioOutput` so the AWACS/JTAC sound like they're on a real radio.
4. **Per-caller state for the JTAC** — multiple flights interleaved.
5. **Better STT** — `small.en` or a tuned `medium.en`; or the user's own LoRA over a Whisper variant on aviation vocabulary.
6. **Read-back validation tightening** — anchor extracted numbers to keywords so "elevation 600 / friendlies 1200" doesn't pass when the brief says the opposite.
7. **Tanker controller** — pre-contact / contact / cleared off, with state coupled to fuel quantity.
8. **A formal `DecisionTree` graph** — once two or three stateful generators exist, extract the pattern.
9. **Slash-command-style direct intents in console** — so a developer can test a flow without speaking.

---

## License

POC code, MIT-style — do whatever, just don't sue. Whisper, Kokoro, NAudio, and ONNX Runtime carry their own licenses; review those if you ship anything binary.
