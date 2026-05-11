using System.Collections.Concurrent;
using RadioMan.Dcs;

namespace RadioMan.Responses;

/// Carrier airboss. Multi-carrier aware — one generator serves any number of
/// carriers in the mission. Per-recipient state (Carrier record, last response
/// for "say again") is keyed by recipient callsign (e.g. "Boss", "Mother", etc.).
///
/// Handles the Case I recovery / launch sequence. Does NOT do LSO talkdown —
/// that's a separate role and is explicitly out of scope.
public sealed class AirbossResponseGenerator : IResponseGenerator
{
    private static readonly Random Rng = new();
    private static readonly string[] CatNumbers = { "one", "two", "three", "four" };
    private static readonly string[] DeckSpots =
    {
        "two", "three", "four", "five", "six", "seven", "eight",
    };

    private readonly IDcsClient _dcs;
    private readonly ConcurrentDictionary<string, Carrier> _carriers;
    private readonly ConcurrentDictionary<string, string> _lastResponses =
        new(StringComparer.OrdinalIgnoreCase);

    /// Used only by AvailableNext for the example phrasings.
    private readonly string _exampleRecipient;

    /// `carriers` is mutable and shared with the RoleManager — entries are
    /// added/removed as DCS units appear and despawn. The generator just
    /// reads from it at call time.
    public AirbossResponseGenerator(
        IDcsClient dcs,
        ConcurrentDictionary<string, Carrier> carriers,
        string exampleRecipient = "Boss")
    {
        _dcs = dcs;
        _carriers = carriers;
        _exampleRecipient = exampleRecipient;
    }

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
            _ => $"{call.Caller}, {call.Recipient}, unable.",
        };

        if (call.Intent != Intent.AirbossSayAgain)
            _lastResponses[call.Recipient] = response;
        return response;
    }

    public IReadOnlyList<NextStep> AvailableNext()
    {
        var caller = "Hornet 1-1";
        return new[]
        {
            new NextStep("ready cats",     $"{_exampleRecipient}, {caller}, ready cats"),
            new NextStep("inbound",        $"{_exampleRecipient}, {caller}, ten miles inbound"),
            new NextStep("ready to push",  $"{_exampleRecipient}, {caller}, ready to push"),
            new NextStep("in the break",   $"{_exampleRecipient}, {caller}, in the break"),
            new NextStep("abeam",          $"{_exampleRecipient}, {caller}, abeam"),
            new NextStep("ball",           $"{_exampleRecipient}, {caller}, see you at the ball"),
            new NextStep("off the wire",   $"{_exampleRecipient}, {caller}, off the wire"),
            new NextStep("say again",      $"{_exampleRecipient}, {caller}, say again"),
        };
    }

    private Carrier? CarrierFor(string recipient)
        => _carriers.TryGetValue(recipient, out var c) ? c : null;

    private AircraftSnapshot? PlayerFor(RadioCall call)
        => call.Caller is null ? null : _dcs.PlayerByCallsign(call.Caller);

    // --- Handlers ---------------------------------------------------------

    private string ReadyForLaunch(RadioCall call)
    {
        if (Rng.NextDouble() < 0.20)
            return $"{call.Caller}, {call.Recipient}, deck fouled, hold position.";

        var cat = CatNumbers[Rng.Next(CatNumbers.Length)];
        return $"{call.Caller}, {call.Recipient}, taxi to cat {cat}, stand by for launch.";
    }

    private string Inbound(RadioCall call)
    {
        var carrier = CarrierFor(call.Recipient);
        var holdAngels = Rng.Next(2, 5);

        if (carrier is null || !_dcs.HasFreshData)
        {
            return $"{call.Caller}, {call.Recipient}, signal Charlie, " +
                   $"hold overhead, angels {BrevityFormat.Digits(holdAngels)}. " +
                   $"Stand by for commence.";
        }

        var player = PlayerFor(call);
        if (player is null)
        {
            return $"{call.Caller}, {call.Recipient}, signal Charlie, " +
                   $"hold overhead, angels {BrevityFormat.Digits(holdAngels)}. " +
                   $"BRC {BrevityFormat.Digits(carrier.BrcDeg, padding: 3)}. " +
                   $"Stand by for commence.";
        }

        var distNm = Geo.DistanceNm(player.Lat, player.Lon, carrier.Lat, carrier.Lon);
        if (distNm > 30)
        {
            var bearingFromShip = Geo.BearingDeg(carrier.Lat, carrier.Lon, player.Lat, player.Lon);
            var dir = Geo.CompassFromBearing(bearingFromShip);
            return $"{call.Caller}, {call.Recipient}, no contact. " +
                   $"Show you {BrevityFormat.Digits((int)Math.Round(distNm))} miles {dir}. " +
                   $"Steer for mom.";
        }

        return $"{call.Caller}, {call.Recipient}, signal Charlie, " +
               $"hold overhead, angels {BrevityFormat.Digits(holdAngels)}. " +
               $"BRC {BrevityFormat.Digits(carrier.BrcDeg, padding: 3)}. " +
               $"Stand by for commence.";
    }

    private string Commence(RadioCall call)
    {
        var nForBreak = Rng.Next(1, 4);
        var breakNumberCall = nForBreak == 1
            ? "number one for the break"
            : $"number {BrevityFormat.Digits(nForBreak)} for the break";

        var carrier = CarrierFor(call.Recipient);
        var brc = carrier?.BrcDeg ?? 180;

        return $"{call.Caller}, {call.Recipient}, cleared to push. " +
               $"Descend pattern altitude, eight, zero, zero feet. " +
               $"{breakNumberCall}, three, five, zero knots. " +
               $"BRC {BrevityFormat.Digits(brc, padding: 3)}. " +
               $"Report in the break.";
    }

    private string InTheBreak(RadioCall call)
    {
        var altCheck = "";
        var player = PlayerFor(call);
        if (player is not null && (player.AltFt < 400 || player.AltFt > 1500))
        {
            altCheck = $" Check altitude — show you " +
                       $"{BrevityFormat.Digits((int)Math.Round(player.AltFt))} feet.";
        }

        return $"{call.Caller}, {call.Recipient}, roger your break. " +
               $"Downwind 600 feet, dirty up, gear and flaps.{altCheck}";
    }

    private string Abeam(RadioCall call)
    {
        var player = PlayerFor(call);
        if (player is null)
            return $"Roger, {ShortCaller(call)}, continue.";

        if (player.AltFt < 400 || player.AltFt > 900)
        {
            return $"{call.Caller}, {call.Recipient}, you're " +
                   $"{BrevityFormat.Digits((int)Math.Round(player.AltFt))} feet. " +
                   $"Pattern altitude is 600. Correct your altitude.";
        }

        return $"Roger, {ShortCaller(call)}, continue, see you at the ball.";
    }

    private string Ball(RadioCall call)
    {
        var carrier = CarrierFor(call.Recipient);
        var player = PlayerFor(call);

        if (carrier is null || player is null)
            return $"Roger ball, {ShortCaller(call)}.";

        var distNm = Geo.DistanceNm(player.Lat, player.Lon, carrier.Lat, carrier.Lon);
        var altAgl = player.AltFt;

        if (distNm > 2 || altAgl > 1500)
        {
            return $"{call.Caller}, {call.Recipient}, negative ball. " +
                   $"Show you {BrevityFormat.Digits((int)Math.Round(distNm))} miles, " +
                   $"{BrevityFormat.Digits((int)Math.Round(altAgl))} feet. Continue approach.";
        }

        return $"Roger ball, {ShortCaller(call)}. Deck is clear.";
    }

    private string OffWire(RadioCall call)
    {
        var spot = DeckSpots[Rng.Next(DeckSpots.Length)];
        var wire = Rng.Next(1, 5);
        var wireWord = wire switch
        {
            1 => "one wire",
            2 => "two wire",
            3 => "three wire",
            _ => "four wire",
        };
        return $"{call.Caller}, {call.Recipient}, good trap, {wireWord}. Taxi to spot {spot}.";
    }

    private string SayAgain(RadioCall call)
    {
        if (_lastResponses.TryGetValue(call.Recipient, out var last) && !string.IsNullOrEmpty(last))
            return last;
        return $"{call.Caller}, {call.Recipient}, no prior transmission to repeat.";
    }

    private string Unrecognized(RadioCall call)
    {
        if (call.Caller is null)
            return $"Unknown station calling {call.Recipient}, say again your callsign.";

        var options = string.Join(", ", AvailableNext().Select(s => s.Hint));
        return $"{call.Caller}, {call.Recipient}, did not copy. Possible calls: {options}.";
    }

    private static string ShortCaller(RadioCall call)
    {
        if (call.Caller is null) return "flight";
        var firstWord = call.Caller.Split(' ', 2)[0];
        return firstWord.TrimEnd(',');
    }
}
