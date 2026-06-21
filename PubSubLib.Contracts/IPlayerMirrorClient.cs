namespace PubSubLib.Mirror;

public interface IPlayerMirrorClient
{
    long PlayerId { get; set; }

    string DataName { get; }

    void ApplyUpdate(byte[] data, string commit);
}
