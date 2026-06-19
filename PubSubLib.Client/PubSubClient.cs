using System.Collections.Generic;
using System.Diagnostics;
using MyConnection;
using PubSubLib.Messages;

namespace PubSubLib.Client;

internal sealed class PubSubClient : IPubSubClient
{
    private IClient? _client;
#pragma warning disable CS0649
    private long _watcherId;
#pragma warning restore CS0649
    private Vector2 _position;
    private float _radius;
    private Vector2? _lastSentPos;
    private float? _lastSentRadius;
    private readonly Dictionary<string, IProvider> _providers = new();
    private readonly Dictionary<(long Id, string Type), Unit> _units = new();
    private readonly int _pingIntervalMs;
    private long _lastPingTimestamp;

    public PubSubClient(Config config)
    {
        _pingIntervalMs = config.PingIntervalMs;
    }

    internal void SetClient(IClient client)
    {
        _client = client;
    }

    public void Tick()
    {
        if (_client == null)
            return;

        var now = Stopwatch.GetTimestamp();
        var elapsedMs = (now - _lastPingTimestamp) * 1000 / Stopwatch.Frequency;
        if (elapsedMs < _pingIntervalMs)
            return;
        _lastPingTimestamp = now;

        CleanDeadUnits();

        var cmd = new PingUnitsCmd { WatcherId = _watcherId };

        var groups = new Dictionary<string, TypeGroup>();
        foreach (var (key, unit) in _units)
        {
            if (!groups.TryGetValue(key.Type, out var group))
            {
                group = new TypeGroup { Type = key.Type };
                groups[key.Type] = group;
            }
            group.UnitIds.Add(key.Id);
            group.Versions.Add(unit.Version);
        }
        cmd.Units.AddRange(groups.Values);

        _client.SendOnUdp("PubSub.Cmd", new PubSubCommand { PingUnits = cmd });
    }

    public void MoveWatcher(Vector2 position, float radius)
    {
        _position = position;
        _radius = radius;

        if (_client == null)
            return;

        if (_lastSentPos.HasValue && _lastSentRadius.HasValue
            && _lastSentPos.Value.x == position.x
            && _lastSentPos.Value.y == position.y
            && _lastSentRadius.Value == radius)
            return;

        _lastSentPos = position;
        _lastSentRadius = radius;

        var cmd = new MoveWatcherCmd
        {
            WatcherId = _watcherId,
            PosX = position.x,
            PosY = position.y,
            Radius = radius
        };

        _client.SendOnUdp("PubSub.Cmd", new PubSubCommand { MoveWatcher = cmd });
    }

    public IPubSubClient AddProvider(IProvider provider)
    {
        _providers[provider.UnitType] = provider;
        return this;
    }

    internal void HandleBatchEnter(BatchEnterMsg msg)
    {
        if (!_providers.TryGetValue(msg.UnitType, out var provider))
            return;

        var obj = CreateUnitObject(provider, msg.UnitId, msg.Version, msg.Data);
        var key = (msg.UnitId, msg.UnitType);
        _units[key] = new Unit(msg.UnitId, msg.Version, msg.UnitType, obj);
    }

    internal void HandleSyncEnter(SyncEnterMsg msg)
    {
        foreach (var item in msg.Units)
        {
            if (!_providers.TryGetValue(item.Type, out var provider))
                continue;

            var obj = CreateUnitObject(provider, item.Id, item.Version, item.Data);
            var key = (item.Id, item.Type);
            _units[key] = new Unit(item.Id, item.Version, item.Type, obj);
        }
    }

    internal void HandleBatchLeave(BatchLeaveMsg msg)
    {
        var key = (msg.UnitId, msg.UnitType);
        if (!_units.TryGetValue(key, out var unit))
            return;

        if (_providers.TryGetValue(msg.UnitType, out var provider))
        {
            if (unit.Target is { } target)
                DestroyUnitObject(provider, msg.UnitId, target);
        }
        _units.Remove(key);
    }

    internal void HandleSyncLeave(SyncLeaveMsg msg)
    {
        foreach (var group in msg.Keys)
        {
            foreach (var unitId in group.UnitIds)
            {
                var key = (unitId, group.Type);
                if (!_units.TryGetValue(key, out var unit))
                    continue;

                if (_providers.TryGetValue(group.Type, out var provider))
                {
                    if (unit.Target is { } target)
                        DestroyUnitObject(provider, unitId, target);
                }
                _units.Remove(key);
            }
        }
    }

    internal void HandleUnitEvent(UnitEventMsg msg, EventTransport transport = EventTransport.Tcp)
    {
        if (!_providers.TryGetValue(msg.UnitType, out var provider))
            return;

        var key = (msg.UnitId, msg.UnitType);
        if (!_units.TryGetValue(key, out var unit))
            return;

        if (unit.Target is { } target)
        {
            var meta = new EventMeta(transport);
            provider.OnEvent(msg.UnitId, target, msg.EventName, msg.Data.ToByteArray(), meta);
        }
    }

    private void CleanDeadUnits()
    {
        var deadKeys = new List<(long Id, string Type)>();
        foreach (var (key, unit) in _units)
        {
            if (!unit.IsAlive)
            {
                if (_providers.TryGetValue(key.Type, out var provider))
                {
                    if (unit.Target is { } target)
                        DestroyUnitObject(provider, key.Id, target);
                }
                deadKeys.Add(key);
            }
        }

        foreach (var key in deadKeys)
            _units.Remove(key);
    }

    private static object CreateUnitObject(IProvider provider, long unitId, int version, Google.Protobuf.ByteString data)
    {
        return provider.CreateObject(unitId, version, data.ToByteArray());
    }

    private static void DestroyUnitObject(IProvider provider, long unitId, object target)
    {
        provider.DestroyObject(unitId, target);
    }
}
