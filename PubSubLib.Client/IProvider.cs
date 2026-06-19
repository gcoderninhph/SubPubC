namespace PubSubLib.Client;

public interface IProvider
{
    string UnitType { get; }

    object CreateObject(long unitId, byte[] data);

    void UpdateObject(long unitId, object obj, byte[] data);

    void DestroyObject(long unitId, object obj);

    void OnEvent(long unitId, object obj, string eventName, byte[] data, EventMeta meta);
}