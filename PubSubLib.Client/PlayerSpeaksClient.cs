using System.Collections.Concurrent;
using Google.Protobuf;
using PubSubLib.Contracts;
using PubSubLib.Messages;
using PubSubLib.Mirror;

namespace PubSubLib.Client;

internal sealed class PlayerSpeaksClient : IPlayerSpeaksClient
{
    private long _playerId;
    private readonly ConcurrentDictionary<Type, IPlayerMirrorClient> _data = new();
    private readonly ConcurrentDictionary<string, IPlayerMirrorClient> _dataByName = new();

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
    }

    public T? GetData<T>() where T : class, IPlayerMirrorClient
    {
        _data.TryGetValue(typeof(T), out var val);
        return val as T;
    }

    public void RemoveData<T>() where T : class, IPlayerMirrorClient
    {
        if (_data.TryRemove(typeof(T), out var removed))
            _dataByName.TryRemove(removed.DataName, out _);
    }

    internal void ApplyUpdate(string dataName, byte[] data, string commit)
    {
        try
        {
            if (_dataByName.TryGetValue(dataName, out var mirror))
                mirror.ApplyUpdate(data, commit);
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

    public void Dispose()
    {
        OnSendToServer = null;
        _data.Clear();
        _dataByName.Clear();
    }
}
