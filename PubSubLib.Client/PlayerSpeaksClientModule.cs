using MyConnection;
using PubSubLib.Messages;

namespace PubSubLib.Client;

internal sealed class PlayerSpeaksClientModule : IPlayerSpeaksClientModule
{
    private readonly PlayerSpeaksClient _client;
    private ISubscribe? _tcpSub;
    private ISubscribe? _welcomeSub;

    public PlayerSpeaksClientModule()
    {
        _client = new PlayerSpeaksClient();
    }

    public void SetIClient(IClient client)
    {
        _welcomeSub = client.SubscribeTcp<PlayerSpeaksWelcomeMsg>("PlayerSpeaks.Welcome", OnWelcome);
        _tcpSub = client.SubscribeTcp<PlayerSpeaksEvent>("PlayerSpeaks.Evt", OnEvent);
    }

    private void OnWelcome(PlayerSpeaksWelcomeMsg msg)
    {
        _client.SetPlayerId(msg.PlayerId);
    }

    private void OnEvent(PlayerSpeaksEvent evt)
    {
        _client.ApplyUpdate(evt.DataName, evt.Data.ToByteArray(), evt.Commit);
    }

    public IPlayerSpeaksClient Get()
    {
        return _client;
    }

    public void Dispose()
    {
        _welcomeSub?.UnSubscribe();
        _tcpSub?.UnSubscribe();
        _client.Dispose();
    }
}
