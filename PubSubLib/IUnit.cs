namespace PubSubLib;

public interface IUnit
{
    long Id { get; }
    string Type { get; }
    Vector2 Position { get; set; }
    bool IsAlive { get; }
    object? Target { get; }
    int Version { get; }
    byte[]? Data { get; set; }
    void PublishEvent(string eventName, object? data);
    void Destroy();
}
