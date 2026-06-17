namespace PubSubLib;

internal class Unit<T> : IUnit<T> where T : class
{
    private Vector2 _position;
    private string _currentCellId = "";
    private WeakReference<T>? _weakRef;
    private byte[]? _data;
    internal IPubSubInternal? PubSub;

    public long Id { get; }
    public string Type { get; }

    public Vector2 Position
    {
        get => _position;
        set
        {
            if (_position.x == value.x && _position.y == value.y) return;
            _position = value;
            PubSub?.OnUnitPositionChanged(this);
        }
    }

    public WeakReference<T> WeakReference => _weakRef!;
    public bool IsAlive => _weakRef != null && _weakRef.TryGetTarget(out _);

    public byte[]? Data { get => _data; set => _data = value; }

    public T? Target
    {
        get
        {
            if (_weakRef != null && _weakRef.TryGetTarget(out var t))
                return t;
            return default;
        }
    }

    void IUnit<T>.PublishEvent(string eventName, object? data)
    {
        PubSub?.PublishEvent(this, eventName, data);
    }

    public string CurrentCellId
    {
        get => _currentCellId;
        set => _currentCellId = value;
    }

    internal Unit(long id, string type, Vector2 position, WeakReference<T> weakRef)
    {
        Id = id;
        Type = type;
        _position = position;
        _weakRef = weakRef;
    }
}
