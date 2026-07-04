using Natify;
using PubSubLib.Contracts;
using PubSubLib.Messages;

namespace PubSubLib;

internal sealed class RegionModule : IRegionModule, IDisposable
{
    private readonly IPubSub _pubSub;
    private readonly RegionNatifySync? _natifySync;
    private readonly Dictionary<UnitKey, object> _units = new();
    private readonly Dictionary<string, Delegate> _factories = new();

    internal RegionModule(RegionConfig config)
    {
        _pubSub = IPubSub.Create(config);

        if (config.NatifyClient != null)
        {
            var natifyAdapter = new NatifyAdapter(config.NatifyClient);
            _natifySync = new RegionNatifySync(natifyAdapter, this);
            _natifySync.OnCreateUnitCmd(HandleCreateUnitCmd);
            _natifySync.OnDestroyUnitCmd(HandleDestroyUnitCmd);
        }
    }

    public void RegisterUnitType<T, TR>(string unitType, Func<TR>? factory = null)
        where T : class, IRegionUnit<TR>, new()
        where TR : class, IAlive
    {
        _factories[unitType] = factory ?? (() => default!);
    }

    private void HandleCreateUnitCmd(CreateUnitCmd cmd)
    {
        if (!_factories.TryGetValue(cmd.UnitType, out var factoryDel))
            return;

        var target = (IAlive)factoryDel.DynamicInvoke(null)!;

        _pubSub.CreateUnit(cmd.UnitId, cmd.UnitType,
            new Vector2 { x = cmd.PosX, y = cmd.PosY },
            target, u =>
            {
                if (cmd.Data != null)
                    u.Data = cmd.Data.ToByteArray();
            });
    }

    private void HandleDestroyUnitCmd(DestroyUnitCmd cmd)
    {
        var unit = _pubSub.GetUnitOfByType(cmd.UnitType, cmd.UnitId);
        if (unit != null)
            unit.Destroy();
    }

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

            _natifySync?.PublishCreateUnit(new CreateUnitEvt
            {
                UnitId = id,
                UnitType = unitType,
                PosX = position.x,
                PosY = position.y,
                Data = Google.Protobuf.ByteString.CopyFrom(iu.Data ?? Array.Empty<byte>())
            });

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
                    try { callback(t.Result); }
                    catch (Exception ex) { PubSubLog.Error(ex, "CreateUnit callback failed"); }
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

    public T GetUnit<T, TR>(long id)
        where T : class, IRegionUnit<TR>, new()
        where TR : class, IAlive
    {
        var wrapper = new T();
        var unitType = ((IRegionUnitInternal)wrapper).GetUnitType();
        var key = new UnitKey(id, unitType);

        if (_units.TryGetValue(key, out var obj) && obj is T t)
            return t;

        throw new KeyNotFoundException($"Unit {unitType}:{id} not found");
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
        var target = t.Get();

        if (target is IRegionUnitOnDestroy od)
            od.OnUnitDestroy();

        var iu = internalWrapper.GetUnit();
        if (iu != null)
            iu.Destroy();

        _units.Remove(key);

        _natifySync?.PublishDestroyUnit(new DestroyUnitEvt
        {
            UnitId = id,
            UnitType = unitType
        });
    }

    public void Dispose()
    {
        _natifySync?.Dispose();
        _pubSub.Dispose();
    }
}
