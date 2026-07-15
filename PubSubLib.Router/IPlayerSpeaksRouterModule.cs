using MyConnection;
using Natify;

namespace PubSubLib.Router;

public interface IPlayerSpeaksRouterModule : IServerModule
{
    static IPlayerSpeaksRouterModule Create(INatifyServer server, string regionId)
    {
        return new PlayerSpeaksRouterModule(server, regionId);
    }
}
