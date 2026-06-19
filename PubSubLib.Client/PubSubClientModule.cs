using MyConnection;
using PubSubLib.Messages;

namespace PubSubLib.Client;

internal sealed class PubSubClientModule : IPubSubClientModule
{
    private readonly PubSubClient _pubSubClient;
    private ISubscribe? _tcpSub;
    private ISubscribe? _udpSub;

    public PubSubClientModule(Config config)
    {
        _pubSubClient = new PubSubClient(config);
    }

    public void SetIClient(IClient client)
    {
        _pubSubClient.SetClient(client);
        _tcpSub = client.SubscribeTcp<PubSubEvent>("PubSub.Evt", evt => OnEvent(evt, EventTransport.Tcp));
        _udpSub = client.SubscribeUdp<PubSubEvent>("PubSub.Evt", evt => OnEvent(evt, EventTransport.Udp));
    }

    private void OnEvent(PubSubEvent evt, EventTransport transport)
    {
        switch (evt.EvtCase)
        {
            case PubSubEvent.EvtOneofCase.BatchEnter:
                _pubSubClient.HandleBatchEnter(evt.BatchEnter);
                break;
            case PubSubEvent.EvtOneofCase.BatchLeave:
                _pubSubClient.HandleBatchLeave(evt.BatchLeave);
                break;
            case PubSubEvent.EvtOneofCase.SyncEnter:
                _pubSubClient.HandleSyncEnter(evt.SyncEnter);
                break;
            case PubSubEvent.EvtOneofCase.SyncLeave:
                _pubSubClient.HandleSyncLeave(evt.SyncLeave);
                break;
            case PubSubEvent.EvtOneofCase.UnitEvent:
                _pubSubClient.HandleUnitEvent(evt.UnitEvent, transport);
                break;
        }
    }

    public IPubSubClient Get()
    {
        return _pubSubClient;
    }

    public void Dispose()
    {
        _tcpSub?.UnSubscribe();
        _udpSub?.UnSubscribe();
        _pubSubClient.Dispose();
    }
}
