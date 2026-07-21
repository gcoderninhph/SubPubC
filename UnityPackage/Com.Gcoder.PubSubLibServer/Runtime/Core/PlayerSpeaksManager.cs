using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using Gcoder.Collections;
using Google.Protobuf;
using Natify;
using PubSubLib.Contracts;
using PubSubLib.Messages;

#nullable enable

namespace PubSubLib
{
    internal sealed class PlayerSpeaksManager : IPlayerSpeaksManager
    {
        private const string EvtTopic = "PlayerSpeaks.Evt";
        private const string MsgTopic = "PlayerSpeaks.Msg";
        private const string ClientMsgTopic = "PlayerSpeaks.ClientMsg";
        private const string StatusTopic = "PlayerSpeaks.Status";
        private const string PingTopic = "PlayerSpeaks.Ping";
        private readonly INatifyClient _natify;

        private readonly int _playerTimeoutSeconds;

        private readonly Dictionary<long, Dictionary<string, IPlayerData>> _data = new();
        private readonly ConcurrentQueue<Action> _mainThreadActions = new();
        private readonly Dictionary<string, IDefaultFactory> _defaults = new();
        private readonly ITimedCollection<long, PlayerMetaData> _playerTimers;
        private readonly Dictionary<long, PlayerMetaData> _playerMetaData = new();
        private bool _disposed;

        private PlayerSpeaksManager(PlayerSpeakerConfig config)
        {
            _playerTimeoutSeconds = config.PlayerTimeoutSeconds;
            _natify = config.ClientFast ?? throw new InvalidOperationException("ClientFast is null");

            _playerTimers = ITimedCollection<long, PlayerMetaData>.NewTimeSortSetSingleThread();
            _playerTimers.OnExpired += OnPlayerExpired;

            _natify.OnMessage<PlayerPingMsg>(PingTopic, OnPing);
            _natify.OnMessage<PlayerOnlineStatusMsg>(StatusTopic, OnStatus);
            _natify.OnMessage<ClientMirrorMessage>(ClientMsgTopic, OnClientMsg);
        }

        internal static IPlayerSpeaksManager Create(PlayerSpeakerConfig config)
        {
            return new PlayerSpeaksManager(config);
        }

        public T CreateData<T>(long playerId) where T : class, IPlayerData, new()
        {
            T data;
            try
            {
                data = new T();
                ((IPlayerDataInternal)data).SetPlayerId(playerId);
            }
            catch (Exception ex)
            {
                PubSubLog.Error(ex, "CreateData init failed");
                throw;
            }

            if (!_data.TryGetValue(playerId, out var playerData))
                _data[playerId] = playerData = new Dictionary<string, IPlayerData>();
            playerData[data.DataName] = data;

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

            return data;
        }

        public T? GetData<T>(long playerId) where T : class, IPlayerData, new()
        {
            try
            {
                var t = new T();

                if (!_data.TryGetValue(playerId, out var playerData))
                    return null;

                if (playerData.TryGetValue(t.DataName, out var existing) && existing is T typed)
                    return typed;

                return null;
            }
            catch (Exception ex)
            {
                PubSubLog.Error(ex, "GetData<T> failed");
                return null;
            }
        }

        public void OnDefault<T>(Func<T, Task<Action>> callback) where T : class, IPlayerData, new()
        {
            try
            {
                var t = new T();
                _defaults[t.DataName] = new DefaultFactory<T>(t.DataName, callback, _mainThreadActions);
                t.DoneInit();
            }
            catch (Exception ex)
            {
                PubSubLog.Error(ex, "OnDefault<T> failed");
            }
        }

        public bool Remove(long playerId)
        {
            try
            {
                if (!_data.TryGetValue(playerId, out var playerData))
                    return false;

                foreach (var data in playerData.Values)
                {
                    if (data.IsOnLine)
                        return false;
                }

                foreach (var data in playerData.Values)
                {
                    if (data is IOnRemove onRemove)
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
                }

                _data.Remove(playerId);
                return true;
            }
            catch (Exception ex)
            {
                PubSubLog.Error(ex, "Remove failed");
                return false;
            }
        }

        private void OnPing(Data<PlayerPingMsg> args)
        {
            try
            {
                var playerId = args.Value.PlayerId;
                ProcessPing(playerId);
            }
            catch (Exception ex)
            {
                PubSubLog.Error(ex, "OnPing failed");
            }
        }

        private void OnStatus(Data<PlayerOnlineStatusMsg> args)
        {
            try
            {
                var msg = args.Value;
                ProcessStatus(msg);
            }
            catch (Exception ex)
            {
                PubSubLog.Error(ex, "OnStatus failed");
            }
        }

        private void OnClientMsg(Data<ClientMirrorMessage> args)
        {
            try
            {
                var msg = args.Value;


                try
                {
                    if (!_data.TryGetValue(msg.PlayerId, out var playerData))
                        return;
                    if (!playerData.TryGetValue(msg.DataName, out var dataObj))
                        return;
                    if (dataObj is not IPlayerDataInternal di)
                        return;

                    if (msg.Subject == "__player_speaks_init__")
                    {
                        dataObj.Commit("init");
                    }
                    else
                    {
                        var actions = di.PrepareMessageDispatch(msg.Subject, msg.Data.ToByteArray());
                        foreach (var a in actions)
                        {
                            try
                            {
                                a.Invoke();
                            }
                            catch (Exception ex)
                            {
                                PubSubLog.Error(ex, "Error on invoke action");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    PubSubLog.Error(ex, "OnClientMsg handler failed");
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

            _playerTimers.Tick();
            _natify.Tick();
        }

        private void ProcessPing(long playerId)
        {
            try
            {
                if (!_playerMetaData.TryGetValue(playerId, out var meta))
                {
                    meta = new PlayerMetaData { LastPingTicks = DateTime.UtcNow.Ticks };
                    _playerMetaData[playerId] = meta;
                    _playerTimers.AddOrUpdate(playerId, meta, TimeSpan.FromSeconds(_playerTimeoutSeconds));

                    foreach (var kv in _defaults)
                    {
                        try
                        {
                            kv.Value.CreateAndInit(playerId, this);
                        }
                        catch (Exception ex)
                        {
                            PubSubLog.Error(ex, $"Auto-create {kv.Key} failed");
                        }
                    }

                    if (_data.TryGetValue(playerId, out var playerData))
                    {
                        foreach (var data in playerData.Values)
                        {
                            try
                            {
                                if (data is IPlayerDataInternal di)
                                {
                                    di.SetOnline(true);
                                    if (di.IsInitDone && data is IOnClientConnect onConnect)
                                        onConnect.OnClientConnect();
                                }
                            }
                            catch (Exception ex)
                            {
                                PubSubLog.Error(ex, "SetOnline/OnClientConnect failed");
                            }
                        }
                    }
                }
                else
                {
                    meta.LastPingTicks = DateTime.UtcNow.Ticks;
                    _playerTimers.AddOrUpdate(playerId, meta, TimeSpan.FromSeconds(_playerTimeoutSeconds));
                }
            }
            catch (Exception ex)
            {
                PubSubLog.Error(ex, "ProcessPing failed");
            }
        }

        private void ProcessStatus(PlayerOnlineStatusMsg msg)
        {
            try
            {
                if (!msg.IsOnline)
                    DisconnectPlayer(msg.PlayerId);
            }
            catch (Exception ex)
            {
                PubSubLog.Error(ex, "ProcessStatus failed");
            }
        }

        private void DisconnectPlayer(long playerId)
        {
            try
            {
                _playerMetaData.Remove(playerId);
                _playerTimers.Remove(playerId);

                if (_data.TryGetValue(playerId, out var playerData))
                {
                    foreach (var data in playerData.Values)
                    {
                        try
                        {
                            if (data is IPlayerDataInternal di)
                                di.SetOnline(false);
                            if (data is IOnClientDisconnect onDisconnect)
                                onDisconnect.OnClientDisconnect();
                        }
                        catch (Exception ex)
                        {
                            PubSubLog.Error(ex, "DisconnectPlayer hook failed");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                PubSubLog.Error(ex, "DisconnectPlayer failed");
            }
        }

        private void OnPlayerExpired(IReadOnlyList<(long, PlayerMetaData)> items)
        {
            foreach (var (playerId, _) in items)
                DisconnectPlayer(playerId);
        }

        private interface IDefaultFactory
        {
            string DataName { get; }
            void CreateAndInit(long playerId, PlayerSpeaksManager manager);
        }

        private sealed class DefaultFactory<T> : IDefaultFactory where T : class, IPlayerData, new()
        {
            private readonly Func<T, Task<Action>>? _callback;
            private readonly ConcurrentQueue<Action> _mainThreadActions;
            public string DataName { get; }

            public DefaultFactory(string dataName, Func<T, Task<Action>>? callback,
                ConcurrentQueue<Action> mainThreadActions)
            {
                DataName = dataName;
                _callback = callback;
                _mainThreadActions = mainThreadActions;
            }

            public void CreateAndInit(long playerId, PlayerSpeaksManager manager)
            {
                var data = manager.CreateData<T>(playerId);

                if (_callback != null)
                {
                    var task = _callback(data);
                    task.ContinueWith(t =>
                    {
                        if (t.IsCompletedSuccessfully && t.Result != null)
                        {
                            _mainThreadActions.Enqueue(() =>
                            {
                                try
                                {
                                    t.Result();
                                }
                                catch (Exception ex)
                                {
                                    PubSubLog.Error(ex, "OnDefault action failed");
                                }

                                data.DoneInit();
                            });
                        }
                        else
                        {
                            _mainThreadActions.Enqueue(() => data.DoneInit());
                        }
                    });
                }
                else
                {
                    _mainThreadActions.Enqueue(() => data.DoneInit());
                }
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed) return;
            _disposed = true;

            _playerMetaData.Clear();
            _playerTimers.Dispose();
            _data.Clear();
            await _natify.DisposeAsync();
        }
    }
}