using Natify;
using PubSubLib.Contracts;
using PubSubLib.Messages;

namespace PubSubLib.Router;

public interface IRegionNatifyClient : IDisposable
{
    static IRegionNatifyClient Create(INatifyServer server, string regionId)
    {
        return new RegionNatifyClient(server, regionId);
    }

    void SendAddWatcher(AddWatcherCmd cmd);
    void SendRemoveWatcher(RemoveWatcherCmd cmd);
    void SendMoveWatcher(MoveWatcherCmd cmd);
    void SendPingUnits(PingUnitsCmd cmd);

    void OnBatchEnter(Action<BatchEnterMsg> callback);
    void OnBatchLeave(Action<BatchLeaveMsg> callback);
    void OnSyncEnter(Action<SyncEnterMsg> callback);
    void OnSyncLeave(Action<SyncLeaveMsg> callback);
    void OnUnitEvent(Action<UnitEventMsg> callback);
}
