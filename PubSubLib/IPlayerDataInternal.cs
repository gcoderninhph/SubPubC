namespace PubSubLib;

public interface IPlayerDataInternal : IPlayerData
{
    void SetOnline(bool isOnline);
    void SetPlayerId(long playerId);
    System.Collections.Generic.List<System.Action> PrepareMessageDispatch(string subject, byte[] data);
}
