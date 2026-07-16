using Google.Protobuf;
using Natify;
using PubSubLib.Contracts;
using PubSubLib.Messages;

namespace PubSubLib;

internal sealed class RegionModule : IRegionModule
{
    private readonly IPubSub _pubSub;
    private readonly INatifyClient? _natifyAdapter;
    private readonly Dictionary<UnitKey, object> _units = new();
    
    private ISubscrible? _subBatchEnter;
    private ISubscrible? _subBatchLeave;
    private ISubscrible? _subSyncEnter;
    private ISubscrible? _subSyncLeave;
    private ISubscrible? _subUnitEvent;

    private const string PubSubCmdTopic = "PubSub.Cmd";
    private const string PubSubEvtTopic = "PubSub.Evt";

    internal RegionModule(RegionConfig config)
    {
        _pubSub = IPubSub.Create(config);

        if (config.NatifyClient != null)
        {
            _natifyAdapter = config.NatifyClient;

            _natifyAdapter.OnMessage<PubSubCommand>(PubSubCmdTopic, OnPubSubCommand);

            Action<(List<long> watcherIds, IUnit unit)> batchEnter = a => OnPubSubBatchEnter(a.unit, a.watcherIds);
            Action<(List<long> watcherIds, IUnit unit)> batchLeave = a => OnPubSubBatchLeave(a.unit, a.watcherIds);
            Action<(long watcherId, List<IUnit> units)> syncEnter = a => OnPubSubSyncEnter(a.watcherId, a.units);
            Action<(long watcherId, List<UnitKey> unitKeys)>
                syncLeave = a => OnPubSubSyncLeave(a.watcherId, a.unitKeys);
            Action<(List<long> watcherIds, IUnit unit, string eventName, object data, bool reliable)> unitEvent =
                a => OnPubSubUnitEvent(a.unit, a.watcherIds, a.eventName, a.data, a.reliable);

            _subBatchEnter = _pubSub.OnUnitEnter(batchEnter);
            _subBatchLeave = _pubSub.OnUnitLeave(batchLeave);
            _subSyncEnter = _pubSub.OnUnitEnter(syncEnter);
            _subSyncLeave = _pubSub.OnUnitLeave(syncLeave);
            _subUnitEvent = _pubSub.OnUnitEvent(unitEvent);
        }
    }
    // ===== Inbound PubSub commands =====

    private void OnPubSubCommand(Data<PubSubCommand> data)
    {
        try
        {
            var cmd = data.Value;
            switch (cmd.CmdCase)
            {
                case PubSubCommand.CmdOneofCase.AddWatcher:
                    HandleAddWatcher(cmd.AddWatcher);
                    break;
                case PubSubCommand.CmdOneofCase.RemoveWatcher:
                    HandleRemoveWatcher(cmd.RemoveWatcher);
                    break;
                case PubSubCommand.CmdOneofCase.MoveWatcher:
                    HandleMoveWatcher(cmd.MoveWatcher);
                    break;
                case PubSubCommand.CmdOneofCase.PingUnits:
                    HandlePingUnits(cmd.PingUnits);
                    break;
            }
        }
        catch (Exception ex)
        {
            PubSubLog.Error(ex, "RegionModule.OnPubSubCommand failed");
        }
    }

    private void HandleAddWatcher(AddWatcherCmd cmd)
    {
        _pubSub.AddWatcher(cmd.WatcherId, new Vector2 { x = cmd.PosX, y = cmd.PosY }, cmd.Radius);
    }

    private void HandleRemoveWatcher(RemoveWatcherCmd cmd)
    {
        _pubSub.RemoveWatcher(cmd.WatcherId);
    }

    private void HandleMoveWatcher(MoveWatcherCmd cmd)
    {
        _pubSub.MoveWatcher(cmd.WatcherId, new Vector2 { x = cmd.PosX, y = cmd.PosY }, cmd.Radius);
    }

    private void HandlePingUnits(PingUnitsCmd cmd)
    {
        var typeVersions = new Dictionary<string, Dictionary<long, int>>();
        foreach (var group in cmd.Units)
        {
            var dict = new Dictionary<long, int>();
            var ids = group.UnitIds;
            var versions = group.Versions;
            var count = Math.Min(ids.Count, versions.Count);
            for (int i = 0; i < count; i++)
                dict[ids[i]] = versions[i];
            typeVersions[group.Type] = dict;
        }

        _pubSub.WatcherPingUnits(cmd.WatcherId, typeVersions);
    }

    // ===== Outbound PubSub events (After* hooks) =====

    private void OnPubSubBatchEnter(IUnit unit, List<long> watcherIds)
    {
        var data = ByteString.CopyFrom(unit.Data ?? Array.Empty<byte>());
        var msg = new BatchEnterMsg
        {
            UnitId = unit.Id,
            UnitType = unit.Type,
            PosX = unit.Position.x,
            PosY = unit.Position.y,
            Data = data,
            Version = unit.Version
        };
        msg.WatcherIds.AddRange(watcherIds);

        _natifyAdapter.Publish(PubSubEvtTopic, new PubSubEvent { BatchEnter = msg });
    }

    private void OnPubSubBatchLeave(IUnit unit, List<long> watcherIds)
    {
        var msg = new BatchLeaveMsg
        {
            UnitId = unit.Id,
            UnitType = unit.Type
        };
        msg.WatcherIds.AddRange(watcherIds);

        _natifyAdapter.Publish(PubSubEvtTopic, new PubSubEvent { BatchLeave = msg });
    }

    private void OnPubSubSyncEnter(long watcherId, List<IUnit> units)
    {
        var msg = new SyncEnterMsg { WatcherId = watcherId };
        foreach (var u in units)
        {
            var data = ByteString.CopyFrom(u.Data ?? Array.Empty<byte>());
            msg.Units.Add(new UnitEnterItem
            {
                Id = u.Id,
                Type = u.Type,
                PosX = u.Position.x,
                PosY = u.Position.y,
                Data = data,
                Version = u.Version
            });
        }

        _natifyAdapter.Publish(PubSubEvtTopic, new PubSubEvent { SyncEnter = msg });
    }

    private void OnPubSubSyncLeave(long watcherId, List<UnitKey> keys)
    {
        var msg = new SyncLeaveMsg { WatcherId = watcherId };
        var groups = new Dictionary<string, TypeGroup>();
        foreach (var k in keys)
        {
            if (!groups.TryGetValue(k.Type, out var g))
            {
                g = new TypeGroup { Type = k.Type };
                groups[k.Type] = g;
            }

            g.UnitIds.Add(k.Id);
        }

        msg.Keys.AddRange(groups.Values);

        _natifyAdapter.Publish(PubSubEvtTopic, new PubSubEvent { SyncLeave = msg });
    }

    private void OnPubSubUnitEvent(IUnit unit, List<long> watcherIds, string eventName, object? data, bool reliable)
    {
        var msg = new UnitEventMsg
        {
            UnitId = unit.Id,
            UnitType = unit.Type,
            EventName = eventName,
            Data = data is byte[] b ? ByteString.CopyFrom(b) : ByteString.Empty,
            UseUdp = !reliable
        };
        msg.WatcherIds.AddRange(watcherIds);

        _natifyAdapter.Publish(PubSubEvtTopic, new PubSubEvent { UnitEvent = msg });
    }

    // ===== RegionModule public API =====

    public Task<T> CreateUnitAsync<T, TR>(long id, Vector2 position, TR target)
        where T : class, IRegionUnit<TR>, new()
        where TR : class, IAlive
    {
        var tcs = new TaskCompletionSource<T>();
        var wrapper = new T();
        var internalWrapper = (IRegionUnitInternal)wrapper;
        var unitType = internalWrapper.GetUnitType();

        _pubSub.CreateUnit(id, unitType, position, target, iu =>
        {
            internalWrapper.SetUnit(iu);

            if (target is ISetRegionUnit<T, TR> su)
                su.SetRegionUnit(wrapper);

            if (target is IRegionUnitOnStart os)
                os.OnUnitStart();

            _units[new UnitKey(id, unitType)] = wrapper;

            tcs.SetResult(wrapper);
        });

        return tcs.Task;
    }

    public void CreateUnit<T, TR>(long id, Vector2 position, TR target, Action<T> callback)
        where T : class, IRegionUnit<TR>, new()
        where TR : class, IAlive
    {
        CreateUnitAsync<T, TR>(id, position, target)
            .ContinueWith(t =>
            {
                if (t.IsCompletedSuccessfully)
                {
                    try
                    {
                        callback(t.Result);
                    }
                    catch (Exception ex)
                    {
                        PubSubLog.Error(ex, "CreateUnit callback failed");
                    }
                }
            });
    }

    public Task<T> CreateUnitAsync<T, TR>(long id, Vector2 position, Func<TR> targetFactory)
        where T : class, IRegionUnit<TR>, new()
        where TR : class, IAlive
    {
        var target = targetFactory();
        return CreateUnitAsync<T, TR>(id, position, target);
    }

    public void CreateUnit<T, TR>(long id, Vector2 position, Func<TR> targetFactory, Action<T> callback)
        where T : class, IRegionUnit<TR>, new()
        where TR : class, IAlive
    {
        var target = targetFactory();
        CreateUnit<T, TR>(id, position, target, callback);
    }

    public T? GetUnit<T, TR>(long id)
        where T : class, IRegionUnit<TR>, new()
        where TR : class, IAlive
    {
        var wrapper = new T();
        var unitType = ((IRegionUnitInternal)wrapper).GetUnitType();
        var key = new UnitKey(id, unitType);

        if (_units.TryGetValue(key, out var obj) && obj is T t)
            return t;

        return null;
    }

    public bool TryGetUnit<T, TR>(long id, out T unit)
        where T : class, IRegionUnit<TR>, new()
        where TR : class, IAlive
    {
        var wrapper = new T();
        var unitType = ((IRegionUnitInternal)wrapper).GetUnitType();
        var key = new UnitKey(id, unitType);

        if (_units.TryGetValue(key, out var obj) && obj is T t)
        {
            unit = t;
            return true;
        }

        unit = null!;
        return false;
    }

    public IList<T> GetUnits<T, TR>()
        where T : class, IRegionUnit<TR>, new()
        where TR : class, IAlive
    {
        var wrapper = new T();
        var unitType = ((IRegionUnitInternal)wrapper).GetUnitType();

        var result = new List<T>();
        foreach (var kvp in _units)
        {
            if (kvp.Key.Type == unitType && kvp.Value is T t)
                result.Add(t);
        }

        return result;
    }

    public void DestroyUnit<T, TR>(long id)
        where T : class, IRegionUnit<TR>, new()
        where TR : class, IAlive
    {
        var wrapper = new T();
        var unitType = ((IRegionUnitInternal)wrapper).GetUnitType();
        var key = new UnitKey(id, unitType);

        if (!_units.TryGetValue(key, out var obj) || obj is not T t)
            return;

        var internalWrapper = (IRegionUnitInternal)t;
        var iu = internalWrapper.GetUnit();
        var target = iu?.Target as TR;

        if (target is IRegionUnitOnDestroy od)
            od.OnUnitDestroy();

        if (iu != null)
            iu.Destroy();

        _units.Remove(key);
    }

    public async ValueTask DisposeAsync()
    {
        _subBatchEnter?.UnSubscribe();
        _subBatchLeave?.UnSubscribe();
        _subSyncEnter?.UnSubscribe();
        _subSyncLeave?.UnSubscribe();
        _subUnitEvent?.UnSubscribe();
        _pubSub.Dispose();
        if (_natifyAdapter != null)
            await _natifyAdapter.DisposeAsync();
    }
}