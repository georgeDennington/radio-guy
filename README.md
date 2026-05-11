# radio-man

A local, voice-driven AI radio comms system for DCS World. Push to talk, address the right unit on the right radio, and an AI controller answers back — with live data pulled from the running mission.

Everything runs on the local machine. No cloud calls. Whisper for STT, Kokoro for TTS, regex + state machines for parsing, and [DCS-gRPC](https://github.com/DCS-gRPC/rust-server) for game-state streaming.

---

## Scope

Four controllers, addressable by per-unit callsign, all sharing one mic and one set of speakers (each unit transmits on its own logical frequency — they never talk over each other within a role, but different roles can talk in parallel):

| Role | Status | Examples of what it answers |
|---|---|---|
| **AWACS / GCI** | shipping | check-in, picture, bogey dope, declare; proactive merge calls |
| **JTAC** | shipping | check-in, 9-line brief with readback verification, calling-in / cleared-hot / off |
| **Carrier Boss (Airboss)** | shipping | Case I recovery — overhead hold → commence → break → abeam → ball → off-the-wire; ready-cats launches |
| **ATC** | planned | departure / pattern / approach for land bases (see Roadmap) |

Each role is one **`RadioAgent`** that can serve **many recipient callsigns** (e.g. AWACS "Wizard" *and* "Magic" *and* "Overlord"), with state partitioned per-recipient. The set of recipients is seeded by static defaults and then unioned with whatever DCS reports at runtime.

---

## What's wired up

- **Multi-recipient routing.** Each agent recognizes a *set* of callsigns. Per-recipient state for AWACS check-ins, JTAC brief flow, and carrier records.
- **DCS-gRPC integration.** Subscribes to airplane / ship / ground streams; tracks player positions, headings, altitudes, types. Pulls bullseye per coalition. Works offline (`--offline`) — generators degrade to generic responses.
- **Dynamic role discovery.** [`RoleManager`](Dcs/RoleManager.cs) reconciles every 10 s, regex-matches DCS units to roles ([`UnitRoleMatcher`](Dcs/UnitRoleMatcher.cs)), and updates each parser's recipient list. Carrier records refresh with live position + BRC.
- **Proactive call scheduler.** Watch-based system ([`WatchScheduler`](Conditions/WatchScheduler.cs)) — supervisor + per-pair watches with hysteresis so we don't poll 200-nm-distant pairs at 5 s intervals.
- **First proactive condition: merge.** AWACS calls *"merged"* when a friendly closes inside 3 nm of a hostile — but only for pilots who have checked in with that AWACS.
- **AWACS check-in / check-out.** Roster is per-AWACS; proactive calls only fire for checked-in pilots.
- **AWACS picture / bogey dope / declare** with real geometry: greedy clustering for groups, nearest-aircraft-on-bearing for declare, bullseye-relative bearings.
- **9-line decision tree** with breaks, readback verification, abort-abort on calling-in without clearance.
- **Loose readback matching.** Pilot's readback verifies if the data is *somewhere* in the transcript, in any order — exact numeric match required (no fuzzy 328↔327).
- **Whisper-aware aliasing.** Mistranscriptions like "Wiper"→Viper, "Wavya"→Warrior route correctly via [`Callsigns.Aliases`](Parsing/Callsigns.cs).
- **Random per-agent voice per session.** A pool of male English Kokoro voices is shuffled at startup so the controllers sound distinct from each other and from last session.
- **Per-agent audio lock.** Different radios = independent transmission. AWACS and Airboss can talk simultaneously.
- **Dynamic console panel.** Shows the natural next things to say after every turn.

---

## Quick start

### Prerequisites

- Windows 10 (build 19041+) or Windows 11
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (`winget install Microsoft.DotNet.SDK.8`)
- A working microphone and speakers
- ~500 MB free disk on first run for the Whisper + Kokoro models
- *Optional*: [DCS-gRPC](https://github.com/DCS-gRPC/rust-server) running on the DCS host (default endpoint `http://localhost:50051`) for live game data

### Run

```powershell
dotnet run --project RadioMan.csproj
```

First launch downloads `ggml-base.en.bin` (~140 MB) and the Kokoro 82M ONNX model (~320 MB) into the project folder. Subsequent launches start in seconds.

### CLI flags

| Flag | What it does |
|---|---|
| `--offline` | Skip DCS-gRPC. Useful for developing the voice pipeline without DCS open. |
| `--list-kokoro-voices` | Dump every Kokoro voice with gender + language and exit. |

---

## How it works

```
                                          ┌─────────────┐
                                          │ AWACS Agent │  recipients: { Wizard, Magic, … }
                                          │  ├ Parser   │  per-recipient roster + state
                                          │  ├ Responder│
                                          │  └ TTS voice│
                                          └──────┬──────┘
 ┌─────┐   ┌─────────┐   ┌────────┐              │
 │ Mic │ → │ Whisper │ → │ Router │ ─────────────┤
 └─────┘   └─────────┘   └────────┘              │
                                          ┌──────┴──────┐
                                          │  JTAC Agent │  recipients: { Hammer, … }
                                          └──────┬──────┘
                                                 │
                                          ┌──────┴──────┐
                                          │Airboss Agent│  recipients: { Boss, Mother, … }
                                          └──────┬──────┘
                                                 │
            ┌───────────┐                ┌───────▼─────┐
            │  Speaker  │  ◀────────────│ Kokoro TTS  │
            └───────────┘                └─────────────┘

                  ▲                              ▲
                  │   proactive calls            │
                  │  (merge, splash, …)          │
                                                 │
                          ┌──────────────────┐   │
                          │ Watch Scheduler  │───┘
                          └────────▲─────────┘
                                   │ ticks
                                   │
                       ┌───────────┴───────────┐
                       │      DCS-gRPC client  │  (airplane + ship + ground streams)
                       └───────────────────────┘
                                   ▲
                                   │ feeds
                                   │
                       ┌───────────┴───────────┐
                       │     RoleManager       │  reconciles DCS units → recipient lists
                       └───────────────────────┘
```

### The pipeline (per PTT cycle)

1. **Capture** — `MicAudioInput` records 16 kHz mono PCM16 into a buffer while SPACE is held.
2. **Transcribe** — `WhisperTranscriber` runs Whisper.net's `base.en` with a vocabulary-biasing prompt.
3. **Route** — `AgentRouter` walks each agent's parser. The first one that says *"addressed to one of my recipients"* wins.
4. **Parse** — `RegexIntentParser` normalizes the transcript and tests each intent rule. Returns `RadioCall { Caller, Recipient, Intent, NormalizedText }`. `Recipient` is the specific callsign matched, not the role.
5. **Respond** — the matched agent's `IResponseGenerator` builds the reply, keyed by `call.Recipient` for any per-unit state, and consults the `IDcsClient` for live data if needed.
6. **Speak** — `KokoroTts` synthesizes streaming sentence-by-sentence; `SpeakerAudioOutput` plays through NAudio. The agent's `AudioLock` serializes its own transmissions.

### Proactive calls

Independent of PTT, the `WatchScheduler` ticks registered `Watch`es. Each watch is a `(Id, Interval, OnTick → ScheduledCall?, ShouldExit, ExpiresAt)`. The first watch that ships is `MergeDetector`: a supervisor iterates `roster.Recipients × players × hostiles`, registers a per-pair watch with hysteresis (50 nm → activate, 60 nm → deactivate), and emits *"merged"* when separation drops below 3 nm.

---

## Project layout

```
radio-man/
├── Program.cs                       ← entry point, PTT loop, agent + scheduler wiring
├── Providers.cs                     ← interfaces (IAudioInput, ITranscriber, IIntentParser, …)
├── RadioCall.cs                     ← Intent enum, RadioCall record, NextStep record
├── RadioPipeline.cs                 ← orchestrator: capture → transcribe → route → respond → speak
├── RadioMan.csproj
│
├── Agents/
│   ├── RadioAgent.cs                ← (Role, Callsign, Parser, Responder, Tts) + per-agent AudioLock
│   └── AgentRouter.cs               ← first-parser-that-recognizes-recipient wins
│
├── Audio/
│   ├── MicAudioInput.cs             ← NAudio WaveInEvent → PCM16 buffer
│   └── SpeakerAudioOutput.cs        ← NAudio WaveOutEvent + WaveFileReader
│
├── Conditions/
│   ├── Watch.cs                     ← reactive watch over DCS state; emits ScheduledCall
│   ├── WatchScheduler.cs            ← ticks registered watches, delivers calls via pipeline
│   ├── AwacsRoster.cs               ← per-recipient check-in roster (recipient → pilot set)
│   └── MergeDetector.cs             ← supervisor + per-pair watch w/ 50/60 nm hysteresis
│
├── Dcs/
│   ├── IDcsClient.cs                ← provider interface
│   ├── DcsGrpcClient.cs             ← gRPC streams (Airplane/Ship/Ground) + bullseye fetch
│   ├── OfflineDcsClient.cs          ← always-empty implementation for --offline mode
│   ├── AircraftSnapshot.cs          ← lat/lon/alt/heading/speed/type per unit
│   ├── Carrier.cs                   ← carrier record (position, BRC, angled-deck offset)
│   ├── Awacs.cs                     ← AWACS-relevant helpers
│   ├── Geo.cs                       ← distance/bearing math, compass octant
│   ├── UnitRoleMatcher.cs           ← regex unit-type → role (AWACS / Carrier / JTAC)
│   └── RoleManager.cs               ← reconcile loop: DCS units → parser recipient lists
│
├── Parsing/
│   ├── Callsigns.cs                 ← canonical callsigns + alias map (Whisper miss-fixes)
│   ├── IntentRulesets.cs            ← AWACS / JTAC / Airboss rule lists
│   └── RegexIntentParser.cs         ← normalize → match intent; SetRecipients() at runtime
│
├── Responses/
│   ├── BrevityFormat.cs             ← digit-by-digit, padding, phonetic helpers
│   ├── AwacsResponseGenerator.cs    ← per-recipient: check-in, picture, declare, bogey dope
│   ├── JtacResponseGenerator.cs     ← per-recipient state machine: 9-line flow + readback
│   └── AirbossResponseGenerator.cs  ← per-carrier: Case I recovery + launches
│
├── Stt/
│   └── WhisperTranscriber.cs        ← Whisper.net base.en + biasing prompt
│
└── Tts/
    ├── KokoroTts.cs                 ← KokoroSharp, shared model, streaming synthesis
    └── WindowsTts.cs                ← WinRT OneCore SAPI fallback (legacy)
```

---

## Conversation examples

### AWACS — check-in then picture

```
You    : "Magic, Viper 2-1, checking in."
Magic  : "Viper 2-1, Magic, copy your check-in. Sweet and sour."

You    : "Magic, Viper 2-1, request picture."
Magic  : "Viper 2-1, Magic, picture: two groups, bullseye two, seven, zero
          for three, five, two, five thousand, hot, hostile."
```

(With another AWACS active, the same pilot could say "Wizard, Viper 2-1, checking in" and Wizard would maintain its own roster — they do not share state.)

### Carrier Boss — Case I recovery

```
You    : "Boss, Hornet 1-1, ten miles inbound."
Boss   : "Hornet 1-1, Boss, signal Charlie, hold overhead, angels three.
          BRC zero, eight, five. Stand by for commence."

You    : "Boss, Hornet 1-1, ready to push."
Boss   : "Hornet 1-1, Boss, cleared to push. Descend pattern altitude,
          eight, zero, zero feet. Number two for the break, three, five, zero
          knots. BRC zero, eight, five. Report in the break."

You    : "Boss, Hornet 1-1, in the break."
Boss   : "Hornet 1-1, Boss, roger your break. Downwind 600 feet, dirty up,
          gear and flaps."

You    : "Boss, Hornet 1-1, abeam."
Boss   : "Roger, Hornet, continue, see you at the ball."

You    : "Boss, Hornet 1-1, see you at the ball."
Boss   : "Roger ball, Hornet. Deck is clear."

You    : "Boss, Hornet 1-1, off the wire."
Boss   : "Hornet 1-1, Boss, good trap, three wire. Taxi to spot four."
```

LSO talkdown is intentionally out of scope.

### JTAC — full 9-line cycle

```
You    : "Hammer, Viper 2-1, ready for tasking."
Hammer : "Viper 2-1, Hammer, copy your check-in. Tasking to follow.
          Advise ready for 9-line."

You    : "Hammer, Viper 2-1, ready for 9-line."
Hammer : "Viper 2-1, Hammer, type 2 in effect.
          Line 1, IP DELTA. Break.
          Line 2, heading two, seven, zero, offset right. Break.
          Line 3, distance four point two miles. Advise ready for the rest."

You    : "Hammer, Viper 2-1, go ahead."     ← any of: ready / send it / continue / proceed / copy
Hammer : "Line 4, target elevation one, two, zero, zero feet. Break.
          Line 5, target description, four BTRs in column. Break.
          Line 6, target location grid papa, alpha, four, five, six, seven,
          eight, nine, zero, one. Advise ready for the rest."

You    : "Hammer, Viper 2-1, send it."
Hammer : "Line 7, mark with laser, code one, six, eight, eight. Break.
          Line 8, friendlies six, zero, zero meters west. Break.
          Line 9, egress north, climb to one, five thousand.
          Read back lines 4, 6, and 8."

You    : "Hammer, Viper 2-1, elevation 1200 feet, grid PA 4567 8901,
          friendlies 600 meters west."
Hammer : "Viper 2-1, Hammer, good readback. Cleared to engage when ready."
```

### Proactive merge call

```
[sched] AWACS: Viper 2-1, Magic, merged.
```

Fires automatically when Viper 2-1 (checked in with Magic) closes inside 3 nm of any hostile in the stream.

---

## Architecture notes

### Provider abstractions

Every external dependency is behind an interface in [Providers.cs](Providers.cs) and [Dcs/IDcsClient.cs](Dcs/IDcsClient.cs):

| Interface | Default implementation | Swap cost |
|---|---|---|
| `IAudioInput` | `MicAudioInput` (NAudio) | new file in `Audio/` |
| `IAudioOutput` | `SpeakerAudioOutput` (NAudio) | new file or radio-FX decorator |
| `ITranscriber` | `WhisperTranscriber` (Whisper.net, base.en) | new file in `Stt/` |
| `ITextToSpeech` | `KokoroTts` (KokoroSharp 82M) | new file in `Tts/` |
| `IIntentParser` | `RegexIntentParser` | new class (LLM-driven parser would slot in here) |
| `IResponseGenerator` | per-role (Awacs / Jtac / Airboss) | new class in `Responses/` |
| `IDcsClient` | `DcsGrpcClient` / `OfflineDcsClient` | new file in `Dcs/` |

`Program.cs` is the only place that knows which concrete implementation goes where.

### Multi-recipient routing

A `RadioAgent` is `(Role, Callsign, Parser, Responder, Tts) + AudioLock`. `Callsign` is a *role label* ("AWACS") shown in the active-agents list — actual recipient callsigns live in the parser and are dynamic. The router runs each agent's parser, and the first that returns `AddressedToRecipient = true` wins. The matched parser fills in `RadioCall.Recipient` with the specific callsign the pilot used, and every per-unit state lookup in the responder keys off that.

### Dynamic discovery

[`RoleManager`](Dcs/RoleManager.cs) ticks every 10 s, walks `dcs.AllAircraft`, and asks [`UnitRoleMatcher`](Dcs/UnitRoleMatcher.cs) what roles each unit fills:

```
AWACS  ← unit type matches  E-[23] | A-50 | KJ-2000 | RC-135 | MQ-9 | EC-130
Carrier ← unit type matches CVN | CV$ | Stennis | Roosevelt | Eisenhower | …
JTAC   ← unit type matches  Humvee | M1043 | Stryker
         OR unit name/callsign contains JTAC | FAC | spotter
```

Discovered callsigns are unioned with static defaults from `Program.cs` and pushed into each parser via `SetRecipients()`. Carrier records get a live position + BRC update each pass. Changes log to console: `[roles] +AWACS: Magic`.

### Proactive scheduler

Built around three ideas:

1. **A `Watch` self-manages its lifecycle** — it knows its own tick `Interval`, what to emit on each tick, and when it should exit (`ShouldExit` / `ExpiresAt`).
2. **A supervisor watch** can register child watches dynamically. `MergeDetector` is the model: one slow supervisor scans the world for pairs that *might* merge soon, then spawns a fast per-pair watch only for those pairs. Hysteresis keeps watches from thrashing on the edge.
3. **Delivery shares the audio path with PTT replies.** Calls go through `pipeline.SpeakAsync` → per-agent `AudioLock` → TTS → speaker, so a scheduled merge call queues cleanly behind whatever the agent is already saying.

### State machines

`AwacsResponseGenerator` is mostly stateless apart from the shared roster. `JtacResponseGenerator` walks an explicit `FlowState` enum per recipient:

```
Idle / Complete
   ↓ CheckIn
CheckedIn
   ↓ Request9Line
BriefInProgress1   ─ ReadyForRest ─▶ BriefInProgress2
                                          │
                                     ReadyForRest
                                          ▼
                                   AwaitingReadback ─◀ wrong readback (stays)
                                          │
                                   correct readback
                                          ▼
                                     BriefDone
                                          ↓ CallingIn
                                     ClearedHot
                                          ↓ Off
                                     Complete  (loops to Idle)
```

`AirbossResponseGenerator` is event-driven rather than state-machine — each intent is a discrete step in the Case I cycle, and the carrier record provides BRC for every response.

### `IResponseGenerator.AvailableNext()`

Each generator exposes the natural next moves for its current state as a list of `NextStep { Hint, Example }`. `Program.cs` prints these after every successful turn so the panel always reflects what's possible *now*.

---

## Extension points

### Adding a new intent to an existing agent

1. Add the value to the `Intent` enum in [RadioCall.cs](RadioCall.cs).
2. Add a regex rule to the appropriate list in [IntentRulesets.cs](Parsing/IntentRulesets.cs). Order matters when patterns can overlap.
3. Add a switch arm in the responder. Optionally advance a state.

### Adding a whole new controller (e.g. ATC)

1. Add intents to the `Intent` enum.
2. Add a rule list `IntentRulesets.Atc` in [IntentRulesets.cs](Parsing/IntentRulesets.cs).
3. Write `Responses/AtcResponseGenerator.cs : IResponseGenerator` keyed by recipient (the tower callsign).
4. Add a role mapping in [UnitRoleMatcher.cs](Dcs/UnitRoleMatcher.cs) if you want auto-discovery (e.g. ATC keyed by airbase).
5. Wire one more `RadioAgent` in [Program.cs](Program.cs) and pass its parser to `RoleManager`.

### Adding a proactive condition

1. Decide what state triggers a call and what makes it stop being relevant.
2. Write a supervisor watch that scans for candidate pairs/units.
3. From the supervisor, register child watches with a fast `Interval`, an `OnTick` that returns a `ScheduledCall` when the condition fires, and a `ShouldExit` for the cleanup edge.

`MergeDetector` is the model — copy-and-mutate is fine.

### Adding a callsign or alias

Open [Parsing/Callsigns.cs](Parsing/Callsigns.cs):

- New canonical → add to `All`.
- Whisper mistranscribes one → add to `Aliases`:
  ```csharp
  ["whatever-whisper-said"] = "ProperCallsign",
  ```

Alias matches get canonicalized — `RadioCall.Caller` ends up as the proper callsign even when the raw transcript was wrong.

---

## Trade-offs and known issues

- **Whisper accuracy is the weak link.** `base.en` is fast but mishears callsigns and aviation jargon. The biasing prompt and alias map help; switching to `small.en` (470 MB) measurably improves recognition.
- **Loose readback matching can miss swaps.** If a pilot reads back the right numbers but assigns them to the wrong lines and both numbers happen to be present, verification passes. Acceptable for natural phrasing; tighten by anchoring extractions to keywords.
- **Word-form numbers** like "twelve hundred" aren't converted (only "zero"–"nine"). Digit-by-digit readback works.
- **Push-to-talk is keyboard-only.** Real DCS PTT is a HOTAS button. Swap the SPACE check for a DirectInput button read in `Program.cs`.
- **No audio FX yet.** Voices are dry. A band-pass + light noise decorator on `IAudioOutput` would belong in a `RadioFxOutput` wrapper.
- **One audio device.** All agents share the speaker — they're on different *radio* frequencies in the model, not different output devices.

---

## Tech stack

| Layer | Library | Why |
|---|---|---|
| Audio I/O | [NAudio](https://github.com/naudio/NAudio) | Mature Windows audio. |
| STT | [Whisper.net](https://github.com/sandrohanea/whisper.net) | whisper.cpp wrapper, local, free, GGML models. |
| TTS | [KokoroSharp.CPU](https://github.com/Lyrcaxis/KokoroSharp) | Kokoro 82M neural TTS via ONNX. Vastly better than SAPI. |
| DCS link | [RurouniJones.Dcs.Grpc](https://www.nuget.org/packages/RurouniJones.Dcs.Grpc) | Generated bindings for [DCS-gRPC](https://github.com/DCS-gRPC/rust-server). |
| Runtime | .NET 8 (`net8.0-windows10.0.19041.0`) | WinRT-capable TFM. |

---

## Roadmap

In rough priority order:

1. **ATC controller** — departure / pattern / approach / taxi for land bases. Recipients keyed by airbase callsign; same multi-recipient pattern as carriers. Auto-discover airbases from DCS-gRPC.
2. **More proactive conditions** — splash, bingo fuel, threat warning (SAM-up), bandit-on-your-six, RTB-approved nudge.
3. **Joystick PTT** — DirectInput button read instead of SPACE.
4. **Radio FX decorator** — band-pass + noise so each agent sounds like it's on a real radio.
5. **Loose readback hardening** — anchor extracted numbers to keywords so out-of-order line assignments fail correctly.
6. **Better STT** — `small.en`, or a Whisper variant fine-tuned on aviation vocabulary.
7. **Per-coalition awareness** — red-side controllers with their own callsign pools.
8. **Decision-tree class** — once ATC and tanker land, the per-role state-machine pattern is worth lifting into a shared graph type. `AvailableNext()` is already shaped for this.

---

## Licenses

- This project: MIT-style — do whatever, just don't sue.
- [DCS-gRPC](https://github.com/DCS-gRPC/rust-server) (Rust server) — AGPL-3.0. The C# bindings are AGPL-3.0 too. Running them as a process you talk to over the wire keeps the boundary clean; embedding them statically would propagate AGPL. radio-man talks to a separately-installed server, so AGPL stays on that side.
- Whisper, Kokoro, NAudio, and ONNX Runtime carry their own licenses; review before shipping binaries.
