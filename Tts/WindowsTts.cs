using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Media.SpeechSynthesis;

namespace RadioMan.Tts;

public sealed class WindowsTts : ITextToSpeech
{
    private readonly SpeechSynthesizer _synth;

    public WindowsTts(string? voiceHint = null)
    {
        _synth = new SpeechSynthesizer();

        var enVoices = SpeechSynthesizer.AllVoices
            .Where(v => v.Language.StartsWith("en", StringComparison.OrdinalIgnoreCase))
            .ToList();

        VoiceInformation? chosen = null;

        if (voiceHint is not null)
        {
            chosen = enVoices.FirstOrDefault(v =>
                v.DisplayName.Contains(voiceHint, StringComparison.OrdinalIgnoreCase) ||
                v.Id.Contains(voiceHint, StringComparison.OrdinalIgnoreCase));
        }

        // Prefer modern OneCore male voices over legacy Desktop ones — better quality.
        chosen ??= enVoices.FirstOrDefault(v =>
                       v.Gender == VoiceGender.Male && !v.DisplayName.Contains("Desktop"))
                ?? enVoices.FirstOrDefault(v => v.Gender == VoiceGender.Male)
                ?? enVoices.FirstOrDefault();

        if (chosen is not null)
        {
            _synth.Voice = chosen;
            Console.WriteLine($"TTS voice : {chosen.DisplayName}  ({chosen.Gender}, {chosen.Language})");
        }

        _synth.Options.SpeakingRate = 1.1;
    }

    public static void ListVoices()
    {
        Console.WriteLine("Available TTS voices:");
        foreach (var v in SpeechSynthesizer.AllVoices)
            Console.WriteLine($"  {v.Gender,-6} {v.Language,-8} {v.DisplayName}");
    }

    public static async Task GeneratePreviewsAsync(string outDir = "voice-previews")
    {
        Directory.CreateDirectory(outDir);

        const string sampleCall =
            "Viper 2-1, Wizard, picture: single group, bullseye 270 for 35, " +
            "25 thousand, hot, hostile.";

        var voices = SpeechSynthesizer.AllVoices
            .Where(v => v.Language.StartsWith("en", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Console.WriteLine($"Generating {voices.Count} preview(s) to {Path.GetFullPath(outDir)}\n");

        foreach (var voice in voices)
        {
            var shortName = voice.DisplayName.Split(" - ")[0];
            var spoken = $"This is {shortName}. {sampleCall}";

            try
            {
                using var synth = new SpeechSynthesizer { Voice = voice };
                synth.Options.SpeakingRate = 1.1;
                using var stream = await synth.SynthesizeTextToStreamAsync(spoken);
                await using var dotnet = stream.AsStreamForRead();
                using var ms = new MemoryStream();
                await dotnet.CopyToAsync(ms);

                var fileName = SanitizeFileName($"{voice.Gender}_{voice.DisplayName}") + ".wav";
                var path = Path.Combine(outDir, fileName);
                await File.WriteAllBytesAsync(path, ms.ToArray());
                Console.WriteLine($"  ✓ {fileName}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  ✗ {voice.DisplayName}: {ex.Message}");
            }
        }
    }

    private static string SanitizeFileName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name.Replace(' ', '_');
    }

    public async Task<byte[]> SynthesizeAsync(string text)
    {
        using var stream = await _synth.SynthesizeTextToStreamAsync(text);
        await using var dotnet = stream.AsStreamForRead();
        using var ms = new MemoryStream();
        await dotnet.CopyToAsync(ms);
        return ms.ToArray();
    }

    public void Dispose() => _synth.Dispose();
}
