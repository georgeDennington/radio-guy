namespace RadioMan.Responses;

public sealed class AwacsResponseGenerator : IResponseGenerator
{
    private static readonly Random Rng = new();
    private readonly string _shortName;

    public AwacsResponseGenerator(string callsign = "Wizard 1-1")
    {
        _shortName = callsign.Split(' ', 2)[0];
    }

    public string Respond(RadioCall call) => call.Intent switch
    {
        Intent.AwacsPicture => Picture(call),
        Intent.AwacsBogeyDope => BogeyDope(call),
        Intent.AwacsDeclare => Declare(call),
        Intent.Unrecognized => Unrecognized(call),
        _ => $"{call.Caller}, {_shortName}, unable.",
    };

    private string Unrecognized(RadioCall call)
    {
        if (call.Caller is null)
            return $"Unknown station calling {_shortName}, say again your callsign.";

        var options = string.Join(", ", AvailableNext().Select(s => s.Hint));
        return $"{call.Caller}, {_shortName}, did not copy. " +
               $"Possible calls: {options}.";
    }

    public IReadOnlyList<NextStep> AvailableNext() => new[]
    {
        new NextStep("picture",    $"{_shortName}, Viper 2-1, request picture"),
        new NextStep("bogey dope", $"{_shortName}, Viper 2-1, bogey dope"),
        new NextStep("declare",    $"{_shortName}, Viper 2-1, declare bullseye 270 35"),
    };

    private string Picture(RadioCall call)
    {
        if (Rng.NextDouble() < 0.15)
            return $"{call.Caller}, {_shortName}, picture clean.";

        int groups = Rng.Next(1, 4);
        var groupWord = groups == 1 ? "single group" : $"{Spell(groups)} groups";
        int bra = Rng.Next(0, 36) * 10;
        int range = Rng.Next(15, 80);
        int alt = Rng.Next(10, 40);
        var aspect = Rng.NextDouble() < 0.5 ? "hot" : "cold";
        return $"{call.Caller}, {_shortName}, picture: {groupWord}, " +
               $"bullseye {bra:D3} for {range}, {alt} thousand, {aspect}, hostile.";
    }

    private string BogeyDope(RadioCall call)
    {
        int bra = Rng.Next(0, 36) * 10;
        int range = Rng.Next(10, 60);
        int alt = Rng.Next(8, 40);
        var aspect = Rng.NextDouble() < 0.5 ? "hot" : "flank";
        return $"{call.Caller}, {_shortName}, bogey dope: " +
               $"bullseye {bra:D3} for {range}, {alt} thousand, {aspect}, hostile.";
    }

    private string Declare(RadioCall call)
    {
        var roll = Rng.NextDouble();
        if (roll < 0.10) return $"{call.Caller}, {_shortName}, declare: clean.";
        if (roll < 0.25) return $"{call.Caller}, {_shortName}, declare: friendly.";
        if (roll < 0.35) return $"{call.Caller}, {_shortName}, declare: unknown, no IFF.";

        var types = new[] { "Su-27", "Su-30", "Su-35", "MiG-29", "MiG-31", "Su-24", "Su-25" };
        var type = types[Rng.Next(types.Length)];
        return $"{call.Caller}, {_shortName}, declare: hostile, type {type}.";
    }

    private static string Spell(int n) => n switch
    {
        1 => "one", 2 => "two", 3 => "three", 4 => "four",
        5 => "five", 6 => "six", 7 => "seven", 8 => "eight", 9 => "nine",
        _ => n.ToString()
    };
}
