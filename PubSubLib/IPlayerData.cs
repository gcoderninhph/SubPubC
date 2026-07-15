namespace PubSubLib;

public interface IPlayerData
{
    long PlayerId { get; }
    bool IsOnLine { get; }

    string DataName { get; }

    void OnChange(Action<byte[], string> handler);
    void OnMessage(Action<string, byte[]> handler);
    void Commit(string commit);
    void DoneInit();
}