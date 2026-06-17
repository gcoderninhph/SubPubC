using Natify;
using PubSubLib.Messages;

namespace PubSubLib;

public interface IPubSubNatifyClient : IDisposable
{
    static IPubSubNatifyClient Create(NatifyServer server, string regionId)
    {
        return new PubSubNatifyClient(server, regionId);
    }

    // ── Send: gửi command → PubSub.Cmd ──

    void SendAddWatcher(AddWatcherCmd cmd);
    void SendRemoveWatcher(RemoveWatcherCmd cmd);
    void SendMoveWatcher(MoveWatcherCmd cmd);
    void SendPingUnits(PingUnitsCmd cmd);
    void SendPublishEvent(PublishEventCmd cmd);

    // ── Receive: nhận event ← PubSub.Evt ──

    void OnBatchEnter(Action<BatchEnterMsg> callback);
    void OnBatchLeave(Action<BatchLeaveMsg> callback);
    void OnSyncEnter(Action<SyncEnterMsg> callback);
    void OnSyncLeave(Action<SyncLeaveMsg> callback);
    void OnUnitEvent(Action<UnitEventMsg> callback);
}
