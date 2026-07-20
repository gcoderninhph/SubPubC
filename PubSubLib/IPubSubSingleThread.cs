using Google.Protobuf;

namespace PubSubLib;

public interface IPubSubSingleThread : IDisposable
{
    static IPubSubSingleThread Create(PubSubConfig config)
    {
        throw new NotImplementedException();
    }

    IUnitSt<T, TR>? GetUnitOfBy<T, TR>(long id) where T : class, IAlive where TR : class, IMessage<TR>;

    IUnitSt<T, TR> CreateUnit<T, TR>(long id, Vector2 position, T target, TR data)
        where T : class, IAlive where TR : class, IMessage<TR>;

    void AddWatcher(long watcherId, Vector2 position, float radius);
    void RemoveWatcher(long watcherId);
    void MoveWatcher(long watcherId, Vector2 position, float radius);
    void WatcherPingUnits(long watcherId, Dictionary<string, Dictionary<long, int>> typeVersions);

    ISubscrible OnUnitEnter<T, TR>(Action<(List<long> notyWatchIds, IUnitSt<T, TR> units)> callBack)
        where T : class, IAlive where TR : class, IMessage<TR>;
    ISubscrible OnUnitLeave<T, TR>(Action<(List<long> notyWatchIds, IUnitSt<T, TR> units)> callBack)
        where T : class, IAlive where TR : class, IMessage<TR>;
    ISubscrible OnUnitEnter<T, TR>(Action<(long notyWatchId, List<IUnit> units)> callBack)
        where T : class, IAlive where TR : class, IMessage<TR>;
    ISubscrible OnUnitLeave<T, TR>(Action<(long notyWatchId, List<UnitKey> unitKeys)> callBack)
        where T : class, IAlive where TR : class, IMessage<TR>;

    ISubscrible OnUnitEvent<T>(
        Action<(List<long> notyWatchId, IUnit units, string eventName, T data, bool reliable)> callBack)
        where T : class, IAlive;

    void Tick();
}