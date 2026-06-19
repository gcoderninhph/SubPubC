namespace PubSubLib.Client;

public interface IProvider
{
    string UnitType { get; }

    object CreateObject(long unitId, int version, byte[] data);

    void DestroyObject(long unitId, object obj);

    void OnEvent(long unitId, object obj, string eventName, byte[] data, EventMeta meta);
}

public sealed class GameObjectTest
{
    public long UnitId { get; set; }
    public string Type { get; set; } = string.Empty;
}
