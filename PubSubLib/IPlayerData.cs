namespace PubSubLib;

public interface IPlayerData
{
    long PlayerId { get; set; }
    bool IsOnLine { get; set; }

    string DataName { get; }
    void OnChange(Action<byte[]> handler);
}