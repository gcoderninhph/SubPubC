namespace PubSubLib.Client;

internal interface IRegionClientUnitInternal
{
    void SetTarget(object target);
    void ApplyUpdate(byte[] mirrorData, string commit);
    void Init(long id, Vector2 position);
    void DispatchMessage(string subject, byte[] data);
}
