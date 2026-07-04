using Natify;
using PubSubLib.Messages;

namespace PubSubLib.Router;

public interface IRegionNatifyClient : IDisposable
{
    static IRegionNatifyClient Create(NatifyServer server, string regionId)
    {
        return new RegionNatifyClient(server, regionId);
    }

    void SendCreateUnit(CreateUnitCmd cmd);
    void SendDestroyUnit(DestroyUnitCmd cmd);

    void OnCreateUnitEvt(Action<CreateUnitEvt> callback);
    void OnDestroyUnitEvt(Action<DestroyUnitEvt> callback);
}
