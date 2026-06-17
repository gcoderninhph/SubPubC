using System.Collections.Concurrent;

namespace PubSubLib;

internal enum EventType
{
    BatchEnter,
    BatchLeave,
    SyncEnter,
    SyncLeave,
    UnitEvent
}

internal sealed class InternalEvent
{
    public EventType Type;
    public long[]? WatcherIds;
    public object? Unit;
    public long WatcherId;
    public object?[]? SyncUnits;
    public UnitKey[]? SyncUnitIds;
    public string? EventName;
    public object? Data;

    public void Reset()
    {
        Type = default;
        WatcherIds = null;
        Unit = null;
        WatcherId = 0;
        SyncUnits = null;
        SyncUnitIds = null;
        EventName = null;
        Data = null;
    }
}

internal static class InternalEventPool
{
    private const int DefaultMaxSize = 64;
    private static readonly ConcurrentBag<InternalEvent> _pool = new();
    private static int _maxSize = DefaultMaxSize;

    public static int MaxSize
    {
        get => _maxSize;
        set => _maxSize = value > 0 ? value : DefaultMaxSize;
    }

    public static InternalEvent Rent()
    {
        if (_pool.TryTake(out var evt))
            return evt;
        return new InternalEvent();
    }

    public static void Return(InternalEvent evt)
    {
        evt.Reset();
        if (_pool.Count < _maxSize)
            _pool.Add(evt);
    }
}
