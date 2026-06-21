namespace PubSubLib;

public interface IPlayerSpeaksManager : IDisposable
{
    static IPlayerSpeaksManager Create(PlayerSpeakerConfig config)
    {
        return PlayerSpeaksManager.Create(config);
    }

    T CreateData<T>(long playerId) where T : class, IPlayerData, new();
}