using System.Text;
using Whisper.net;
using Whisper.net.Ggml;

namespace RadioMan.Stt;

public sealed class WhisperTranscriber : ITranscriber
{
    private readonly WhisperFactory _factory;
    private readonly WhisperProcessor _processor;

    private const string VocabularyPrompt =
        "Aviation radio brevity calls. Flight callsigns: Wizard 1-1, Viper 2-1, " +
        "Eagle 1-1, Hornet 3-1, Falcon, Magic, Overlord, Darkstar, Warrior 1-1. " +
        "AWACS phrases: request picture, bogey dope, declare, vector, posit, " +
        "contact bullseye. " +
        "JTAC / CAS phrases: checking in, ready for tasking, ready for 9-line, " +
        "ready for the rest, say again, in hot from south, off west, cleared hot, " +
        "abort, BDA, IP alpha, type 2 in effect. " +
        "Readback: elevation feet, grid papa alpha, friendlies meters west. " +
        "Other: RTB, bingo, joker, tally, no joy, visual, blind, spike, splash, " +
        "fox two, fox three.";

    public WhisperTranscriber(string modelPath)
    {
        _factory = WhisperFactory.FromPath(modelPath);
        _processor = _factory.CreateBuilder()
            .WithLanguage("en")
            .WithPrompt(VocabularyPrompt)
            .Build();
    }

    public static async Task<string> EnsureModelAsync(
        GgmlType type = GgmlType.BaseEn,
        string fileName = "ggml-base.en.bin")
    {
        if (File.Exists(fileName)) return fileName;

        Console.WriteLine($"Downloading {type} model (~140MB for base.en)...");
        await using var src = await WhisperGgmlDownloader.GetGgmlModelAsync(type);
        await using var dst = File.Create(fileName);
        await src.CopyToAsync(dst);
        Console.WriteLine("Model ready.");
        return fileName;
    }

    public async Task<string> TranscribeAsync(byte[] pcm16Mono, int sampleRate)
    {
        if (pcm16Mono.Length == 0) return string.Empty;

        using var wav = WrapAsWav(pcm16Mono, sampleRate);
        var sb = new StringBuilder();
        await foreach (var seg in _processor.ProcessAsync(wav))
            sb.Append(seg.Text);
        return sb.ToString().Trim();
    }

    private static MemoryStream WrapAsWav(byte[] pcm, int sampleRate)
    {
        var ms = new MemoryStream(pcm.Length + 44);
        var w = new BinaryWriter(ms, Encoding.ASCII, leaveOpen: true);
        const short channels = 1;
        const short bitsPerSample = 16;
        int byteRate = sampleRate * channels * bitsPerSample / 8;
        short blockAlign = (short)(channels * bitsPerSample / 8);

        w.Write("RIFF"u8.ToArray());
        w.Write(36 + pcm.Length);
        w.Write("WAVE"u8.ToArray());
        w.Write("fmt "u8.ToArray());
        w.Write(16);
        w.Write((short)1);
        w.Write(channels);
        w.Write(sampleRate);
        w.Write(byteRate);
        w.Write(blockAlign);
        w.Write(bitsPerSample);
        w.Write("data"u8.ToArray());
        w.Write(pcm.Length);
        w.Write(pcm);
        w.Flush();
        ms.Position = 0;
        return ms;
    }

    public void Dispose()
    {
        _processor.Dispose();
        _factory.Dispose();
    }
}
