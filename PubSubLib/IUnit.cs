namespace PubSubLib;

public interface IUnit<T> where T : class
{
    long Id { get; }
    string Type { get; }
    Vector2 Position { get; set; }
    WeakReference<T> WeakReference { get; }
    bool IsAlive { get; }
    T? Target { get; }
    byte[]? Data { get; set; }
    void PublishEvent(string eventName, object? data);

    static IUnit<T> Create(long id, string type, Vector2 position, T target)
    {
        return new Unit<T>(id, type, position, new WeakReference<T>(target));
    }
}
