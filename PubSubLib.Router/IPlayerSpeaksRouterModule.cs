using MyConnection;
using Natify;

namespace PubSubLib.Router;

public interface IPlayerSpeaksRouterModule : IServerModule
{
    static IPlayerSpeaksRouterModule Create(NatifyServer server, string regionId)
    {
        return new PlayerSpeaksRouterModule(server, regionId);
    }
}
