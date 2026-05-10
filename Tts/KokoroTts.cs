using System.Text;
using KokoroSharp;
using KokoroSharp.Core;
using KokoroSharp.Processing;

namespace RadioMan.Tts;

public sealed class KokoroTts : ITextToSpeech
{
    // Kokoro 82M v1.0 outputs at 24 kHz mono float32.
    private const int SampleRate = 24_000;

    private readonly KokoroTTS _tts;
    private readonly KokoroVoice _voice;
    private readonly float _speed;
    private readonly bool _ownsTts;

    public KokoroTts(string voiceName, float speed = 1f)
        : this(KokoroTTS.LoadModel(), voiceName, speed, ownsTts: true) { }

    public KokoroTts(KokoroTTS sharedModel, string voiceName, float speed = 1f)
        : this(sharedModel, voiceName, speed, ownsTts: false) { }

    private KokoroTts(KokoroTTS tts, string voiceName, float speed, bool ownsTts)
    {
        _tts = tts;
        _voice = KokoroVoiceManager.GetVoice(voiceName);
        _speed = speed;
        _ownsTts = ownsTts;
        Console.WriteLine($"  voice : Kokoro/{voiceName}  ({_voice.Gender}, {_voice.Language}, speed {speed:0.00})");
    }

    public static KokoroTTS LoadSharedModel() => KokoroTTS.LoadModel();

    public static void ListVoices()
    {
        Console.WriteLine("Available Kokoro voices:");
        foreach (var v in KokoroVoiceManager.Voices.OrderBy(v => v.Name))
            Console.WriteLine($"  {v.Gender,-6} {v.Language,-25} {v.Name}");
    }

    public Task<byte[]> SynthesizeAsync(string text)
    {
        var tokens = Tokenizer.Tokenize(text.Trim(), _voice.GetLangCode(), preprocess: true);
        var tcs = new TaskCompletionSource<float[]>(TaskCreationOptions.RunContinuationsAsynchronously);

        var job = KokoroJob.Create(tokens, _voice, speed: _speed, samples =>
        {
            tcs.TrySetResult(samples);
        });

        _tts.EnqueueJob(job);
        return tcs.Task.ContinueWith(t => SamplesToWav(t.Result, SampleRate));
    }

    private static byte[] SamplesToWav(float[] samples, int sampleRate)
    {
        const short channels = 1;
        const short bitsPerSample = 16;
        int byteRate = sampleRate * channels * bitsPerSample / 8;
        short blockAlign = (short)(channels * bitsPerSample / 8);
        int dataSize = samples.Length * 2;

        using var ms = new MemoryStream(dataSize + 44);
        var w = new BinaryWriter(ms, Encoding.ASCII, leaveOpen: true);

        w.Write("RIFF"u8.ToArray());
        w.Write(36 + dataSize);
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
        w.Write(dataSize);

        foreach (var f in samples)
        {
            int v = (int)Math.Round(Math.Clamp(f, -1f, 1f) * 32767f);
            w.Write((short)v);
        }

        w.Flush();
        return ms.ToArray();
    }

    public void Dispose()
    {
        if (_ownsTts) _tts.Dispose();
    }
}
