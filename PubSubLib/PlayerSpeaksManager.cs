using System.Collections.Concurrent;
using System.Threading;
using Google.Protobuf;
using Natify;
using PubSubLib.Contracts;
using PubSubLib.Messages;

namespace PubSubLib;

internal sealed class PlayerSpeaksManager : IPlayerSpeaksManager
{
    private const string EvtTopic = "PlayerSpeaks.Evt";
    private const string MsgTopic = "PlayerSpeaks.Msg";
    private const string ClientMsgTopic = "PlayerSpeaks.ClientMsg";
    private const string StatusTopic = "PlayerSpeaks.Status";

    private readonly int _playerTimeoutSeconds;
    private readonly int _cleanupIntervalSeconds;
    private readonly INatifyAdapter _natify;
    private readonly ConcurrentDictionary<PlayerDataKey, IPlayerData> _data = new();
    private readonly ConcurrentDictionary<PlayerDataKey, long> _lastActiveTicks = new();
    private readonly ConcurrentQueue<System.Action> _mainThreadActions = new();
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
        _natify.Subscribe<ClientMirrorMessage>(ClientMsgTopic, OnClientMsg);

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
        T data;
        PlayerDataKey key;
        try
        {
            data = new T();
            ((IPlayerDataInternal)data).SetPlayerId(playerId);
            key = new PlayerDataKey(data.DataName, playerId);
        }
        catch (Exception ex)
        {
            PubSubLog.Error(ex, "CreateData init failed");
            throw;
        }

        _data[key] = data;
        _lastActiveTicks[key] = DateTime.UtcNow.Ticks;

        try
        {
            data.OnChange((bytes, commit) =>
            {
                try
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
                }
                catch (Exception ex) { PubSubLog.Error(ex, "OnChange publish failed"); }
            });
        }
        catch (Exception ex) { PubSubLog.Error(ex, "CreateData OnChange registration failed"); }

        try
        {
            data.OnMessage((subject, bytes) =>
            {
                try
                {
                    if (!data.IsOnLine) return;
                    var evt = new MirrorMessageEvent
                    {
                        DataName = data.DataName,
                        PlayerId = playerId,
                        Subject = subject,
                        Data = ByteString.CopyFrom(bytes)
                    };
                    _natify.Publish(MsgTopic, evt);
                }
                catch (Exception ex) { PubSubLog.Error(ex, "OnMessage publish failed"); }
            });
        }
        catch (Exception ex) { PubSubLog.Error(ex, "CreateData OnMessage registration failed"); }

        return data;
    }

    private void OnOnlineStatus(Data<PlayerOnlineStatusMsg> args)
    {
        try
        {
            var msg = args.Value;
            foreach (var kv in _data)
            {
                if (kv.Key.PlayerId == msg.PlayerId)
                {
                    try
                    {
                        if (kv.Value is IPlayerDataInternal di)
                            di.SetOnline(msg.IsOnline);
                        if (msg.IsOnline)
                            _lastActiveTicks[kv.Key] = DateTime.UtcNow.Ticks;
                    }
                    catch (Exception ex) { PubSubLog.Error(ex, "OnOnlineStatus SetOnline failed"); }
                }
            }
        }
        catch (Exception ex) { PubSubLog.Error(ex, "OnOnlineStatus failed"); }
    }

    private void OnClientMsg(Data<ClientMirrorMessage> args)
    {
        try
        {
            var msg = args.Value;
            var key = new PlayerDataKey(msg.DataName, msg.PlayerId);
            if (_data.TryGetValue(key, out var data) && data is IPlayerDataInternal di)
            {
                var actions = di.PrepareMessageDispatch(msg.Subject, msg.Data.ToByteArray());
                foreach (var a in actions)
                    _mainThreadActions.Enqueue(a);
            }
        }
        catch (Exception ex) { PubSubLog.Error(ex, "OnClientMsg failed"); }
    }

    public void Tick()
    {
        while (_mainThreadActions.TryDequeue(out var action))
        {
            try { action(); }
            catch (Exception ex) { PubSubLog.Error(ex, "Tick action failed"); }
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
            catch (Exception ex)
            {
                PubSubLog.Error(ex, "CleanupLoop failed");
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
