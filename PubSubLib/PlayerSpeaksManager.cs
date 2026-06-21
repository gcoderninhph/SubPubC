using System.Collections.Concurrent;
using System.Threading;
using Google.Protobuf;
using Natify;
using PubSubLib.Messages;

namespace PubSubLib;

internal sealed class PlayerSpeaksManager : IPlayerSpeaksManager
{
    private const string EvtTopic = "PlayerSpeaks.Evt";
    private const string StatusTopic = "PlayerSpeaks.Status";

    private readonly int _playerTimeoutSeconds;
    private readonly int _cleanupIntervalSeconds;
    private readonly INatifyAdapter _natify;
    private readonly ConcurrentDictionary<PlayerDataKey, IPlayerData> _data = new();
    private readonly ConcurrentDictionary<PlayerDataKey, long> _lastActiveTicks = new();
    private readonly object _lock = new();
    private CancellationTokenSource? _cts;
    private Task? _cleanupTask;
    private bool _disposed;

    private PlayerSpeaksManager(PlayerSpeakerConfig config, INatifyAdapter natify)
    {
        _playerTimeoutSeconds = config.PlayerTimeoutSeconds;
        _cleanupIntervalSeconds = config.PlayerCleanupIntervalSeconds;
        _natify = natify;

        _natify.Subscribe<PlayerOnlineStatusMsg>(StatusTopic, OnOnlineStatus);

        _cts = new CancellationTokenSource();
        _cleanupTask = Task.Run(() => CleanupLoop(_cts.Token));
    }

    internal static IPlayerSpeaksManager Create(PlayerSpeakerConfig config)
    {
        INatifyAdapter natify;
        if (config.ClientFast != null)
            natify = new NatifyAdapter(config.ClientFast);
        else if (config.Client != null)
            natify = new NatifyAdapter(config.Client);
        else
            throw new InvalidOperationException("PlayerSpeakerConfig requires ClientFast or Client");

        return new PlayerSpeaksManager(config, natify);
    }

    public T CreateData<T>(long playerId) where T : class, IPlayerData, new()
    {
        var data = new T();
        ((IPlayerDataInternal)data).SetPlayerId(playerId);
        var key = new PlayerDataKey(data.DataName, playerId);

        _data[key] = data;
        _lastActiveTicks[key] = DateTime.UtcNow.Ticks;

        data.OnChange((bytes, commit) =>
        {
            if (!data.IsOnLine)
                return;

            var evt = new PlayerSpeaksEvent
            {
                DataName = data.DataName,
                PlayerId = playerId,
                Data = ByteString.CopyFrom(bytes),
                Commit = commit
            };
            _natify.Publish(EvtTopic, evt);
        });

        return data;
    }

    private void OnOnlineStatus(Data<PlayerOnlineStatusMsg> args)
    {
        var msg = args.Value;
        foreach (var kv in _data)
        {
            if (kv.Key.PlayerId == msg.PlayerId)
            {
                if (kv.Value is IPlayerDataInternal di)
                    di.SetOnline(msg.IsOnline);
                if (msg.IsOnline)
                    _lastActiveTicks[kv.Key] = DateTime.UtcNow.Ticks;
            }
        }
    }

    private async Task CleanupLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_cleanupIntervalSeconds * 1000, ct);
                var now = DateTime.UtcNow.Ticks;
                var timeout = _playerTimeoutSeconds * TimeSpan.TicksPerSecond;

                foreach (var kv in _lastActiveTicks)
                {
                    if (now - kv.Value > timeout)
                    {
                        if (_data.TryRemove(kv.Key, out var stale))
                            if (stale is IPlayerDataInternal di)
                                di.SetOnline(false);
                        _lastActiveTicks.TryRemove(kv.Key, out _);
                    }
                }
            }
            catch (TaskCanceledException)
            {
                break;
            }
            catch
            {
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _cts?.Cancel();
        _cleanupTask?.Wait(TimeSpan.FromSeconds(5));
        _cts?.Dispose();

        _data.Clear();
        _lastActiveTicks.Clear();
        _natify.Dispose();
    }
}
