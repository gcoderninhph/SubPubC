using Natify;

namespace PubSubLib;

public interface IPubSub : IDisposable
{
    static IPubSub Create(PubSubConfig config)
    {
        return PubSub.Create(config);
    }

    IUnit? GetUnitOfByType(string type, long id);
    void CreateUnit<T>(long id, string type, Vector2 position, T target, Action<IUnit> onCreated, byte[]? data = null) where T : class, IAlive;
    Task<IUnit> CreateUnitAsync<T>(long id, string type, Vector2 position, T target, byte[]? data = null) where T : class, IAlive;
    Task FlushAsync();

    void AddWatcher(long watcherId, Vector2 position, float radius);
    void RemoveWatcher(long watcherId);
    void MoveWatcher(long watcherId, Vector2 position, float radius);
    void WatcherPingUnits(long watcherId, Dictionary<string, Dictionary<long, int>> typeVersions);

    void AddNatify(NatifyClientFast client);
    void AddNatify(NatifyClient client);

    void OnUnitEnter(Action<(List<long> notyWatchIds, IUnit units)> callBack);
    void OnUnitLeave(Action<(List<long> notyWatchIds, IUnit units)> callBack);

    void OnUnitEnter(Action<(long notyWatchId, List<IUnit> units)> callBack);
    void OnUnitLeave(Action<(long notyWatchId, List<UnitKey> unitKeys)> callBack);

    void OnUnitEvent(Action<(List<long> notyWatchId, IUnit units, string eventName, object data, bool reliable)> callBack);
}

internal interface IPubSubInternal
{
    void OnUnitPositionChanged(Unit unit);
    void OnUnitDestroyed(Unit unit);
    void PublishEvent(Unit unit, string eventName, object? data, bool reliable);
}
