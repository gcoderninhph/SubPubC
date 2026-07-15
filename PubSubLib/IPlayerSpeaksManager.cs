namespace PubSubLib;

public interface IPlayerSpeaksManager : IAsyncDisposable
{
    static IPlayerSpeaksManager Create(PlayerSpeakerConfig config)
    {
        return PlayerSpeaksManager.Create(config);
    }

    T CreateData<T>(long playerId) where T : class, IPlayerData, new();
    T? GetData<T>(long playerId) where T : class, IPlayerData, new();
    Task<bool> RemoveAsync(long playerId);
    void Tick();
    void OnDefault<T>(Func<T, Task>? callback) where T : class, IPlayerData, new();
    void OnRemove<T>(Func<T, Task>? callback) where T : class, IPlayerData, new();
}