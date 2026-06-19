namespace PubSubLib.Client;

public interface IProvider
{
    string UnitType { get; }

    IAlive CreateObject(long unitId, byte[] data);

    void UpdateObject(long unitId, IAlive obj, byte[] data);

    void DestroyObject(long unitId, IAlive obj);

    void OnEvent(long unitId, IAlive obj, string eventName, byte[] data, EventMeta meta);
}

public interface IProvider<T> : IProvider where T : class, IAlive
{
    new T CreateObject(long unitId, byte[] data);

    void UpdateObject(long unitId, T obj, byte[] data);

    void DestroyObject(long unitId, T obj);

    void OnEvent(long unitId, T obj, string eventName, byte[] data, EventMeta meta);

    IAlive IProvider.CreateObject(long unitId, byte[] data) => CreateObject(unitId, data);

    void IProvider.UpdateObject(long unitId, IAlive obj, byte[] data) => UpdateObject(unitId, (T)obj, data);

    void IProvider.DestroyObject(long unitId, IAlive obj) => DestroyObject(unitId, (T)obj);

    void IProvider.OnEvent(long unitId, IAlive obj, string eventName, byte[] data, EventMeta meta) => OnEvent(unitId, (T)obj, eventName, data, meta);
}
