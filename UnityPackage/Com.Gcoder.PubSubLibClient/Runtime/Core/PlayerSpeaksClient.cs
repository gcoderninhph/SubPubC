using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf;
using PubSubLib.Contracts;
using PubSubLib.Messages;
using PubSubLib.Mirror;

namespace PubSubLib.Client
{
    internal sealed class PlayerSpeaksClient : IPlayerSpeaksClient
    {
        private const string InitSubject = "__player_speaks_init__";
        private static readonly byte[] EmptyBytes = Array.Empty<byte>();
    
        private long _playerId;
        private readonly ConcurrentDictionary<Type, IPlayerMirrorClient> _data = new();
        private readonly ConcurrentDictionary<string, IPlayerMirrorClient> _dataByName = new();
        private readonly ConcurrentDictionary<string, Action> _initActions = new();
    
        private CancellationTokenSource? _actionCts;
        private Task? _actionTask;
        private readonly object _actionLock = new();
    
        internal long PlayerId => _playerId;
    
        internal Action<string, long, string, byte[]>? OnSendToServer;
    
        internal void SetPlayerId(long playerId)
        {
            _playerId = playerId;
            foreach (var instance in _data.Values)
                instance.PlayerId = playerId;
        }
    
        public void AddData<T>() where T : class, IPlayerMirrorClient, new()
        {
            var data = new T { PlayerId = _playerId };
            _data[typeof(T)] = data;
            _dataByName[data.DataName] = data;
            data.OnSendMessage((subject, playerId, bytes) => OnSendToServer?.Invoke(data.DataName, playerId, subject, bytes));
    
            if (!data.IsInitialized)
            {
                AddInitAction(data.DataName, () =>
                    OnSendToServer?.Invoke(data.DataName, _playerId, InitSubject, EmptyBytes));
            }
        }
    
        public T? GetData<T>() where T : class, IPlayerMirrorClient
        {
            _data.TryGetValue(typeof(T), out var val);
            return val as T;
        }
    
        public void RemoveData<T>() where T : class, IPlayerMirrorClient
        {
            if (_data.TryRemove(typeof(T), out var removed))
            {
                _dataByName.TryRemove(removed.DataName, out _);
                RemoveInitAction(removed.DataName);
            }
        }
    
        internal void ApplyUpdate(string dataName, byte[] data, string commit)
        {
            try
            {
                if (_dataByName.TryGetValue(dataName, out var mirror))
                {
                    bool wasInitialized = mirror.IsInitialized;
                    mirror.ApplyUpdate(data, commit);
                    if (!wasInitialized && mirror.IsInitialized)
                        RemoveInitAction(dataName);
                }
            }
            catch (Exception ex) { PubSubLog.Error(ex, "ApplyUpdate failed"); }
        }
    
        internal void DispatchMessage(string dataName, string subject, byte[] data)
        {
            try
            {
                if (_dataByName.TryGetValue(dataName, out var mirror))
                    mirror.DispatchMessage(subject, data);
            }
            catch (Exception ex) { PubSubLog.Error(ex, "DispatchMessage failed"); }
        }
    
        private void AddInitAction(string dataName, Action action)
        {
            _initActions[dataName] = action;
            StartActionLoop();
        }
    
        private void RemoveInitAction(string dataName)
        {
            if (_initActions.TryRemove(dataName, out _) && _initActions.IsEmpty)
                StopActionLoop();
        }
    
        private void StartActionLoop()
        {
            lock (_actionLock)
            {
                if (_actionTask != null) return;
                _actionCts = new CancellationTokenSource();
                var ct = _actionCts.Token;
                _actionTask = Task.Run(() => ActionLoop(ct));
            }
        }
    
        private void StopActionLoop()
        {
            Task? task;
            lock (_actionLock)
            {
                _actionCts?.Cancel();
                task = _actionTask;
                _actionTask = null;
            }
    
            if (task != null)
            {
                try { task.Wait(TimeSpan.FromSeconds(2)); } catch { }
            }
    
            lock (_actionLock)
            {
                _actionCts?.Dispose();
                _actionCts = null;
            }
        }
    
        private async Task ActionLoop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(1000, ct);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
    
                foreach (var action in _initActions.Values)
                {
                    try { action(); }
                    catch (Exception ex) { PubSubLog.Error(ex, "InitAction failed"); }
                }
            }
        }
    
        public void Dispose()
        {
            StopActionLoop();
            OnSendToServer = null;
            _data.Clear();
            _dataByName.Clear();
        }
    }

}

