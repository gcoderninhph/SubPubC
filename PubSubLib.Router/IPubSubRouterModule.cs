using MyConnection;
using Natify;
using PubSubLib.Messages;

namespace PubSubLib.Router;

public interface IPubSubRouterModule : IServerModule
{
    static IPubSubRouterModule Create(NatifyServer server, string regionId)
    {
        return new PubSubRouterModule(server, regionId);
    }
}
