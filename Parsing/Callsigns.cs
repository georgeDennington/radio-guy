namespace RadioMan.Parsing;

public static class Callsigns
{
    public static readonly IReadOnlyList<string> All = new[]
    {
        "Achilles", "Ace", "Adder", "Albatross", "Amber", "Anaconda", "Anvil",
        "Apollo", "Arco", "Asp", "Athena", "Avalanche",
        "Bandit", "Bayonet", "Bear", "Bentley", "Bishop", "Bison", "Blade",
        "Blizzard", "Bloodhound", "Boar", "Bobcat", "Bourbon", "Brandy",
        "Bronco", "Buick", "Bulldog", "Bullet", "Bullseye", "Buzzard",
        "Cadillac", "Camaro", "Centurion", "Charger", "Cheetah", "Chevy",
        "Citgo", "Cleaver", "Cobalt", "Cobra", "Colt", "Condor", "Corvette",
        "Cougar", "Cowboy", "Crimson", "Crow", "Crusader", "Cyclone", "Cypher",
        "Dagger", "Darkstar", "Diamond", "Diamondback", "Dodge",
        "Eagle", "Echo", "Enfield", "Esso",
        "Falcon", "Ferrari", "Ford", "Frost",
        "Galaxy", "Gator", "Glitch", "Goose", "Goshawk", "Greyhound", "Gunslinger",
        "Hades", "Halberd", "Hammer", "Harpy", "Hauler", "Hawg", "Hawk", "Helix",
        "Hercules", "Hermes", "Hollywood", "Hornet", "Hound", "Hurricane", "Husky",
        "Iceman", "Inferno",
        "Jade", "Jaguar", "Jester", "Joker",
        "Kestrel", "King", "Knight",
        "Lance", "Leopard", "Lightning", "Lincoln", "Lion", "Lynx",
        "Mace", "Magic", "Magnum", "Mamba", "Marshal", "Mars", "Martini",
        "Maserati", "Mastiff", "Matrix", "Maverick", "Merlin", "Mobil",
        "Mojito", "Mustang",
        "Neon", "Ninja", "Nova",
        "Olympus", "Onyx", "Osprey", "Outlaw", "Overlord", "Owl",
        "Panther", "Pegasus", "Pelican", "Pharaoh", "Photon", "Phoenix", "Pig",
        "Pistol", "Pixel", "Pontiac", "Pony", "Porsche", "Pulse", "Puma", "Python",
        "Quantum",
        "Ranger", "Rattler", "Raven", "Razor", "Reaper", "Renegade", "Rhino",
        "Rifle", "Rogue", "Ruby",
        "Saber", "Samurai", "Sapphire", "Sentry", "Shark", "Sheriff", "Shell",
        "Sidewinder", "Slider", "Smith", "Spade", "Sparrow", "Spartan", "Spear",
        "Springfield", "Stallion", "Stinger", "Storm", "Sundown",
        "Talon", "Templar", "Tempest", "Tequila", "Texaco", "Thunder", "Tiger",
        "Titan", "Topaz", "Tornado", "Trident", "Trigger", "Trojan", "Tusk",
        "Uzi",
        "Vector", "Vigilante", "Viper", "Vodka", "Volcano", "Vulture",
        "Warrior", "Wasp", "Whiskey", "Wildcard", "Wizard", "Wolf", "Wolfman",
        "Wolverine", "Worker",
    };

    // Whisper acoustic mistranscriptions of canonical callsigns.
    // Lowercase alias → canonical name (from `All`).
    // Add entries as you spot them in the "heard" line of the console.
    public static readonly IReadOnlyDictionary<string, string> Aliases =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // Most common: plurals/possessives Whisper invents
            ["vipers"] = "Viper",   ["vipa"] = "Viper",    ["wiper"] = "Viper",
            ["eagles"] = "Eagle",   ["egal"] = "Eagle",
            ["hornets"] = "Hornet", ["hornit"] = "Hornet", ["hornette"] = "Hornet",
            ["falcons"] = "Falcon", ["falken"] = "Falcon",
            ["wizards"] = "Wizard", ["wisard"] = "Wizard", ["wizurd"] = "Wizard",
            ["magics"] = "Magic",   ["magick"] = "Magic",
            ["overlords"] = "Overlord",
            ["darkstars"] = "Darkstar", ["dark star"] = "Darkstar",
            ["sentries"] = "Sentry",

            // Pop-culture / Top Gun handles
            ["mavricks"] = "Maverick", ["mavric"] = "Maverick", ["maverik"] = "Maverick",
            ["geese"] = "Goose", ["goosh"] = "Goose",
            ["icemen"] = "Iceman",
            ["sliders"] = "Slider",
            ["merlins"] = "Merlin",
            ["wolfmen"] = "Wolfman",

            // Cars / DCS canonical
            ["chevys"] = "Chevy",  ["chevvy"] = "Chevy",
            ["fords"] = "Ford",
            ["dodges"] = "Dodge",
            ["pontiacs"] = "Pontiac",
            ["buicks"] = "Buick",
            ["enfields"] = "Enfield", ["en field"] = "Enfield",
            ["springfields"] = "Springfield", ["spring field"] = "Springfield",

            // Animals
            ["tigers"] = "Tiger",   ["tigre"] = "Tiger",
            ["wolves"] = "Wolf",    ["wolfe"] = "Wolf",
            ["cobras"] = "Cobra",   ["kobra"] = "Cobra",
            ["pythons"] = "Python",
            ["mambas"] = "Mamba",
            ["sharks"] = "Shark",
            ["panthers"] = "Panther",
            ["leopards"] = "Leopard",
            ["bears"] = "Bear",
            ["bobcats"] = "Bobcat",
            ["lions"] = "Lion",

            // Weapons / military
            ["bandits"] = "Bandit", ["banditt"] = "Bandit",
            ["outlaws"] = "Outlaw",
            ["reapers"] = "Reaper", ["reepar"] = "Reaper",
            ["razors"] = "Razor",   ["razer"] = "Razor",
            ["sabers"] = "Saber",   ["sabre"] = "Saber",
            ["daggers"] = "Dagger",
            ["hammers"] = "Hammer",
            ["spears"] = "Spear",
            ["colts"] = "Colt",
            ["smiths"] = "Smith",

            // Mythology / Greek
            ["spartans"] = "Spartan",
            ["trojans"] = "Trojan",
            ["centurions"] = "Centurion",
            ["titans"] = "Titan",
            ["apollos"] = "Apollo",

            // Tankers
            ["shells"] = "Shell",
            ["essos"] = "Esso",
            ["citgos"] = "Citgo",

            // JTAC-canonical
            ["warriors"] = "Warrior", ["worrier"] = "Warrior", ["warrier"] = "Warrior",
            ["wavya"] = "Warrior", ["wahya"] = "Warrior", ["warya"] = "Warrior",
        };

    public static string? Canonical(string name)
    {
        var fromAll = All.FirstOrDefault(c =>
            string.Equals(c, name, StringComparison.OrdinalIgnoreCase));
        if (fromAll is not null) return fromAll;

        return Aliases.TryGetValue(name, out var c) ? c : null;
    }
}
