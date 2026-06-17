namespace PubSubLib;

public interface IUnit<T> where T : class
{
    long Id { get; }
    string Type { get; }
    Vector2 Position { get; set; }
    WeakReference<T> WeakReference { get; }
    bool IsAlive { get; }
    T? Target { get; }
    int Version { get; }
    byte[]? Data { get; set; }
    void PublishEvent(string eventName, object? data);
    void Destroy();
}
