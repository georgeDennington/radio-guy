using RadioMan.Dcs;

namespace RadioMan.Responses;

/// Carrier airboss. Runs launches and recoveries.
///
/// Handles the part the airboss actually does — deck state, cat clearances,
/// signal Charlie, the "roger ball" handoff, post-trap taxi. It does NOT do
/// LSO talkdown (the rapid "power"/"right for lineup"/"easy with it" calls
/// during the last 15 seconds of an approach) — that's a separate role and
/// the user explicitly excluded it.
///
/// Pulls real player position from DcsExportClient so range/bearing-from-mom
/// in the responses match where the pilot actually is.
public sealed class AirbossResponseGenerator : IResponseGenerator
{
    private static readonly Random Rng = new();
    private static readonly string[] CatNumbers = { "one", "two", "three", "four" };
    private static readonly string[] DeckSpots =
    {
        "two", "three", "four", "five", "six", "seven", "eight",
    };

    private readonly string _shortName;
    private readonly IDcsClient _dcs;
    private readonly Carrier _carrier;
    private string _lastResponse = "";

    public AirbossResponseGenerator(IDcsClient dcs, Carrier carrier)
    {
        _shortName = carrier.AirbossCallsign;
        _dcs = dcs;
        _carrier = carrier;
    }

    /// Resolves the caller's live position via gRPC, or returns null when DCS
    /// isn't connected / the player isn't tracked. Generators use the null
    /// case to degrade to generic responses.
    private AircraftSnapshot? PlayerFor(RadioCall call)
        => call.Caller is null ? null : _dcs.PlayerByCallsign(call.Caller);

    public string Respond(RadioCall call)
    {
        var response = call.Intent switch
        {
            Intent.AirbossReadyForLaunch => ReadyForLaunch(call),
            Intent.AirbossInbound => Inbound(call),
            Intent.AirbossCommence => Commence(call),
            Intent.AirbossInTheBreak => InTheBreak(call),
            Intent.AirbossAbeam => Abeam(call),
            Intent.AirbossBall => Ball(call),
            Intent.AirbossOffWire => OffWire(call),
            Intent.AirbossSayAgain => SayAgain(call),
            Intent.Unrecognized => Unrecognized(call),
            _ => $"{call.Caller}, {_shortName}, unable.",
        };

        if (call.Intent != Intent.AirbossSayAgain)
            _lastResponse = response;
        return response;
    }

    public IReadOnlyList<NextStep> AvailableNext()
    {
        var caller = "Hornet 1-1";
        return new[]
        {
            new NextStep("ready cats",     $"{_shortName}, {caller}, ready cats"),
            new NextStep("inbound",        $"{_shortName}, {caller}, ten miles inbound"),
            new NextStep("ready to push",  $"{_shortName}, {caller}, ready to push"),
            new NextStep("in the break",   $"{_shortName}, {caller}, in the break"),
            new NextStep("abeam",          $"{_shortName}, {caller}, abeam"),
            new NextStep("ball",           $"{_shortName}, {caller}, see you at the ball"),
            new NextStep("off the wire",   $"{_shortName}, {caller}, off the wire"),
            new NextStep("say again",      $"{_shortName}, {caller}, say again"),
        };
    }

    private string ReadyForLaunch(RadioCall call)
    {
        // ~20% chance the deck is fouled — adds variability to the flow.
        if (Rng.NextDouble() < 0.20)
            return $"{call.Caller}, {_shortName}, deck fouled, hold position.";

        var cat = CatNumbers[Rng.Next(CatNumbers.Length)];
        return $"{call.Caller}, {_shortName}, taxi to cat {cat}, stand by for launch.";
    }

    /// Case I 10-mile inbound. Boss assigns a holding altitude in the overhead
    /// stack (angels 2 is the bottom of the stack; higher arrivals stack above
    /// in 1000ft increments). Pilot orbits there until Boss clears them to
    /// commence the descent for the break.
    private string Inbound(RadioCall call)
    {
        var holdAngels = Rng.Next(2, 5); // 2, 3, or 4 thousand feet

        var player = PlayerFor(call);
        if (player is null)
        {
            return $"{call.Caller}, {_shortName}, signal Charlie, " +
                   $"hold overhead, angels {BrevityFormat.Digits(holdAngels)}. " +
                   $"BRC {BrevityFormat.Digits(_carrier.BrcDeg, padding: 3)}. " +
                   $"Stand by for commence.";
        }

        var distNm = Geo.DistanceNm(player.Lat, player.Lon, _carrier.Lat, _carrier.Lon);

        // Sanity check: if pilot says "inbound" but they're way out, push back.
        if (distNm > 30)
        {
            var bearingFromShip = Geo.BearingDeg(_carrier.Lat, _carrier.Lon, player.Lat, player.Lon);
            var dir = Geo.CompassFromBearing(bearingFromShip);
            return $"{call.Caller}, {_shortName}, no contact. " +
                   $"Show you {BrevityFormat.Digits((int)Math.Round(distNm))} miles {dir}. " +
                   $"Steer for mom.";
        }

        return $"{call.Caller}, {_shortName}, signal Charlie, " +
               $"hold overhead, angels {BrevityFormat.Digits(holdAngels)}. " +
               $"BRC {BrevityFormat.Digits(_carrier.BrcDeg, padding: 3)}. " +
               $"Stand by for commence.";
    }

    /// Pilot's "ready to push" / "ready to commence" — clears them down from
    /// the overhead stack to break altitude (800ft) and assigns the break number.
    private string Commence(RadioCall call)
    {
        var nForBreak = Rng.Next(1, 4); // "number 1/2/3 for the break"
        var breakNumberCall = nForBreak == 1
            ? "number one for the break"
            : $"number {BrevityFormat.Digits(nForBreak)} for the break";

        return $"{call.Caller}, {_shortName}, cleared to push. " +
               $"Descend pattern altitude, eight, zero, zero feet. " +
               $"{breakNumberCall}, three, five, zero knots. " +
               $"Report in the break.";
    }

    /// Pilot has rolled in over the bow into downwind. Boss confirms entry and
    /// reads the downwind pattern altitude + gear/flap reminder.
    private string InTheBreak(RadioCall call)
    {
        var altCheck = "";
        var player = PlayerFor(call);
        if (player is not null)
        {
            // Initial break should be at ~800ft. Anything wildly off, flag it.
            if (player.AltFt < 400 || player.AltFt > 1500)
            {
                altCheck = $" Check altitude — show you " +
                           $"{BrevityFormat.Digits((int)Math.Round(player.AltFt))} feet.";
            }
        }

        return $"{call.Caller}, {_shortName}, roger your break. " +
               $"Downwind 600 feet, dirty up, gear and flaps.{altCheck}";
    }

    /// Abeam the LSO platform on downwind. Pattern altitude should be ~600ft,
    /// configured for landing. Boss acknowledges; turn to base comes next.
    private string Abeam(RadioCall call)
    {
        var player = PlayerFor(call);
        if (player is null)
            return $"Roger, {ShortCaller(call)}, continue.";

        // Pattern altitude is ~600 ft AGL. Anything outside 400-900 is wrong.
        if (player.AltFt < 400 || player.AltFt > 900)
        {
            return $"{call.Caller}, {_shortName}, you're " +
                   $"{BrevityFormat.Digits((int)Math.Round(player.AltFt))} feet. " +
                   $"Pattern altitude is 600. Correct your altitude.";
        }

        return $"Roger, {ShortCaller(call)}, continue, see you at the ball.";
    }

    private string Ball(RadioCall call)
    {
        var player = PlayerFor(call);
        if (player is null)
            return $"Roger ball, {ShortCaller(call)}.";

        var distNm = Geo.DistanceNm(player.Lat, player.Lon, _carrier.Lat, _carrier.Lon);
        var altAgl = player.AltFt;  // ship is ~60ft above water, close enough to MSL

        // "Ball" call is at ~3/4 mile, ~370 ft. Anything wildly off, push back.
        if (distNm > 2 || altAgl > 1500)
        {
            return $"{call.Caller}, {_shortName}, negative ball. " +
                   $"Show you {BrevityFormat.Digits((int)Math.Round(distNm))} miles, " +
                   $"{BrevityFormat.Digits((int)Math.Round(altAgl))} feet. Continue approach.";
        }

        // Real flow would hand off to LSO here ("paddles contact"). We don't have
        // an LSO, so the airboss just clears the trap and stays out of the way.
        return $"Roger ball, {ShortCaller(call)}. Deck is clear.";
    }

    private string OffWire(RadioCall call)
    {
        var spot = DeckSpots[Rng.Next(DeckSpots.Length)];
        // Real carriers have 4 arresting wires. 3-wire is target; 1 and 4 are
        // off-target. Random for variety.
        var wire = Rng.Next(1, 5);
        var wireWord = wire switch
        {
            1 => "one wire",
            2 => "two wire",
            3 => "three wire",
            _ => "four wire",
        };
        return $"{call.Caller}, {_shortName}, good trap, {wireWord}. Taxi to spot {spot}.";
    }

    private string SayAgain(RadioCall call)
        => string.IsNullOrEmpty(_lastResponse)
           ? $"{call.Caller}, {_shortName}, no prior transmission to repeat."
           : _lastResponse;

    private string Unrecognized(RadioCall call)
    {
        if (call.Caller is null)
            return $"Unknown station calling {_shortName}, say again your callsign.";

        var options = string.Join(", ", AvailableNext().Select(s => s.Hint));
        return $"{call.Caller}, {_shortName}, did not copy. Possible calls: {options}.";
    }

    /// Just the squadron name ("Hornet") without flight number — the airboss
    /// uses the short form once contact is established, e.g. "Roger ball, Hornet".
    private static string ShortCaller(RadioCall call)
    {
        if (call.Caller is null) return "flight";
        var firstWord = call.Caller.Split(' ', 2)[0];
        return firstWord.TrimEnd(',');
    }
}
