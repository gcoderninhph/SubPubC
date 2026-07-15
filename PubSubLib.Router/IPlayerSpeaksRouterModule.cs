using MyConnection;
using Natify;

namespace PubSubLib.Router;

public interface IPlayerSpeaksRouterModule : IServerModule, IDisposable
{
    static IPlayerSpeaksRouterModule Create(INatifyServer server, string regionId)
    {
        return new PlayerSpeaksRouterModule(server, regionId);
    }
}
