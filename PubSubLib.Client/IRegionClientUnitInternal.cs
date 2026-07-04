namespace PubSubLib.Client;

internal interface IRegionClientUnitInternal
{
    void SetTarget(IAlive target);
    string GetUnitType();
    void ApplyUpdate(byte[] mirrorData, string commit);
    void Init(long id, Vector2 position);
    long GetId();
    IAlive GetTarget();
    void DispatchMessage(string subject, byte[] data);
}
