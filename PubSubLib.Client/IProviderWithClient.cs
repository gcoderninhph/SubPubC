namespace PubSubLib.Client;

internal interface IProviderWithClient
{
    void SetClient(IPubSubClient client);
    void OnStart();
    void OnDispose();
}
