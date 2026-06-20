using System.Collections.Generic;

namespace PubSubLib.Client;

public abstract class ProviderAbstract<T> : IProvider<T>, IProviderWithClient where T : class, IAlive
{
    private IPubSubClient? _client;

    void IProviderWithClient.SetClient(IPubSubClient client) => _client = client;
    void IProviderWithClient.OnStart() => OnStart();
    void IProviderWithClient.OnDispose() => OnDispose();

    protected virtual void OnStart() { }
    protected virtual void OnDispose() { }

    public abstract string UnitType { get; }
    public abstract T CreateObject(long unitId, byte[] data);
    public abstract void UpdateObject(long unitId, T obj, byte[] data);
    public abstract void DestroyObject(long unitId, T obj);
    public abstract void OnEvent(long unitId, T obj, string eventName, byte[] data, EventMeta meta);

    public IReadOnlyList<IUnit<T>> GetAllUnits()
    {
        var list = new List<IUnit<T>>();
        if (_client != null)
        {
            foreach (var unit in _client.GetAllUnitsByType(UnitType))
                list.Add(new TypedUnit<T>(unit));
        }
        return list;
    }

    public IUnit<T>? GetUnit(long unitId)
    {
        var unit = _client?.GetUnit(unitId, UnitType);
        return unit == null ? null : new TypedUnit<T>(unit);
    }
}
