namespace PubSubLib;

internal class Unit : IUnit
{
    private Vector2 _position;
    private string _currentCellId = "";
    private WeakReference<object>? _weakRef;
    private byte[]? _data;
    private int _version;
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
            _version++;
            PubSub?.OnUnitPositionChanged(this);
        }
    }

    public bool IsAlive => _weakRef != null && _weakRef.TryGetTarget(out _);

    public int Version => _version;

    public byte[]? Data { get => _data; set { _data = value; _version++; } }

    public object? Target
    {
        get
        {
            if (_weakRef != null && _weakRef.TryGetTarget(out var t))
                return t;
            return null;
        }
    }

    void IUnit.PublishEvent(string eventName, object? data, bool reliable)
    {
        PubSub?.PublishEvent(this, eventName, data, reliable);
    }

    public void Destroy()
    {
        PubSub?.OnUnitDestroyed(this);
    }

    public string CurrentCellId
    {
        get => _currentCellId;
        set => _currentCellId = value;
    }

    internal Unit(long id, string type, Vector2 position, object target)
    {
        Id = id;
        Type = type;
        _position = position;
        _weakRef = new WeakReference<object>(target);
    }
}
