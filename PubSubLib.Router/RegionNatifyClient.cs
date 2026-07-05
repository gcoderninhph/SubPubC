using Natify;
using PubSubLib.Contracts;
using PubSubLib.Messages;

namespace PubSubLib.Router;

internal sealed class RegionNatifyClient : IRegionNatifyClient
{
    private readonly NatifyServer _server;
    private readonly string _regionId;

    private const string RegionCmdTopic = "Region.Cmd";
    private const string RegionEvtTopic = "Region.Evt";
    private const string PubSubCmdTopic = "PubSub.Cmd";
    private const string PubSubEvtTopic = "PubSub.Evt";

    private Action<CreateUnitEvt>? _onCreateUnitEvt;
    private Action<DestroyUnitEvt>? _onDestroyUnitEvt;

    private Action<BatchEnterMsg>? _onBatchEnter;
    private Action<BatchLeaveMsg>? _onBatchLeave;
    private Action<SyncEnterMsg>? _onSyncEnter;
    private Action<SyncLeaveMsg>? _onSyncLeave;
    private Action<UnitEventMsg>? _onUnitEvent;

    internal RegionNatifyClient(NatifyServer server, string regionId)
    {
        _server = server;
        _regionId = regionId;
        _server.OnMessage<RegionEvent>(RegionEvtTopic, OnRegionEvent);
        _server.OnMessage<PubSubEvent>(PubSubEvtTopic, OnPubSubEvent);
    }

    // ===== Region event handler =====

    private void OnRegionEvent((string regionId, Data<RegionEvent> data) args)
    {
        var evt = args.data.Value;
        switch (evt.EvtCase)
        {
            case RegionEvent.EvtOneofCase.CreateUnit:
                _onCreateUnitEvt?.Invoke(evt.CreateUnit);
                break;
            case RegionEvent.EvtOneofCase.DestroyUnit:
                _onDestroyUnitEvt?.Invoke(evt.DestroyUnit);
                break;
        }
    }

    // ===== PubSub event handler =====

    private void OnPubSubEvent((string regionId, Data<PubSubEvent> data) args)
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

    // ===== Region send =====

    public void SendCreateUnit(CreateUnitCmd cmd)
    {
        _server.Publish(RegionCmdTopic, _regionId, new RegionCommand { CreateUnit = cmd });
    }

    public void SendDestroyUnit(DestroyUnitCmd cmd)
    {
        _server.Publish(RegionCmdTopic, _regionId, new RegionCommand { DestroyUnit = cmd });
    }

    // ===== PubSub send =====

    public void SendAddWatcher(AddWatcherCmd cmd)
    {
        _server.Publish(PubSubCmdTopic, _regionId, new PubSubCommand { AddWatcher = cmd });
    }

    public void SendRemoveWatcher(RemoveWatcherCmd cmd)
    {
        _server.Publish(PubSubCmdTopic, _regionId, new PubSubCommand { RemoveWatcher = cmd });
    }

    public void SendMoveWatcher(MoveWatcherCmd cmd)
    {
        _server.Publish(PubSubCmdTopic, _regionId, new PubSubCommand { MoveWatcher = cmd });
    }

    public void SendPingUnits(PingUnitsCmd cmd)
    {
        _server.Publish(PubSubCmdTopic, _regionId, new PubSubCommand { PingUnits = cmd });
    }

    // ===== Region callback registration =====

    public void OnCreateUnitEvt(Action<CreateUnitEvt> callback) { _onCreateUnitEvt = callback; }
    public void OnDestroyUnitEvt(Action<DestroyUnitEvt> callback) { _onDestroyUnitEvt = callback; }

    // ===== PubSub callback registration =====

    public void OnBatchEnter(Action<BatchEnterMsg> callback) { _onBatchEnter = callback; }
    public void OnBatchLeave(Action<BatchLeaveMsg> callback) { _onBatchLeave = callback; }
    public void OnSyncEnter(Action<SyncEnterMsg> callback) { _onSyncEnter = callback; }
    public void OnSyncLeave(Action<SyncLeaveMsg> callback) { _onSyncLeave = callback; }
    public void OnUnitEvent(Action<UnitEventMsg> callback) { _onUnitEvent = callback; }

    public void Dispose()
    {
    }
}
