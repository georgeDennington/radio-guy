using NAudio.Wave;

namespace RadioMan.Audio;

public sealed class MicAudioInput : IAudioInput
{
    public int SampleRate { get; } = 16000;

    private readonly WaveInEvent _waveIn;
    private readonly List<byte> _buffer = new();
    private readonly object _lock = new();
    private TaskCompletionSource? _stopTcs;

    public MicAudioInput()
    {
        _waveIn = new WaveInEvent
        {
            WaveFormat = new WaveFormat(SampleRate, 16, 1),
            BufferMilliseconds = 50,
        };
        _waveIn.DataAvailable += OnDataAvailable;
        _waveIn.RecordingStopped += OnRecordingStopped;
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        lock (_lock)
        {
            for (int i = 0; i < e.BytesRecorded; i++)
                _buffer.Add(e.Buffer[i]);
        }
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
        => _stopTcs?.TrySetResult();

    public void Start()
    {
        lock (_lock) _buffer.Clear();
        _waveIn.StartRecording();
    }

    public async Task<byte[]> StopAsync()
    {
        _stopTcs = new TaskCompletionSource();
        _waveIn.StopRecording();
        await _stopTcs.Task;
        lock (_lock) return _buffer.ToArray();
    }

    public void Dispose()
    {
        _waveIn.DataAvailable -= OnDataAvailable;
        _waveIn.RecordingStopped -= OnRecordingStopped;
        _waveIn.Dispose();
    }
}
