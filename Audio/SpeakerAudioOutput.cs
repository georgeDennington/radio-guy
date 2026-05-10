using NAudio.Wave;

namespace RadioMan.Audio;

public sealed class SpeakerAudioOutput : IAudioOutput
{
    public Task PlayAsync(byte[] wavBytes)
    {
        var tcs = new TaskCompletionSource();
        var ms = new MemoryStream(wavBytes, writable: false);
        var reader = new WaveFileReader(ms);
        var output = new WaveOutEvent();

        output.PlaybackStopped += (_, _) =>
        {
            output.Dispose();
            reader.Dispose();
            ms.Dispose();
            tcs.TrySetResult();
        };

        output.Init(reader);
        output.Play();
        return tcs.Task;
    }

    public void Dispose() { }
}
