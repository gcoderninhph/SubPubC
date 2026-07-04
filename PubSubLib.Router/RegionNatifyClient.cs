using Natify;
using PubSubLib.Contracts;
using PubSubLib.Messages;

namespace PubSubLib.Router;

internal sealed class RegionNatifyClient : IRegionNatifyClient
{
    private readonly NatifyServer _server;
    private readonly string _regionId;

    private Action<CreateUnitEvt>? _onCreateUnitEvt;
    private Action<DestroyUnitEvt>? _onDestroyUnitEvt;

    private const string CmdTopic = "Region.Cmd";
    private const string EvtTopic = "Region.Evt";

    internal RegionNatifyClient(NatifyServer server, string regionId)
    {
        _server = server;
        _regionId = regionId;
        _server.OnMessage<RegionEvent>(EvtTopic, OnEvent);
    }

    private void OnEvent((string regionId, Data<RegionEvent> data) args)
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

    public void SendCreateUnit(CreateUnitCmd cmd)
    {
        _server.Publish(CmdTopic, _regionId, new RegionCommand { CreateUnit = cmd });
    }

    public void SendDestroyUnit(DestroyUnitCmd cmd)
    {
        _server.Publish(CmdTopic, _regionId, new RegionCommand { DestroyUnit = cmd });
    }

    public void OnCreateUnitEvt(Action<CreateUnitEvt> callback) { _onCreateUnitEvt = callback; }
    public void OnDestroyUnitEvt(Action<DestroyUnitEvt> callback) { _onDestroyUnitEvt = callback; }

    public void Dispose()
    {
        _server.Dispose();
    }
}
