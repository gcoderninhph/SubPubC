using MyConnection;
using Natify;

namespace PubSubLib.Router;

public interface IRegionRouterModule : IServerModule
{
    static IRegionRouterModule Create(INatifyServer server, string regionId)
    {
        return new RegionRouterModule(server, regionId);
    }
}
