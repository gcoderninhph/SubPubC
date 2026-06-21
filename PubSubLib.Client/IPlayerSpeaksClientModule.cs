using MyConnection;
using PubSubLib.Mirror;

namespace PubSubLib.Client;

public interface IPlayerSpeaksClientModule : IClientModule, IDisposable
{
    static IPlayerSpeaksClientModule Create()
    {
        return new PlayerSpeaksClientModule();
    }

    IPlayerSpeaksClient Get();
}
