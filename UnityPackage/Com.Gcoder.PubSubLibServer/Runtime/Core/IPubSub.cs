using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

#nullable enable
namespace PubSubLib
{
    public interface IPubSub : IDisposable
    {
        static IPubSub Create(PubSubConfig config)
        {
            return PubSub.Create(config);
        }

        Task<IUnit?> GetUnitOfByTypeAsync(string type, long id);

        void CreateUnit<T>(long id, string type, Vector2 position, T target, Action<IUnit> onCreated,
            byte[]? data = null) where T : class, IAlive;

        Task<IUnit> CreateUnitAsync<T>(long id, string type, Vector2 position, T target, byte[]? data = null)
            where T : class, IAlive;

        Task FlushAsync();

        void AddWatcher(long watcherId, Vector2 position, float radius);
        void RemoveWatcher(long watcherId);
        void MoveWatcher(long watcherId, Vector2 position, float radius);
        void WatcherPingUnits(long watcherId, Dictionary<string, Dictionary<long, int>> typeVersions);

        ISubscrible OnUnitEnter(Action<(List<long> notyWatchIds, IUnit units)> callBack);
        ISubscrible OnUnitLeave(Action<(List<long> notyWatchIds, IUnit units)> callBack);

        ISubscrible OnUnitEnter(Action<(long notyWatchId, List<IUnit> units)> callBack);
        ISubscrible OnUnitLeave(Action<(long notyWatchId, List<UnitKey> unitKeys)> callBack);

        ISubscrible OnUnitEvent(
            Action<(List<long> notyWatchId, IUnit units, string eventName, object data, bool reliable)> callBack);
    }
}