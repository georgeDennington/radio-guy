using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace RadioMan.Dcs;

/// Listens on a UDP port for snapshots pushed from DCS's Export.lua script.
/// Keeps the latest snapshot in memory; older packets are discarded.
///
/// Companion script: dcs-export/Export.lua — symlink or copy that into
/// %USERPROFILE%\Saved Games\DCS\Scripts\ (or DCS.openbeta, etc.).
public sealed class DcsExportClient : IDisposable
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private readonly UdpClient _udp;
    private readonly CancellationTokenSource _cts = new();
    private DcsSnapshot? _latest;

    public int Port { get; }
    public DcsSnapshot? Latest => _latest;
    public bool HasFreshData => _latest is { } s && s.Age < TimeSpan.FromSeconds(5);

    public DcsExportClient(int port = 49152)
    {
        Port = port;
        _udp = new UdpClient(new IPEndPoint(IPAddress.Loopback, port));
        _ = Task.Run(ReceiveLoopAsync);
    }

    private async Task ReceiveLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                var result = await _udp.ReceiveAsync(_cts.Token);
                var json = Encoding.UTF8.GetString(result.Buffer);
                var snap = JsonSerializer.Deserialize<DcsSnapshot>(json, JsonOpts);
                if (snap is not null) _latest = snap;
            }
            catch (OperationCanceledException) { return; }
            catch
            {
                // Skip bad packets — don't kill the listen loop.
            }
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        try { _udp.Close(); } catch { }
        _udp.Dispose();
        _cts.Dispose();
    }
}
