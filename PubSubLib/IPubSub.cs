namespace PubSubLib;

public interface IPubSub : IDisposable
{
    static IPubSub Create<T>(PubSubConfig config) where T : class
    {
        return PubSub<T>.Create(config);
    }

    void AddUnit<T>(IUnit<T> unit) where T : class;
    void RemoveUnit<T>(IUnit<T> unit) where T : class;

    void AddWatcher(long watcherId, Vector2 position, float radius);
    void RemoveWatcher(long watcherId);
    void MoveWatcher(long watcherId, Vector2 position, float radius);
    void WatcherPingUnits(long watcherId, string unitType, List<UnitKey> unitKeys);

    void PublishEvent<T>(IUnit<T> unit, string eventName, object? data) where T : class;

    void OnUnitEnter<T>(Action<(List<long> notyWatchIds, IUnit<T> units)> callBack) where T : class;
    void OnUnitLeave<T>(Action<(List<long> notyWatchIds, IUnit<T> units)> callBack) where T : class;

    void OnUnitEnter<T>(Action<(long notyWatchId, List<IUnit<T>> units)> callBack) where T : class;
    void OnUnitLeave<T>(Action<(long notyWatchId, List<UnitKey> unitKeys)> callBack) where T : class;

    void OnUnitEvent<T>(Action<(List<long> notyWatchId, IUnit<T> units, string eventName, object data)> callBack) where T : class;
}

internal interface IPubSubInternal
{
    void OnUnitPositionChanged<T>(Unit<T> unit) where T : class;
    void OnUnitDestroyed<T>(Unit<T> unit) where T : class;
}
