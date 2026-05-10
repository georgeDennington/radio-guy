using System.Text;

namespace RadioMan.Responses;

internal static class BrevityFormat
{
    private static readonly string[] DigitWords =
        { "zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine" };

    private static readonly Dictionary<char, string> PhoneticLetters = new()
    {
        ['A'] = "alpha",   ['B'] = "bravo",   ['C'] = "charlie",  ['D'] = "delta",
        ['E'] = "echo",    ['F'] = "foxtrot", ['G'] = "golf",     ['H'] = "hotel",
        ['I'] = "india",   ['J'] = "juliet",  ['K'] = "kilo",     ['L'] = "lima",
        ['M'] = "mike",    ['N'] = "november",['O'] = "oscar",    ['P'] = "papa",
        ['Q'] = "quebec",  ['R'] = "romeo",   ['S'] = "sierra",   ['T'] = "tango",
        ['U'] = "uniform", ['V'] = "victor",  ['W'] = "whiskey",  ['X'] = "x-ray",
        ['Y'] = "yankee",  ['Z'] = "zulu",
    };

    /// 270 -> "two seven zero". Use `padding` for fixed-width like headings (D3) or grid digits (D4).
    public static string Digits(int n, int padding = 0)
        => Digits(padding > 0 ? n.ToString($"D{padding}") : n.ToString());

    /// "4567" -> "four, five, six, seven". Commas slow the TTS so each digit lands clearly.
    /// Non-digit characters in the input are treated as separators and dropped.
    public static string Digits(string s)
    {
        var words = new List<string>();
        foreach (var c in s)
        {
            if (char.IsDigit(c))
                words.Add(DigitWords[c - '0']);
        }
        return string.Join(", ", words);
    }

    /// 4.2 -> "four point two".
    public static string Decimal(double d, int decimals = 1)
    {
        var s = d.ToString($"F{decimals}");
        var parts = s.Split('.');
        return parts.Length == 2
            ? $"{Digits(parts[0])} point {Digits(parts[1])}"
            : Digits(parts[0]);
    }

    /// "PA" -> "papa, alpha". Commas force a brief pause between letters.
    public static string Phonetic(string letters)
    {
        var words = new List<string>();
        foreach (var c in letters)
        {
            if (PhoneticLetters.TryGetValue(char.ToUpper(c), out var word))
                words.Add(word);
        }
        return string.Join(", ", words);
    }
}
