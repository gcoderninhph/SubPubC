namespace PubSubLib;

public readonly struct PlayerDataKey
{
    public string DataName { get; }
    public long PlayerId { get; }

    public PlayerDataKey(string dataName, long playerId)
    {
        DataName = dataName;
        PlayerId = playerId;
    }
}