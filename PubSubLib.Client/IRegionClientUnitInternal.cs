namespace PubSubLib.Client;

internal interface IRegionClientUnitInternal
{
    void SetTarget(IAlive target);
    void ApplyUpdate(byte[] mirrorData, string commit);
    void Init(long id, Vector2 position);
    IAlive GetTarget();
    void DispatchMessage(string subject, byte[] data);
}
