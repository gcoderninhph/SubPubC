using System.Collections.Generic;

namespace PubSubLib.Client;

public abstract class ProviderAbstract<T> : IProvider<T> where T : class, IAlive
{
    private readonly IPubSubClient _client;

    protected ProviderAbstract(IPubSubClient client) => _client = client;

    public abstract string UnitType { get; }
    public abstract T CreateObject(long unitId, byte[] data);
    public abstract void UpdateObject(long unitId, T obj, byte[] data);
    public abstract void DestroyObject(long unitId, T obj);
    public abstract void OnEvent(long unitId, T obj, string eventName, byte[] data, EventMeta meta);

    public IReadOnlyList<IUnit<T>> GetAllUnits()
    {
        var list = new List<IUnit<T>>();
        foreach (var unit in _client.GetAllUnitsByType(UnitType))
            list.Add(new TypedUnit<T>(unit));
        return list;
    }

    public IUnit<T>? GetUnit(long unitId)
    {
        var unit = _client.GetUnit(unitId, UnitType);
        return unit == null ? null : new TypedUnit<T>(unit);
    }
}
