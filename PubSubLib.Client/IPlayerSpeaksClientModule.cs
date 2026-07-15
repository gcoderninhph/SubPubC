using MyConnection;
using PubSubLib.Mirror;

namespace PubSubLib.Client;

public interface IPlayerSpeaksClientModule : IClientModule, IDisposable
{
    static IPlayerSpeaksClientModule Create(int pingIntervalMs = 2000)
    {
        return new PlayerSpeaksClientModule(pingIntervalMs);
    }

    IPlayerSpeaksClient Get();
    void Tick();
}
