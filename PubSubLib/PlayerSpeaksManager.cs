using System.Collections.Concurrent;
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
    private readonly INatifyClient _natify;

    private readonly int _playerTimeoutSeconds;

    private readonly int _cleanupIntervalSeconds;

    // private readonly INatifyAdapter _natify;
    private readonly ConcurrentDictionary<PlayerDataKey, IPlayerData> _data = new();
    private readonly ConcurrentDictionary<PlayerDataKey, long> _lastActiveTicks = new();
    private readonly ConcurrentQueue<Action> _mainThreadActions = new();
    private readonly ConcurrentDictionary<string, IDefaultFactory> _defaults = new();
    private readonly ConcurrentDictionary<string, IRemovalHandler> _removeHandlers = new();
    private CancellationTokenSource? _cts;
    private Task? _cleanupTask;
    private bool _disposed;

    private PlayerSpeaksManager(PlayerSpeakerConfig config)
    {
        _playerTimeoutSeconds = config.PlayerTimeoutSeconds;
        _cleanupIntervalSeconds = config.PlayerCleanupIntervalSeconds;
        _natify = config.ClientFast ?? throw new InvalidOperationException("ClientFast is null");

        _natify.OnMessage<PlayerOnlineStatusMsg>(StatusTopic, OnOnlineStatus);
        _natify.OnMessage<ClientMirrorMessage>(ClientMsgTopic, OnClientMsg);

        _cts = new CancellationTokenSource();
        _cleanupTask = Task.Run(() => CleanupLoop(_cts.Token));
    }

    internal static IPlayerSpeaksManager Create(PlayerSpeakerConfig config)
    {
        return new PlayerSpeaksManager(config);
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
                catch (Exception ex)
                {
                    PubSubLog.Error(ex, "OnChange publish failed");
                }
            });
        }
        catch (Exception ex)
        {
            PubSubLog.Error(ex, "CreateData OnChange registration failed");
        }

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
                catch (Exception ex)
                {
                    PubSubLog.Error(ex, "OnMessage publish failed");
                }
            });
        }
        catch (Exception ex)
        {
            PubSubLog.Error(ex, "CreateData OnMessage registration failed");
        }

        if (data is IOnCreate onCreate)
        {
            try
            {
                onCreate.OnCreate();
            }
            catch (Exception ex)
            {
                PubSubLog.Error(ex, "IOnCreate.OnCreate failed");
            }
        }

        return data;
    }

    public T? GetData<T>(long playerId) where T : class, IPlayerData, new()
    {
        try
        {
            var t = new T();
            var key = new PlayerDataKey(t.DataName, playerId);

            if (_data.TryGetValue(key, out var existing) && existing is T typed)
                return typed;

            return null;
        }
        catch (Exception ex)
        {
            PubSubLog.Error(ex, "GetData<T> failed");
            return null;
        }
    }

    public void OnDefault<T>(Func<T, Task>? callback) where T : class, IPlayerData, new()
    {
        try
        {
            var t = new T();
            _defaults[t.DataName] = new DefaultFactory<T>(t.DataName, callback);
        }
        catch (Exception ex)
        {
            PubSubLog.Error(ex, "OnDefault<T> failed");
        }
    }

    public void OnRemove<T>(Func<T, Task>? callback) where T : class, IPlayerData, new()
    {
        try
        {
            var t = new T();
            _removeHandlers[t.DataName] = new RemovalHandler<T>(t.DataName, callback);
        }
        catch (Exception ex)
        {
            PubSubLog.Error(ex, "OnRemove<T> failed");
        }
    }

    public async Task<bool> RemoveAsync(long playerId)
    {
        try
        {
            var keys = new List<PlayerDataKey>();
            foreach (var kv in _data)
            {
                if (kv.Key.PlayerId == playerId)
                    keys.Add(kv.Key);
            }

            if (keys.Count == 0)
                return false;

            foreach (var key in keys)
            {
                if (_data.TryGetValue(key, out var existing) && existing.IsOnLine)
                    return false;
            }

            foreach (var key in keys)
            {
                if (_data.TryGetValue(key, out var data) && data is IOnRemove onRemove)
                {
                    try
                    {
                        onRemove.OnRemove();
                    }
                    catch (Exception ex)
                    {
                        PubSubLog.Error(ex, "IOnRemove.OnRemove failed");
                    }
                }

                if (_data.TryRemove(key, out data))
                {
                    if (_removeHandlers.TryGetValue(key.DataName, out var handler))
                    {
                        try
                        {
                            await handler.HandleAsync(data);
                        }
                        catch (Exception ex)
                        {
                            PubSubLog.Error(ex, "RemoveAsync OnRemove failed");
                        }
                    }

                    if (data is IPlayerDataInternal di)
                        di.SetOnline(false);

                    _lastActiveTicks.TryRemove(key, out _);
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            PubSubLog.Error(ex, "RemoveAsync failed");
            return false;
        }
    }

    private async Task OnOnlineStatus(Data<PlayerOnlineStatusMsg> args)
    {
        try
        {
            var msg = args.Value;

            if (msg.IsOnline)
            {
                foreach (var kv in _defaults)
                {
                    var key = new PlayerDataKey(kv.Key, msg.PlayerId);
                    if (!_data.ContainsKey(key))
                    {
                        try
                        {
                            await kv.Value.CreateAndInitAsync(msg.PlayerId, this);
                        }
                        catch (Exception ex)
                        {
                            PubSubLog.Error(ex, $"Auto-create {kv.Key} failed");
                        }
                    }
                }
            }

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
                    catch (Exception ex)
                    {
                        PubSubLog.Error(ex, "OnOnlineStatus SetOnline failed");
                    }
                }
            }

            foreach (var kv in _data)
            {
                if (kv.Key.PlayerId == msg.PlayerId)
                {
                    if (msg.IsOnline && kv.Value is IOnClientConnect onConnect)
                    {
                        try
                        {
                            onConnect.OnClientConnect();
                        }
                        catch (Exception ex)
                        {
                            PubSubLog.Error(ex, "IOnClientConnect failed");
                        }
                    }
                    else if (!msg.IsOnline && kv.Value is IOnClientDisconnect onDisconnect)
                    {
                        try
                        {
                            onDisconnect.OnClientDisconnect();
                        }
                        catch (Exception ex)
                        {
                            PubSubLog.Error(ex, "IOnClientDisconnect failed");
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            PubSubLog.Error(ex, "OnOnlineStatus failed");
        }
    }

    private void OnClientMsg(Data<ClientMirrorMessage> args)
    {
        try
        {
            var msg = args.Value;
            var key = new PlayerDataKey(msg.DataName, msg.PlayerId);
            if (_data.TryGetValue(key, out var data) && data is IPlayerDataInternal di)
            {
                if (msg.Subject == "__player_speaks_init__")
                {
                    data.Commit("init");
                }
                else
                {
                    var actions = di.PrepareMessageDispatch(msg.Subject, msg.Data.ToByteArray());
                    foreach (var a in actions)
                        _mainThreadActions.Enqueue(a);
                }
            }
        }
        catch (Exception ex)
        {
            PubSubLog.Error(ex, "OnClientMsg failed");
        }
    }

    public void Tick()
    {
        while (_mainThreadActions.TryDequeue(out var action))
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                PubSubLog.Error(ex, "Tick action failed");
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
                        if (_data.TryGetValue(kv.Key, out var stale) && stale is IOnRemove onRemove)
                        {
                            try
                            {
                                onRemove.OnRemove();
                            }
                            catch (Exception ex)
                            {
                                PubSubLog.Error(ex, "IOnRemove.OnRemove failed");
                            }
                        }

                        if (_data.TryRemove(kv.Key, out stale))
                        {
                            if (_removeHandlers.TryGetValue(kv.Key.DataName, out var handler))
                            {
                                try
                                {
                                    await handler.HandleAsync(stale);
                                }
                                catch (Exception ex)
                                {
                                    PubSubLog.Error(ex, "CleanupLoop OnRemove failed");
                                }
                            }

                            if (stale is IPlayerDataInternal di)
                                di.SetOnline(false);
                        }

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

    private interface IDefaultFactory
    {
        string DataName { get; }
        Task<IPlayerData> CreateAndInitAsync(long playerId, PlayerSpeaksManager manager);
    }

    private sealed class DefaultFactory<T> : IDefaultFactory where T : class, IPlayerData, new()
    {
        private readonly Func<T, Task>? _callback;
        public string DataName { get; }

        public DefaultFactory(string dataName, Func<T, Task>? callback)
        {
            DataName = dataName;
            _callback = callback;
        }

        public async Task<IPlayerData> CreateAndInitAsync(long playerId, PlayerSpeaksManager manager)
        {
            var data = manager.CreateData<T>(playerId);
            if (_callback != null)
            {
                try
                {
                    await _callback(data);
                }
                catch (Exception ex)
                {
                    PubSubLog.Error(ex, "OnDefault callback failed");
                }
            }

            return data;
        }
    }

    private interface IRemovalHandler
    {
        string DataName { get; }
        Task HandleAsync(IPlayerData data);
    }

    private sealed class RemovalHandler<T> : IRemovalHandler where T : class, IPlayerData
    {
        private readonly Func<T, Task>? _callback;
        public string DataName { get; }

        public RemovalHandler(string dataName, Func<T, Task>? callback)
        {
            DataName = dataName;
            _callback = callback;
        }

        public async Task HandleAsync(IPlayerData data)
        {
            if (_callback != null && data is T typed)
            {
                try
                {
                    await _callback(typed);
                }
                catch (Exception ex)
                {
                    PubSubLog.Error(ex, "OnRemove callback failed");
                }
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        _cts?.Cancel();
        _cleanupTask?.Wait(TimeSpan.FromSeconds(5));
        _cts?.Dispose();

        _data.Clear();
        _lastActiveTicks.Clear();
        await _natify.DisposeAsync();
    }
}