namespace PubSubLib;

public interface IPlayerData
{
    long PlayerId { get; }
    bool IsOnLine { get; }

    string DataName { get; }
    void OnChange(Action<byte[]> handler);
}