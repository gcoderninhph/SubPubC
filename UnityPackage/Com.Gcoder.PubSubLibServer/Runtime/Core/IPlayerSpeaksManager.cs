using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
namespace PubSubLib
{

public interface IPlayerSpeaksManager : IAsyncDisposable
{
    static IPlayerSpeaksManager Create(PlayerSpeakerConfig config)
    {
        return PlayerSpeaksManager.Create(config);
    }

    T CreateData<T>(long playerId) where T : class, IPlayerData, new();
    T? GetData<T>(long playerId) where T : class, IPlayerData, new();
    bool Remove(long playerId);
    void Tick();
    void OnDefault<T>(Func<T, Task<Action>> callback) where T : class, IPlayerData, new();
}
}
