using MyConnection;
using Natify;

namespace PubSubLib.Router;

public interface IRegionRouterModule : IServerModule
{
    static IRegionRouterModule Create(NatifyServer server, string regionId)
    {
        return new RegionRouterModule(server, regionId);
    }
}
