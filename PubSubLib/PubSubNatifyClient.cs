using Natify;
using PubSubLib.Messages;

namespace PubSubLib;

internal sealed class PubSubNatifyClient : IPubSubNatifyClient
{
    private readonly NatifyServer _server;
    private readonly string _regionId;

    private Action<BatchEnterMsg>? _onBatchEnter;
    private Action<BatchLeaveMsg>? _onBatchLeave;
    private Action<SyncEnterMsg>? _onSyncEnter;
    private Action<SyncLeaveMsg>? _onSyncLeave;
    private Action<UnitEventMsg>? _onUnitEvent;

    private const string CmdTopic = "PubSub.Cmd";
    private const string EvtTopic = "PubSub.Evt";

    internal PubSubNatifyClient(NatifyServer server, string regionId)
    {
        _server = server;
        _regionId = regionId;
        _server.OnMessage<PubSubEvent>(EvtTopic, OnEvent);
    }

    // ===== Receive =====

    private void OnEvent((string regionId, Data<PubSubEvent> data) args)
    {
        var evt = args.data.Value;
        switch (evt.EvtCase)
        {
            case PubSubEvent.EvtOneofCase.BatchEnter:
                _onBatchEnter?.Invoke(evt.BatchEnter);
                break;
            case PubSubEvent.EvtOneofCase.BatchLeave:
                _onBatchLeave?.Invoke(evt.BatchLeave);
                break;
            case PubSubEvent.EvtOneofCase.SyncEnter:
                _onSyncEnter?.Invoke(evt.SyncEnter);
                break;
            case PubSubEvent.EvtOneofCase.SyncLeave:
                _onSyncLeave?.Invoke(evt.SyncLeave);
                break;
            case PubSubEvent.EvtOneofCase.UnitEvent:
                _onUnitEvent?.Invoke(evt.UnitEvent);
                break;
        }
    }

    // ===== Send =====

    public void SendAddWatcher(AddWatcherCmd cmd)
    {
        _server.Publish(CmdTopic, _regionId, new PubSubCommand { AddWatcher = cmd });
    }

    public void SendRemoveWatcher(RemoveWatcherCmd cmd)
    {
        _server.Publish(CmdTopic, _regionId, new PubSubCommand { RemoveWatcher = cmd });
    }

    public void SendMoveWatcher(MoveWatcherCmd cmd)
    {
        _server.Publish(CmdTopic, _regionId, new PubSubCommand { MoveWatcher = cmd });
    }

    public void SendPingUnits(PingUnitsCmd cmd)
    {
        _server.Publish(CmdTopic, _regionId, new PubSubCommand { PingUnits = cmd });
    }

    public void SendPublishEvent(PublishEventCmd cmd)
    {
        _server.Publish(CmdTopic, _regionId, new PubSubCommand { PublishEvent = cmd });
    }

    // ===== Callback registration =====

    public void OnBatchEnter(Action<BatchEnterMsg> callback)
    {
        _onBatchEnter = callback;
    }

    public void OnBatchLeave(Action<BatchLeaveMsg> callback)
    {
        _onBatchLeave = callback;
    }

    public void OnSyncEnter(Action<SyncEnterMsg> callback)
    {
        _onSyncEnter = callback;
    }

    public void OnSyncLeave(Action<SyncLeaveMsg> callback)
    {
        _onSyncLeave = callback;
    }

    public void OnUnitEvent(Action<UnitEventMsg> callback)
    {
        _onUnitEvent = callback;
    }

    // ===== IDisposable =====

    public void Dispose()
    {
        _server.Dispose();
    }
}
