using System.Collections.Concurrent;
using Grpc.Net.Client;
using RurouniJones.Dcs.Grpc.V0.Coalition;
using RurouniJones.Dcs.Grpc.V0.Common;
using RurouniJones.Dcs.Grpc.V0.Mission;

namespace RadioMan.Dcs;

/// Connects to a DCS-gRPC server (https://github.com/DCS-gRPC/rust-server)
/// running on the DCS game host and subscribes to StreamUnits for all
/// airplane category units. Maintains an in-memory map keyed on unit id.
///
/// Resilient to the server being down: the streaming loop catches RPC failures,
/// flips IsConnected to false, waits, and retries. radio-man stays usable in
/// "no DCS" mode — generators see HasFreshData=false and degrade.
public sealed class DcsGrpcClient : IDcsClient
{
    private readonly GrpcChannel _channel;
    private readonly MissionService.MissionServiceClient _mission;
    private readonly CoalitionService.CoalitionServiceClient _coalition;
    private readonly ConcurrentDictionary<uint, AircraftSnapshot> _aircraft = new();
    private readonly ConcurrentDictionary<string, BullseyePoint> _bullseyes = new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource _cts = new();

    public string Endpoint { get; }
    public bool IsConnected { get; private set; }

    public bool HasFreshData =>
        _aircraft.Values.Any(p => p.Age < TimeSpan.FromSeconds(5));

    public IReadOnlyCollection<AircraftSnapshot> AllAircraft => _aircraft.Values.ToArray();

    public IReadOnlyCollection<AircraftSnapshot> AllPlayers =>
        _aircraft.Values.Where(a => a.IsPlayer).ToArray();

    public DcsGrpcClient(string endpoint = "http://localhost:50051")
    {
        Endpoint = endpoint;
        _channel = GrpcChannel.ForAddress(endpoint);
        _mission = new MissionService.MissionServiceClient(_channel);
        _coalition = new CoalitionService.CoalitionServiceClient(_channel);
        // Three category streams — airplanes for AWACS/players, ships for
        // carriers, ground for JTAC-capable vehicles. Each has its own
        // independent retry loop and shares the _aircraft dictionary.
        _ = Task.Run(() => StreamCategoryLoopAsync(GroupCategory.Airplane));
        _ = Task.Run(() => StreamCategoryLoopAsync(GroupCategory.Ship));
        _ = Task.Run(() => StreamCategoryLoopAsync(GroupCategory.Ground));
    }

    public AircraftSnapshot? PlayerByCallsign(string callsign)
    {
        var key = Normalize(callsign);
        foreach (var p in _aircraft.Values)
        {
            if (!p.IsPlayer) continue;
            if (Normalize(p.Callsign) == key) return p;
            if (Normalize(p.PlayerName ?? "") == key) return p;
            if (Normalize(p.Name) == key) return p;
        }
        return null;
    }

    public BullseyePoint? Bullseye(string coalition)
    {
        if (_bullseyes.TryGetValue(coalition, out var cached)) return cached;
        if (!IsConnected) return null;

        // Fetch and cache. Mission-static, so once we have it we never re-fetch.
        try
        {
            var coalEnum = ToCoalition(coalition);
            if (coalEnum is null) return null;

            var resp = _coalition.GetBullseye(new GetBullseyeRequest { Coalition = coalEnum.Value });
            var point = new BullseyePoint(resp.Position.Lat, resp.Position.Lon);
            _bullseyes[coalition] = point;
            return point;
        }
        catch
        {
            // Fetch failed — return null so the caller falls back to generic responses.
            // Will retry on next call if IsConnected stays true.
            return null;
        }
    }

    private static Coalition? ToCoalition(string s) => s.ToLowerInvariant() switch
    {
        "red" => RurouniJones.Dcs.Grpc.V0.Common.Coalition.Red,
        "blue" => RurouniJones.Dcs.Grpc.V0.Common.Coalition.Blue,
        "neutral" => RurouniJones.Dcs.Grpc.V0.Common.Coalition.Neutral,
        _ => null,
    };

    /// "Viper 2-1" / "viper21" / "Viper-2-1" all collapse to "viper21".
    private static string Normalize(string s)
        => new string((s ?? "").Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();

    private async Task StreamCategoryLoopAsync(GroupCategory category)
    {
        var consecutiveFailures = 0;
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                using var call = _mission.StreamUnits(
                    new StreamUnitsRequest
                    {
                        PollRate = 1,
                        MaxBackoff = 30,
                        Category = category,
                    },
                    cancellationToken: _cts.Token);

                if (!IsConnected)
                {
                    IsConnected = true;
                    Console.WriteLine($"[dcs] connected to gRPC server at {Endpoint}");
                }
                consecutiveFailures = 0;

                while (await call.ResponseStream.MoveNext(_cts.Token))
                {
                    HandleUpdate(call.ResponseStream.Current);
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                // Only log first failure per category to avoid spam — three
                // concurrent loops all logging "unavailable" every 5s is noise.
                if (consecutiveFailures == 0)
                {
                    Console.WriteLine($"[dcs:{category}] gRPC unavailable ({ex.GetType().Name}: {ShortMsg(ex)}). " +
                                      $"Will retry every 5s.");
                }
                IsConnected = false;
                consecutiveFailures++;

                try { await Task.Delay(TimeSpan.FromSeconds(5), _cts.Token); }
                catch (OperationCanceledException) { return; }
            }
        }
    }

    private static string ShortMsg(Exception ex)
    {
        var msg = ex.Message.Split('\n')[0];
        return msg.Length > 100 ? msg[..100] + "..." : msg;
    }

    private void HandleUpdate(StreamUnitsResponse update)
    {
        switch (update.UpdateCase)
        {
            case StreamUnitsResponse.UpdateOneofCase.Unit:
                _aircraft[update.Unit.Id] = ToSnapshot(update.Unit);
                break;

            case StreamUnitsResponse.UpdateOneofCase.Gone:
                _aircraft.TryRemove(update.Gone.Id, out _);
                break;
        }
    }

    private static AircraftSnapshot ToSnapshot(Unit u)
    {
        // DCS-gRPC proto semantics:
        //   Position.Alt   — metres above sea level
        //   Velocity.Speed — metres per second
        //   Velocity.Heading — radians, 0 = north, positive clockwise
        const double MetresToFeet = 3.28084;
        const double MpsToKts = 1.94384;

        var hdg = u.Velocity.Heading * 180.0 / Math.PI;
        if (hdg < 0) hdg += 360.0;

        var playerName = u.HasPlayerName ? u.PlayerName : null;

        return new AircraftSnapshot(
            Id: u.Id,
            Name: u.Name ?? "",
            Callsign: u.Callsign ?? "",
            PlayerName: playerName,
            AircraftType: u.Type ?? "",
            Coalition: u.Coalition.ToString(),
            Lat: u.Position.Lat,
            Lon: u.Position.Lon,
            AltFt: u.Position.Alt * MetresToFeet,
            HeadingDeg: hdg,
            SpeedKts: u.Velocity.Speed * MpsToKts,
            UpdatedAt: DateTime.UtcNow);
    }

    public void Dispose()
    {
        _cts.Cancel();
        try { _channel.Dispose(); } catch { }
        _cts.Dispose();
    }
}
