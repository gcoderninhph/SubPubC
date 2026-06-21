namespace PubSubLib;

public interface IPlayerDataInternal : IPlayerData
{
    void SetOnline(bool isOnline);
    void SetPlayerId(long playerId);
}
