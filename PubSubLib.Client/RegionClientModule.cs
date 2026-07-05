using System.Diagnostics;
using MyConnection;
using PubSubLib;
using PubSubLib.Contracts;
using PubSubLib.Messages;

namespace PubSubLib.Client;

internal sealed class RegionClientModule : IRegionClientModule, IDisposable
{
    private IClient? _client;
    private ISubscribe? _tcpSubRegion;
    private ISubscribe? _tcpSubPubSub;
    private ISubscribe? _udpSubPubSub;

    private readonly Dictionary<(long Id, string Type), object> _units = new();
    private readonly Dictionary<string, RegionUnitFactory> _factories = new();

    private long _watcherId;
    private Vector2 _position;
    private float _radius;
    private Vector2? _lastSentPos;
    private float? _lastSentRadius;
    private readonly int _pingIntervalMs;
    private long _lastPingTimestamp;

    public RegionClientModule(Config config)
    {
        _pingIntervalMs = config.PingIntervalMs;
    }

    public void SetIClient(IClient client)
    {
        _client = client;

        _tcpSubRegion = client.SubscribeTcp<RegionEvent>("Region.Evt", OnRegionEvent);

        _tcpSubPubSub = client.SubscribeTcp<PubSubEvent>("PubSub.Evt",
            evt => OnPubSubEvent(evt));

        _udpSubPubSub = client.SubscribeUdp<PubSubEvent>("PubSub.Evt",
            evt => OnPubSubEvent(evt));
    }

    private bool TryParseWatcherId()
    {
        return true;
    }

    private void OnRegionEvent(RegionEvent evt)
    {
        try
        {
            switch (evt.EvtCase)
            {
                case RegionEvent.EvtOneofCase.CreateUnit:
                    HandleCreateUnit(evt.CreateUnit);
                    break;
                case RegionEvent.EvtOneofCase.DestroyUnit:
                    HandleDestroyUnit(evt.DestroyUnit);
                    break;
            }
        }
        catch (Exception ex) { PubSubLog.Error(ex, "RegionClientModule.OnRegionEvent failed"); }
    }

    private void HandleCreateUnit(CreateUnitEvt evt)
    {
        if (!_factories.TryGetValue(evt.UnitType, out var factory))
            return;

        var key = (evt.UnitId, evt.UnitType);
        if (_units.ContainsKey(key))
            return;

        var wrapper =         factory.CreateWrapper();
        var internalWrapper = (IRegionClientUnitInternal)wrapper;

        internalWrapper.Init(evt.UnitId,
            new Vector2 { x = evt.PosX, y = evt.PosY });

        var target = factory.CreateTarget(wrapper);
        internalWrapper.SetTarget((IAlive)target);

        if (target is not IAlive aliveTarget || !aliveTarget.IsAlive)
            return;

        factory.OnSetup(wrapper, target);

        if (evt.Data != null && evt.Data.Length > 0)
            internalWrapper.ApplyUpdate(evt.Data.ToByteArray(), "");

        _units[key] = wrapper;
    }

    private void HandleDestroyUnit(DestroyUnitEvt evt)
    {
        DestroyUnitInternal((evt.UnitId, evt.UnitType));
    }

    private void DestroyUnitInternal((long Id, string Type) key)
    {
        if (!_units.TryGetValue(key, out var obj))
            return;

        var internalWrapper = (IRegionClientUnitInternal)obj;
        var target = internalWrapper.GetTarget();

        if (target is IRegionOnDestroy od)
            od.OnDestroyUnit();

        _units.Remove(key);
    }

    private void OnPubSubEvent(PubSubEvent evt)
    {
        try
        {
            switch (evt.EvtCase)
            {
                case PubSubEvent.EvtOneofCase.BatchEnter:
                    HandleBatchEnter(evt.BatchEnter);
                    break;
                case PubSubEvent.EvtOneofCase.BatchLeave:
                    HandleBatchLeave(evt.BatchLeave);
                    break;
                case PubSubEvent.EvtOneofCase.SyncEnter:
                    HandleSyncEnter(evt.SyncEnter);
                    break;
                case PubSubEvent.EvtOneofCase.SyncLeave:
                    HandleSyncLeave(evt.SyncLeave);
                    break;
                case PubSubEvent.EvtOneofCase.UnitEvent:
                    HandleUnitEvent(evt.UnitEvent);
                    break;
            }
        }
        catch (Exception ex) { PubSubLog.Error(ex, "RegionClientModule.OnPubSubEvent failed"); }
    }

    private void HandleBatchEnter(BatchEnterMsg msg)
    {
        var key = (msg.UnitId, msg.UnitType);
        if (!_units.TryGetValue(key, out var obj))
        {
            if (_factories.TryGetValue(msg.UnitType, out var factory))
            {
                HandleCreateUnit(new CreateUnitEvt
                {
                    UnitId = msg.UnitId,
                    UnitType = msg.UnitType,
                    PosX = msg.PosX,
                    PosY = msg.PosY,
                    Data = msg.Data
                });
            }
            return;
        }

        if (msg.Data != null && msg.Data.Length > 0)
            ((IRegionClientUnitInternal)obj).ApplyUpdate(msg.Data.ToByteArray(), "");
    }

    private void HandleBatchLeave(BatchLeaveMsg msg)
    {
        DestroyUnitInternal((msg.UnitId, msg.UnitType));
    }

    private void HandleSyncEnter(SyncEnterMsg msg)
    {
        foreach (var item in msg.Units)
        {
            var key = (item.Id, item.Type);
            if (_units.ContainsKey(key))
                continue;

            if (!_factories.TryGetValue(item.Type, out var factory))
                continue;

            HandleCreateUnit(new CreateUnitEvt
            {
                UnitId = item.Id,
                UnitType = item.Type,
                PosX = item.PosX,
                PosY = item.PosY,
                Data = item.Data
            });
        }
    }

    private void HandleSyncLeave(SyncLeaveMsg msg)
    {
        foreach (var group in msg.Keys)
        {
            foreach (var id in group.UnitIds)
            {
                DestroyUnitInternal((id, group.Type));
            }
        }
    }

    private void HandleUnitEvent(UnitEventMsg msg)
    {
        var key = (msg.UnitId, msg.UnitType);
        if (!_units.TryGetValue(key, out var obj))
            return;

        var internalWrapper = (IRegionClientUnitInternal)obj;

        switch (msg.EventName)
        {
            case "commit":
                try
                {
                    var commit = RegionCommit.Parser.ParseFrom(msg.Data);
                    internalWrapper.ApplyUpdate(commit.MirrorData.ToByteArray(), commit.Commit);

                    var target = internalWrapper.GetTarget();
                    if (target is IRegionOnCommit oc)
                        oc.OnCommitUnit(commit.Commit);
                }
                catch (Exception ex) { PubSubLog.Error(ex, "HandleUnitEvent commit failed"); }
                break;
            case "message":
                try
                {
                    var rmsg = RegionMessage.Parser.ParseFrom(msg.Data);
                    internalWrapper.DispatchMessage(rmsg.Subject, rmsg.Data.ToByteArray());
                }
                catch (Exception ex) { PubSubLog.Error(ex, "HandleUnitEvent message failed"); }
                break;
        }
    }

    public void OnCreateUnit<T, TR>(Func<T, TR> unit)
        where T : class, new()
        where TR : class, IAlive
    {
        var temp = Activator.CreateInstance<T>();
        var internalWrapper = (IRegionClientUnitInternal)temp!;
        var unitType = internalWrapper.GetUnitType();

        _factories[unitType] = new RegionUnitFactory(
            () => Activator.CreateInstance<T>(),
            w => unit((T)w),
            (w, t) =>
            {
                if (t is ISetRegionUnit<T, TR> su)
                    su.SetRegionUnit((T)w);
                if (t is IRegionOnStart os)
                    os.OnStartUnit();
            }
        );
    }

    public T GetUnit<T, TR>(long id)
        where T : class, new()
        where TR : class, IAlive
    {
        var temp = Activator.CreateInstance<T>();
        var internalWrapper = (IRegionClientUnitInternal)temp!;
        var unitType = internalWrapper.GetUnitType();
        var key = (id, unitType);

        if (_units.TryGetValue(key, out var obj) && obj is T t)
            return t;

        throw new KeyNotFoundException($"Unit {unitType}:{id} not found");
    }

    public IList<T> GetUnits<T, TR>()
        where T : class, new()
        where TR : class, IAlive
    {
        var temp = Activator.CreateInstance<T>();
        var internalWrapper = (IRegionClientUnitInternal)temp!;
        var unitType = internalWrapper.GetUnitType();

        var result = new List<T>();
        foreach (var kvp in _units)
        {
            if (kvp.Key.Type == unitType && kvp.Value is T t)
                result.Add(t);
        }
        return result;
    }

    public void Destroy<T, TR>(T unit)
        where T : class
        where TR : class, IAlive
    {
        var internalWrapper = (IRegionClientUnitInternal)unit;
        var key = (internalWrapper.GetId(), internalWrapper.GetUnitType());
        DestroyUnitInternal(key);
    }

    public void Tick()
    {
        if (_client == null)
            return;

        if (!TryParseWatcherId())
            return;

        var now = Stopwatch.GetTimestamp();
        var elapsedMs = (now - _lastPingTimestamp) * 1000 / Stopwatch.Frequency;
        if (elapsedMs < _pingIntervalMs)
            return;
        _lastPingTimestamp = now;

        CleanDeadUnits();

        var cmd = new PingUnitsCmd { WatcherId = _watcherId };

        var groups = new Dictionary<string, TypeGroup>();
        foreach (var (key, obj) in _units)
        {
            if (!groups.TryGetValue(key.Type, out var group))
            {
                group = new TypeGroup { Type = key.Type };
                groups[key.Type] = group;
            }
            group.UnitIds.Add(key.Id);
            group.Versions.Add(0);
        }
        cmd.Units.AddRange(groups.Values);

        _client.SendOnUdp("PubSub.Cmd", new PubSubCommand { PingUnits = cmd });
    }

    public void MoveWatcher(Vector2 position, float range)
    {
        _position = position;
        _radius = range;

        if (_client == null)
            return;

        if (!TryParseWatcherId())
            return;

        if (_lastSentPos.HasValue && _lastSentRadius.HasValue
            && _lastSentPos.Value.x == position.x
            && _lastSentPos.Value.y == position.y
            && _lastSentRadius.Value == range)
            return;

        _lastSentPos = position;
        _lastSentRadius = range;

        var cmd = new MoveWatcherCmd
        {
            WatcherId = _watcherId,
            PosX = position.x,
            PosY = position.y,
            Radius = range
        };

        _client.SendOnUdp("PubSub.Cmd", new PubSubCommand { MoveWatcher = cmd });
    }

    private void CleanDeadUnits()
    {
        var deadKeys = new List<(long, string)>();
        foreach (var (key, obj) in _units)
        {
            var internalWrapper = (IRegionClientUnitInternal)obj;
            var target = internalWrapper.GetTarget();
            if (target == null || !target.IsAlive)
                deadKeys.Add(key);
        }

        foreach (var key in deadKeys)
        {
            if (_units.TryGetValue(key, out var obj))
            {
                var internalWrapper = (IRegionClientUnitInternal)obj;
                var target = internalWrapper.GetTarget();
                if (target is IRegionOnDestroy od)
                    od.OnDestroyUnit();
            }
            _units.Remove(key);
        }
    }

    public void Dispose()
    {
        _tcpSubRegion?.UnSubscribe();
        _tcpSubPubSub?.UnSubscribe();
        _udpSubPubSub?.UnSubscribe();
    }

    private sealed class RegionUnitFactory
    {
        private readonly Func<object> _createWrapper;
        private readonly Func<object, object> _createTarget;
        private readonly Action<object, object> _onSetup;

        public RegionUnitFactory(
            Func<object> createWrapper,
            Func<object, object> createTarget,
            Action<object, object> onSetup)
        {
            _createWrapper = createWrapper;
            _createTarget = createTarget;
            _onSetup = onSetup;
        }

        public object CreateWrapper() => _createWrapper();

        public object CreateTarget(object wrapper) => _createTarget(wrapper);

        public void OnSetup(object wrapper, object target) => _onSetup(wrapper, target);
    }
}
